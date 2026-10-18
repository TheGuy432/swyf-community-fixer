// Shared logic used by both the window (Gui.cs) and the text mode (Fixer.cs): what is installed / possible on
// this PC (Detect + Describe), applying the chosen fixes (Apply) and the end-of-run tips (Tips).
// All output goes through Out()/Line(), so the window can show it in its log.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

static partial class Fixer
{
    // ---- Output sink: console by default, the window's log when the GUI runs ----
    static Action<string> Sink;
    static void Out(string s) { if (Sink != null) Sink(s); else Console.Write(s); }
    static void Line(string s) { Out(s + Environment.NewLine); }

    enum Pill { Recommended, Installed, Optional, NotNeeded, NotAvailable, Experimental }

    sealed class Item
    {
        public string Key, Name, Status;
        public bool Available, Installed, Wanted;
        public bool Partial;        // installed, but something is still left to do (speed-up)
        public bool Recommended;    // ticked by default because it helps this PC
        public bool NotNeeded;      // shown as "not needed" rather than "not available"
        public bool Untested;       // not confirmed in the game yet -> UNTESTED pill
        public bool Experimental;   // shown in the separate experimental box
        public bool IsTest;         // the graphics card test: changes no files
        public bool ShowAlways;     // experimental item shown even when not available (with its reason)
        public bool NeedCpuOption, NeedDownload;   // what the player still has to do first (buttons in the window)

        public bool IsChange
        {
            get
            {
                if (!Available) return false;
                if (IsTest) return Wanted;
                return Wanted != Installed || (Wanted && Partial);
            }
        }

        public Pill GetPill()
        {
            if (!Available) return NotNeeded ? Pill.NotNeeded : Pill.NotAvailable;
            if (Installed && !Partial) return Pill.Installed;
            if (Experimental) return Pill.Experimental;
            if (IsTest) return Pill.Optional;
            if (Recommended) return Pill.Recommended;
            if (NotNeeded) return Pill.NotNeeded;
            return Pill.Optional;
        }
    }

    sealed class Session
    {
        public string GameDir, ModelDir, Dll, Launch, CallLanguage, GameBuild, Provider;
        public bool HasAvx, HasAvx2, SpeedInstalled, SpeedTodo, CpuSpeech, StrongCpu;
        public bool OwnFixes;      // the game has the developers' own fixes (18 Oct 2026 update or newer)
        public bool AmdNvidia;     // AMD processor graphics + NVIDIA card
        public int SteamBuild, RamMb;
        public string DllHash, BigFile;
        public int Threads;
        public CpuFixState CpuState;
        public string PocketHash, AsmHash, SadHash;
        public PocketState Pocket;
        public AsmState Asm;
        public Item Cpu, Speed, Voice, Qwen, Keep, Gpu, Big, Bench;
        public Item[] Items;   // console menu order
    }

    // Why the player should add the -whisper-cpu launch option (None = no need, or they already run speech on the CPU).
    enum CpuReason { None, GpuTest, AmdNvidia, Language, Bench }

    sealed class ApplyResult
    {
        public ProbeResult Probe;          // null if the graphics card test didn't run
        public ProbeResult ProbeNoLayers;  // second test with Vulkan implicit layers off (only after a failed test)
        public List<string> Layers;        // implicit layers on this PC (only after a failed test)
        public bool GpuProblem, GpuNone, HasCpuOption, KeepHint;
        public CpuReason Reason;
        public BenchResult BenchGpu, BenchCpu;   // speech speed test (null = not run)
        public List<string> BenchLines;          // its result, as shown to the player
        public string Launch;
        public int Steps;
    }

    // Called by Apply around each step (the window uses these for its progress list).
    static Action<Item> OnStepBegin;
    static Action<Item, bool> OnStepEnd;

