// EXPERIMENTAL: bigger speech model for languages other than English.
//
// The game recognises every language except English with Whisper "base" (StreamingAssets\Whisper\ggml-base.bin),
// which makes many mistakes outside English ("Prefeito" for Perfeito, "100 dores" for dólares). This installs
// Whisper "small" in its place: the official whisper.cpp ggml-small.bin, converted to 8-bit by this tool's own
// converter (byte-identical to whisper.cpp's quantize tool). GitHub builds that file and attaches it to the release
// (SWYF-Speech-Model-small-q8.bin, ~250 MB); the player downloads it and puts it next to this program. The tool itself
// downloads nothing.
//
// "small" needs roughly 2-3x the work per sentence, so it is only offered on PCs that can take it:
//   - the call language isn't English (English uses its own model, ggml-base.en.bin, which stays as it is)
//   - speech runs on the processor (-whisper-cpu, or a graphics card the game keeps speech off): the game's
//     non-English speech is very slow on graphics cards, and a bigger model would make that worse
//   - AVX2, at least 8 threads and 12 GB of memory
//   - and, before anything is installed, a speed test with the game's own speech engine: the bigger model must
//     transcribe a 4-second sentence on this processor in at most 2.5 s.
// The shorter listening window (speed-up) is installed with it. Undo puts the previous ggml-base.bin back.
using System;
using System.IO;
using System.Runtime.InteropServices;

static partial class Fixer
{
    const string BigModelFile = "SWYF-Speech-Model-small-q8.bin";
    const string BigModelSha256 = "49c8fb02b65e6049d5fa6c04f81f53b867b5ec9540406812c643f177317f779f";   // from the GitHub build (verify-model-conversion)
    const string BigBackupSuffix = ".bigmodel-backup";
    const int BigMaxRunMs = 2500;       // at most 2.5 s for a 4 s sentence on the processor
    const int BigMinThreads = 8, BigMinRamMb = 12000;

    static string assumeModelDir;   // test builds only: where to look for the model file
    static int assumeRamMb;

