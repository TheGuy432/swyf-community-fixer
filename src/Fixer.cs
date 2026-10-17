// SWYF Speech Fixer v4 (UNOFFICIAL) - for Scam With Your Friends
// One tool for the community speech fixes:
//   1. Old CPU fix (CPUs without AVX2, game build 0.1.71)
//   2. Speech speed-up (8-bit speech model + listening window sized to the phrase)
//   3. Vulkan speech test - tells the player to add -whisper-cpu if their graphics card crashes it
//   4. Advice for lower-end PCs (BanditCam watermark, FPS cap)
// Run again to re-check or to undo everything.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

static partial class Fixer
{
    const string Version = "4";
    const string AppId = "4954910";
    const string GameFolderName = "Scam With Your Friends";
    const string GameExe = "Scam With Your Friends.exe";

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool IsProcessorFeaturePresent(uint feature);

    static string assumeCpu, simulateProbe;

    static int Main(string[] args)
    {
        string gameDir = null;
        bool uninstallFlag = false, noPause = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--vulkan-probe" && i + 1 < args.Length)
            {
                string dir = args[++i], sim = null;
                foreach (string x in args) if (x.StartsWith("--simulate=")) sim = x.Substring(11);
                try { return ProbeMain(dir, sim); }
                catch (Exception e) { Console.WriteLine("PROBEERROR " + e.Message); return 4; }
            }
            if (a == "--convert" && i + 2 < args.Length) // test hook
            {
                try { Quantize(args[i + 1], args[i + 2]); Console.WriteLine("converted"); return 0; }
                catch (Exception e) { Console.WriteLine("error: " + e.Message); return 1; }
            }
            if (a.StartsWith("--assume-cpu=")) assumeCpu = a.Substring(13);
            else if (a.StartsWith("--simulate-probe=")) simulateProbe = a.Substring(17);
            else if (a == "--uninstall") uninstallFlag = true;
            else if (a == "--no-pause") noPause = true;
            else gameDir = a.Trim(new[] { '"' });
        }

