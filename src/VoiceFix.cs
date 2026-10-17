// Caller voice fix (PocketTTS.dll, game builds 0.1.71 - 0.1.74):
// The Portuguese voice model's "end of speech" score is high for its first 3-5 steps (~0.3 s) before it starts
// talking. The game stops at the first such step, so every Portuguese caller line is cut off after ~0.3 s.
// Fix: ignore the end-of-speech score for the first 8 steps (~0.64 s). Other languages never end that early,
// except one-word lines, which get up to ~0.5 s of near-silence added at the end.
//
// In PocketTTSModel.RunPrompt the check
//     if (num13 < 0 && num15 > -4f) num13 = num14;      // IL 0x06C6, 18 bytes -> NOPs
// is moved into the space of an optional profiling block right before the break check
//     if (EnableProfiling) frameBirthTimes[num14] = ...  // IL 0x0793, 54 bytes
// and becomes
//     if (num13 < 0 && num14 >= 8 && num15 > -4f) num13 = num14;
// frameBirthTimes only feeds the [TTS RT] debug log line.
using System;
using System.IO;

static partial class Fixer
{
    static readonly string PocketDllRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "Managed"), "PocketTTS.dll");
    const string PocketOriginal = "b47b39cd9be3417efd2c1c0e61a124f762685b4caf116e20e08165b706bf11bf";
    const string PocketPatched  = "6cf88903554073d028f07fca9a8a8fd8274fa46f32b37e4518e226b6754bd109";
    const string VoiceBackupSuffix = ".voicefix-backup";
    const int PocketCode = 0x3280;                    // file offset of RunPrompt's IL code
    const int LatchAt = PocketCode + 0x06C6, ProfileAt = PocketCode + 0x0793;
    // The original file is identified by its SHA-256, so the bytes at both places are known exactly.
    const int LatchLen = 18, ProfileLen = 54;
    const string NewLatch = "1128162F13112D1F08320D113122000080C03604112D1328"; // 24 bytes, K = 8

    static string PocketPath(string g) { return Path.Combine(g, PocketDllRel); }

    static void InstallVoiceFix(string gameDir)
    {
        string dll = PocketPath(gameDir);
        string h = File.Exists(dll) ? Sha256(dll) : null;
        if (h == PocketPatched) { Line(Tr("  Already installed.", "  Já instalada.")); return; }
        if (h != PocketOriginal)
        {
            Line(Tr("  SKIPPED (your game version has a different PocketTTS.dll - maybe it's fixed).", "  PULADA (sua versão do jogo tem outro PocketTTS.dll - talvez já esteja corrigido)."));
            return;
        }
        Console.Write(Tr("  Fixing Portuguese callers being cut off after a split second ... ", "  Corrigindo as vozes em português cortadas depois de uma fração de segundo ... "));
        byte[] data = File.ReadAllBytes(dll);
        for (int i = 0; i < LatchLen; i++) data[LatchAt + i] = 0x00;               // nop
        byte[] nl = Hex(NewLatch);
        for (int i = 0; i < ProfileLen; i++) data[ProfileAt + i] = i < nl.Length ? nl[i] : (byte)0x00;
        if (Sha256(data) != PocketPatched) throw new Exception("Voice fix verification failed; PocketTTS.dll not changed.");
        string bak = dll + VoiceBackupSuffix;
        if (!File.Exists(bak) || Sha256(bak) != PocketOriginal) File.Copy(dll, bak, true);
        string tmp = dll + TempSuffix;
        File.WriteAllBytes(tmp, data);
        File.Copy(tmp, dll, true);
        File.Delete(tmp);
        if (Sha256(dll) != PocketPatched) throw new Exception("Writing PocketTTS.dll failed. Run Steam > Verify integrity of game files.");
        Line(Tr("done", "pronto"));
    }

    static bool VoiceFixInstalled(string gameDir)
    {
        string dll = PocketPath(gameDir);
        return File.Exists(dll) && Sha256(dll) == PocketPatched;
    }

    static int UninstallVoiceFix(string gameDir)
    {
        string dll = PocketPath(gameDir), bak = dll + VoiceBackupSuffix;
        if (!File.Exists(dll) || Sha256(dll) != PocketPatched)
        {
            TryDelete(bak);   // a game update replaced the file: keep the game's version
            return 0;
        }
        if (!File.Exists(bak) || Sha256(bak) != PocketOriginal)
        {
            Line(Tr("  Could not restore PocketTTS.dll (backup missing). Run Steam > Verify integrity of game files.", "  Não consegui restaurar o PocketTTS.dll (backup não encontrado). Use Steam > Verificar integridade dos arquivos do jogo."));
            return 1;
        }
        File.Copy(bak, dll, true);
        if (Sha256(dll) != PocketOriginal) { Line(Tr("  Could not restore PocketTTS.dll. Run Steam > Verify integrity of game files.", "  Não consegui restaurar o PocketTTS.dll. Use Steam > Verificar integridade dos arquivos do jogo.")); return 1; }
        TryDelete(bak);
        Line(Tr("  Caller voice fix removed - original PocketTTS.dll restored.", "  Correção das vozes removida - PocketTTS.dll original restaurado."));
        return 0;
    }
}
