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
        if (h == AsmPatched) { Line(Tr("  Already installed.", "  Já instalada.")); return; }
        if (h != AsmOriginal)
        {
            Line(Tr("  SKIPPED (your game version has a different Assembly-CSharp.dll).", "  PULADA (sua versão do jogo tem outro Assembly-CSharp.dll)."));
            return;
        }
        Console.Write(Tr("  Lowering the Qwen3 TTS limits to 7680 MB graphics memory / 15000 MB RAM ... ",
                         "  Baixando os limites do Qwen3 TTS para 7680 MB de memória de vídeo / 15000 MB de RAM ... "));
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
        Line(Tr("done", "pronto"));
        Line(Tr("  In the game, pick Qwen3 in the voice settings. If the game stutters, runs out of memory or",
                "  No jogo, escolha Qwen3 nas configurações de voz. Se o jogo travar, ficar sem memória ou"));
        Line(Tr("  crashes, switch back to the normal voices or run this tool again and untick this.",
                "  fechar sozinho, volte para as vozes normais ou rode esta ferramenta de novo e desmarque isto."));
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
            Line(Tr("  Could not restore Assembly-CSharp.dll (backup missing). Run Steam > Verify integrity of game files.",
                    "  Não consegui restaurar o Assembly-CSharp.dll (backup não encontrado). Use Steam > Verificar integridade dos arquivos do jogo."));
            return 1;
        }
        File.Copy(bak, dll, true);
        if (Sha256(dll) != AsmOriginal)
        {
            Line(Tr("  Could not restore Assembly-CSharp.dll. Run Steam > Verify integrity of game files.",
                    "  Não consegui restaurar o Assembly-CSharp.dll. Use Steam > Verificar integridade dos arquivos do jogo."));
            return 1;
        }
        TryDelete(bak);
        Line(Tr("  Qwen3 8 GB patch removed - original Assembly-CSharp.dll restored.",
                "  Patch do Qwen3 8 GB removido - Assembly-CSharp.dll original restaurado."));
        return 0;
    }
}
