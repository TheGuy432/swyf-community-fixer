// SWYF Speech Fixer v4.5 (UNOFFICIAL) - for Scam With Your Friends
// One tool for the community fixes. Double-click: a window to pick fixes (Gui.cs). --console: the same in text mode.
//   1. Old CPU fix (CPUs without AVX2, game build 0.1.71)
//   2. Speech speed-up (8-bit speech model + listening window sized to the phrase; 0.1.69 - 0.1.74 and the
//      18 Oct 2026 update, Steam build 26020526)
//   3. Caller voice fix (Portuguese callers cut off after ~0.3 s, game 0.1.74; built into the game since 26020526)
//   4. EXPERIMENTAL: Qwen3 TTS on 8 GB graphics cards (0.1.74; built into the game since 26020526)
//   5. Better English speech recognition (Whisper instead of Moonshine on CPU speech, 0.1.74; built in since 26020526)
//   6. Vulkan speech test - tells the player to add -whisper-cpu if their graphics card crashes it, and retests
//      without Vulkan add-on layers to narrow down why
//   then -whisper-cpu advice (AMD + NVIDIA PCs, languages other than English) and tips for lower-end PCs.
// Texts come from lang/<code>.txt (embedded). Run again to change the selection or to undo everything.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

static partial class Fixer
{
    const string Version = "4.5.0";
    const string AppId = "4954910";
    const string GameFolderName = "Scam With Your Friends";
    const string GameExe = "Scam With Your Friends.exe";
    const string PrefsKey = @"Software\Jatater Worldwide\Scam With Your Friends";