    static Session Detect(string gameDir)
    {
        var s = new Session();
        s.GameDir = gameDir;
        s.ModelDir = Path.Combine(gameDir, WhisperRel);
        s.Dll = Path.Combine(gameDir, WhisperDllRel);
        GetCpu(out s.HasAvx, out s.HasAvx2);
        s.Threads = assumeThreads > 0 ? assumeThreads : Environment.ProcessorCount;
        s.CpuState = s.HasAvx ? GetCpuFixState(gameDir, s.HasAvx2) : CpuFixState.NotNeeded;

        string dllHash = File.Exists(s.Dll) ? Sha256(s.Dll) : null;
        s.DllHash = dllHash;
        s.SpeedInstalled = ModelsConverted(s.ModelDir) || IsDllPatched(dllHash);
        s.SpeedTodo = IsDllOriginal(dllHash);
        foreach (string n in ModelNames)
        {
            string p = Path.Combine(s.ModelDir, n);
            if (File.Exists(p) && ReadFtype(p) == FtypeF16) s.SpeedTodo = true;
        }
        s.PocketHash = File.Exists(PocketPath(gameDir)) ? Sha256(PocketPath(gameDir)) : null;
        s.AsmHash = File.Exists(GameAsmPath(gameDir)) ? Sha256(GameAsmPath(gameDir)) : null;
        s.Pocket = ReadPocketState(gameDir);
        s.Asm = ReadAsmState(gameDir);
        s.SadHash = File.Exists(SadPath(gameDir)) ? Sha256(SadPath(gameDir)) : null;
        s.SteamBuild = ReadSteamBuildId(gameDir);
        s.OwnFixes = GameHasOwnFixes(s);
        s.Launch = ReadLaunchOptions();
        s.CpuSpeech = GameUsesCpuSpeech(s.Launch, s.OwnFixes);
        s.StrongCpu = s.HasAvx2 && s.Threads >= 8;
        s.CallLanguage = ReadCallLanguage();
        s.Provider = ReadSpeechProvider();
        s.AmdNvidia = IsAmdPlusNvidia(GraphicsCardIds());
        s.RamMb = TotalRamMb();
        s.BigFile = FindBigModelFile();
        if (s.Asm.Known || s.SadHash == SadOriginal || s.SadHash == SadPatched) s.GameBuild = "0.1.74";
        else
        {
            string sh = File.Exists(ScriptsPath(gameDir)) ? Sha256(ScriptsPath(gameDir)) : null;
            if (sh == ScriptsOriginal || sh == ScriptsPatched) s.GameBuild = "0.1.71";
            else if (s.SteamBuild > 0) s.GameBuild = s.SteamBuild.ToString();
        }

        s.Cpu = new Item { Key = "cpu" };
        s.Speed = new Item { Key = "speed" };
        s.Voice = new Item { Key = "voice", Untested = true };
        s.Qwen = new Item { Key = "qwen", Experimental = true, Untested = true };
        s.Keep = new Item { Key = "keep", Untested = true };
        s.Gpu = new Item { Key = "gpu", IsTest = true };
        s.Big = new Item { Key = "big", Experimental = true, Untested = true };
        s.Bench = new Item { Key = "bench", IsTest = true, Experimental = true };
        // Order = the order steps run in: the speed-up before the bigger model, the speed test last.
        s.Items = new[] { s.Cpu, s.Speed, s.Voice, s.Qwen, s.Keep, s.Big, s.Gpu, s.Bench };
        Describe(s);

        foreach (Item it in s.Items) it.Wanted = it.Available;      // recommended: everything that applies
        s.Qwen.Wanted = s.Qwen.Installed;                             // experimental: only when the player ticks it
        s.Big.Wanted = s.Big.Installed;
        s.Bench.Wanted = false;
        s.Keep.Wanted = s.Keep.Installed || (s.Keep.Available && s.CpuSpeech && s.StrongCpu);
        s.Cpu.Recommended = s.Cpu.Available && !s.Cpu.Installed;
        s.Speed.Recommended = s.Speed.Available && s.SpeedTodo;
        s.Voice.Recommended = s.Voice.Available && (!s.Voice.Installed || s.Voice.Partial);
        s.Keep.Recommended = s.Keep.Available && !s.Keep.Installed && s.CpuSpeech && s.StrongCpu;
        return s;
    }

