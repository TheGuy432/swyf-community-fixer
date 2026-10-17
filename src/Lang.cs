// Translations. Every text the player sees lives in lang/<code>.txt (embedded in the .exe as "lang.<code>.txt").
// File format (UTF-8): "key = text" per line, "#" starts a comment, "\n" is a line break, {0} {1} are filled in.
// A missing key falls back to English, then to the key itself, so a half-done translation still works.
// Adding a language = adding a lang/xx.txt file; build.cmd embeds every file in lang\.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

static partial class Fixer
{
    static readonly Dictionary<string, Dictionary<string, string>> Langs = new Dictionary<string, Dictionary<string, string>>();
    static Dictionary<string, string> CurLang, EnLang;
    static string LangCode = "en";

    static void LoadLanguages()
    {
        Langs.Clear();
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (string res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("lang.", StringComparison.Ordinal) || !res.EndsWith(".txt", StringComparison.Ordinal)) continue;
            string code = res.Substring(5, res.Length - 9).ToLowerInvariant();
            using (Stream st = asm.GetManifestResourceStream(res))
            using (var rd = new StreamReader(st, Encoding.UTF8))
                Langs[code] = ParseLang(rd.ReadToEnd());
        }
        if (!Langs.ContainsKey("en")) Langs["en"] = new Dictionary<string, string>();
        EnLang = Langs["en"];
        CurLang = EnLang;
    }

    static Dictionary<string, string> ParseLang(string text)
    {
        var d = new Dictionary<string, string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string t = line.TrimStart();
            if (t.Length == 0 || t[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();
            d[key] = Unescape(val);
        }
        return d;
    }

    static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                if (n == 'n') sb.Append('\n');
                else if (n == 't') sb.Append('\t');
                else sb.Append(n);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // Text for key in the current language, with {0}.. filled in.
    static string T(string key, params object[] args)
    {
        string v;
        if (!CurLang.TryGetValue(key, out v) && !EnLang.TryGetValue(key, out v)) v = key;
        if (args == null || args.Length == 0) return v;
        try { return string.Format(v, args); }
        catch (FormatException)
        {
            string e;
            return EnLang.TryGetValue(key, out e) ? string.Format(e, args) : v;
        }
    }

    static bool HasLanguage(string code) { return code != null && Langs.ContainsKey(code); }

    static void SetLanguage(string code)
    {
        if (!HasLanguage(code)) code = "en";
        LangCode = code;
        CurLang = Langs[code];
    }

    static List<string> LanguageCodes()
    {
        var l = new List<string>(Langs.Keys);
        l.Sort(StringComparer.Ordinal);
        l.Remove("en");
        l.Insert(0, "en");
        return l;
    }

    // The language's own name ("English", "Português (Brasil)").
    static string NativeName(string code)
    {
        string v;
        return Langs.ContainsKey(code) && Langs[code].TryGetValue("_name", out v) ? v : code;
    }

    static string NextLanguage()
    {
        List<string> l = LanguageCodes();
        return l[(l.IndexOf(LangCode) + 1) % l.Count];
    }

    static string LanguageMenuHint()
    {
        string next = NextLanguage();
        return next == LangCode ? LangCode : NativeName(next);
    }

    // Shown small in the window for machine-made translations; empty for English.
    static string TranslatorNote()
    {
        string v;
        return LangCode != "en" && CurLang.TryGetValue("_translator_note", out v) ? v : "";
    }

    // Game language first (call language, then menu language), then Windows, else English.
    static string DetectLanguage()
    {
        foreach (string pref in new[] { "Kolkata.CallLanguage", "LanguageManager.CurrentLanguage" })
        {
            string g = ReadPrefString(pref);
            if (!string.IsNullOrEmpty(g) && g.Length >= 2)
            {
                string c = g.Substring(0, 2).ToLowerInvariant();
                if (HasLanguage(c)) return c;
            }
        }
        try
        {
            string w = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (HasLanguage(w)) return w;
        }
        catch { }
        return "en";
    }
}
