// Old CPU fix (same logic and backup names as SWYF-OldCPU-Fix v3) for game build 0.1.71:
// Unity preloads Plugins\x86_64\ggml-cpu.dll (AVX2 build), so the game's EnsureAvxBackend() check always fails
// on CPUs without AVX2. Fix: copy the developer's own avx\ggml-cpu.dll over it and skip that call.
using System;
using System.IO;

static partial class Fixer
{
    static readonly string ScriptsRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "Managed"), "ScriptsAssDef.dll");
    static readonly string PluginRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "Plugins"), "x86_64");
    const string CpuBackupSuffix = ".oldcpufix171-backup";
    const string ScriptsOriginal = "b73088146d09e75d00b074ac05ce822f9911d181eb0d184f7dbaa5fc8406c2f9";
    const string ScriptsPatched  = "76d6f0d311de3bd7dcf6d16f2025848e43b8c07b7c533a80df441d29f81f6bb4";
    const string AvxBuild        = "52e1ce23beb706a45b9fe8cbf9cb3860aeb63dca082bb908ec6347fae7e3ad53";
    const string BepInExWinhttp  = "d3b62f4c9c3e2196ef82603c52d6b98c043a0d6c125c081fd33d7ca3798b41b8";
    const int CpuPatchOffset = 0x117E97;
    static readonly byte[] OriginalCall = { 0x28, 0x5b, 0x38, 0x00, 0x06 };
    static readonly byte[] PatchedCall  = { 0x00, 0x00, 0x00, 0x00, 0x00 };

    enum CpuFixState { NotNeeded, Installed, Installable, UnknownVersion, FixedByDevelopers }

    // Since 0.1.74 the game ships its own AVX backend as avx\ggml-cpu-avx.dll (different module name),
    // which fixes the preload clash this fix worked around.
    static bool DeveloperAvxFixPresent(string g)
    {
        return File.Exists(Path.Combine(Path.Combine(Path.Combine(g, PluginRel), "avx"), "ggml-cpu-avx.dll"));
    }

    static string ScriptsPath(string g) { return Path.Combine(g, ScriptsRel); }
    static string GgmlCpuPath(string g) { return Path.Combine(Path.Combine(g, PluginRel), "ggml-cpu.dll"); }
    static string AvxPath(string g) { return Path.Combine(Path.Combine(Path.Combine(g, PluginRel), "avx"), "ggml-cpu.dll"); }

    static bool CpuFixInstalled(string g)
    {
        string s = ScriptsPath(g), c = GgmlCpuPath(g);
        return File.Exists(s) && File.Exists(c) && Sha256(s) == ScriptsPatched && Sha256(c) == AvxBuild;
    }

    static CpuFixState GetCpuFixState(string g, bool hasAvx2)
    {
        if (CpuFixInstalled(g)) return CpuFixState.Installed;
        if (DeveloperAvxFixPresent(g)) return CpuFixState.FixedByDevelopers;
        if (hasAvx2) return CpuFixState.NotNeeded;
        string s = ScriptsPath(g), a = AvxPath(g);
        if (!File.Exists(s) || !File.Exists(GgmlCpuPath(g)) || !File.Exists(a)) return CpuFixState.UnknownVersion;
        string sh = Sha256(s);
        if ((sh != ScriptsOriginal && sh != ScriptsPatched) || Sha256(a) != AvxBuild) return CpuFixState.UnknownVersion;
        return CpuFixState.Installable;
    }

    static void InstallCpuFix(string g)
    {
        string scripts = ScriptsPath(g), ggml = GgmlCpuPath(g), avx = AvxPath(g);
        string sHash = Sha256(scripts), gHash = Sha256(ggml);
        CpuBackupOnce(scripts, ScriptsOriginal);
        CpuBackupOnce(ggml, null);
        if (gHash != AvxBuild) WriteAtomic(ggml, File.ReadAllBytes(avx));
        if (sHash != ScriptsPatched)
        {
            byte[] data = File.ReadAllBytes(scripts);
            for (int i = 0; i < OriginalCall.Length; i++)
                if (data[CpuPatchOffset + i] != OriginalCall[i]) throw new Exception("Unexpected ScriptsAssDef.dll contents; aborting.");
            Buffer.BlockCopy(PatchedCall, 0, data, CpuPatchOffset, PatchedCall.Length);
            if (Sha256(data) != ScriptsPatched) throw new Exception("Old CPU patch verification failed; aborting.");
            WriteAtomic(scripts, data);
        }
        if (!CpuFixInstalled(g)) throw new Exception("Old CPU fix final check failed. Run Steam > Verify integrity of game files.");
    }

    static bool UninstallCpuFix(string g)
    {
        string scripts = ScriptsPath(g), ggml = GgmlCpuPath(g);
        string sb = scripts + CpuBackupSuffix, gb = ggml + CpuBackupSuffix;
        bool ok = true;
        // Only ever restore over OUR patched files. If a game update replaced a file, keep the game's version
        // and just remove the stale backup (restoring it would mix old and new game files).
        if (File.Exists(scripts) && Sha256(scripts) == ScriptsPatched)
        {
            if (File.Exists(sb) && Sha256(sb) == ScriptsOriginal) File.Copy(sb, scripts, true); else ok = false;
        }
        if (File.Exists(ggml) && Sha256(ggml) == AvxBuild)
        {
            if (File.Exists(gb) && Sha256(gb) != AvxBuild) File.Copy(gb, ggml, true); else ok = false;
        }
        if (ok) { TryDelete(sb); TryDelete(gb); }
        return ok;
    }

    // After a game update that includes the developers' own fix: put back their original ggml-cpu.dll if our
    // copy is still there, and remove our leftover backups.
    static void CleanUpAfterDeveloperFix(string g)
    {
        string scripts = ScriptsPath(g), ggml = GgmlCpuPath(g), gb = ggml + CpuBackupSuffix;
        if (File.Exists(ggml) && Sha256(ggml) == AvxBuild && File.Exists(gb) && Sha256(gb) != AvxBuild)
        {
            File.Copy(gb, ggml, true);
            Line(Tr("  Removed the old CPU fix's replacement file - the game now has its own fix.", "  Removi o arquivo da correção antiga para CPU - o jogo agora tem a própria correção."));
        }
        if (!File.Exists(scripts) || Sha256(scripts) != ScriptsPatched) TryDelete(scripts + CpuBackupSuffix);
        if (!File.Exists(ggml) || Sha256(ggml) != AvxBuild) TryDelete(gb);
    }

    static void CpuBackupOnce(string path, string requiredHash)
    {
        string bak = path + CpuBackupSuffix;
        if (File.Exists(bak)) return;
        string h = Sha256(path);
        if (requiredHash != null && h != requiredHash) return;
        if (h == AvxBuild) return;
        File.Copy(path, bak, false);
    }

    static void DisableOldBepInEx(string g)
    {
        string w = Path.Combine(g, "winhttp.dll");
        if (File.Exists(w) && Sha256(w) == BepInExWinhttp)
        {
            string target = w + ".disabled-by-oldcpufix";
            if (File.Exists(target)) File.Delete(target);
            File.Move(w, target);
            Line(Tr("  Disabled the old BepInEx loader from the first fix version (it caused a crash).", "  Desativei o carregador BepInEx da primeira versão da correção (ele causava travamento)."));
        }
    }

    static void WriteAtomic(string path, byte[] data)
    {
        string tmp = path + ".fixer-tmp";
        File.WriteAllBytes(tmp, data);
        File.Copy(tmp, path, true);
        File.Delete(tmp);
    }

    static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