    // What the caller voice fix does, for the player's call language (game setting "Kolkata.CallLanguage").
    // Hook for later: per-language descriptions once more languages are fixed. For now always the generic text.
    static string VoiceFixDescription(string callLanguage)
    {
        string c = (callLanguage ?? "").Length >= 2 ? callLanguage.Substring(0, 2).ToLowerInvariant() : "";
        switch (c)
        {
            case "pt": return T("voice.desc_pt");
            case "fr": return T("voice.desc_fr");
            case "de": return T("voice.desc_de");
            case "it": return T("voice.desc_it");
            case "ko": return T("voice.desc_ko");
            case "hi": return T("voice.desc_hi");
            case "zh": case "ja": case "ru": case "pl": case "tr": case "hu":
            case "sv": case "da": case "el": case "ar": case "fa":
                return T("voice.desc_moss");
            default: return T("voice.desc_general");
        }
    }

    // Names and status texts (language dependent; call again after switching language).
    static void Describe(Session s)
    {
        Item cpu = s.Cpu, speed = s.Speed, voice = s.Voice, qwen = s.Qwen, keep = s.Keep, gpu = s.Gpu;
        CpuFixState cpuState = s.CpuState;
        bool hasAvx = s.HasAvx;

        cpu.Name = T("core.old_cpu_fix");
        cpu.Installed = cpuState == CpuFixState.Installed;
        cpu.Available = cpuState == CpuFixState.Installed || cpuState == CpuFixState.Installable;
        cpu.NotNeeded = cpuState == CpuFixState.NotNeeded || cpuState == CpuFixState.FixedByDevelopers;
        cpu.Status = !hasAvx ? T("core.your_cpu_has_no_avx")
            : cpuState == CpuFixState.NotNeeded ? T("core.not_needed_your_cpu_has")
            : cpuState == CpuFixState.FixedByDevelopers ? T("core.not_needed_game_has_its")
            : cpuState == CpuFixState.Installed ? T("core.installed")
            : cpuState == CpuFixState.Installable ? T("core.needed_for_your_cpu")
            : T("core.not_available_for_this_game");

        speed.Name = T("core.faster_speech_recognition");
        speed.Installed = s.SpeedInstalled;
        speed.Partial = s.SpeedInstalled && s.SpeedTodo;
        speed.Available = hasAvx && (s.SpeedInstalled || s.SpeedTodo);
        speed.Status = !hasAvx ? T("core.your_cpu_can_t_run")
            : s.SpeedInstalled && !s.SpeedTodo ? T("core.installed_2")
            : s.SpeedInstalled ? T("core.partly_installed")
            : s.SpeedTodo ? T("core.not_installed_recommended_mostly_for")
            : T("core.not_available_for_this_game");

        voice.Name = T("core.caller_voice_fix");
        bool pocketKnown = s.Pocket != PocketState.Unknown;
        bool anyVoice = s.Pocket == PocketState.Fixed || s.Pocket == PocketState.OldFix || (s.Asm.Known && (s.Asm.Card || s.Asm.Fillers));
        bool fullVoice = (pocketKnown || s.Asm.Known) && (!pocketKnown || s.Pocket == PocketState.Fixed)
                         && (!s.Asm.Known || (s.Asm.Card && s.Asm.Fillers));
        voice.Available = pocketKnown || s.Asm.Known;
        voice.Installed = anyVoice;                    // unticking removes whatever is there
        voice.Partial = anyVoice && !fullVoice;        // an older or partial version: ticked = update
        voice.NotNeeded = !voice.Available && s.OwnFixes;
        voice.Status = fullVoice ? T("core.installed")
            : anyVoice ? T("voice.status_update") + " " + VoiceFixDescription(s.CallLanguage)
            : voice.Available ? T("voice.status_not_installed") + " " + VoiceFixDescription(s.CallLanguage)
            : s.OwnFixes ? T("core.fixed_in_game")
            : T("core.not_available_for_this_game_2");

        qwen.Name = T("core.qwen3_tts_on_8_gb");
        qwen.Installed = s.Asm.Known && s.Asm.Qwen;
        qwen.Available = s.Asm.Known;
        qwen.NotNeeded = !qwen.Available && s.OwnFixes;
        qwen.Status = qwen.Installed ? T("core.installed_untick_to_remove_if")
            : qwen.Available ? T("core.off_unlocks_qwen3_voices_on")
            : s.OwnFixes ? T("core.fixed_in_game")
            : T("core.not_available_for_this_game");

        keep.Name = T("core.better_english_speech_recognition_whisper");
        keep.Installed = s.SadHash == SadPatched;
        keep.Available = hasAvx && (s.SadHash == SadPatched || s.SadHash == SadOriginal);
        keep.NotNeeded = !s.CpuSpeech || (!keep.Available && s.OwnFixes);
        keep.Status = keep.Installed ? T("core.installed_english_speech_uses_more")
            : !keep.Available && s.OwnFixes ? T("core.fixed_in_game")
            : !keep.Available ? T("core.not_available_for_this_game")
            : s.CpuSpeech && s.StrongCpu ? T("core.not_installed_recommended_on_your")
            : s.CpuSpeech ? T("core.not_installed_more_accurate_english")
            : T("core.not_needed_now_only_matters");

        DescribeBig(s);
        Item bench = s.Bench;
        bench.Name = T("bench.name");
        bench.Available = s.HasAvx2 && File.Exists(Path.Combine(s.ModelDir, BenchModelName(s)));
        bench.Status = bench.Available ? T("bench.status", LanguageLabel(s.CallLanguage ?? "en")) : T("bench.status_unavailable");

        gpu.Name = T("core.graphics_card_speech_test");
        gpu.Installed = false;
        gpu.Available = hasAvx;
        gpu.Status = hasAvx ? T("core.checks_if_speech_can_use")
            : T("core.skipped_your_cpu_can_t");
    }

