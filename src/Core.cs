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
        public string GameDir, ModelDir, Dll, Launch, CallLanguage, GameBuild;
        public bool HasAvx, HasAvx2, SpeedInstalled, SpeedTodo, CpuSpeech, StrongCpu;
        public int Threads;
        public CpuFixState CpuState;
        public string PocketHash, AsmHash, SadHash;
        public Item Cpu, Speed, Voice, Qwen, Keep, Gpu;
        public Item[] Items;   // console menu order
    }

    sealed class ApplyResult
    {
        public ProbeResult Probe;          // null if the graphics card test didn't run
        public bool GpuProblem, GpuNone, HasCpuOption, KeepHint;
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
        s.SpeedInstalled = ModelsConverted(s.ModelDir) || dllHash == DllPatched;
        s.SpeedTodo = dllHash == DllOriginal;
        foreach (string n in ModelNames)
        {
            string p = Path.Combine(s.ModelDir, n);
            if (File.Exists(p) && ReadFtype(p) == FtypeF16) s.SpeedTodo = true;
        }
        s.PocketHash = File.Exists(PocketPath(gameDir)) ? Sha256(PocketPath(gameDir)) : null;
        s.AsmHash = File.Exists(GameAsmPath(gameDir)) ? Sha256(GameAsmPath(gameDir)) : null;
        s.SadHash = File.Exists(SadPath(gameDir)) ? Sha256(SadPath(gameDir)) : null;
        s.Launch = ReadLaunchOptions();
        s.CpuSpeech = GameUsesCpuSpeech(s.Launch);
        s.StrongCpu = s.HasAvx2 && s.Threads >= 8;
        s.CallLanguage = ReadPrefString("Kolkata.CallLanguage");
        if (s.AsmHash == AsmOriginal || s.AsmHash == AsmPatched || s.SadHash == SadOriginal || s.SadHash == SadPatched) s.GameBuild = "0.1.74";
        else
        {
            string sh = File.Exists(ScriptsPath(gameDir)) ? Sha256(ScriptsPath(gameDir)) : null;
            if (sh == ScriptsOriginal || sh == ScriptsPatched) s.GameBuild = "0.1.71";
        }

        s.Cpu = new Item { Key = "cpu" };
        s.Speed = new Item { Key = "speed" };
        s.Voice = new Item { Key = "voice" };
        s.Qwen = new Item { Key = "qwen", Experimental = true, Untested = true };
        s.Keep = new Item { Key = "keep", Untested = true };
        s.Gpu = new Item { Key = "gpu", IsTest = true };
        s.Items = new[] { s.Cpu, s.Speed, s.Voice, s.Qwen, s.Keep, s.Gpu };
        Describe(s);

        foreach (Item it in s.Items) it.Wanted = it.Available;      // recommended: everything that applies
        s.Qwen.Wanted = s.Qwen.Installed;                             // experimental: only when the player ticks it
        s.Keep.Wanted = s.Keep.Installed || (s.Keep.Available && s.CpuSpeech && s.StrongCpu);
        s.Cpu.Recommended = s.Cpu.Available && !s.Cpu.Installed;
        s.Speed.Recommended = s.Speed.Available && s.SpeedTodo;
        s.Voice.Recommended = s.Voice.Available && !s.Voice.Installed;
        s.Keep.Recommended = s.Keep.Available && !s.Keep.Installed && s.CpuSpeech && s.StrongCpu;
        return s;
    }

    // What the caller voice fix does, for the player's call language (game setting "Kolkata.CallLanguage").
    // Hook for later: per-language descriptions once more languages are fixed. For now always the generic text.
    static string VoiceFixDescription(string callLanguage)
    {
        return T("core.not_installed_stops_portuguese_callers");
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
        voice.Installed = s.PocketHash == PocketPatched;
        voice.Available = s.PocketHash == PocketPatched || s.PocketHash == PocketOriginal;
        voice.Status = voice.Installed ? T("core.installed")
            : voice.Available ? VoiceFixDescription(s.CallLanguage)
            : T("core.not_available_for_this_game_2");

        qwen.Name = T("core.qwen3_tts_on_8_gb");
        qwen.Installed = s.AsmHash == AsmPatched;
        qwen.Available = s.AsmHash == AsmPatched || s.AsmHash == AsmOriginal;
        qwen.Status = qwen.Installed ? T("core.installed_untick_to_remove_if")
            : qwen.Available ? T("core.off_unlocks_qwen3_voices_on")
            : T("core.not_available_for_this_game");

        keep.Name = T("core.better_english_speech_recognition_whisper");
        keep.Installed = s.SadHash == SadPatched;
        keep.Available = hasAvx && (s.SadHash == SadPatched || s.SadHash == SadOriginal);
        keep.NotNeeded = !s.CpuSpeech;
        keep.Status = keep.Installed ? T("core.installed_english_speech_uses_more")
            : !keep.Available ? T("core.not_available_for_this_game")
            : s.CpuSpeech && s.StrongCpu ? T("core.not_installed_recommended_on_your")
            : s.CpuSpeech ? T("core.not_installed_more_accurate_english")
            : T("core.not_needed_now_only_matters");

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
        foreach (Item it in s.Items) if (StepWanted(it, onlyChanges)) r.Steps++;
        int step = 0;
        foreach (Item it in s.Items)
        {
            if (!StepWanted(it, onlyChanges)) continue;
            step++;
            if (Sink == null) Header(step, r.Steps, it.IsTest ? it.Name + (" " + T("core.takes_few_seconds")) : it.Name);
            else Line("[" + step + "/" + r.Steps + "] " + it.Name);
            if (OnStepBegin != null) OnStepBegin(it);
            bool ok = RunStep(s, it, r);
            if (OnStepEnd != null) OnStepEnd(it, ok);
        }
        return r;
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
            case "gpu":
                return RunGpuTest(s, r);
        }
        return true;
    }

    static bool RunGpuTest(Session s, ApplyResult r)
    {
        ProbeResult probe = RunVulkanProbe(s.GameDir, simulateProbe);
        r.Probe = probe;
        r.Launch = ReadLaunchOptions();
        r.HasCpuOption = r.Launch != null && Regex.IsMatch(r.Launch, @"(^|\s)-whisper-cpu(\s|$)", RegexOptions.IgnoreCase);
        r.GpuProblem = probe.Outcome == ProbeOutcome.Crashed || probe.Outcome == ProbeOutcome.Hung;
        r.GpuNone = probe.Outcome == ProbeOutcome.NoGpu;
        switch (probe.Outcome)
        {
            case ProbeOutcome.Passed:
                Line("  " + T("core.passed_on_speech_can_use", probe.GpuName));
                if (r.HasCpuOption)
                    Line("  " + T("core.note_you_have_whisper_cpu"));
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
}