        try { Console.Title = "SWYF Speech Fixer v" + Version; } catch { }
        Line("==============================================================");
        Line(" SWYF Speech Fixer v" + Version + " (unofficial)");
        Line(" Fixes and speeds up speech-to-text in Scam With Your Friends.");
        Line(" Not made or supported by the game's developers.");
        Line("==============================================================");
        Line("");
        int code;
        try { code = Run(gameDir, uninstallFlag); }
        catch (Exception e) { Line(""); Line("Something went wrong: " + e.Message); Line("Your original files were not removed."); code = 1; }
        if (!noPause) { Line(""); Console.Write("Press Enter to close..."); Console.ReadLine(); }
        return code;
    }

    static int Run(string gameDir, bool uninstallFlag)
    {
        gameDir = gameDir ?? FindGameDir();
        if (gameDir == null)
        {
            Line("Could not find the game folder automatically.");
            Line("Paste the game folder path (Steam > right-click game > Manage > Browse local files), then press Enter:");
            Console.Write("> ");
            gameDir = (Console.ReadLine() ?? "").Trim().Trim(new[] { '"' });
        }
        if (!File.Exists(Path.Combine(gameDir, GameExe)))
        {
            Line("That doesn't look like the game folder (no \"" + GameExe + "\" there). Nothing was changed.");
            return 1;
        }
        Line("Game folder: " + gameDir);
        if (Process.GetProcessesByName("Scam With Your Friends").Length > 0)
        {
            Line("The game is running. Close it completely and run this again. Nothing was changed.");
            return 1;
        }

        string modelDir = Path.Combine(gameDir, WhisperRel);
        string dll = Path.Combine(gameDir, WhisperDllRel);
        bool hasAvx, hasAvx2;
        GetCpu(out hasAvx, out hasAvx2);

        // ---- Already installed? Offer undo ----
        bool anyInstalled = CpuFixInstalled(gameDir) || ModelsConverted(modelDir) || (File.Exists(dll) && Sha256(dll) == DllPatched);
        if (uninstallFlag || anyInstalled)
        {
            if (!uninstallFlag)
            {
                Line("");
                Line("Fixes from this tool are already installed.");
                Console.Write("Type U and press Enter to UNDO everything, or just press Enter to re-check: ");
                uninstallFlag = string.Equals((Console.ReadLine() ?? "").Trim(), "U", StringComparison.OrdinalIgnoreCase);
            }
            if (uninstallFlag) return UndoAll(gameDir, modelDir, dll);
        }

        DisableOldBepInEx(gameDir);
        Line("");
        Line("[1/4] Processor");
        Line("  AVX " + YesNo(hasAvx) + ", AVX2 " + YesNo(hasAvx2) + ", " + Environment.ProcessorCount + " threads");
        if (!hasAvx)
        {
            Line("  Your CPU doesn't support AVX, so the speech engine can't run on it.");
            Line("  You can still type to the callers. Nothing was changed.");
            return 0;
        }
        switch (GetCpuFixState(gameDir, hasAvx2))
        {
            case CpuFixState.NotNeeded: Line("  OK - no CPU fix needed."); break;
            case CpuFixState.Installed: Line("  Old CPU fix: already installed."); break;
            case CpuFixState.Installable:
                Console.Write("  Installing old CPU fix (no AVX2) ... ");
                InstallCpuFix(gameDir);
                Line("done");
                break;
            default:
                Line("  Old CPU fix: SKIPPED - your game version is different from 0.1.71.");
                Line("  (Maybe the developers fixed it. If speech says 'incompatible Whisper CPU backend', ask for a new version.)");
                break;
        }

        Line("");
        Line("[2/4] Speech speed-up");
        InstallSpeedup(modelDir, dll);

        Line("");
        Line("[3/4] Graphics card speech test (Vulkan) - takes a few seconds...");
        ProbeResult probe = RunVulkanProbe(gameDir, simulateProbe);
        string launch = ReadLaunchOptions();
        bool hasCpuOption = launch != null && Regex.IsMatch(launch, @"(^|\s)-whisper-cpu(\s|$)", RegexOptions.IgnoreCase);
        bool gpuProblem = probe.Outcome == ProbeOutcome.Crashed || probe.Outcome == ProbeOutcome.Hung;
        switch (probe.Outcome)
        {
            case ProbeOutcome.Passed:
                Line("  PASSED on " + probe.GpuName + ". Speech can use your graphics card.");
                if (hasCpuOption)
                {
                    Line("  Note: you have -whisper-cpu in your launch options. You can try removing it for faster");
                    Line("  speech. If the game then crashes when speech starts, put it back.");
                }
                break;
            case ProbeOutcome.NoGpu:
                Line("  No usable Vulkan graphics card found - the game will use the CPU for speech by itself. OK.");
                break;
            case ProbeOutcome.LoadFailed:
                Line("  Could not load the game's speech files (" + probe.Detail + ").");
                Line("  Windows may be blocking them: check Windows Security > App & browser control > Smart App Control,");
                Line("  and your antivirus. Then run this tool again.");
                break;
            default:
                Line("  FAILED - the speech engine " + (probe.Outcome == ProbeOutcome.Hung ? "froze" : "crashed") + " during the graphics card test"
                     + (probe.Detail != "" ? " (" + probe.Detail + ")" : "") + ".");
                Line("  Without the fix below, the game will probably crash when speech starts.");
                break;
        }
        if (gpuProblem)
        {
            Line("");
            if (hasCpuOption) Line("  You already have the launch option -whisper-cpu set. Good - that prevents this crash.");
            else LaunchOptionPrompt(launch);
        }

        Line("");
        Line("[4/4] Tips");
        bool lowEnd = !hasAvx2 || gpuProblem || probe.Outcome == ProbeOutcome.NoGpu || Environment.ProcessorCount <= 4;
        int watermark = ReadWatermarkSetting();
        if (lowEnd)
        {
            Line("  Your PC is on the slower side for this game, so:");
            if (watermark == 0) Line("  - BanditCam watermark: already OFF. Good.");
            else
            {
                Line("  - Turn OFF the BanditCam WATERMARK: open the BanditCam app on the in-game computer and");
                Line("    click the watermark toggle. After every call the game re-encodes the recording to add the");
                Line("    watermark, and that slows down speech recognition right when the next call starts.");
            }
            Line("  - Turn on VSync or cap your FPS (the game runs uncapped by default).");
            Line("  - Speak after the caller has finished, then give it a few seconds.");
        }
        else Line("  Nothing else needed. Speak after the caller has finished talking.");

        Line("");
        Line("ALL DONE.");
        Line("Run this tool again any time to re-check, or type U to undo everything.");
        Line("A game update or 'Verify integrity of game files' may undo the fixes - just run this again.");
        return 0;
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
        if (found == 0) Line("  Speech model files not found - skipped. (Steam > Verify integrity of game files.)");
        else if (todo.Count == 0) Line("  Faster speech model: already installed.");
        else
        {
            long need = 0;
            foreach (string p in todo) need += new FileInfo(p).Length / 2 + (20L << 20);
            bool space = true;
            try { space = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(modelDir))).AvailableFreeSpace >= need; } catch { }
            if (!space) Line("  Not enough free disk space for the faster model (need about " + (need >> 20) + " MB) - skipped.");
            else foreach (string p in todo)
            {
                Console.Write("  Converting " + Path.GetFileName(p) + " to the faster format ... ");
                var sw = Stopwatch.StartNew();
                string tmp = p + TempSuffix;
                try { Quantize(p, tmp); Validate(tmp); }
                catch { TryDelete(tmp); throw; }
                string bak = p + BackupSuffix;
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(p, bak);
                File.Move(tmp, p);
                Line("done (" + sw.Elapsed.TotalSeconds.ToString("0.0") + " s)");
            }
        }
        string h = File.Exists(dll) ? Sha256(dll) : null;
        if (h == DllOriginal) { Console.Write("  Shorter listening window ... "); PatchDll(dll); Line("done"); }
        else if (h == DllPatched) Line("  Shorter listening window: already installed.");
        else Line("  Shorter listening window: SKIPPED (your game version has a different com.whisper.unity.dll).");
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
        if (!UninstallCpuFix(gameDir)) { ok = false; Line("Could not restore the old CPU fix files."); }
        if (UninstallModels(modelDir) != 0) ok = false;
        if (UninstallDll(dll) != 0) ok = false;
        Line("");
        if (ok)
        {
            Line("UNDONE - original game files restored.");
            Line("If you added -whisper-cpu to the launch options and want it gone, remove it in Steam yourself.");
        }
        else Line("Some files could not be restored. Run Steam > Properties > Installed Files > Verify integrity of game files.");
        return ok ? 0 : 1;
    }

    static void LaunchOptionPrompt(string current)
    {
        Line("  ##############################################################");
        Line("  #  ACTION NEEDED: add a Steam launch option                   #");
        Line("  ##############################################################");
        Line("  1. Open Steam > Library.");
        Line("  2. Right-click 'Scam With Your Friends' > Properties.");
        Line("  3. On the General tab, find 'Launch Options' and type:   -whisper-cpu");
        if (!string.IsNullOrEmpty(current))
            Line("     (You already have \"" + current + "\" there - keep it and add -whisper-cpu after a space.)");
        Line("  4. Close the Properties window.");
        Line("  This makes speech use the processor instead of the graphics card, so the game won't crash.");
        Console.Write("  Press Enter when you have done this... ");
        Console.ReadLine();
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
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Jatater Worldwide\Scam With Your Friends"))
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

    static string YesNo(bool b) { return b ? "yes" : "NO"; }

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
        string testRoot = Environment.GetEnvironmentVariable("SWYF_FIXER_TEST_STEAM"); // test hook
        if (!string.IsNullOrEmpty(testRoot)) roots.Insert(0, testRoot);
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