    [DllImport("kernel32.dll")]
    static extern bool IsProcessorFeaturePresent(uint feature);
    [DllImport("kernel32.dll")] static extern bool AllocConsole();
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);

    // only settable in test builds (SWYF_TEST)
    static string assumeCpu, simulateProbe, assumePc;
    static int assumeThreads;

    [STAThread]
    static int Main(string[] args)
    {
        string gameDir = null, lang = null;
        bool uninstallFlag = false, noPause = false, console = false;
        var guiTest = new List<string>();
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
            if (a == "--convert" && i + 2 < args.Length) // test hook (used by the GitHub build check)
            {
                EnsureConsole();
                try { Quantize(args[i + 1], args[i + 2]); Console.WriteLine("converted"); return 0; }
                catch (Exception e) { Console.WriteLine("error: " + e.Message); return 1; }
            }
#if SWYF_TEST
            if (a.StartsWith("--assume-cpu=")) { assumeCpu = a.Substring(13); continue; }
            if (a.StartsWith("--simulate-probe=")) { simulateProbe = a.Substring(17); continue; }
            if (a.StartsWith("--assume-gpu=")) { assumeGpu = a.Substring(13); continue; }
            if (a.StartsWith("--assume-threads=")) { assumeThreads = int.Parse(a.Substring(17)); continue; }
            if (a.StartsWith("--assume-pc=")) { assumePc = a.Substring(12); continue; }
            if (a.StartsWith("--assume-build=")) { assumeBuild = a.Substring(15); continue; }
            if (a.StartsWith("--assume-lang=")) { assumeLanguage = a.Substring(14); continue; }
            if (a.StartsWith("--assume-provider=")) { assumeProvider = a.Substring(18); continue; }
            if (a.StartsWith("--assume-layers=")) { assumeLayers = a.Substring(16); continue; }
            if (a.StartsWith("--test-")) { guiTest.Add(a); continue; }
#endif
            if (a.StartsWith("--lang=")) lang = a.Substring(7).ToLowerInvariant();
            else if (a == "--uninstall") uninstallFlag = true;
            else if (a == "--no-pause") noPause = true;
            else if (a == "--console") console = true;
            else gameDir = a.Trim(new[] { '"' });
        }
        LoadLanguages();
        SetLanguage(lang ?? DetectLanguage());

        if (!console && !uninstallFlag)
            return RunWindow(gameDir, guiTest);

        EnsureConsole();
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
        try { Console.Title = "SWYF Speech Fixer v" + Version; } catch { }
        Banner();
        int code;
        try { code = Run(gameDir, uninstallFlag); }
        catch (Exception e)
        {
            Line("");
            Line((T("main.something_went_wrong") + " ") + e.Message);
            Line(T("main.your_original_files_were_not"));
            code = 1;
        }
        if (!noPause) { Line(""); Out(T("main.press_enter_to_close")); Console.ReadLine(); }
        return code;
    }

    // The .exe is a window app; text mode needs a console. Keep redirected output (pipes/files) as it is.
    static void EnsureConsole()
    {
        if (IsUnix) return;
        try { if (GetStdHandle(-11) == IntPtr.Zero) AllocConsole(); } catch { }
    }

    static void Banner()
    {
        Line("==============================================================");
        Line(" SWYF Speech Fixer v" + Version + " " + T("main.unofficial"));
        Line(" " + T("main.fixes_speech_to_text_and"));
        Line(" " + T("main.not_made_or_supported_by"));
        Line("==============================================================");
        Line("");
    }

    // Prints text word-wrapped to the console width, every line starting with indent.
    static void Wrapped(string indent, string text) { Wrapped(indent, indent, text); }
    static void Wrapped(string first, string next, string text)
    {
        string indent = first;
        var line = new StringBuilder();
        foreach (string w in text.Split(' '))
        {
            if (line.Length > 0 && indent.Length + line.Length + 1 + w.Length > 100) { Line(indent + line); line.Length = 0; indent = next; }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0) Line(indent + line);
    }

    // Finds the game folder; null (after printing why) if it isn't usable.
    static string ConsoleGameDir(string gameDir)
    {
        gameDir = gameDir ?? FindGameDir();
        if (gameDir == null)
        {
            Line(T("main.could_not_find_game_folder"));
            Line(T("main.paste_game_folder_path_steam"));
            Out("> ");
            gameDir = (Console.ReadLine() ?? "").Trim().Trim(new[] { '"' });
        }
        if (!IsGameDir(gameDir))
        {
            Line(T("main.that_doesn_t_look_like", GameExe));
            return null;
        }
        return gameDir;
    }

    static bool IsGameDir(string d) { return !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, GameExe)); }
    static bool GameRunning() { return Process.GetProcessesByName("Scam With Your Friends").Length > 0; }

    static int Run(string gameDir, bool uninstallFlag)
    {
        gameDir = ConsoleGameDir(gameDir);
        if (gameDir == null) return 1;
        Line((T("main.game_folder") + " ") + gameDir);
        if (GameRunning())
        {
            Line(T("main.game_is_running_close_it"));
            return 1;
        }
        string modelDir = Path.Combine(gameDir, WhisperRel), dll = Path.Combine(gameDir, WhisperDllRel);
        if (uninstallFlag) { DisableOldBepInEx(gameDir); return UndoAll(gameDir, modelDir, dll); }

        Session s = Detect(gameDir);
        Line((T("main.processor_avx") + " ") + YesNo(s.HasAvx) + ", AVX2 " + YesNo(s.HasAvx2) + ", "
             + s.Threads + " " + T("main.threads"));

        // ---- Menu ----
        Item[] items = s.Items;
        while (true)
        {
            Line("");
            Line(T("main.choose_what_to_do_recommended"));
            for (int i = 0; i < items.Length; i++)
            {
                Item it = items[i];
                string box = !it.Available ? "[ ]" : it.Wanted ? "[X]" : "[ ]";
                string mark = it.Available ? (i + 1) + "." : "  ";
                string name = it.Experimental ? (T("main.experimental") + " ") + it.Name : it.Name;
                if (it.Untested && it.Available) name += " [" + T("main.untested") + "]";
                Line("  " + mark + " " + box + " " + name);
                Wrapped("          ", it.Status);
            }
            Line("");
            Line("  " + T("main.type_number_to_tick_untick"));
            Line("  " + T("main.enter_go_u_undo_everything")
                 + "    L = " + LanguageMenuHint());
            Out("> ");
            string input = Console.ReadLine();
            if (input == null) break;                       // no console input (scripted run): use the ticks
            input = input.Trim().ToUpperInvariant();
            if (input == "") break;
            if (input == "Q") { Line(T("main.nothing_was_changed")); return 0; }
            if (input == "U") { DisableOldBepInEx(gameDir); return UndoAll(gameDir, modelDir, dll); }
            if (input == "L") { SetLanguage(NextLanguage()); Describe(s); continue; }
            if (input == "P" || input == "E" || HasLanguage(input.ToLowerInvariant()))   // P/E kept from v4.2
            {
                SetLanguage(input == "P" ? "pt" : input == "E" ? "en" : input.ToLowerInvariant());
                Describe(s);
                continue;
            }
            foreach (string tok in input.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (int.TryParse(tok, out n) && n >= 1 && n <= items.Length && items[n - 1].Available)
                    items[n - 1].Wanted = !items[n - 1].Wanted;
                else Line("  " + T("main.ignored", tok));
            }
        }

        // ---- Apply ----
        ApplyResult r = Apply(s, false);
        if (r.Reason != CpuReason.None && !r.HasCpuOption)
        {
            Line("");
            LaunchOptionPrompt(r.Launch, CpuReasonText(r.Reason, s));
            if (r.KeepHint)
            {
                Wrapped("  ", T("main.with_whisper_cpu_english_speech"));
            }
        }

        Line("");
        Line(T("main.tips"));
        string intro;
        List<string> tips = Tips(s, r, out intro);
        if (intro != null) { Line("  " + intro); foreach (string t in tips) Wrapped("  - ", "    ", t); }
        else foreach (string t in tips) Wrapped("  ", t);

        Line("");
        Line(T("main.all_done"));
        Line(T("main.run_this_tool_again_any"));
        Line(T("main.game_update_or_verify_integrity"));
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
        if (found == 0) Line(("  " + T("main.speech_model_files_not_found")));
        else if (todo.Count == 0) Line(("  " + T("main.faster_speech_model_already_installed")));
        else
        {
            long need = 0;
            foreach (string p in todo) need += new FileInfo(p).Length / 2 + (20L << 20);
            bool space = true;
            try { space = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(modelDir))).AvailableFreeSpace >= need; } catch { }
            if (!space) Line("  " + T("main.not_enough_free_disk_space", (need >> 20)));
            else foreach (string p in todo)
            {
                Out("  " + (T("main.converting_to_faster_format", Path.GetFileName(p)) + " "));
                var sw = Stopwatch.StartNew();
                string tmp = p + TempSuffix;
                try { Quantize(p, tmp); Validate(tmp); }
                catch { TryDelete(tmp); throw; }
                string bak = p + BackupSuffix;
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(p, bak);
                File.Move(tmp, p);
                Line(T("main.done_s", sw.Elapsed.TotalSeconds.ToString("0.0")));
            }
        }
        string h = File.Exists(dll) ? Sha256(dll) : null;
        if (IsDllOriginal(h)) { Out("  " + (T("main.shorter_listening_window") + " ")); PatchDll(dll); Line(T("main.done")); }
        else if (IsDllPatched(h)) Line(("  " + T("main.shorter_listening_window_already_installed")));
        else Line(("  " + T("main.shorter_listening_window_skipped_your")));
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
        if (!UninstallCpuFix(gameDir)) { ok = false; Line(T("main.could_not_restore_old_cpu")); }
        if (UninstallModels(modelDir) != 0) ok = false;
        if (UninstallDll(dll) != 0) ok = false;
        if (UninstallVoiceFix(gameDir) != 0) ok = false;
        if (UninstallQwenFix(gameDir) != 0) ok = false;
        if (UninstallWhisperKeep(gameDir) != 0) ok = false;
        Line("");
        if (ok)
        {
            Line(T("main.undone_original_game_files_restored"));
            Line(T("main.if_you_added_whisper_cpu"));
        }
        else Line(T("main.some_files_could_not_be"));
        return ok ? 0 : 1;
    }

    static void LaunchOptionPrompt(string current, string why)
    {
        Line("  ##############################################################");
        Line(("  " + T("main.action_needed_add_steam_launch")));
        Line("  ##############################################################");
        if (!string.IsNullOrEmpty(why)) { Wrapped("  ", why); Line(""); }
        Line(("  " + T("main.1_open_steam_library")));
        Line(("  " + T("main.2_right_click_scam_with")));
        Line(("  " + T("main.3_on_general_tab_find")));
        if (!string.IsNullOrEmpty(current))
            Line("     " + T("main.you_already_have_there_keep", current));
        Line(("  " + T("main.4_close_properties_window")));
        Line(("  " + T("main.this_makes_speech_use_processor")));
        Out("  " + (T("main.press_enter_when_you_have") + " "));
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

    static string YesNo(bool b) { return b ? T("main.yes") : T("main.no"); }

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

}
