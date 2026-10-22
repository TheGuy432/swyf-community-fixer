// Where the game keeps its settings.
//
// Up to game 0.1.82 the settings (call language, menu language, speech recognition, BanditCam watermark) were Unity
// PlayerPrefs in the registry (HKCU\Software\Jatater Worldwide\Scam With Your Friends). Since 0.1.83 (Steam build
// 26097532, 21 Oct 2026) the game keeps them in
//   %USERPROFILE%\AppData\LocalLow\Jatater Worldwide\Scam With Your Friends\MetaSave\local.json
// as {"version":1,"updatedUtc":...,"importLegacy":true,"values":{"Kolkata.CallLanguage":"pt",...}}.
// The game copies a registry value into that file the first time it reads the key, but never deletes the registry
// value, and later changes only go to the file. So the file wins; the registry only counts for a key the file
// doesn't have yet while "importLegacy" is still on (that's what the game itself does).
//
// local.json also holds the player's Deepgram API key in plain text: only the keys asked for are ever read out of
// it, nothing else from the file is kept, shown or put in the report.
using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

static partial class Fixer
{
    class MetaSaveLocal
    {
        public Dictionary<string, object> Values;
        public bool ImportLegacy;
    }

    static MetaSaveLocal metaSaveCache;
    static bool metaSaveRead;

    static string MetaSaveDir()
    {
        string profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrEmpty(profile)) return null;
        return Path.Combine(profile, @"AppData\LocalLow\Jatater Worldwide\Scam With Your Friends\MetaSave");
    }

    // null = no local.json (game 0.1.82 or older, or never started) or it couldn't be read.
    static MetaSaveLocal ReadMetaSaveLocal()
    {
        if (metaSaveRead) return metaSaveCache;
        metaSaveRead = true;
        string dir = MetaSaveDir();
        if (dir == null) return null;
        // The game writes local.json.tmp and swaps it in, keeping the previous file as local.json.bak.
        foreach (string name in new[] { "local.json", "local.json.bak" })
        {
            try
            {
                string file = Path.Combine(dir, name);
                if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024 * 1024) continue;
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var doc = js.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                if (doc == null) continue;
                object values, legacy;
                var v = doc.TryGetValue("values", out values) ? values as Dictionary<string, object> : null;
                if (v == null) continue;
                metaSaveCache = new MetaSaveLocal
                {
                    Values = v,
                    ImportLegacy = doc.TryGetValue("importLegacy", out legacy) && legacy is bool && (bool)legacy
                };
                return metaSaveCache;
            }
            catch { }
        }
        return null;
    }

    // found = the game has this setting in local.json (then the registry is out of date and must not be used).
    static object MetaSaveValue(string key, out bool useRegistry)
    {
        MetaSaveLocal m = ReadMetaSaveLocal();
        useRegistry = true;
        if (m == null) return null;
        object v;
        if (m.Values.TryGetValue(key, out v) && v != null) { useRegistry = false; return v; }
        useRegistry = m.ImportLegacy;
        return null;
    }

    // A game setting as text (local.json first, then the registry). null = not set.
    static string ReadPrefString(string key)
    {
        bool useRegistry;
        object v = MetaSaveValue(key, out useRegistry);
        if (v != null) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        return useRegistry ? ReadRegistryPrefString(key) : null;
    }

    // A game setting as a whole number (local.json first, then the registry). null = not set, couldn't read it.
    static long? ReadPrefInt(string key)
    {
        bool useRegistry;
        object v = MetaSaveValue(key, out useRegistry);
        if (v != null)
        {
            try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }
        return useRegistry ? ReadRegistryPrefInt(key) : null;
    }
}
