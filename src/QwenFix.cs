// EXPERIMENTAL: Qwen3 TTS on 8 GB graphics cards (Assembly-CSharp.dll, game build 0.1.74).
// QwenWorkerClient.HardwareEligible requires vramMb >= 8192 and ramMb >= 16000, but 8 GB cards report
// 7957-8018 MB and 16 GB PCs report about 15700-15900 MB, so they never get the Qwen3 voice option.
// This lowers the two limits to 7680 MB VRAM and 15000 MB RAM. The other checks (NVIDIA/AMD, blocked
// devices, 6 GB free RAM, 4 threads, 64-bit Windows) are unchanged.
//     IL 0x0002  ldc.i4 8192   -> 7680
//     IL 0x0073  ldc.i4 16000  -> 15000
// Opt-in only: on an 8 GB card the voice model may not fit next to the game, so it can stutter or crash.
using System;
using System.IO;

static partial class Fixer
{
    static readonly string GameAsmRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "Managed"), "Assembly-CSharp.dll");
    const string AsmOriginal = "99791cf93ec829d4784ee514ab78eba19e5e4a7ea841b291d979432a5c911d8f";
    const string AsmPatched  = "3914ec19783fb0bf29bb470636ec1d14e911fa7041d4067425af81c651476bc6";
    const string QwenBackupSuffix = ".qwen8gb-backup";
    const int QwenVramAt = 0x11B8F7, QwenRamAt = 0x11B968;   // int32 operands of the two ldc.i4
    const int QwenVramNew = 7680, QwenRamNew = 15000;

    static string GameAsmPath(string g) { return Path.Combine(g, GameAsmRel); }

    static void InstallQwenFix(string gameDir)
    {
        string dll = GameAsmPath(gameDir);
        string h = File.Exists(dll) ? Sha256(dll) : null;
        if (h == AsmPatched) { Line(("  " + T("core.already_installed"))); return; }
        if (h != AsmOriginal)
        {
            Line(("  " + T("qwen.skipped_your_game_version_has")));
            return;
        }
        Out(("  " + T("qwen.lowering_qwen3_tts_limits_to") + " "));
        byte[] data = File.ReadAllBytes(dll);
        BitConverter.GetBytes(QwenVramNew).CopyTo(data, QwenVramAt);
        BitConverter.GetBytes(QwenRamNew).CopyTo(data, QwenRamAt);
        if (Sha256(data) != AsmPatched) throw new Exception("Qwen3 patch verification failed; Assembly-CSharp.dll not changed.");
        string bak = dll + QwenBackupSuffix;
        if (!File.Exists(bak) || Sha256(bak) != AsmOriginal) File.Copy(dll, bak, true);
        string tmp = dll + TempSuffix;
        File.WriteAllBytes(tmp, data);
        File.Copy(tmp, dll, true);
        File.Delete(tmp);
        if (Sha256(dll) != AsmPatched) throw new Exception("Writing Assembly-CSharp.dll failed. Run Steam > Verify integrity of game files.");
        Line(T("main.done"));
        Line(("  " + T("qwen.in_game_pick_qwen3_in")));
        Line(("  " + T("qwen.crashes_switch_back_to_normal")));
    }

    static int UninstallQwenFix(string gameDir)
    {
        string dll = GameAsmPath(gameDir), bak = dll + QwenBackupSuffix;
        if (!File.Exists(dll) || Sha256(dll) != AsmPatched)
        {
            TryDelete(bak);   // a game update replaced the file: keep the game's version
            return 0;
        }
        if (!File.Exists(bak) || Sha256(bak) != AsmOriginal)
        {
            Line(("  " + T("qwen.could_not_restore_assembly_csharp")));
            return 1;
        }
        File.Copy(bak, dll, true);
        if (Sha256(dll) != AsmOriginal)
        {
            Line(("  " + T("qwen.could_not_restore_assembly_csharp_2")));
            return 1;
        }
        TryDelete(bak);
        Line(("  " + T("qwen.qwen3_8_gb_patch_removed")));
        return 0;
    }
}
