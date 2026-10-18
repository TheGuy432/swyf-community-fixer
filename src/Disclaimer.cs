// The disclaimer players have to accept once before the tool changes anything: who made it, that the game's
// developers don't make or support it, that it changes game files, and how to undo it.
// Acceptance is remembered per Windows user in HKCU\Software\SWYF Community Fixer (DisclaimerAccepted = version).
// Raise DisclaimerVersion when the text changes in a way players should read again.
using System;
using Microsoft.Win32;

static partial class Fixer
{
    const int DisclaimerVersion = 1;
    const string FixerKey = @"Software\SWYF Community Fixer";

    static bool DisclaimerAccepted()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(FixerKey))
            {
                object v = k == null ? null : k.GetValue("DisclaimerAccepted");
                return v is int && (int)v >= DisclaimerVersion;
            }
        }
        catch { return false; }
    }

    static void AcceptDisclaimer()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(FixerKey))
                if (k != null) k.SetValue("DisclaimerAccepted", DisclaimerVersion, RegistryValueKind.DWord);
        }
        catch { }   // can't remember it: the player is simply asked again next time
    }

    static string[] DisclaimerPoints()
    {
        return new[] { T("disclaimer.made_by"), T("disclaimer.no_dev_support"), T("disclaimer.risk"), T("disclaimer.undo") };
    }

    // Text mode: show the disclaimer and ask for Y. Returns false if the player didn't accept.
    static bool ConsoleDisclaimer()
    {
        if (DisclaimerAccepted()) return true;
        Line(T("disclaimer.title"));
        foreach (string p in DisclaimerPoints()) Wrapped("  - ", "    ", p);
        Line("  " + T("disclaimer.thread", ModThreadUrl));
        Line("");
        Out(T("disclaimer.console_prompt") + " ");
        string a = (Console.ReadLine() ?? "").Trim();
        if (!a.Equals("y", StringComparison.OrdinalIgnoreCase) && !a.Equals(T("disclaimer.console_yes"), StringComparison.OrdinalIgnoreCase))
        {
            Line(T("main.nothing_was_changed"));
            return false;
        }
        AcceptDisclaimer();
        Line("");
        return true;
    }
}