    static bool StepWanted(Item it, bool onlyChanges)
    {
        if (onlyChanges) return it.IsChange;
        if (it.IsTest) return it.Available && it.Wanted;
        return it.Available && (it.Wanted || it.Installed);
    }

    // onlyChanges: the window only runs steps that change something; the text mode also shows "Already installed".
    static ApplyResult Apply(Session s, bool onlyChanges)
    {
        var r = new ApplyResult();
        string gameDir = s.GameDir;
        DisableOldBepInEx(gameDir);
        if (s.CpuState == CpuFixState.FixedByDevelopers) CleanUpAfterDeveloperFix(gameDir);
        if (s.OwnFixes) CleanUpAfterGameUpdate(gameDir, s.Dll);
        if (!IsBigModel(BigTarget(s.ModelDir))) TryDelete(BigTarget(s.ModelDir) + BigBackupSuffix);
        r.Launch = ReadLaunchOptions();
        r.HasCpuOption = HasCpuOption(r.Launch);
        r.Reason = CpuAdvice(s);
        foreach (Item it in s.Items) if (StepWanted(it, onlyChanges)) r.Steps++;
        int step = 0;
        foreach (Item it in s.Items)
        {
            if (!StepWanted(it, onlyChanges)) continue;
            step++;
            if (Sink == null) Header(step, r.Steps, it.IsTest ? it.Name + " " + T(it.Key == "bench" ? "bench.takes_minutes" : "core.takes_few_seconds") : it.Name);
            else Line("[" + step + "/" + r.Steps + "] " + it.Name);
            if (OnStepBegin != null) OnStepBegin(it);
            bool ok = RunStep(s, it, r);
            if (OnStepEnd != null) OnStepEnd(it, ok);
        }
        if (r.GpuProblem) r.Reason = CpuReason.GpuTest;
        return r;
    }

    static bool HasCpuOption(string launch)
    {
        return launch != null && Regex.IsMatch(launch, @"(^|\s)-whisper-cpu(\s|$)", RegexOptions.IgnoreCase);
    }

    // Would -whisper-cpu help on this PC, whatever the launch options are now? (The graphics card test aside.)
    static CpuReason CpuOptionHelps(Session s)
    {
        if (!s.HasAvx) return CpuReason.None;
        if (s.AmdNvidia) return CpuReason.AmdNvidia;
        string l = s.CallLanguage;
        if (l != null && l != "en" && s.Provider != "Deepgram" && !(s.OwnFixes && s.Provider == "Moonshine" && IsMoonshineLanguage(l)))
            return CpuReason.Language;
        return CpuReason.None;
    }