    [StructLayout(LayoutKind.Sequential)]
    sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx m);

    static int TotalRamMb()
    {
        if (assumeRamMb > 0) return assumeRamMb;
        if (IsUnix) return 0;
        try { var m = new MemoryStatusEx(); return GlobalMemoryStatusEx(m) ? (int)(m.ullTotalPhys >> 20) : 0; }
        catch { return 0; }
    }

    // Where the downloaded model file may be: next to this program, or in the Downloads folder.
    static string FindBigModelFile()
    {
        var dirs = new System.Collections.Generic.List<string>();
        if (assumeModelDir != null) dirs.Add(assumeModelDir);
        else
        {
            dirs.Add(AppDomain.CurrentDomain.BaseDirectory);
            try { dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); } catch { }
        }
        foreach (string d in dirs)
        {
            try { string p = Path.Combine(d, BigModelFile); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    // Whisper hyperparameters: n_vocab, n_audio_ctx, n_audio_state, n_audio_head, n_audio_layer, n_text_ctx,
    // n_text_state, n_text_head, n_text_layer, n_mels, ftype. null if not a whisper.cpp model.
    static int[] ReadModelHeader(string path)
    {
        try
        {
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.BaseStream.Length < 48 || r.ReadInt32() != Magic) return null;
                var h = new int[11];
                for (int i = 0; i < 11; i++) h[i] = r.ReadInt32();
                return h;
            }
        }
        catch { return null; }
    }

    // Multilingual Whisper "small" in 8-bit: 51865 tokens, 768 wide, 12 + 12 layers.
    static bool IsBigModel(string path)
    {
        int[] h = File.Exists(path) ? ReadModelHeader(path) : null;
        return h != null && h[0] == 51865 && h[2] == 768 && h[4] == 12 && h[8] == 12 && h[10] % QntFactor == FtypeQ8;
    }

    static string BigModelProblem(string file)
    {
        if (file == null) return T("big.status_download", BigModelFile);
        if (!IsBigModel(file)) return T("big.status_bad_file", BigModelFile);
        if (CheckBigHash && Sha256(file) != BigModelSha256) return T("big.status_bad_file", BigModelFile);
        return null;
    }

    // Test builds can use a random-weights stand-in (SWYF_TEST_ANY_MODEL=1); release builds always check the hash.
    static bool CheckBigHash
    {
        get
        {
#if SWYF_TEST
            if (Environment.GetEnvironmentVariable("SWYF_TEST_ANY_MODEL") == "1") return false;
#endif
            return BigModelSha256 != "";
        }
    }

    static string BigTarget(string modelDir) { return Path.Combine(modelDir, "ggml-base.bin"); }

    static void DescribeBig(Session s)
    {
        Item big = s.Big;
        big.Name = T("big.name");
        bool installed = IsBigModel(BigTarget(s.ModelDir));
        big.Installed = installed;
        bool otherLanguage = s.CallLanguage != null && s.CallLanguage != "en";
        big.ShowAlways = installed || (otherLanguage && s.HasAvx);
        string why = null;
        big.NeedCpuOption = big.NeedDownload = false;
        if (!installed)
        {
            if (!otherLanguage) why = T("big.status_english");
            else if (!s.HasAvx2 || s.Threads < BigMinThreads || (s.RamMb > 0 && s.RamMb < BigMinRamMb)) why = T("big.status_pc", BigMinThreads, BigMinRamMb / 1000);
            else if (!IsDllOriginal(s.DllHash) && !IsDllPatched(s.DllHash)) why = T("core.not_available_for_this_game");
            else
            {
                // This PC could run it: say what's still missing, with buttons in the window.
                string fileProblem = BigModelProblem(s.BigFile);
                big.NeedCpuOption = !s.CpuSpeech;
                big.NeedDownload = fileProblem != null;
                if (big.NeedCpuOption && big.NeedDownload) why = T("big.status_needs_both", BigModelFile);
                else if (big.NeedCpuOption) why = T("big.status_needs_cpu");
                else why = fileProblem;
            }
        }
        big.Available = installed || why == null;
        big.Status = installed ? T("big.status_installed") : why ?? T("big.status_off");
    }

    static void InstallBigModel(Session s)
    {
        string target = BigTarget(s.ModelDir), file = s.BigFile;
        if (IsBigModel(target)) { Line("  " + T("core.already_installed")); return; }
        string problem = BigModelProblem(file);
        if (problem != null) { Line("  " + problem); return; }

        // The listening window has to come with it, or every sentence pays for a full 30-second window.
        string h = File.Exists(s.Dll) ? Sha256(s.Dll) : null;
        if (IsDllOriginal(h)) { Out("  " + T("main.shorter_listening_window") + " "); PatchDll(s.Dll); Line(T("main.done")); }
        else if (!IsDllPatched(h)) { Line("  " + T("core.not_available_for_this_game")); return; }

        // Speed test on this processor, with the game's own engine: the game's model first, then the bigger one.
        Out("  " + T("big.testing") + " ");
        BenchResult now = File.Exists(target) ? RunSpeechBench(s.GameDir, target, s.CallLanguage, false, 120000, true) : null;
        BenchResult big = RunSpeechBench(s.GameDir, file, s.CallLanguage, false, 180000, true);
        Line(T("main.done"));
        if (big.Outcome == BenchOutcome.NoClip) { Line("  " + T("big.no_clip")); return; }
        if (big.Outcome != BenchOutcome.Ok) { Line("  " + T("big.test_failed", BenchOutcomeText(big))); return; }
        if (now != null && now.Outcome == BenchOutcome.Ok) Line("  " + T("big.result_now", Secs(now.RunMs)));
        Line("  " + T("big.result_big", Secs(big.RunMs)));
        if (big.RunMs > BigMaxRunMs) { Line("  " + T("big.too_slow", Secs(BigMaxRunMs))); return; }

        long need = new FileInfo(file).Length + (50L << 20);
        try { if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))).AvailableFreeSpace < need) { Line("  " + T("main.not_enough_free_disk_space", need >> 20)); return; } } catch { }
        Out("  " + T("big.installing") + " ");
        string tmp = target + ".bigmodel-tmp", bak = target + BigBackupSuffix;
        TryDelete(tmp);
        File.Copy(file, tmp);
        if (!IsBigModel(tmp) || (CheckBigHash && Sha256(tmp) != BigModelSha256)) { TryDelete(tmp); throw new Exception("Copying the speech model failed; nothing was changed."); }
        if (File.Exists(target))
        {
            if (File.Exists(bak)) File.Delete(target);   // the backup already holds the game's model
            else File.Move(target, bak);
        }
        File.Move(tmp, target);
        Line(T("main.done"));
        Line("  " + T("big.installed_note"));
    }

    static int UninstallBigModel(string modelDir)
    {
        string target = BigTarget(modelDir), bak = target + BigBackupSuffix;
        if (!IsBigModel(target))
        {
            TryDelete(bak);   // a game update or Verify already put the game's model back
            return 0;
        }
        if (!File.Exists(bak) || ReadModelHeader(bak) == null)
        {
            Line("  " + T("big.could_not_restore"));
            return 1;
        }
        File.Delete(target);
        File.Move(bak, target);
        Line("  " + T("big.removed"));
        return 0;
    }

    static string BenchOutcomeText(BenchResult b)
    {
        switch (b.Outcome)
        {
            case BenchOutcome.Froze: return T("core.froze");
            case BenchOutcome.Crashed: return T("core.crashed") + (b.Detail != "" ? " (" + b.Detail + ")" : "");
            case BenchOutcome.NoClip: return T("bench.no_clip_short");
            default: return T("bench.failed_short");
        }
    }
}
