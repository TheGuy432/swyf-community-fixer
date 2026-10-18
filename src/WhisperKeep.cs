// Better English speech recognition on PCs the game moves to CPU speech (ScriptsAssDef.dll, game build 0.1.74).
// The game does not run speech recognition on some graphics cards (WindowsSpeechCompatibility.NeedsCpuRecognition:
// NVIDIA 1F82, 2803 = RTX 4060 Ti, 2B85; AMD 15D8, 67DF = RX 470/570/580, 699F, 744C = RX 7900 XTX) or when the
// player uses the -whisper-cpu launch option. Since 0.1.74, English speech on those PCs then uses the small
// Moonshine tiny model instead of Whisper, which is much less accurate.
// Fix: in WindowsSpeechCompatibility.ShouldPreferMoonshine(whisperSupported, windowsArm64, cpuCompatibility,
// cpuEnvironment, arguments) the loads of cpuCompatibility and cpuEnvironment become "false":
//     IL 0x0009  ldarg.2 -> ldc.i4.0
//     IL 0x000B  ldarg.3 -> ldc.i4.0
// so Moonshine is only used when Whisper can't run at all (no AVX) or with the -moonshine launch option.
// Whisper still runs on the CPU (KOLKATA_WHISPER_CPU_ONLY stays set), exactly like it already does for
// every other language on those PCs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

static partial class Fixer
{
    const string SadOriginal = "4cf9f54e3760c2392952ef17f981e01c06fb483c655f9953ba9f6386889f0b2e";
    const string SadPatched  = "ede26084d0551f287d3d9c0f76a15d5f9c674b1b6d088d8952672a1bec0fc736";
    const string WhisperKeepBackupSuffix = ".whisperkeep-backup";
    const int KeepCpuCompatAt = 0x119ECD, KeepCpuEnvAt = 0x119ECF;   // ldarg.2 (04), ldarg.3 (05) -> ldc.i4.0 (16)

    static string assumeGpu;   // test builds only: "10de:2803" or "1002:1638,10de:2520"

    // Same list as the game's NeedsCpuRecognition (0.1.74). The 18 Oct 2026 update added NVIDIA 11A1 and 1F99 and
    // the Microsoft Basic Render Driver (no graphics driver installed).
    static readonly string[] CpuSpeechGpus = { "10de:1f82", "10de:2803", "10de:2b85", "1002:15d8", "1002:67df", "1002:699f", "1002:744c" };
    static readonly string[] CpuSpeechGpusOct18 = { "10de:11a1", "10de:1f99", "1414:008c" };

    static List<string> GraphicsCardIds()
    {
        var ids = new List<string>();
        if (assumeGpu != null) { ids.AddRange(assumeGpu.ToLowerInvariant().Split(',')); return ids; }
        if (IsUnix) return ids;
        try
        {
            using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            {
                if (cls == null) return ids;
                foreach (string sub in cls.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey k = cls.OpenSubKey(sub))
                        {
                            string m = k == null ? null : k.GetValue("MatchingDeviceId") as string;
                            if (m == null) continue;
                            Match x = Regex.Match(m, @"ven_([0-9a-f]{4})&dev_([0-9a-f]{4})", RegexOptions.IgnoreCase);
                            if (x.Success) ids.Add((x.Groups[1].Value + ":" + x.Groups[2].Value).ToLowerInvariant());
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        return ids;
    }

    // Display names of the graphics adapters (registry DriverDesc), for the window's "Your PC" line.
    static List<string> GraphicsCardNames()
    {
        var names = new List<string>();
        if (assumePc != null) { string[] p = assumePc.Split('|'); if (p.Length > 1) names.Add(p[1]); return names; }
        if (IsUnix) return names;
        try
        {
            using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
            {
                if (cls == null) return names;
                foreach (string sub in cls.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey k = cls.OpenSubKey(sub))
                        {
                            string d = k == null ? null : k.GetValue("DriverDesc") as string;
                            if (!string.IsNullOrEmpty(d) && !names.Contains(d)) names.Add(d);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        return names;
    }

    // True if the game will run speech on the CPU on this PC (on 0.1.74 English then uses Moonshine).
    static bool GameUsesCpuSpeech(string launchOptions, bool oct18)
    {
        if (launchOptions != null && Regex.IsMatch(launchOptions, @"(^|\s)-whisper-cpu(\s|$)", RegexOptions.IgnoreCase)) return true;
        foreach (string id in GraphicsCardIds())
            if (Array.IndexOf(CpuSpeechGpus, id) >= 0 || (oct18 && Array.IndexOf(CpuSpeechGpusOct18, id) >= 0)) return true;
        return false;
    }

    static string SadPath(string g) { return Path.Combine(g, ScriptsRel); }

    static void InstallWhisperKeep(string gameDir)
    {
        string dll = SadPath(gameDir);
        string h = File.Exists(dll) ? Sha256(dll) : null;
        if (h == SadPatched) { Line(("  " + T("core.already_installed"))); return; }
        if (h != SadOriginal)
        {
            Line(("  " + T("keep.skipped_your_game_version_has")));
            return;
        }
        Out(("  " + T("keep.keeping_whisper_speech_model_for") + " "));
        byte[] data = File.ReadAllBytes(dll);
        if (data[KeepCpuCompatAt] != 0x04 || data[KeepCpuEnvAt] != 0x05) throw new Exception("Unexpected ScriptsAssDef.dll contents; not changed.");
        data[KeepCpuCompatAt] = 0x16;
        data[KeepCpuEnvAt] = 0x16;
        if (Sha256(data) != SadPatched) throw new Exception("Speech model patch verification failed; ScriptsAssDef.dll not changed.");
        string bak = dll + WhisperKeepBackupSuffix;
        if (!File.Exists(bak) || Sha256(bak) != SadOriginal) File.Copy(dll, bak, true);
        WriteAtomic(dll, data);
        if (Sha256(dll) != SadPatched) throw new Exception("Writing ScriptsAssDef.dll failed. Run Steam > Verify integrity of game files.");
        Line(T("main.done"));
    }

    static int UninstallWhisperKeep(string gameDir)
    {
        string dll = SadPath(gameDir), bak = dll + WhisperKeepBackupSuffix;
        if (!File.Exists(dll) || Sha256(dll) != SadPatched)
        {
            TryDelete(bak);   // a game update replaced the file: keep the game's version
            return 0;
        }
        if (!File.Exists(bak) || Sha256(bak) != SadOriginal)
        {
            Line(("  " + T("keep.could_not_restore_scriptsassdef_dll")));
            return 1;
        }
        File.Copy(bak, dll, true);
        if (Sha256(dll) != SadOriginal)
        {
            Line(("  " + T("keep.could_not_restore_scriptsassdef_dll_2")));
            return 1;
        }
        TryDelete(bak);
        Line(("  " + T("keep.english_speech_model_patch_removed")));
        return 0;
    }
}