    // What to tell the player before the graphics card test runs. None if speech already runs on the processor.
    static CpuReason CpuAdvice(Session s)
    {
        if (HasCpuOption(ReadLaunchOptions()) || s.CpuSpeech) return CpuReason.None;
        return CpuOptionHelps(s);
    }

    static string CpuReasonText(CpuReason why, Session s, ApplyResult r)
    {
        switch (why)
        {
            case CpuReason.GpuTest: return T("ui.launch_why");
            case CpuReason.Bench:
                return r != null && r.BenchGpu != null && r.BenchGpu.Outcome == BenchOutcome.Ok && r.BenchCpu != null
                    ? T("ui.launch_why_bench", Secs(r.BenchGpu.RunMs), Secs(r.BenchCpu.RunMs))
                    : T("ui.launch_why_bench_froze");
            case CpuReason.AmdNvidia: return T("ui.launch_why_amd_nvidia");
            case CpuReason.Language:
                return T("ui.launch_why_language") + (s.OwnFixes && IsMoonshineLanguage(s.CallLanguage) ? " " + T("ui.launch_or_moonshine") : "");
        }
        return "";
    }

    // Returns false if the step finished but didn't fully work (the window shows a warning icon).
    static bool RunStep(Session s, Item it, ApplyResult r)
    {
        string gameDir = s.GameDir;
        switch (it.Key)
        {
            case "cpu":
                if (it.Wanted && !it.Installed)
                {
                    Out("  " + (T("core.installing") + " "));
                    InstallCpuFix(gameDir);
                    Line(T("main.done"));
                }
                else if (it.Wanted) Line("  " + T("core.already_installed"));
                else if (UninstallCpuFix(gameDir)) Line("  " + T("core.removed_original_files_restored"));
                else
                {
                    Line("  " + T("core.could_not_restore_files_run"));
                    return false;
                }
                return true;
            case "speed":
                if (it.Wanted) { InstallSpeedup(s.ModelDir, s.Dll); return true; }
                return (UninstallModels(s.ModelDir) | UninstallDll(s.Dll)) == 0;
            case "voice":
                if (it.Wanted) { InstallVoiceFix(gameDir); return true; }
                return UninstallVoiceFix(gameDir) == 0;
            case "qwen":
                if (it.Wanted) { InstallQwenFix(gameDir); return true; }
                return UninstallQwenFix(gameDir) == 0;
            case "keep":
                if (it.Wanted) { InstallWhisperKeep(gameDir); return true; }
                return UninstallWhisperKeep(gameDir) == 0;
            case "big":
                if (it.Wanted) { InstallBigModel(s); return IsBigModel(BigTarget(s.ModelDir)); }
                return UninstallBigModel(s.ModelDir) == 0;
            case "gpu":
                return RunGpuTest(s, r);
            case "bench":
                return RunBenchStep(s, r);
        }
        return true;
    }

