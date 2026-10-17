// Vulkan speech test: a child process loads the game's own ggml.dll (the developer's build, which loads
// ggml-vulkan.dll unless KOLKATA_WHISPER_CPU_ONLY=1), enumerates devices and initializes each GPU device -
// the same steps the game takes when speech starts. If that crashes or hangs, only the child dies.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static partial class Fixer
{
    enum ProbeOutcome { Passed, NoGpu, Crashed, Hung, LoadFailed }

    sealed class ProbeResult
    {
        public ProbeOutcome Outcome;
        public string Detail = "";
        public string GpuName = "";
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr DevCountFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DevGetFn(UIntPtr i);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DevTypeFn(IntPtr dev);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DevStrFn(IntPtr dev);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DevInitFn(IntPtr dev, IntPtr param);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeFn(IntPtr backend);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetDllDirectoryW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
#if SWYF_TEST
    // Lets the developer test suite run the probe on Linux (mono + a Linux build of ggml). Not in release builds.
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] static extern IntPtr dlopen(string path, int flags);
    [DllImport("libdl.so.2", EntryPoint = "dlsym")] static extern IntPtr dlsym(IntPtr h, string name);
    static bool IsUnix { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }
#else
    static bool IsUnix { get { return false; } }
#endif

    static IntPtr LoadLib(string dir, string winName, string unixName)
    {
#if SWYF_TEST
        if (IsUnix) return dlopen(Path.Combine(dir, unixName), 2 | 0x100);
#endif
        return LoadLibraryExW(Path.Combine(dir, winName), IntPtr.Zero, 0x00000008); // LOAD_WITH_ALTERED_SEARCH_PATH
    }

    static T Fn<T>(IntPtr lib, string name) where T : class
    {
#if SWYF_TEST
        IntPtr p = IsUnix ? dlsym(lib, name) : GetProcAddress(lib, name);
#else
        IntPtr p = GetProcAddress(lib, name);
#endif
        if (p == IntPtr.Zero) throw new EntryPointNotFoundException(name);
        return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }

    static string Str(IntPtr p) { return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p); }

    // Child process entry: prints STAGE/DEV/GPUOK lines; exit 0 = a GPU initialized, 2 = no GPU device, 3 = load failure.
    static int ProbeMain(string pluginDir, string simulate)
    {
        if (!IsUnix) SetErrorMode(0x0001 | 0x0002 | 0x8000); // no crash dialogs
        Environment.SetEnvironmentVariable("KOLKATA_WHISPER_CPU_ONLY", null);
#if SWYF_TEST
        if (simulate == "crash") { Console.WriteLine("STAGE registry"); Console.Out.Flush(); Environment.FailFast("simulated crash"); }
        if (simulate == "hang") { Console.WriteLine("STAGE registry"); Console.Out.Flush(); System.Threading.Thread.Sleep(-1); }
#endif
        if (!IsUnix) SetDllDirectoryW(pluginDir);
        IntPtr b = LoadLib(pluginDir, "ggml-base.dll", "libggml-base.so");
        IntPtr g = LoadLib(pluginDir, "ggml.dll", "libggml.so");
        if (b == IntPtr.Zero || g == IntPtr.Zero)
        {
            Console.WriteLine("LOADFAIL " + Marshal.GetLastWin32Error());
            return 3;
        }
        var devCount = Fn<DevCountFn>(g, "ggml_backend_dev_count");
        var devGet = Fn<DevGetFn>(g, "ggml_backend_dev_get");
        var devType = Fn<DevTypeFn>(b, "ggml_backend_dev_type");
        var devName = Fn<DevStrFn>(b, "ggml_backend_dev_name");
        var devDesc = Fn<DevStrFn>(b, "ggml_backend_dev_description");
        var devInit = Fn<DevInitFn>(b, "ggml_backend_dev_init");
        var free = Fn<FreeFn>(b, "ggml_backend_free");

        Console.WriteLine("STAGE registry"); Console.Out.Flush();
        ulong n = (ulong)devCount();
        bool gpuOk = false;
        for (ulong i = 0; i < n; i++)
        {
            IntPtr dev = devGet((UIntPtr)i);
            int type = devType(dev);
            string desc = Str(devDesc(dev));
            Console.WriteLine("DEV " + type + " " + Str(devName(dev)) + " | " + desc); Console.Out.Flush();
            if (type != 1) continue; // GGML_BACKEND_DEVICE_TYPE_GPU
            Console.WriteLine("STAGE init " + desc); Console.Out.Flush();
            IntPtr backend = devInit(dev, IntPtr.Zero);
            if (backend == IntPtr.Zero) { Console.WriteLine("INITFAIL " + desc); continue; }
            free(backend);
            Console.WriteLine("GPUOK " + desc); Console.Out.Flush();
            gpuOk = true;
        }
        return gpuOk ? 0 : 2;
    }

    static ProbeResult RunVulkanProbe(string gameDir, string simulate)
    {
        var r = new ProbeResult();
        string pluginDir = Path.Combine(gameDir, PluginRel);
        string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(), // never leave crash files in the game folder
        };
        string args = "--vulkan-probe \"" + pluginDir + "\"";
#if SWYF_TEST
        if (simulate != null) args += " --simulate=" + simulate;
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
            bool exited = p.WaitForExit(30000);
            if (!exited) { try { p.Kill(); } catch { } p.WaitForExit(5000); }
            else p.WaitForExit(); // flush async output
            string text; lock (output) text = output.ToString();
            string lastStage = "", gpu = "";
            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim();
                if (l.StartsWith("STAGE ")) lastStage = l.Substring(6);
                if (l.StartsWith("GPUOK ") && gpu == "") gpu = l.Substring(6);
            }
            r.GpuName = gpu;
            string step = lastStage == "registry" ? T("probe.loading_vulkan") : lastStage.StartsWith("init ") ? (T("probe.starting") + " ") + lastStage.Substring(5) : T("probe.starting_test");
            if (!exited) { r.Outcome = ProbeOutcome.Hung; r.Detail = (T("probe.step") + " ") + step; }
            else if (p.ExitCode == 0) r.Outcome = ProbeOutcome.Passed;
            else if (p.ExitCode == 2) r.Outcome = ProbeOutcome.NoGpu;
            else if (p.ExitCode == 3) { r.Outcome = ProbeOutcome.LoadFailed; r.Detail = text.Trim(); }
            else { r.Outcome = ProbeOutcome.Crashed; r.Detail = (T("probe.step") + " ") + step + T("probe.code_0x") + p.ExitCode.ToString("X"); }
        }
        return r;
    }
}
