// Caller voice fix (v4.4). Four parts, all switched on or off together:
//
// PocketTTS.dll (game builds 0.1.71 - 0.1.74, same file):
//  a) End-of-line rule. PocketTTSModel.RunPrompt stops 3 frames after the first frame whose end-of-speech score
//     (eos) is above -4. Several voice models briefly cross -4 mid-sentence or right at the start (Portuguese:
//     every line cut after ~0.3 s; French/German/Italian/Korean lines cut mid-sentence). New rule, tested on all 12
//     Pocket languages (118/120 lines right vs 97/120):
//         from frame 8: stop when eos > 0, or when eos > -3 for 10 frames in a row
//     The original check (IL 0x06C6, 18 bytes) becomes NOPs. The new check goes in the space of the optional
//     profiling block before the loop's break test (IL 0x0793, 54 bytes; frameBirthTimes only fed a debug log):
//         V_55 = (num14 != 0 && num15 > -3f) ? V_55 + 1 : 0;          // V_55 is only used after the loop
//         if (num13 < 0 && num14 >= 8 && (num15 > 0f || V_55 >= 10)) num13 = num14;
//     v4.2 had a simpler version (ignore eos for frames 0-7); a v4.2-patched file is recognised and upgraded.
//  b) Text preparation. TextProcessor.PrepareText did Normalize(FormD) and dropped every NonSpacingMark: Korean
//     Hangul became loose Jamo the Korean tokenizer doesn't know (half of each line <unk>), and Hindi lost vowel
//     signs, virama and nasal marks. Now Normalize(FormC) (IL 0x0066 ldc.i4.2 -> ldc.i4.1) and no mark is dropped
//     (IL 0x008B: compare with -1 instead of NonSpacingMark 5). Latin accents are still mapped by the game's own
//     table first, so other languages get the same text as before.
//
// Assembly-CSharp.dll (0.1.74, see QwenFix.cs): c) card numbers read in one go, d) filler lines end with ".".
using System;
using System.IO;

static partial class Fixer
{
    static readonly string PocketDllRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "Managed"), "PocketTTS.dll");
    const string PocketOriginal = "b47b39cd9be3417efd2c1c0e61a124f762685b4caf116e20e08165b706bf11bf";
    const string PocketPatched  = "894ff1195b093819239821d45e90c0320b6e4fd09ab70b8a5cc6e9d2d58b715b";   // v4.4
    const string PocketPatchedV42 = "6cf88903554073d028f07fca9a8a8fd8274fa46f32b37e4518e226b6754bd109";
    const string VoiceBackupSuffix = ".voicefix-backup";   // v4.2 kept a backup; no longer needed
    const int PocketCode = 0x3280;                          // file offset of RunPrompt's IL code

    static readonly Region[] PocketRegions =
    {
        new Region(PocketCode + 0x06C6, "1128162F0D113122000080C03604112D1328",
                   "000000000000000000000000000000000000"),
        new Region(PocketCode + 0x0793,
                   "027B4C0000042C2E11237B7C0000047B770000047B74000004112D11237B7C0000047B770000047B710000046FDC00000A6FE700000A",
                   "112D2C0F113122000040C03606113717582B011613371128162F18112D1E321311312200000000300611371F0A3204112D1328000000",
                   // v4.2: if (num13 < 0 && num14 >= 8 && num15 > -4f) num13 = num14;
                   "1128162F13112D1F08320D113122000080C03604112D1328000000000000000000000000000000000000000000000000000000000000"),
        new Region(0x8AA, "18", "17"),    // PrepareText: Normalize(FormD) -> Normalize(FormC)
        new Region(0x8CF, "1B", "15"),    // PrepareText: drop NonSpacingMark -> drop nothing
    };

    static string PocketPath(string g) { return Path.Combine(g, PocketDllRel); }

    enum PocketState { Unknown, Original, OldFix, Fixed }

    static PocketState ReadPocketState(string gameDir)
    {
        byte[] d = ReadIfExists(PocketPath(gameDir));
        if (KnownOriginal(d, PocketOriginal, new[] { PocketRegions }) == null) return PocketState.Unknown;
        string h = Sha256(d);
        if (h == PocketOriginal) return PocketState.Original;
        if (h == PocketPatched) return PocketState.Fixed;
        return PocketState.OldFix;   // v4.2 fix, or a partly patched file
    }

    static void InstallVoiceFix(string gameDir)
    {
        string dll = PocketPath(gameDir);
        byte[] d = ReadIfExists(dll);
        byte[] orig = KnownOriginal(d, PocketOriginal, new[] { PocketRegions });
        bool didSomething = false;
        if (orig == null)
            Line("  " + T("voice.skipped_your_game_version_has"));
        else if (Sha256(d) == PocketPatched)
            Line("  " + T("voice.lines_already"));
        else
        {
            Out("  " + T("voice.fixing_lines") + " ");
            byte[] target = (byte[])orig.Clone();
            SetGroup(target, PocketRegions, true);
            if (Sha256(target) != PocketPatched) throw new Exception("Voice fix verification failed; PocketTTS.dll not changed.");
            WriteAtomic(dll, target);
            if (Sha256(dll) != PocketPatched) throw new Exception("Writing PocketTTS.dll failed. Run Steam > Verify integrity of game files.");
            Line(T("main.done"));
            didSomething = true;
        }
        if (orig != null) TryDelete(dll + VoiceBackupSuffix);

        AsmState st = ReadAsmState(gameDir);
        if (!st.Known) Line("  " + T("voice.cards_skipped"));
        else if (st.Card && st.Fillers) Line("  " + T("voice.cards_already"));
        else
        {
            Out("  " + T("voice.fixing_cards") + " ");
            SetAsmFixes(gameDir, null, true, true);
            Line(T("main.done"));
            didSomething = true;
        }
        if (didSomething) Line("  " + T("voice.report_note"));
    }

    static int UninstallVoiceFix(string gameDir)
    {
        string dll = PocketPath(gameDir);
        byte[] d = ReadIfExists(dll);
        byte[] orig = KnownOriginal(d, PocketOriginal, new[] { PocketRegions });
        bool removed = false;
        if (orig != null)
        {
            if (Sha256(d) != PocketOriginal)
            {
                WriteAtomic(dll, orig);
                if (Sha256(dll) != PocketOriginal)
                {
                    Line("  " + T("voice.could_not_restore_pockettts_dll_2"));
                    return 1;
                }
                removed = true;
            }
            TryDelete(dll + VoiceBackupSuffix);
        }
        AsmState st = ReadAsmState(gameDir);
        if (st.Known && (st.Card || st.Fillers)) { SetAsmFixes(gameDir, null, false, false); removed = true; }
        if (removed) Line("  " + T("voice.caller_voice_fix_removed_original"));
        return 0;
    }
}