    static bool RunGpuTest(Session s, ApplyResult r)
    {
        ProbeResult probe = RunVulkanProbe(s.GameDir, simulateProbe);
        r.Probe = probe;
        r.Launch = ReadLaunchOptions();
        r.HasCpuOption = HasCpuOption(r.Launch);
        r.GpuProblem = probe.Outcome == ProbeOutcome.Crashed || probe.Outcome == ProbeOutcome.Hung;
        r.GpuNone = probe.Outcome == ProbeOutcome.NoGpu;
        switch (probe.Outcome)
        {
            case ProbeOutcome.Passed:
                Line("  " + T("core.passed_on_speech_can_use", probe.GpuName));
                bool benchDecides = s.Bench != null && s.Bench.Available && s.Bench.Wanted;   // the speed test measures it
                if (r.HasCpuOption && !benchDecides && CpuOptionHelps(s) == CpuReason.None)
                    Line("  " + T("core.note_you_have_whisper_cpu"));
                else if (r.HasCpuOption && !benchDecides)
                    Line("  " + T("core.keep_whisper_cpu"));
                break;
            case ProbeOutcome.NoGpu:
                Line("  " + T("core.no_usable_vulkan_graphics_card"));
                break;
            case ProbeOutcome.LoadFailed:
                Line("  " + T("core.could_not_load_game_s", probe.Detail));
                Line("  " + T("core.windows_may_be_blocking_them"));
                return false;
            default:
                string what = probe.Outcome == ProbeOutcome.Hung ? T("core.froze") : T("core.crashed");
                string detail = probe.Detail != "" ? " (" + probe.Detail + ")" : "";
                Line("  " + T("core.failed_speech_engine_during_graphics", what, detail));
                // Same test again without the Vulkan add-on layers (overlays, recorders, AMD switchable graphics):
                // tells us whether one of those is behind the freeze. Changes nothing on the PC.
                Out("  " + T("core.retest_without_layers") + " ");
                r.ProbeNoLayers = RunVulkanProbe(s.GameDir, simulateProbe, true);
                r.Layers = ImplicitLayerNames();
                Line(r.ProbeNoLayers.Outcome == ProbeOutcome.Passed ? T("core.retest_passed") : T("core.retest_failed"));
                if (r.Layers.Count > 0) Line("  " + T("core.layers_on_pc", string.Join(", ", r.Layers.ToArray())));
                Line("  " + T("core.without_whisper_cpu_launch_option"));
                if (r.HasCpuOption)
                    Line("  " + T("core.you_already_have_launch_option"));
                r.KeepHint = !s.Keep.Wanted && s.Keep.Available && s.StrongCpu;
                return false;
        }
        return true;
    }

    // End-of-run advice. intro is null or a sentence shown before the list.
    static List<string> Tips(Session s, ApplyResult r, out string intro)
    {
        var tips = new List<string>();
        intro = null;
        bool gpuProblem = r != null && r.GpuProblem, gpuNone = r != null && r.GpuNone;
        bool lowEnd = !s.HasAvx2 || gpuProblem || gpuNone || s.Threads <= 4;
        if (lowEnd && s.HasAvx)
        {
            intro = T("core.your_pc_is_on_slower");
            if (ReadWatermarkSetting() == 0) tips.Add(T("core.banditcam_watermark_already_off_good"));
            else tips.Add(T("core.turn_off_banditcam_watermark_open"));
            tips.Add(T("core.turn_on_vsync_or_cap"));
            tips.Add(T("core.speak_after_caller_has_finished"));
        }
        else if (s.HasAvx) tips.Add(T("core.speak_after_caller_has_finished_2"));
        else tips.Add(T("core.your_cpu_can_t_run_2"));
        if (s.HasAvx) tips.Add(T(s.Big != null && s.Big.Installed || (r != null && s.Big != null && s.Big.Wanted && s.Big.Available) ? "core.tip_first_load_big" : "core.tip_first_load"));
        if (s.Big != null && !s.Big.Available && (s.Big.NeedCpuOption || s.Big.NeedDownload))
            tips.Add(T("core.tip_big", BigModelFile));
        bool moonshineInCard = r != null && r.Reason == CpuReason.Language && !r.HasCpuOption;   // already said there
        if (s.HasAvx && s.OwnFixes && s.CallLanguage != null && s.CallLanguage != "en" && IsMoonshineLanguage(s.CallLanguage)
            && s.Provider != "Moonshine" && !moonshineInCard)
            tips.Add(T("core.tip_moonshine"));
        return tips;
    }

