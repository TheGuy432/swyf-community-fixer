// Fixes in Assembly-CSharp.dll (game build 0.1.74). Three independent region groups, see Patches.cs:
//
// 1. EXPERIMENTAL Qwen3 TTS on 8 GB graphics cards (menu item "qwen", opt-in only).
//    QwenWorkerClient.HardwareEligible requires vramMb >= 8192 and ramMb >= 16000, but 8 GB cards report 7957-8018 MB
//    and 16 GB PCs report about 15700-15900 MB. Lowered to 7680 MB / 15000 MB:
//        IL 0x0002  ldc.i4 8192  -> 7680      IL 0x0073  ldc.i4 16000 -> 15000
//    On an 8 GB card the voice model may not fit next to the game, so it can stutter or crash.
//
// 2. Card numbers read in one go (part of the caller voice fix).
//    SentinelValueFactory.BuildGroupsSpoken appends a "[50ms]" pause tag after each '-' separated group, so the voice
//    engine generates every 4-digit group as a separate line; with MOSS each group runs 2-10x too long (up to 63 s
//    for one card number). The '-' test (IL 0x0096 ldc.i4.s 45) becomes ldc.i4.s 0, so no pause tags are added.
//
// 3. Filler lines end with "." instead of "..." (part of the caller voice fix).
//    CallerResponseFillers.Get returns e.g. "Um, okay..." / "嗯，好的……"; MOSS often keeps going for 5-30 s after an
//    ellipsis. The 66 string literals end with ".  " / "。 " instead (same length; the spaces are ignored).
using System;
using System.Collections.Generic;
using System.IO;

static partial class Fixer
{
    static readonly string GameAsmRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "Managed"), "Assembly-CSharp.dll");
    const string AsmOriginal = "99791cf93ec829d4784ee514ab78eba19e5e4a7ea841b291d979432a5c911d8f";
    const string QwenBackupSuffix = ".qwen8gb-backup";   // v4.3 kept a backup; no longer needed

    static readonly Region[] QwenRegions =
    {
        new Region(0x11B8F7, "00200000", "001E0000"),   // 8192 -> 7680
        new Region(0x11B968, "803E0000", "983A0000"),   // 16000 -> 15000
    };
    static readonly Region[] CardRegions = { new Region(0x40AFF, "2D", "00") };
    static readonly Region[] FillerRegions = BuildFillerRegions();

    static Region[] BuildFillerRegions()
    {
        int[] dots =
        {
            0x24268E, 0x2426AC, 0x2426C6, 0x2426E0, 0x242702, 0x24271A, 0x242736, 0x242758, 0x24276C, 0x242784,
            0x2427A4, 0x2427BC, 0x24280A, 0x24282A, 0x24284C, 0x242866, 0x242888, 0x2428A4, 0x2428BA, 0x2428D0,
            0x2428E0, 0x2428FA, 0x24291A, 0x242936, 0x242950, 0x242970, 0x24298E, 0x2429AA, 0x2429C4, 0x2429DE,
            0x2429FC, 0x242A18, 0x242A2E, 0x242A46, 0x242A62, 0x242A7A, 0x242A92, 0x242AAE, 0x242AC6, 0x242ADE,
            0x242B00, 0x242B16, 0x242B34, 0x242B54, 0x242B70, 0x242B88, 0x242BA2, 0x242BBC, 0x242BDA, 0x242BF6,
            0x242C18, 0x242C2E, 0x242C4C, 0x242C64, 0x242C7C, 0x242C98, 0x242CB2, 0x242CCA, 0x242CEA, 0x242D04
        };
        int[] cjk = { 0x242658, 0x242666, 0x242674, 0x2427D0, 0x2427E6, 0x2427F4 };
        var list = new List<Region>();
        foreach (int o in dots) list.Add(new Region(o, "2E002E002E00", "2E0020002000"));   // "..." -> ".  "
        foreach (int o in cjk) list.Add(new Region(o, "26202620", "02302000"));             // "……" -> "。 "
        return list.ToArray();
    }

    static Region[][] AllAsmGroups { get { return new[] { QwenRegions, CardRegions, FillerRegions }; } }

    static string GameAsmPath(string g) { return Path.Combine(g, GameAsmRel); }

    // State of Assembly-CSharp.dll. Known = the 0.1.74 file, possibly with any of our fixes.
    sealed class AsmState { public bool Known, Qwen, Card, Fillers; }

    static AsmState ReadAsmState(string gameDir)
    {
        var st = new AsmState();
        byte[] d = ReadIfExists(GameAsmPath(gameDir));
        if (KnownOriginal(d, AsmOriginal, AllAsmGroups) == null) return st;
        st.Known = true;
        st.Qwen = GroupOn(d, QwenRegions);
        st.Card = GroupOn(d, CardRegions);
        st.Fillers = GroupOn(d, FillerRegions);
        return st;
    }

    // Sets the wanted groups (null = leave as is). Returns false if the file isn't the known game version.
    static bool SetAsmFixes(string gameDir, bool? qwen, bool? card, bool? fillers)
    {
        string dll = GameAsmPath(gameDir);
        byte[] d = ReadIfExists(dll);
        if (KnownOriginal(d, AsmOriginal, AllAsmGroups) == null) return false;
        byte[] target = (byte[])d.Clone();
        if (qwen.HasValue) SetGroup(target, QwenRegions, qwen.Value);
        if (card.HasValue) SetGroup(target, CardRegions, card.Value);
        if (fillers.HasValue) SetGroup(target, FillerRegions, fillers.Value);
        if (KnownOriginal(target, AsmOriginal, AllAsmGroups) == null) throw new Exception("Assembly-CSharp.dll patch verification failed; file not changed.");
        if (Sha256(target) != Sha256(d))
        {
            WriteAtomic(dll, target);
            if (Sha256(dll) != Sha256(target)) throw new Exception("Writing Assembly-CSharp.dll failed. Run Steam > Verify integrity of game files.");
        }
        TryDelete(dll + QwenBackupSuffix);   // leftover from v4.3
        return true;
    }

    static void InstallQwenFix(string gameDir)
    {
        AsmState st = ReadAsmState(gameDir);
        if (!st.Known) { Line("  " + T("qwen.skipped_your_game_version_has")); return; }
        if (st.Qwen) { Line("  " + T("core.already_installed")); return; }
        Out("  " + T("qwen.lowering_qwen3_tts_limits_to") + " ");
        SetAsmFixes(gameDir, true, null, null);
        Line(T("main.done"));
        Line("  " + T("qwen.in_game_pick_qwen3_in"));
        Line("  " + T("qwen.crashes_switch_back_to_normal"));
    }

    static int UninstallQwenFix(string gameDir)
    {
        AsmState st = ReadAsmState(gameDir);
        if (!st.Known || !st.Qwen) { if (!st.Known) TryDelete(GameAsmPath(gameDir) + QwenBackupSuffix); return 0; }
        SetAsmFixes(gameDir, false, null, null);
        Line("  " + T("qwen.qwen3_8_gb_patch_removed"));
        return 0;
    }
}
