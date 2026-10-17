// SWYF Speech Fixer v4.2 (UNOFFICIAL) - for Scam With Your Friends
// One tool for the community fixes. The player picks what to install from a menu:
//   1. Old CPU fix (CPUs without AVX2, game build 0.1.71)
//   2. Speech speed-up (8-bit speech model + listening window sized to the phrase)
//   3. Caller voice fix (Portuguese callers cut off after ~0.3 s, game 0.1.74)
//   4. Vulkan speech test - tells the player to add -whisper-cpu if their graphics card crashes it
//   then advice for lower-end PCs (BanditCam watermark, FPS cap).
// English and Brazilian Portuguese. Run again to change the selection or to undo everything.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

static partial class Fixer
{
    const string Version = "4.2.0";
    const string AppId = "4954910";
    const string GameFolderName = "Scam With Your Friends";
    const string GameExe = "Scam With Your Friends.exe";
    const string PrefsKey = @"Software\Jatater Worldwide\Scam With Your Friends";

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool IsProcessorFeaturePresent(uint feature);

    static string assumeCpu, simulateProbe; // only settable in test builds (SWYF_TEST)

    // ---- Language ----
    static bool Pt;
    static string Tr(string en, string pt) { return Pt ? pt : en; }

    static bool DetectPortuguese()
    {
        try { if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt") return true; } catch { }
        string game = ReadPrefString("Kolkata.CallLanguage") ?? ReadPrefString("LanguageManager.CurrentLanguage");
        return game != null && game.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
    }

    static int Main(string[] args)
    {
        string gameDir = null, lang = null;
        bool uninstallFlag = false, noPause = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--vulkan-probe" && i + 1 < args.Length)
            {
                string dir = args[++i], sim = null;
#if SWYF_TEST
                foreach (string x in args) if (x.StartsWith("--simulate=")) sim = x.Substring(11);
#endif
                try { return ProbeMain(dir, sim); }
                catch (Exception e) { Console.WriteLine("PROBEERROR " + e.Message); return 4; }
            }
            if (a == "--convert" && i + 2 < args.Length) // test hook
            {
                try { Quantize(args[i + 1], args[i + 2]); Console.WriteLine("converted"); return 0; }
                catch (Exception e) { Console.WriteLine("error: " + e.Message); return 1; }
            }
#if SWYF_TEST
            if (a.StartsWith("--assume-cpu=")) { assumeCpu = a.Substring(13); continue; }
            if (a.StartsWith("--simulate-probe=")) { simulateProbe = a.Substring(17); continue; }
#endif
            if (a.StartsWith("--lang=")) lang = a.Substring(7).ToLowerInvariant();
            else if (a == "--uninstall") uninstallFlag = true;
            else if (a == "--no-pause") noPause = true;
            else gameDir = a.Trim(new[] { '"' });
        }
        Pt = lang != null ? lang.StartsWith("pt") : DetectPortuguese();
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        try { Console.Title = "SWYF Speech Fixer v" + Version; } catch { }
        Banner();
        int code;
        try { code = Run(gameDir, uninstallFlag); }
        catch (Exception e)
        {
            Line("");
            Line(Tr("Something went wrong: ", "Algo deu errado: ") + e.Message);
            Line(Tr("Your original files were not removed.", "Seus arquivos originais não foram apagados."));
            code = 1;
        }
        if (!noPause) { Line(""); Console.Write(Tr("Press Enter to close...", "Aperte Enter para fechar...")); Console.ReadLine(); }
        return code;
    }

    static void Banner()
    {
        Line("==============================================================");
        Line(" SWYF Speech Fixer v" + Version + Tr(" (unofficial)", " (não oficial)"));
        Line(Tr(" Fixes speech-to-text and caller voices in Scam With Your Friends.",
                " Corrige o reconhecimento de voz e as vozes das ligações em Scam With Your Friends."));
        Line(Tr(" Not made or supported by the game's developers.",
                " Não é feito nem suportado pelos desenvolvedores do jogo."));
        Line("==============================================================");
        Line("");
    }

    // ---- Menu items ----
    sealed class Item
    {
        public string Name, Status;
        public bool Available, Installed, Wanted;
    }