    // Short processor / graphics card names for the window ("i5-14400F", "RTX 4060 Ti").
    static string ShortCpuName()
    {
        string n = null;
        try
        {
            object v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null);
            if (v != null) n = v.ToString();
        }
        catch { }
        if (n == null) return null;
        n = Regex.Replace(n, @"\((R|TM|tm)\)", "");
        n = Regex.Replace(n, @"\b(\d+(th|rd|nd|st) Gen )?Intel Core\b|\bAMD\b|\bCPU\b|\bProcessor\b|@.*$|with Radeon.*$|\d+-Core", "", RegexOptions.IgnoreCase);
        return Regex.Replace(n, @"\s+", " ").Trim();
    }

    static string ShortGpuName()
    {
        foreach (string d in GraphicsCardNames())
        {
            if (d.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 || d.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            string n = Regex.Replace(d, @"\(R\)|\(TM\)|NVIDIA GeForce|NVIDIA|AMD Radeon|Intel\(R\)|Graphics$", "").Trim();
            return n.Length > 0 ? n : d;
        }
        return null;
    }

    // A short, English, copy-paste report for the fixer thread on Discord (what Tiger needs to help).
    static string BuildReport(Session s, ApplyResult r)
    {
        var sb = new System.Text.StringBuilder();
        Func<bool, string> yn = b => b ? "yes" : "no";
        sb.AppendLine("SWYF Community Fixer " + Version + " report");
        sb.AppendLine("Game: build " + (s.GameBuild ?? "unknown") + (s.OwnFixes ? " (has the 18 Oct fixes)" : ""));
        string cpu = assumePc != null ? assumePc.Split('|')[0] : ShortCpuName();
        sb.AppendLine("Processor: " + (cpu ?? "?") + ", " + s.Threads + " threads, AVX " + yn(s.HasAvx) + ", AVX2 " + yn(s.HasAvx2)
                      + ", memory " + (s.RamMb > 0 ? (s.RamMb / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB" : "?"));
        sb.AppendLine("Graphics: " + string.Join(", ", GraphicsCardNames().ToArray()) + (s.AmdNvidia ? " (AMD + NVIDIA)" : ""));
        sb.AppendLine("Call language: " + (s.CallLanguage ?? "?") + ", speech recognition setting: " + (string.IsNullOrEmpty(s.Provider) ? "Automatic" : s.Provider));
        string launch = r != null ? r.Launch : s.Launch;
        sb.AppendLine("Launch options: " + (string.IsNullOrEmpty(launch) ? "(none)" : launch));
        // What is installed now, read from the game folder again (a step can refuse, e.g. the bigger model on a slow PC).
        Session now = s;
        if (r != null) { try { now = Detect(s.GameDir); } catch { } }
        var on = new List<string>();
        foreach (Item it in now.Items)
            if (!it.IsTest && it.Installed) on.Add(TEn("fixname." + it.Key));
        sb.AppendLine("Fixer options on: " + (on.Count > 0 ? string.Join(", ", on.ToArray()) : "none"));
        if (r != null && r.Probe != null)
        {
            string p = r.Probe.Outcome.ToString() + (r.Probe.GpuName != "" ? " on " + r.Probe.GpuName : "") + (r.Probe.Detail != "" ? " (" + r.Probe.Detail + ")" : "");
            if (r.ProbeNoLayers != null) p += "; without Vulkan layers: " + r.ProbeNoLayers.Outcome;
            sb.AppendLine("Graphics card test: " + p);
            if (r.Layers != null && r.Layers.Count > 0) sb.AppendLine("Vulkan layers: " + string.Join(", ", r.Layers.ToArray()));
        }
        if (r != null && (r.BenchGpu != null || r.BenchCpu != null))
        {
            sb.AppendLine("Speed test (" + (s.CallLanguage ?? "en") + ", 4 s sentence): graphics card " + ReportBench(r.BenchGpu) + ", processor " + ReportBench(r.BenchCpu));
        }
        sb.AppendLine("Post this in Tiger's fixer thread: " + ModThreadUrl);
        return sb.ToString();
    }

    static string ReportBench(BenchResult b)
    {
        if (b == null) return "not tested";
        if (b.Outcome != BenchOutcome.Ok) return b.Outcome.ToString().ToLowerInvariant() + (b.Detail != "" ? " (" + b.Detail + ")" : "");
        string t = Secs(b.RunMs) + " s (load " + Secs(b.LoadMs) + " s";
        if (b.EncodeMs >= 50) t += ", encoder " + Secs(b.EncodeMs) + " s";   // on graphics cards whisper.cpp's encoder time can read ~0
        return t + ")";
    }
}
