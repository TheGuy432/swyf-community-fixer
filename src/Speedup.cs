// SWYF Speech Speed-up (UNOFFICIAL) - for Scam With Your Friends
// Converts the game's Whisper speech-recognition models (StreamingAssets\Whisper\ggml-base*.bin) from
// 16-bit (f16) weights to 8-bit (q8_0) weights, exactly like whisper.cpp's own "quantize" tool does.
// Same model, near-identical accuracy, much faster on older CPUs (no F16C, e.g. Intel 2nd gen Core / Sandy Bridge),
// and ~half the memory.
// v2 also patches com.whisper.unity.dll (WhisperWrapper.InferenceWhisper) so Whisper's encoder window (audio_ctx)
// matches the clip length (+1.5 s, minimum 512 = ~10 s) instead of always processing a full 30 s window.
// Clips longer than ~28.5 s keep the original full window. Originals are kept as backups; run again to undo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

static partial class Fixer
{
        static readonly string WhisperRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "StreamingAssets"), "Whisper");
    static readonly string[] ModelNames = { "ggml-base.en.bin", "ggml-base.bin" };
    const string BackupSuffix = ".speedup-backup";
    static readonly string WhisperDllRel = Path.Combine(Path.Combine("Scam With Your Friends_Data", "Managed"), "com.whisper.unity.dll");
    const string DllOriginal = "611ca87d34dbb017ca0bac5a4a493c92d122cd5af211d78ab58bd46ec653e07b";
    const string DllPatched  = "fb2ebfbe63008e3953776d11ce24a6bb0a99d5d1c73862fb4a3f4ccd8352fdc7";
    const int BodyOffset = 0x19F4;
    const string OrigBody = "72970400702881000006735c00000a0a066f5d00000a03250c2c05088e692d0516e00b2b0908168f2e000001e00b027b780000040407038e6928b80000060d092c1772dd040070098c2d000001284e00000a287f000006162a140c723f050070066f5e00000a8c2c000001284e00000a2881000006172a";
    const string NewBody  = "038e6920400100005b1f4b580d0920000200002f0620000200000d0920dc0500003202160d0f02097d1501000403250c2c05088e692d0516e00b2b0908168f2e000001e00b027b780000040407038e6928b80000060d092c1772dd040070098c2d000001284e00000a287f000006162a140c172a000000";
    const string TempSuffix = ".speedup-tmp";

    const int Magic = 0x67676d6c;
    const int FtypeF16 = 1, FtypeQ8 = 7, QntVersion = 2, QntFactor = 1000;
    const int TypeF32 = 0, TypeF16 = 1, TypeQ8_0 = 8;
    static readonly HashSet<string> Skip = new HashSet<string> {
        "encoder.conv1.bias", "encoder.conv2.bias", "encoder.positional_embedding", "decoder.positional_embedding" };

    static int UninstallModels(string dir)
    {
        bool ok = true;
        foreach (string n in ModelNames)
        {
            string p = Path.Combine(dir, n), bak = p + BackupSuffix;
            if (!File.Exists(bak)) { if (File.Exists(p) && ReadFtype(p) == FtypeQ8) ok = false; continue; }
            if (File.Exists(p) && ReadFtype(p) != FtypeQ8)
            {
                // The game (update / Verify) already put its own model back: keep it, drop our stale backup.
                TryDelete(bak);
                continue;
            }
            if (ReadFtype(bak) != FtypeF16) { ok = false; continue; }
            if (File.Exists(p)) File.Delete(p);
            File.Move(bak, p);
        }
        Line("");
        if (ok) Line("Original speech model files restored.");
        else Line("Could not restore everything. Run Steam > Properties > Installed Files > Verify integrity of game files.");
        return ok ? 0 : 1;
    }

    static void PatchDll(string dll)
    {
        byte[] data = File.ReadAllBytes(dll);
        byte[] ob = Hex(OrigBody), nb = Hex(NewBody);
        for (int i = 0; i < ob.Length; i++) if (data[BodyOffset + i] != ob[i]) throw new Exception("Unexpected com.whisper.unity.dll contents; not patched.");
        Buffer.BlockCopy(nb, 0, data, BodyOffset, nb.Length);
        if (Sha256(data) != DllPatched) throw new Exception("Patch verification failed; com.whisper.unity.dll not changed.");
        string bak = dll + BackupSuffix;
        if (!File.Exists(bak) || Sha256(bak) != DllOriginal) File.Copy(dll, bak, true);
        string tmp = dll + TempSuffix;
        File.WriteAllBytes(tmp, data);
        File.Copy(tmp, dll, true);
        File.Delete(tmp);
        if (Sha256(dll) != DllPatched) throw new Exception("Writing com.whisper.unity.dll failed. Run Steam > Verify integrity of game files.");
    }

    static int UninstallDll(string dll)
    {
        if (!File.Exists(dll) || Sha256(dll) != DllPatched) return 0;
        byte[] data = File.ReadAllBytes(dll);
        byte[] ob = Hex(OrigBody);
        Buffer.BlockCopy(ob, 0, data, BodyOffset, ob.Length);
        if (Sha256(data) != DllOriginal) { Line("Could not restore com.whisper.unity.dll. Run Steam > Verify integrity of game files."); return 1; }
        File.WriteAllBytes(dll, data);
        try { File.Delete(dll + BackupSuffix); } catch { }
        Line("Shorter listening window removed - original com.whisper.unity.dll restored.");
        return 0;
    }

    static byte[] Hex(string h)
    {
        byte[] b = new byte[h.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
        return b;
    }

    static string Sha256(string path) { return Sha256(File.ReadAllBytes(path)); }
    static string Sha256(byte[] data)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }

    static int ReadFtype(string path)
    {
        using (var r = new BinaryReader(File.OpenRead(path)))
        {
            if (r.BaseStream.Length < 48 || r.ReadInt32() != Magic) return -1;
            for (int i = 0; i < 10; i++) r.ReadInt32();
            return r.ReadInt32() % QntFactor;
        }
    }

    // ---- whisper.cpp examples/quantize equivalent (q8_0) ----
    static void Quantize(string inPath, string outPath)
    {
        using (var r = new BinaryReader(new BufferedStream(File.OpenRead(inPath), 1 << 20)))
        using (var w = new BinaryWriter(new BufferedStream(File.Create(outPath), 1 << 20)))
        {
            long len = r.BaseStream.Length;
            if (r.ReadInt32() != Magic) throw new Exception("not a Whisper model file");
            w.Write(Magic);
            for (int i = 0; i < 10; i++) w.Write(r.ReadInt32());
            int ftype = r.ReadInt32();
            if (ftype % QntFactor != FtypeF16) throw new Exception("model is not in 16-bit format (ftype " + ftype + ")");
            w.Write(QntVersion * QntFactor + FtypeQ8);

            int nMel = r.ReadInt32(), nFft = r.ReadInt32();
            w.Write(nMel); w.Write(nFft);
            w.Write(r.ReadBytes(checked(nMel * nFft * 4)));

            int nVocab = r.ReadInt32();
            w.Write(nVocab);
            for (int i = 0; i < nVocab; i++)
            {
                int l = r.ReadInt32();
                if (l < 0 || l > 128) throw new Exception("bad vocabulary entry");
                w.Write(l); w.Write(r.ReadBytes(l));
            }

            int tensors = 0;
            while (r.BaseStream.Position < len)
            {
                int nDims = r.ReadInt32(), nameLen = r.ReadInt32(), ttype = r.ReadInt32();
                if (nDims < 1 || nDims > 4 || nameLen < 1 || nameLen > 256) throw new Exception("bad tensor header");
                int[] ne = new int[nDims];
                long n = 1;
                for (int i = 0; i < nDims; i++) { ne[i] = r.ReadInt32(); n *= ne[i]; }
                byte[] nameBytes = r.ReadBytes(nameLen);
                string name = System.Text.Encoding.UTF8.GetString(nameBytes);

                bool quant = nDims == 2 && !Skip.Contains(name);
                if (quant && ttype != TypeF16 && ttype != TypeF32) throw new Exception("unsupported tensor type in " + name);
                if (!quant && ttype != TypeF16 && ttype != TypeF32) throw new Exception("unexpected tensor type in " + name);
                if (quant && ne[0] % 32 != 0) throw new Exception("tensor row size not divisible by 32: " + name);

                w.Write(nDims); w.Write(nameLen); w.Write(quant ? TypeQ8_0 : ttype);
                foreach (int d in ne) w.Write(d);
                w.Write(nameBytes);

                int bpe = ttype == TypeF32 ? 4 : 2;
                byte[] raw = r.ReadBytes(checked((int)(n * bpe)));
                if (raw.Length != n * bpe) throw new Exception("model file is truncated");
                if (!quant) { w.Write(raw); }
                else
                {
                    float[] f = new float[n];
                    if (ttype == TypeF16) for (long i = 0; i < n; i++) f[i] = F16ToF32((ushort)(raw[2 * i] | (raw[2 * i + 1] << 8)));
                    else Buffer.BlockCopy(raw, 0, f, 0, raw.Length);
                    w.Write(QuantizeQ8_0(f));
                }
                tensors++;
            }
            if (tensors == 0) throw new Exception("no tensors in model file");
        }
    }

    // ggml quantize_row_q8_0_ref: blocks of 32 -> fp16 scale + 32 x int8
    static byte[] QuantizeQ8_0(float[] x)
    {
        int nb = x.Length / 32;
        byte[] o = new byte[nb * 34];
        for (int b = 0; b < nb; b++)
        {
            int xi = b * 32, oi = b * 34;
            float amax = 0f;
            for (int j = 0; j < 32; j++) { float v = Math.Abs(x[xi + j]); if (v > amax) amax = v; }
            float d = amax / 127f;
            float id = d != 0f ? 1.0f / d : 0.0f;
            ushort hd = F32ToF16(d);
            o[oi] = (byte)hd; o[oi + 1] = (byte)(hd >> 8);
            for (int j = 0; j < 32; j++)
            {
                float x0 = (float)(x[xi + j] * id);
                o[oi + 2 + j] = (byte)(sbyte)Math.Round((double)x0, MidpointRounding.AwayFromZero); // roundf
            }
        }
        return o;
    }

    static float[] f16Table;
    static float F16ToF32(ushort h)
    {
        if (f16Table == null)
        {
            var t = new float[65536];
            for (int i = 0; i < 65536; i++)
            {
                int s = i >> 15, e = (i >> 10) & 0x1f, m = i & 0x3ff;
                float v;
                if (e == 0) v = m * 5.9604644775390625e-8f; // 2^-24, exact
                else if (e == 31) v = m == 0 ? float.PositiveInfinity : float.NaN;
                else v = BitConverter.ToSingle(BitConverter.GetBytes((e + 112) << 23 | m << 13), 0);
                t[i] = s != 0 ? -v : v;
            }
            f16Table = t;
        }
        return f16Table[h];
    }

    // ggml_compute_fp32_to_fp16 (round to nearest even)
    static ushort F32ToF16(float f)
    {
        const float scaleToInf = 5.192296858534828e+33f;   // 0x1.0p+112
        const float scaleToZero = 7.703719777548943e-34f;  // 0x1.0p-110
        float bas = (float)((float)(Math.Abs(f) * scaleToInf) * scaleToZero);
        uint w = Bits(f);
        uint shl1W = w + w;
        uint sign = w & 0x80000000u;
        uint bias = shl1W & 0xFF000000u;
        if (bias < 0x71000000u) bias = 0x71000000u;
        bas = (float)(FromBits((bias >> 1) + 0x07800000u) + bas);
        uint bits = Bits(bas);
        uint expBits = (bits >> 13) & 0x00007C00u;
        uint mantBits = bits & 0x00000FFFu;
        uint nonsign = expBits + mantBits;
        return (ushort)((sign >> 16) | (shl1W > 0xFF000000u ? 0x7E00u : nonsign));
    }
    static uint Bits(float f) { return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0); }
    static float FromBits(uint u) { return BitConverter.ToSingle(BitConverter.GetBytes(u), 0); }

    // Re-read the converted file end to end and check every tensor's size matches its declared type.
    static void Validate(string path)
    {
        using (var r = new BinaryReader(new BufferedStream(File.OpenRead(path), 1 << 20)))
        {
            long len = r.BaseStream.Length;
            if (r.ReadInt32() != Magic) throw new Exception("validation failed (magic)");
            for (int i = 0; i < 10; i++) r.ReadInt32();
            if (r.ReadInt32() % QntFactor != FtypeQ8) throw new Exception("validation failed (ftype)");
            int nMel = r.ReadInt32(), nFft = r.ReadInt32();
            r.BaseStream.Seek((long)nMel * nFft * 4, SeekOrigin.Current);
            int nVocab = r.ReadInt32();
            for (int i = 0; i < nVocab; i++) { int l = r.ReadInt32(); r.BaseStream.Seek(l, SeekOrigin.Current); }
            int tensors = 0;
            while (r.BaseStream.Position < len)
            {
                int nDims = r.ReadInt32(), nameLen = r.ReadInt32(), ttype = r.ReadInt32();
                long n = 1;
                for (int i = 0; i < nDims; i++) n *= r.ReadInt32();
                r.BaseStream.Seek(nameLen, SeekOrigin.Current);
                long bytes = ttype == TypeF32 ? n * 4 : ttype == TypeF16 ? n * 2 : ttype == TypeQ8_0 ? n / 32 * 34 : -1;
                if (bytes < 0 || r.BaseStream.Position + bytes > len) throw new Exception("validation failed (tensor)");
                r.BaseStream.Seek(bytes, SeekOrigin.Current);
                tensors++;
            }
            if (tensors == 0 || r.BaseStream.Position != len) throw new Exception("validation failed (end)");
        }
    }



}