    static int Run(string gameDir, bool uninstallFlag)
    {
        gameDir = gameDir ?? FindGameDir();
        if (gameDir == null)
        {
            Line(Tr("Could not find the game folder automatically.", "Não encontrei a pasta do jogo automaticamente."));
            Line(Tr("Paste the game folder path (Steam > right-click game > Manage > Browse local files), then press Enter:",
                    "Cole o caminho da pasta do jogo (Steam > botão direito no jogo > Gerenciar > Explorar arquivos locais) e aperte Enter:"));
            Console.Write("> ");
            gameDir = (Console.ReadLine() ?? "").Trim().Trim(new[] { '"' });
        }
        if (!File.Exists(Path.Combine(gameDir, GameExe)))
        {
            Line(Tr("That doesn't look like the game folder (no \"" + GameExe + "\" there). Nothing was changed.",
                    "Essa não parece ser a pasta do jogo (não tem \"" + GameExe + "\" nela). Nada foi alterado."));
            return 1;
        }
        Line(Tr("Game folder: ", "Pasta do jogo: ") + gameDir);
        if (Process.GetProcessesByName("Scam With Your Friends").Length > 0)
        {
            Line(Tr("The game is running. Close it completely and run this again. Nothing was changed.",
                    "O jogo está aberto. Feche ele completamente e rode isto de novo. Nada foi alterado."));
            return 1;
        }

        string modelDir = Path.Combine(gameDir, WhisperRel);
        string dll = Path.Combine(gameDir, WhisperDllRel);
        bool hasAvx, hasAvx2;
        GetCpu(out hasAvx, out hasAvx2);
        DisableOldBepInEx(gameDir);
        if (uninstallFlag) return UndoAll(gameDir, modelDir, dll);

        Line(Tr("Processor: AVX ", "Processador: AVX ") + YesNo(hasAvx) + ", AVX2 " + YesNo(hasAvx2) + ", "
             + Environment.ProcessorCount + Tr(" threads", " threads"));

        // ---- Work out what is possible / installed ----
        CpuFixState cpuState = hasAvx ? GetCpuFixState(gameDir, hasAvx2) : CpuFixState.NotNeeded;
        var cpu = new Item();
        var speed = new Item();
        var voice = new Item();
        var gpu = new Item();
        var items = new[] { cpu, speed, voice, gpu };

        string dllHash = File.Exists(dll) ? Sha256(dll) : null;
        bool speedInstalled = ModelsConverted(modelDir) || dllHash == DllPatched;
        bool speedTodo = dllHash == DllOriginal;
        foreach (string n in ModelNames)
        {
            string p = Path.Combine(modelDir, n);
            if (File.Exists(p) && ReadFtype(p) == FtypeF16) speedTodo = true;
        }
        string pocketHash = File.Exists(PocketPath(gameDir)) ? Sha256(PocketPath(gameDir)) : null;

        Action describe = () =>
        {
            cpu.Name = Tr("Old CPU fix", "Correção para CPU antiga");
            cpu.Installed = cpuState == CpuFixState.Installed;
            cpu.Available = cpuState == CpuFixState.Installed || cpuState == CpuFixState.Installable;
            cpu.Status = !hasAvx ? Tr("your CPU has no AVX, speech can't run on it", "sua CPU não tem AVX, o reconhecimento de voz não roda nela")
                : cpuState == CpuFixState.NotNeeded ? Tr("not needed (your CPU has AVX2)", "não precisa (sua CPU tem AVX2)")
                : cpuState == CpuFixState.FixedByDevelopers ? Tr("not needed (the game has its own fix now)", "não precisa (o jogo já tem a própria correção)")
                : cpuState == CpuFixState.Installed ? Tr("installed", "instalada")
                : cpuState == CpuFixState.Installable ? Tr("needed for your CPU", "necessária para sua CPU")
                : Tr("not available for this game version", "não disponível para esta versão do jogo");

            speed.Name = Tr("Faster speech recognition", "Reconhecimento de voz mais rápido");
            speed.Installed = speedInstalled;
            speed.Available = hasAvx && (speedInstalled || speedTodo);
            speed.Status = !hasAvx ? Tr("your CPU can't run speech recognition", "sua CPU não roda o reconhecimento de voz")
                : speedInstalled && !speedTodo ? Tr("installed", "instalado")
                : speedInstalled ? Tr("partly installed", "parcialmente instalado")
                : speedTodo ? Tr("not installed - recommended, mostly for older PCs", "não instalado - recomendado, principalmente para PCs mais antigos")
                : Tr("not available for this game version", "não disponível para esta versão do jogo");

            voice.Name = Tr("Caller voice fix", "Correção das vozes das ligações");
            voice.Installed = pocketHash == PocketPatched;
            voice.Available = pocketHash == PocketPatched || pocketHash == PocketOriginal;
            voice.Status = voice.Installed ? Tr("installed", "instalada")
                : voice.Available ? Tr("not installed - stops Portuguese callers being cut off after a split second",
                                       "não instalada - impede que as vozes em português sejam cortadas depois de uma fração de segundo")
                : Tr("not available for this game version (maybe already fixed)", "não disponível para esta versão do jogo (talvez já corrigido)");

            gpu.Name = Tr("Graphics card speech test", "Teste da placa de vídeo");
            gpu.Installed = false;
            gpu.Available = hasAvx;
            gpu.Status = hasAvx ? Tr("checks if speech can use your graphics card (changes no files)",
                                     "verifica se o reconhecimento de voz pode usar sua placa de vídeo (não altera arquivos)")
                : Tr("skipped, your CPU can't run speech recognition", "pulado, sua CPU não roda o reconhecimento de voz");
        };
        describe();
        foreach (Item it in items) it.Wanted = it.Available;   // recommended: everything that applies

        // ---- Menu ----
        while (true)
        {
            Line("");
            Line(Tr("Choose what to do (recommended choices are already ticked):",
                    "Escolha o que fazer (as opções recomendadas já estão marcadas):"));
            for (int i = 0; i < items.Length; i++)
            {
                Item it = items[i];
                string box = !it.Available ? "[ ]" : it.Wanted ? "[X]" : "[ ]";
                string mark = it.Available ? (i + 1) + "." : "  ";
                Line("  " + mark + " " + box + " " + it.Name);
                Line("          " + it.Status);
            }
            Line("");
            Line(Tr("  Type a number to tick/untick it. Unticking something that is installed removes it.",
                    "  Digite um número para marcar/desmarcar. Desmarcar algo instalado remove essa correção."));
            Line(Tr("  Enter = go    U = undo everything    Q = quit without changes    P = Português",
                    "  Enter = aplicar    U = desfazer tudo    Q = sair sem alterar    E = English"));
            Console.Write("> ");
            string input = Console.ReadLine();
            if (input == null) break;                       // no console input (scripted run): use the ticks
            input = input.Trim().ToUpperInvariant();
            if (input == "") break;
            if (input == "Q") { Line(Tr("Nothing was changed.", "Nada foi alterado.")); return 0; }
            if (input == "U") return UndoAll(gameDir, modelDir, dll);
            if (input == "P" || input == "E") { Pt = input == "P"; describe(); continue; }
            foreach (string tok in input.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (int.TryParse(tok, out n) && n >= 1 && n <= items.Length && items[n - 1].Available)
                    items[n - 1].Wanted = !items[n - 1].Wanted;
                else Line(Tr("  (ignored \"", "  (ignorado \"") + tok + "\")");
            }
        }

        // ---- Apply ----
        int step = 0, steps = 0;
        foreach (Item it in items) if (it.Available && (it.Wanted || it.Installed)) steps++;
        if (cpuState == CpuFixState.FixedByDevelopers) CleanUpAfterDeveloperFix(gameDir);

        if (cpu.Available && (cpu.Wanted || cpu.Installed))
        {
            Header(++step, steps, cpu.Name);
            if (cpu.Wanted && !cpu.Installed)
            {
                Console.Write(Tr("  Installing ... ", "  Instalando ... "));
                InstallCpuFix(gameDir);
                Line(Tr("done", "pronto"));
            }
            else if (cpu.Wanted) Line(Tr("  Already installed.", "  Já instalada."));
            else
            {
                Line(UninstallCpuFix(gameDir) ? Tr("  Removed - original files restored.", "  Removida - arquivos originais restaurados.")
                    : Tr("  Could not restore the files. Run Steam > Verify integrity of game files.",
                         "  Não consegui restaurar os arquivos. Use Steam > Verificar integridade dos arquivos do jogo."));
            }
        }

        if (speed.Available && (speed.Wanted || speed.Installed))
        {
            Header(++step, steps, speed.Name);
            if (speed.Wanted) InstallSpeedup(modelDir, dll);
            else { UninstallModels(modelDir); UninstallDll(dll); }
        }

        if (voice.Available && (voice.Wanted || voice.Installed))
        {
            Header(++step, steps, voice.Name);
            if (voice.Wanted) InstallVoiceFix(gameDir);
            else { UninstallVoiceFix(gameDir); }
        }

        bool gpuProblem = false, gpuNone = false;
        if (gpu.Available && gpu.Wanted)
        {
            Header(++step, steps, gpu.Name + Tr(" - takes a few seconds...", " - leva alguns segundos..."));
            ProbeResult probe = RunVulkanProbe(gameDir, simulateProbe);
            string launch = ReadLaunchOptions();
            bool hasCpuOption = launch != null && Regex.IsMatch(launch, @"(^|\s)-whisper-cpu(\s|$)", RegexOptions.IgnoreCase);
            gpuProblem = probe.Outcome == ProbeOutcome.Crashed || probe.Outcome == ProbeOutcome.Hung;
            gpuNone = probe.Outcome == ProbeOutcome.NoGpu;
            switch (probe.Outcome)
            {
                case ProbeOutcome.Passed:
                    Line(Tr("  PASSED on ", "  PASSOU na ") + probe.GpuName + Tr(". Speech can use your graphics card.", ". O reconhecimento de voz pode usar sua placa de vídeo."));
                    if (hasCpuOption)
                    {
                        Line(Tr("  Note: you have -whisper-cpu in your launch options. You can try removing it for faster",
                                "  Obs.: você tem -whisper-cpu nas opções de inicialização. Pode tentar remover para deixar"));
                        Line(Tr("  speech. If the game then crashes when speech starts, put it back.",
                                "  a voz mais rápida. Se o jogo travar quando a voz começar, coloque de volta."));
                    }
                    break;
                case ProbeOutcome.NoGpu:
                    Line(Tr("  No usable Vulkan graphics card found - the game will use the CPU for speech by itself. OK.",
                            "  Nenhuma placa de vídeo Vulkan utilizável - o jogo vai usar a CPU para a voz sozinho. OK."));
                    break;
                case ProbeOutcome.LoadFailed:
                    Line(Tr("  Could not load the game's speech files (", "  Não consegui carregar os arquivos de voz do jogo (") + probe.Detail + ").");
                    Line(Tr("  Windows may be blocking them: check Windows Security > App & browser control > Smart App Control,",
                            "  O Windows pode estar bloqueando: veja Segurança do Windows > Controle de aplicativos e do navegador > Controle Inteligente de Aplicativos,"));
                    Line(Tr("  and your antivirus. Then run this tool again.", "  e seu antivírus. Depois rode esta ferramenta de novo."));
                    break;
                default:
                    Line(Tr("  FAILED - the speech engine ", "  FALHOU - o reconhecimento de voz ")
                         + (probe.Outcome == ProbeOutcome.Hung ? Tr("froze", "congelou") : Tr("crashed", "travou"))
                         + Tr(" during the graphics card test", " durante o teste da placa de vídeo")
                         + (probe.Detail != "" ? " (" + probe.Detail + ")" : "") + ".");
                    Line(Tr("  Without the fix below, the game will probably crash when speech starts.",
                            "  Sem a correção abaixo, o jogo provavelmente vai travar quando a voz começar."));
                    break;
            }
            if (gpuProblem)
            {
                Line("");
                if (hasCpuOption) Line(Tr("  You already have the launch option -whisper-cpu set. Good - that prevents this crash.",
                                          "  Você já tem a opção de inicialização -whisper-cpu. Ótimo - isso evita esse travamento."));
                else LaunchOptionPrompt(launch);
            }
        }

        Line("");
        Line(Tr("Tips", "Dicas"));
        bool lowEnd = !hasAvx2 || gpuProblem || gpuNone || Environment.ProcessorCount <= 4;
        int watermark = ReadWatermarkSetting();
        if (lowEnd && hasAvx)
        {
            Line(Tr("  Your PC is on the slower side for this game, so:", "  Seu PC é mais fraco para este jogo, então:"));
            if (watermark == 0) Line(Tr("  - BanditCam watermark: already OFF. Good.", "  - Marca d'água do BanditCam: já está DESLIGADA. Ótimo."));
            else
            {
                Line(Tr("  - Turn OFF the BanditCam WATERMARK: open the BanditCam app on the in-game computer and",
                        "  - DESLIGUE a MARCA D'ÁGUA do BanditCam: abra o app BanditCam no computador dentro do jogo e"));
                Line(Tr("    click the watermark toggle. After every call the game re-encodes the recording to add the",
                        "    clique na opção da marca d'água. Depois de cada ligação o jogo recodifica a gravação para"));
                Line(Tr("    watermark, and that slows down speech recognition right when the next call starts.",
                        "    colocar a marca d'água, e isso deixa a voz lenta justo quando a próxima ligação começa."));
            }
            Line(Tr("  - Turn on VSync or cap your FPS (the game runs uncapped by default).",
                    "  - Ligue o VSync ou limite o FPS (o jogo roda sem limite por padrão)."));
            Line(Tr("  - Speak after the caller has finished, then give it a few seconds.",
                    "  - Fale depois que a pessoa na ligação terminar, e espere alguns segundos."));
        }
        else if (hasAvx) Line(Tr("  Speak after the caller has finished talking.", "  Fale depois que a pessoa na ligação terminar de falar."));
        else Line(Tr("  Your CPU can't run the game's speech recognition, but you can type to the callers.",
                     "  Sua CPU não roda o reconhecimento de voz do jogo, mas você pode digitar para quem liga."));

        Line("");
        Line(Tr("ALL DONE.", "PRONTO."));
        Line(Tr("Run this tool again any time to change what is installed, or type U to undo everything.",
                "Rode esta ferramenta de novo quando quiser para mudar o que está instalado, ou digite U para desfazer tudo."));
        Line(Tr("A game update or 'Verify integrity of game files' may undo the fixes - just run this again.",
                "Uma atualização do jogo ou 'Verificar integridade dos arquivos' pode desfazer as correções - é só rodar de novo."));
        return 0;
    }

