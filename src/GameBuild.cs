// Which game version is installed, and what the 18 Oct 2026 update (Steam build 26020526) fixed by itself.
//
// That update includes the developers' own versions of the caller voice fix (Pocket end-of-line rule, Korean/Hindi
// text, card numbers, filler lines), "Better English speech recognition" (English keeps Whisper on CPU speech) and
// the Qwen3 8 GB limits. On that build or newer those fixer options show as "fixed in the game".
// It also added Esc > Settings > Speech recognition > "Moonshine (6 languages)" (en, de, es, ar, ja, zh on the CPU).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

static partial class Fixer
{
    const int BuildOct18 = 26020526;
    const string PocketOct18 = "37b76e8b372460e5eb6372022ea4277a459289b38f2ef04fe97573f9c35d2e95";
    const string AsmOct18    = "0edee3f51411f069a876be2f24e4b4acc41e1a08fe6d01c3a0a00f872f6007a7";
    const string SadOct18    = "75cc108ace20b54d29579e739fbc20ee6a3381a61f2de10afe5c85f772308d22";

    // Languages the game's "Moonshine (6 languages)" setting covers (since build 26020526).
    static readonly string[] MoonshineLanguages = { "en", "de", "es", "ar", "ja", "zh" };

    static string assumeBuild, assumeLanguage, assumeProvider;   // test builds only

    // Steam's build id from <library>\steamapps\appmanifest_4954910.acf. 0 = unknown.
    static int ReadSteamBuildId(string gameDir)
    {
        if (assumeBuild != null) { int t; return int.TryParse(assumeBuild, out t) ? t : 0; }
        try
        {
            string common = Path.GetDirectoryName(gameDir.TrimEnd('\\', '/'));
            string steamapps = common == null ? null : Path.GetDirectoryName(common);
            if (steamapps == null) return 0;
            string acf = Path.Combine(steamapps, "appmanifest_" + AppId + ".acf");
            if (!File.Exists(acf)) return 0;
            Match m = Regex.Match(File.ReadAllText(acf), "\"buildid\"\\s+\"(\\d+)\"");
            int b;
            return m.Success && int.TryParse(m.Groups[1].Value, out b) ? b : 0;
        }
        catch { return 0; }
    }

    // True when the installed game already has the developers' own fixes (18 Oct 2026 update or newer).
    // Never true while any file is still a 0.1.74 file we know (patched or not).
    static bool GameHasOwnFixes(Session s)
    {
        bool old174 = s.Asm.Known || s.Pocket != PocketState.Unknown || s.SadHash == SadOriginal || s.SadHash == SadPatched;
        if (old174) return false;
        return s.SteamBuild >= BuildOct18 || s.PocketHash == PocketOct18 || s.AsmHash == AsmOct18 || s.SadHash == SadOct18;
    }

    // Two-letter call language ("pt"), or null if unknown. The game keeps it in PlayerPrefs.
    static string ReadCallLanguage()
    {
        if (assumeLanguage != null) return assumeLanguage == "" ? null : assumeLanguage;
        string v = ReadPrefString("Kolkata.CallLanguage");
        if (string.IsNullOrEmpty(v) || v == "auto") v = ReadPrefString("LanguageManager.CurrentLanguage");
        if (string.IsNullOrEmpty(v) || v == "auto" || v.Length < 2) return null;
        return v.Substring(0, 2).ToLowerInvariant();
    }

    // Esc > Settings > Speech recognition: "Automatic", "Moonshine" or "Deepgram" (null = never changed = Automatic).
    static string ReadSpeechProvider()
    {
        if (assumeProvider != null) return assumeProvider;
        return ReadPrefString("SpeechRecognition.Provider");
    }

    static bool IsMoonshineLanguage(string lang) { return lang != null && Array.IndexOf(MoonshineLanguages, lang) >= 0; }

    // AMD processor graphics (vendor 1002) together with an NVIDIA card (10de). On every such PC we've seen,
    // the game's speech engine starts loading on the graphics card and never finishes (dev item 10).
    static bool IsAmdPlusNvidia(List<string> ids)
    {
        bool amd = false, nv = false;
        foreach (string id in ids)
        {
            if (id.StartsWith("1002:")) amd = true;
            if (id.StartsWith("10de:")) nv = true;
        }
        return amd && nv;
    }

    // After a game update: remove backups of older game files that nothing can restore any more.
    static void CleanUpAfterGameUpdate(string gameDir, string dll)
    {
        bool removed = false;
        string sad = SadPath(gameDir), sadBak = sad + WhisperKeepBackupSuffix;
        if (File.Exists(sadBak) && Sha256(sadBak) == SadOriginal && (!File.Exists(sad) || Sha256(sad) != SadPatched)) { TryDelete(sadBak); removed = true; }
        string dllBak = dll + BackupSuffix;
        if (File.Exists(dllBak) && IsDllOriginal(Sha256(dllBak)) && (!File.Exists(dll) || !IsDllPatched(Sha256(dll)))) { TryDelete(dllBak); removed = true; }
        foreach (string bak in new[] { PocketPath(gameDir) + VoiceBackupSuffix, GameAsmPath(gameDir) + QwenBackupSuffix })
            if (File.Exists(bak)) { TryDelete(bak); removed = true; }
        if (removed) Line("  " + T("core.removed_old_backups"));
    }

    // Vulkan implicit layers (overlays, recorders, AMD switchable graphics...) registered on this PC: short names.
    // The Vulkan loader adds these to every program that uses Vulkan, including the game's speech engine.
    static string assumeLayers;   // test builds only, "a.json|b.json"
    static List<string> ImplicitLayerNames()
    {
        var names = new List<string>();
        var paths = new List<string>();
        if (assumeLayers != null) paths.AddRange(assumeLayers.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        else if (!IsUnix)
        {
            foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using (RegistryKey k = root.OpenSubKey(@"SOFTWARE\Khronos\Vulkan\ImplicitLayers"))
                    {
                        if (k == null) continue;
                        foreach (string n in k.GetValueNames())
                        {
                            object v = k.GetValue(n);
                            if (v is int && (int)v != 0) continue;   // non-zero = disabled
                            paths.Add(n);
                        }
                    }
                }
                catch { }
            }
            // Graphics drivers register their layers under the display adapter's class key.
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey k = cls.OpenSubKey(sub))
                                {
                                    object v = k == null ? null : k.GetValue("VulkanImplicitLayers");
                                    if (v is string[]) paths.AddRange((string[])v);
                                    else if (v is string) paths.Add((string)v);
                                }
                            }
                            catch { }
                        }
                }
            }
            catch { }
        }
        foreach (string p in paths)
        {
            string n = p.Trim();
            int slash = n.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) n = n.Substring(slash + 1);
            if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
            if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
        }
        return names;
    }
}
