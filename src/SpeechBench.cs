// Speech speed test. A child process (--speech-bench) loads the game's own speech engine (libwhisper.dll and ggml from
// Plugins\x86_64), loads a speech model the way the game does (from memory, whisper_init_from_buffer_with_params) and
// transcribes a 4-second piece of one of the game's own caller voice clips, twice, in the player's language.
// It prints how long loading and each transcription took. If the engine crashes or freezes, only the child dies.
// Used for the speech speed test, and to check that a PC is fast enough before installing the bigger speech model.
// Nothing on the PC is changed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static partial class Fixer
{
    // whisper.cpp v1.7.5 structs (the version the game ships), same layout as in the game's com.whisper.unity.dll.
    [StructLayout(LayoutKind.Sequential)]
    struct WContextParams
    {
        [MarshalAs(UnmanagedType.U1)] public bool use_gpu;
        [MarshalAs(UnmanagedType.U1)] public bool flash_attn;
        public int gpu_device;
        [MarshalAs(UnmanagedType.U1)] public bool dtw_token_timestamps;
        public int dtw_aheads_preset;
        public int dtw_n_top;
        public UIntPtr dtw_aheads_n_heads;
        public IntPtr dtw_aheads_heads;
        public UIntPtr dtw_mem_size;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WFullParams
    {
        public int strategy, n_threads, n_max_text_ctx, offset_ms, duration_ms;
        [MarshalAs(UnmanagedType.U1)] public bool translate;
        [MarshalAs(UnmanagedType.U1)] public bool no_context;
        [MarshalAs(UnmanagedType.U1)] public bool no_timestamps;
        [MarshalAs(UnmanagedType.U1)] public bool single_segment;
        [MarshalAs(UnmanagedType.U1)] public bool print_special;
        [MarshalAs(UnmanagedType.U1)] public bool print_progress;
        [MarshalAs(UnmanagedType.U1)] public bool print_realtime;
        [MarshalAs(UnmanagedType.U1)] public bool print_timestamps;
        [MarshalAs(UnmanagedType.U1)] public bool token_timestamps;
        public float thold_pt, thold_ptsum;
        public int max_len;
        [MarshalAs(UnmanagedType.U1)] public bool split_on_word;
        public int max_tokens;
        [MarshalAs(UnmanagedType.U1)] public bool debug_mode;
        public int audio_ctx;
        [MarshalAs(UnmanagedType.U1)] public bool tdrz_enable;
        public IntPtr suppress_regex, initial_prompt, prompt_tokens;
        public int prompt_n_tokens;
        public IntPtr language;
        [MarshalAs(UnmanagedType.U1)] public bool detect_language;
        [MarshalAs(UnmanagedType.U1)] public bool suppress_blank;
        [MarshalAs(UnmanagedType.U1)] public bool suppress_nst;
        public float temperature, max_initial_ts, length_penalty, temperature_inc, entropy_thold, logprob_thold, no_speech_thold;
        public int greedy_best_of;
        public int beam_size;
        public float beam_patience;
        public IntPtr new_segment_callback, new_segment_callback_user_data, progress_callback, progress_callback_user_data,
            encoder_begin_callback, encoder_begin_callback_user_data, abort_callback, abort_callback_user_data,
            logits_filter_callback, logits_filter_callback_user_data, grammar_rules;
        public UIntPtr n_grammar_rules, i_start_rule;
        public float grammar_penalty;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CtxDefaultsFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FullDefaultsFn(int strategy);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr InitFn(IntPtr buffer, UIntPtr size, WContextParams p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FullFn(IntPtr ctx, WFullParams p, IntPtr samples, int n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NSegFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr SegTextFn(IntPtr ctx, int i);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeCtxFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr TimingsFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void LogFn(int level, IntPtr text, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void LogSetFn(LogFn cb, IntPtr user);
    static readonly LogFn QuietLog = delegate { };   // static, so it stays alive for the whole child process

    const int BenchSeconds = 4;   // a typical sentence

    // Child entry. Prints STAGE / LOADMS / RUNMS / SPLITMS / TEXT lines. Exit 0 = ok, 3 = engine didn't load, 5 = model
    // didn't load, 6 = transcription failed, 7 = no usable voice clip.
    // capped: limit decoding to 64 tokens and no temperature retries, i.e. the cost of a normal sentence. Uncapped =
    // exactly the game's settings, including its retries (used by the speed test, which looks for problems).
    static int BenchMain(string pluginDir, string modelPath, string wavPath, string lang, bool gpu, int audioCtx, bool capped, string simulate)
    {
        if (!IsUnix) SetErrorMode(0x0001 | 0x0002 | 0x8000); // no crash dialogs
#if SWYF_TEST
        // fake timings for the test suite: "<gpu ms>/<cpu ms>", "hang" (GPU run freezes)
        if (simulate != null)
        {
            string[] p = simulate.Split('/');
            string ms = gpu ? p[0] : p[p.Length - 1];
            if (ms == "hang") { Console.WriteLine("STAGE load"); Console.Out.Flush(); System.Threading.Thread.Sleep(-1); }
            Console.WriteLine("LOADMS 800"); Console.WriteLine("RUNMS " + ms); Console.WriteLine("RUNMS " + ms); Console.WriteLine("TEXT " + Convert.ToBase64String(Encoding.UTF8.GetBytes("simulated")));
            return 0;
        }
#endif
        // The developers' ggml build skips its Vulkan backend when this is set, like the game does with -whisper-cpu.
        Environment.SetEnvironmentVariable("KOLKATA_WHISPER_CPU_ONLY", gpu ? null : "1");
        float[] pcm = ReadVoiceClip(wavPath, BenchSeconds);
        if (pcm == null) { Console.WriteLine("NOCLIP"); return 7; }

        if (!IsUnix) SetDllDirectoryW(pluginDir);
        IntPtr b = LoadLib(pluginDir, "ggml-base.dll", "libggml-base.so");
        IntPtr g = LoadLib(pluginDir, "ggml.dll", "libggml.so");
        IntPtr w = LoadLib(pluginDir, "libwhisper.dll", "libwhisper.so");
        if (b == IntPtr.Zero || g == IntPtr.Zero || w == IntPtr.Zero) { Console.WriteLine("LOADFAIL " + Marshal.GetLastWin32Error()); return 3; }
        try { Fn<LogSetFn>(w, "whisper_log_set")(QuietLog, IntPtr.Zero); } catch { }
        var ctxDefaults = Fn<CtxDefaultsFn>(w, "whisper_context_default_params_by_ref");
        var fullDefaults = Fn<FullDefaultsFn>(w, "whisper_full_default_params_by_ref");
        var init = Fn<InitFn>(w, "whisper_init_from_buffer_with_params");
        var full = Fn<FullFn>(w, "whisper_full");
        var nSeg = Fn<NSegFn>(w, "whisper_full_n_segments");
        var segText = Fn<SegTextFn>(w, "whisper_full_get_segment_text");
        var freeCtx = Fn<FreeCtxFn>(w, "whisper_free");
        TimingsFn timings = null; FreeCtxFn resetTimings = null;
        try { timings = Fn<TimingsFn>(w, "whisper_get_timings"); resetTimings = Fn<FreeCtxFn>(w, "whisper_reset_timings"); } catch { }

        var cp = (WContextParams)Marshal.PtrToStructure(ctxDefaults(), typeof(WContextParams));
        cp.use_gpu = gpu;
        cp.flash_attn = false;

        byte[] model = File.ReadAllBytes(modelPath);
        int modelLen = model.Length;
        IntPtr buf = Marshal.AllocHGlobal(modelLen);
        Marshal.Copy(model, 0, buf, modelLen);
        model = null;
        Console.WriteLine("STAGE load"); Console.Out.Flush();
        var sw = Stopwatch.StartNew();
        IntPtr ctx = init(buf, (UIntPtr)(ulong)modelLen, cp);
        Marshal.FreeHGlobal(buf);
        if (ctx == IntPtr.Zero) { Console.WriteLine("MODELFAIL"); return 5; }
        Console.WriteLine("LOADMS " + sw.ElapsedMilliseconds); Console.Out.Flush();

        // The game's settings: greedy decoding, no context from earlier lines, its call language, default threads.
        var fp = (WFullParams)Marshal.PtrToStructure(fullDefaults(0), typeof(WFullParams));
        IntPtr langPtr = Marshal.StringToHGlobalAnsi(lang);
        fp.language = langPtr;
        fp.detect_language = false;
        fp.translate = false;
        fp.no_context = true;
        fp.print_progress = false; fp.print_realtime = false; fp.print_special = false; fp.print_timestamps = false;
        fp.audio_ctx = audioCtx;
        if (capped) { fp.max_tokens = 64; fp.temperature_inc = 0f; }
        IntPtr samples = Marshal.AllocHGlobal(pcm.Length * 4);
        Marshal.Copy(pcm, 0, samples, pcm.Length);
        for (int run = 0; run < 2; run++)
        {
            Console.WriteLine("STAGE run"); Console.Out.Flush();
            if (resetTimings != null) resetTimings(ctx);
            sw.Reset(); sw.Start();
            int rc = full(ctx, fp, samples, pcm.Length);
            if (rc != 0) { Console.WriteLine("RUNFAIL " + rc); return 6; }
            long ms = sw.ElapsedMilliseconds;
            // whisper_timings: sample_ms, encode_ms, decode_ms, batchd_ms, prompt_ms (floats)
            IntPtr t = timings != null ? timings(ctx) : IntPtr.Zero;
            if (t != IntPtr.Zero)
            {
                float[] f = new float[5];
                Marshal.Copy(t, f, 0, 5);
                Console.WriteLine("SPLITMS " + ms + " " + (int)f[1] + " " + (int)(f[0] + f[2] + f[3] + f[4]));
            }
            Console.WriteLine("RUNMS " + ms); Console.Out.Flush();
        }
        var text = new StringBuilder();
        int n = nSeg(ctx);
        for (int i = 0; i < n; i++) text.Append(Utf8(segText(ctx, i)));
        // Base64, so no console code page is involved (setting Console.OutputEncoding throws without a console window).
        Console.WriteLine("TEXT " + Convert.ToBase64String(Encoding.UTF8.GetBytes(text.ToString().Trim())));
        freeCtx(ctx);
        Marshal.FreeHGlobal(samples);
        Marshal.FreeHGlobal(langPtr);
        return 0;
    }

    static string Utf8(IntPtr p)
    {
        if (p == IntPtr.Zero) return "";
        int len = 0;
        while (Marshal.ReadByte(p, len) != 0) len++;
        byte[] b = new byte[len];
        Marshal.Copy(p, b, 0, len);
        return Encoding.UTF8.GetString(b);
    }

    // One of the game's own caller voice clips (StreamingAssets\Callers\Defaults\<id>\voice.wav). null if none.
    static string FindVoiceClip(string gameDir)
    {
        string root = Path.Combine(Path.Combine(Path.Combine(gameDir, "Scam With Your Friends_Data"), "StreamingAssets"), Path.Combine("Callers", "Defaults"));
        try
        {
            if (!Directory.Exists(root)) return null;
            var dirs = new List<string>(Directory.GetDirectories(root));
            dirs.Sort(StringComparer.Ordinal);
            foreach (string d in dirs)
            {
                string f = Path.Combine(d, "voice.wav");
                if (File.Exists(f) && ReadVoiceClip(f, BenchSeconds) != null) return f;
            }
        }
        catch { }
        return null;
    }

    // Reads a PCM WAV file (16-bit or 32-bit float, any rate, mono or stereo) and returns `seconds` of 16 kHz mono,
    // starting 0.5 s in. null if it isn't a usable WAV or is too short.
    static float[] ReadVoiceClip(string path, int seconds)
    {
        try
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 44 || Encoding.ASCII.GetString(d, 0, 4) != "RIFF" || Encoding.ASCII.GetString(d, 8, 4) != "WAVE") return null;
            int fmt = 0, ch = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
            for (int i = 12; i + 8 <= d.Length; )
            {
                string id = Encoding.ASCII.GetString(d, i, 4);
                int len = BitConverter.ToInt32(d, i + 4);
                if (len < 0) return null;
                if (id == "fmt ")
                {
                    fmt = BitConverter.ToInt16(d, i + 8); ch = BitConverter.ToInt16(d, i + 10);
                    rate = BitConverter.ToInt32(d, i + 12); bits = BitConverter.ToInt16(d, i + 22);
                }
                else if (id == "data") { dataAt = i + 8; dataLen = Math.Min(len, d.Length - dataAt); break; }
                i += 8 + len + (len & 1);
            }
            bool pcm16 = fmt == 1 && bits == 16, f32 = fmt == 3 && bits == 32;
            if (dataAt < 0 || ch < 1 || rate < 8000 || (!pcm16 && !f32)) return null;
            int frameBytes = ch * bits / 8;
            int frames = dataLen / frameBytes;
            double start = 0.5 * rate;
            int outN = seconds * 16000;
            if (start + (double)outN * rate / 16000 >= frames) return null;
            var o = new float[outN];
            for (int k = 0; k < outN; k++)
            {
                double pos = start + (double)k * rate / 16000;
                int i0 = (int)pos;
                double t = pos - i0;
                o[k] = (float)((1 - t) * Sample(d, dataAt, i0, ch, pcm16) + t * Sample(d, dataAt, i0 + 1, ch, pcm16));
            }
            return o;
        }
        catch { return null; }
    }

    static double Sample(byte[] d, int at, int frame, int ch, bool pcm16)
    {
        double s = 0;
        for (int c = 0; c < ch; c++)
        {
            if (pcm16) s += BitConverter.ToInt16(d, at + (frame * ch + c) * 2) / 32768.0;
            else s += BitConverter.ToSingle(d, at + (frame * ch + c) * 4);
        }
        return s / ch;
    }

    enum BenchOutcome { Ok, Froze, Crashed, Failed, NoClip }

    sealed class BenchResult
    {
        public BenchOutcome Outcome;
        public int LoadMs = -1, RunMs = -1;   // RunMs = the faster of the two runs (the first one warms up)
        public int EncodeMs = -1, DecodeMs = -1;   // that faster run: encoder (whisper.cpp's own timing) and everything else
        public string Text = "", Detail = "";
    }

    static string simulateBench;   // test builds only

    static BenchResult RunSpeechBench(string gameDir, string modelPath, string lang, bool gpu, int timeoutMs, bool capped)
    {
        var r = new BenchResult();
        string clip = FindVoiceClip(gameDir);
#if SWYF_TEST
        if (clip == null && simulateBench != null) clip = "simulated";
#endif
        if (clip == null) { r.Outcome = BenchOutcome.NoClip; return r; }
        string pluginDir = Path.Combine(gameDir, PluginRel);
        string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
            StandardOutputEncoding = Encoding.UTF8,
        };
        int audioCtx = IsDllPatched(File.Exists(Path.Combine(gameDir, WhisperDllRel)) ? Sha256(Path.Combine(gameDir, WhisperDllRel)) : null)
            ? Math.Max(512, BenchSeconds * 16000 / 320 + 75) : 0;   // what the game uses for a 4 s line
        string args = "--speech-bench \"" + pluginDir + "\" \"" + modelPath + "\" \"" + clip + "\" " + lang + " " + (gpu ? "1" : "0") + " " + audioCtx + " " + (capped ? "1" : "0");
#if SWYF_TEST
        if (simulateBench != null) args += " --simulate=" + simulateBench;
#endif
        if (IsUnix) { psi.FileName = "mono"; psi.Arguments = "\"" + self + "\" " + args; }
        else { psi.FileName = self; psi.Arguments = args; }
        psi.EnvironmentVariables.Remove("KOLKATA_WHISPER_CPU_ONLY");

        var output = new StringBuilder();
        using (var p = new Process { StartInfo = psi })
        {
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            bool exited = p.WaitForExit(timeoutMs);
            if (!exited) { try { p.Kill(); } catch { } p.WaitForExit(5000); }
            else p.WaitForExit();
            string text; lock (output) text = output.ToString();
            string stage = "";
            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim();
                int v;
                if (l.StartsWith("STAGE ")) stage = l.Substring(6);
                else if (l.StartsWith("LOADMS ") && int.TryParse(l.Substring(7), out v)) r.LoadMs = v;
                else if (l.StartsWith("RUNMS ") && int.TryParse(l.Substring(6), out v)) r.RunMs = r.RunMs < 0 ? v : Math.Min(r.RunMs, v);
                else if (l.StartsWith("SPLITMS "))
                {
                    string[] f = l.Substring(8).Split(' ');
                    int total, enc, dec;
                    if (f.Length == 3 && int.TryParse(f[0], out total) && int.TryParse(f[1], out enc) && int.TryParse(f[2], out dec)
                        && (r.RunMs < 0 || total <= r.RunMs)) { r.EncodeMs = enc; r.DecodeMs = Math.Max(0, total - enc); }
                }
                else if (l.StartsWith("TEXT ")) { try { r.Text = Encoding.UTF8.GetString(Convert.FromBase64String(l.Substring(5))); } catch { } }
                else if (l.StartsWith("BENCHERROR ")) r.Detail = l.Substring(11);
            }
            if (!exited) { r.Outcome = BenchOutcome.Froze; r.Detail = stage; }
            else if (r.RunMs >= 0) r.Outcome = BenchOutcome.Ok;   // timings came through; a problem while closing doesn't matter
            else if (p.ExitCode == 7) r.Outcome = BenchOutcome.NoClip;
            else if (p.ExitCode == 3 || p.ExitCode == 4 || p.ExitCode == 5 || p.ExitCode == 6)
            {
                r.Outcome = BenchOutcome.Failed;   // 4 = an error in the test program itself (BENCHERROR line)
                if (r.Detail == "") r.Detail = LastLine(text);
            }
            else { r.Outcome = BenchOutcome.Crashed; r.Detail = "0x" + p.ExitCode.ToString("X") + (stage != "" ? ", " + stage : ""); }
        }
        return r;
    }

    static string BenchModelName(Session s) { return (s.CallLanguage ?? "en") == "en" ? "ggml-base.en.bin" : "ggml-base.bin"; }

    static string LanguageLabel(string code)
    {
        switch (code)
        {
            case "en": return "English"; case "pt": return "Português"; case "de": return "Deutsch"; case "es": return "Español";
            case "fr": return "Français"; case "it": return "Italiano"; case "ru": return "Русский"; case "pl": return "Polski";
            case "tr": return "Türkçe"; case "ko": return "한국어"; case "ja": return "日本語"; case "zh": return "中文";
            case "ar": return "العربية"; case "hi": return "हिन्दी"; case "cs": return "Čeština"; case "hu": return "Magyar";
            case "sv": return "Svenska"; case "da": return "Dansk"; case "el": return "Ελληνικά"; case "fa": return "فارسی";
            case "id": return "Bahasa Indonesia"; case "et": return "Eesti";
        }
        return code;
    }

    // The speech speed test step: the player's language on the graphics card (if the game would use it) and on the
    // processor, then advice from the measured times. Changes no files.
    static bool RunBenchStep(Session s, ApplyResult r)
    {
        string lang = s.CallLanguage ?? "en";
        string model = Path.Combine(s.ModelDir, BenchModelName(s));
        var lines = new List<string>();
        r.BenchLines = lines;
        Action<string> say = delegate (string t) { lines.Add(t); Line("  " + t); };

        // The game never uses the graphics card for speech when it's on its list, or when the graphics card test failed.
        bool gpuListed = s.CpuSpeech && !HasCpuOption(s.Launch);
        bool skipGpu = gpuListed || r.GpuProblem || r.GpuNone;
        if (!skipGpu)
        {
            Out("  " + T("bench.testing_gpu", LanguageLabel(lang)) + " ");
            r.BenchGpu = RunSpeechBench(s.GameDir, model, lang, true, 60000, false);
            Line(T("main.done"));
            if (r.BenchGpu.Outcome == BenchOutcome.NoClip) { say(T("bench.no_clip")); return false; }
            say(r.BenchGpu.Outcome == BenchOutcome.Ok
                ? T("bench.result_gpu", Secs(r.BenchGpu.RunMs), Secs(r.BenchGpu.LoadMs))
                : T("bench.result_gpu_failed", BenchOutcomeText(r.BenchGpu)));
        }
        Out("  " + T("bench.testing_cpu", LanguageLabel(lang)) + " ");
        r.BenchCpu = RunSpeechBench(s.GameDir, model, lang, false, 60000, false);
        Line(T("main.done"));
        if (r.BenchCpu.Outcome == BenchOutcome.NoClip) { say(T("bench.no_clip")); return false; }
        say(r.BenchCpu.Outcome == BenchOutcome.Ok
            ? T("bench.result_cpu", Secs(r.BenchCpu.RunMs), Secs(r.BenchCpu.LoadMs))
            : T("bench.result_cpu_failed", BenchOutcomeText(r.BenchCpu)));

        BenchResult g = r.BenchGpu, c = r.BenchCpu;
        bool cpuOk = c.Outcome == BenchOutcome.Ok;
        bool gpuBad = g != null && (g.Outcome != BenchOutcome.Ok || (cpuOk && g.RunMs > 2 * c.RunMs && g.RunMs > 3000));
        bool gpuFaster = g != null && g.Outcome == BenchOutcome.Ok && cpuOk && g.RunMs * 3 < c.RunMs * 2;
        if (gpuBad && cpuOk)
        {
            say(T("bench.verdict_cpu"));
            if (r.Reason == CpuReason.None || r.Reason == CpuReason.Language) r.Reason = CpuReason.Bench;
        }
        else if (gpuFaster)
        {
            say(HasCpuOption(r.Launch) && CpuOptionHelps(s) != CpuReason.AmdNvidia ? T("bench.verdict_gpu_remove") : T("bench.verdict_gpu"));
            if (r.Reason == CpuReason.Language) r.Reason = CpuReason.None;   // measured: the graphics card is fine here
        }
        else if (g == null && cpuOk) say(gpuListed ? T("bench.verdict_cpu_only_listed") : T("bench.verdict_cpu_only"));
        if (cpuOk && c.RunMs > 4000) say(T("bench.verdict_slow_cpu"));
        say(T("bench.screenshot"));
        return cpuOk;
    }

    static string LastLine(string text)
    {
        string[] lines = text.Trim().Split('\n');
        return lines.Length == 0 ? "" : lines[lines.Length - 1].Trim();
    }

    static string Secs(int ms) { return (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }
}