    static void Header(int step, int steps, string name)
    {
        Line("");
        Line("[" + step + "/" + steps + "] " + name);
    }

    static void InstallSpeedup(string modelDir, string dll)
    {
        var todo = new List<string>();
        int found = 0;
        foreach (string n in ModelNames)
        {
            string p = Path.Combine(modelDir, n);
            if (!File.Exists(p)) continue;
            found++;
            if (ReadFtype(p) == FtypeF16) todo.Add(p);
        }
        if (found == 0) Line(Tr("  Speech model files not found - skipped. (Steam > Verify integrity of game files.)",
                                "  Arquivos do modelo de voz não encontrados - pulado. (Steam > Verificar integridade dos arquivos do jogo.)"));
        else if (todo.Count == 0) Line(Tr("  Faster speech model: already installed.", "  Modelo de voz mais rápido: já instalado."));
        else
        {
            long need = 0;
            foreach (string p in todo) need += new FileInfo(p).Length / 2 + (20L << 20);
            bool space = true;
            try { space = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(modelDir))).AvailableFreeSpace >= need; } catch { }
            if (!space) Line(Tr("  Not enough free disk space for the faster model (need about ", "  Sem espaço livre suficiente para o modelo mais rápido (precisa de uns ")
                             + (need >> 20) + " MB)" + Tr(" - skipped.", " - pulado."));
            else foreach (string p in todo)
            {
                Console.Write(Tr("  Converting ", "  Convertendo ") + Path.GetFileName(p) + Tr(" to the faster format ... ", " para o formato mais rápido ... "));
                var sw = Stopwatch.StartNew();
                string tmp = p + TempSuffix;
                try { Quantize(p, tmp); Validate(tmp); }
                catch { TryDelete(tmp); throw; }
                string bak = p + BackupSuffix;
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(p, bak);
                File.Move(tmp, p);
                Line(Tr("done (", "pronto (") + sw.Elapsed.TotalSeconds.ToString("0.0") + " s)");
            }
        }
        string h = File.Exists(dll) ? Sha256(dll) : null;
        if (h == DllOriginal) { Console.Write(Tr("  Shorter listening window ... ", "  Janela de escuta menor ... ")); PatchDll(dll); Line(Tr("done", "pronto")); }
        else if (h == DllPatched) Line(Tr("  Shorter listening window: already installed.", "  Janela de escuta menor: já instalada."));
        else Line(Tr("  Shorter listening window: SKIPPED (your game version has a different com.whisper.unity.dll).",
                     "  Janela de escuta menor: PULADA (sua versão do jogo tem outro com.whisper.unity.dll)."));
    }

    static bool ModelsConverted(string modelDir)
    {
        foreach (string n in ModelNames)
        {
            string p = Path.Combine(modelDir, n);
            if (File.Exists(p + BackupSuffix) || (File.Exists(p) && ReadFtype(p) == FtypeQ8)) return true;
        }
        return false;
    }

    static int UndoAll(string gameDir, string modelDir, string dll)
    {
        Line("");
        bool ok = true;
        if (!UninstallCpuFix(gameDir)) { ok = false; Line(Tr("Could not restore the old CPU fix files.", "Não consegui restaurar os arquivos da correção para CPU antiga.")); }
        if (UninstallModels(modelDir) != 0) ok = false;
        if (UninstallDll(dll) != 0) ok = false;
        if (UninstallVoiceFix(gameDir) != 0) ok = false;
        Line("");
        if (ok)
        {
            Line(Tr("UNDONE - original game files restored.", "DESFEITO - arquivos originais do jogo restaurados."));
            Line(Tr("If you added -whisper-cpu to the launch options and want it gone, remove it in Steam yourself.",
                    "Se você colocou -whisper-cpu nas opções de inicialização e quer tirar, remova na Steam você mesmo."));
        }
        else Line(Tr("Some files could not be restored. Run Steam > Properties > Installed Files > Verify integrity of game files.",
                     "Alguns arquivos não puderam ser restaurados. Use Steam > Propriedades > Arquivos instalados > Verificar integridade dos arquivos do jogo."));
        return ok ? 0 : 1;
    }

    static void LaunchOptionPrompt(string current)
    {
        Line("  ##############################################################");
        Line(Tr("  #  ACTION NEEDED: add a Steam launch option                   #",
                "  #  AÇÃO NECESSÁRIA: adicione uma opção de inicialização       #"));
        Line("  ##############################################################");
        Line(Tr("  1. Open Steam > Library.", "  1. Abra a Steam > Biblioteca."));
        Line(Tr("  2. Right-click 'Scam With Your Friends' > Properties.", "  2. Clique com o botão direito em 'Scam With Your Friends' > Propriedades."));
        Line(Tr("  3. On the General tab, find 'Launch Options' and type:   -whisper-cpu",
                "  3. Na aba Geral, em 'Opções de inicialização', digite:   -whisper-cpu"));
        if (!string.IsNullOrEmpty(current))
            Line(Tr("     (You already have \"" + current + "\" there - keep it and add -whisper-cpu after a space.)",
                    "     (Você já tem \"" + current + "\" lá - mantenha e adicione -whisper-cpu depois de um espaço.)"));
        Line(Tr("  4. Close the Properties window.", "  4. Feche a janela de Propriedades."));
        Line(Tr("  This makes speech use the processor instead of the graphics card, so the game won't crash.",
                "  Isso faz a voz usar o processador em vez da placa de vídeo, então o jogo não vai travar."));
        Console.Write(Tr("  Press Enter when you have done this... ", "  Aperte Enter quando terminar... "));
        Console.ReadLine();
    }

    // Unity PlayerPrefs string (REG_BINARY, UTF-8, trailing NUL) whose name starts with key + "_h". null = not found.
    static string ReadPrefString(string key)
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PrefsKey))
            {
                if (k == null) return null;
                foreach (string name in k.GetValueNames())
                    if (name.StartsWith(key + "_h", StringComparison.Ordinal))
                    {
                        byte[] b = k.GetValue(name) as byte[];
                        return b == null ? null : Encoding.UTF8.GetString(b).TrimEnd('\0');
                    }
            }
        }
        catch { }
        return null;
    }

    // Steam's userdata\<id>\config\localconfig.vdf -> apps -> 4954910 -> LaunchOptions. null = couldn't read.
    static string ReadLaunchOptions()
    {
        string result = null;
        foreach (string root in SteamRoots())
        {
            string ud = Path.Combine(root, "userdata");
            if (!Directory.Exists(ud)) continue;
            foreach (string user in Directory.GetDirectories(ud))
            {
                string vdf = Path.Combine(Path.Combine(user, "config"), "localconfig.vdf");
                try
                {
                    if (!File.Exists(vdf)) continue;
                    string text = File.ReadAllText(vdf);
                    string block = FindVdfBlock(text, AppId);
                    if (block == null) continue;
                    Match m = Regex.Match(block, "\"LaunchOptions\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
                    string opt = m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : "";
                    if (result == null || opt.Length > result.Length) result = opt;
                }
                catch { }
            }
        }
        return result;
    }

    static string FindVdfBlock(string text, string key)
    {
        int at = 0;
        while ((at = text.IndexOf("\"" + key + "\"", at, StringComparison.Ordinal)) >= 0)
        {
            int i = at + key.Length + 2;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i < text.Length && text[i] == '{')
            {
                int depth = 0;
                for (int j = i; j < text.Length; j++)
                {
                    if (text[j] == '{') depth++;
                    else if (text[j] == '}' && --depth == 0) return text.Substring(i, j - i + 1);
                }
                return null;
            }
            at = i;
        }
        return null;
    }

    // Unity PlayerPrefs (HKCU\Software\Jatater Worldwide\Scam With Your Friends): 0 = off, 1 = on (default), -1 = unknown
    static int ReadWatermarkSetting()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PrefsKey))
            {
                if (k == null) return 1;
                foreach (string name in k.GetValueNames())
                    if (name.StartsWith("KolkataOS.BanditCam.Watermark_h", StringComparison.Ordinal))
                        return Convert.ToInt64(k.GetValue(name)) == 0 ? 0 : 1;
            }
            return 1;
        }
        catch { return -1; }
    }

    static void GetCpu(out bool avx, out bool avx2)
    {
        if (assumeCpu != null) { avx = assumeCpu != "none"; avx2 = assumeCpu == "avx2"; return; }
        avx = IsProcessorFeaturePresent(39);   // PF_AVX_INSTRUCTIONS_AVAILABLE
        avx2 = IsProcessorFeaturePresent(40);  // PF_AVX2_INSTRUCTIONS_AVAILABLE
    }

    static string YesNo(bool b) { return b ? Tr("yes", "sim") : Tr("NO", "NÃO"); }

    static string FindGameDir()
    {
        var candidates = new List<string>();
        string here = AppDomain.CurrentDomain.BaseDirectory;
        candidates.Add(here);
        candidates.Add(Path.GetDirectoryName(here.TrimEnd('\\', '/')) ?? here);
        foreach (string lib in SteamLibraries())
            candidates.Add(Path.Combine(Path.Combine(Path.Combine(lib, "steamapps"), "common"), GameFolderName));
        foreach (string c in candidates)
            if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, GameExe))) return c;
        return null;
    }

    static List<string> SteamRoots()
    {
        var roots = new List<string>();
        try
        {
            object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
            if (v != null) roots.Add(v.ToString().Replace('/', '\\'));
            v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null);
            if (v != null && !roots.Contains(v.ToString())) roots.Add(v.ToString());
        }
        catch { }
        if (!roots.Contains(@"C:\Program Files (x86)\Steam")) roots.Add(@"C:\Program Files (x86)\Steam");
#if SWYF_TEST
        string testRoot = Environment.GetEnvironmentVariable("SWYF_FIXER_TEST_STEAM");
        if (!string.IsNullOrEmpty(testRoot)) roots.Insert(0, testRoot);
#endif
        return roots;
    }

    static IEnumerable<string> SteamLibraries()
    {
        var libs = new List<string>();
        foreach (string r in SteamRoots())
        {
            if (!libs.Contains(r)) libs.Add(r);
            string vdf = Path.Combine(Path.Combine(r, "steamapps"), "libraryfolders.vdf");
            try
            {
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (!libs.Contains(p)) libs.Add(p);
                }
            }
            catch { }
        }
        return libs;
    }

    static void Line(string s) { Console.WriteLine(s); }
}
