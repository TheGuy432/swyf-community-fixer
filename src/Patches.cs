// Byte-region patches. A game file can carry several independent fixes at once (for example Assembly-CSharp.dll:
// the Qwen3 limits, the card-number pauses and the filler lines). Each fix is a list of regions; every region has
// the game's original bytes and the patched bytes. A file is accepted only if every region holds either its
// original or its patched bytes AND putting all regions back to original gives exactly the game's original file
// (SHA-256). So any fix can be switched on or off on its own, and removing everything restores the original
// file without needing a backup.
using System;
using System.Collections.Generic;
using System.IO;

static partial class Fixer
{
    sealed class Region
    {
        public readonly int Offset;
        public readonly byte[] Original;
        public readonly byte[][] Patched;   // [0] = what we write; others = older versions we recognise and replace
        public Region(int offset, string original, params string[] patched)
        {
            Offset = offset; Original = Hex(original);
            Patched = new byte[patched.Length][];
            for (int i = 0; i < patched.Length; i++)
            {
                Patched[i] = Hex(patched[i]);
                if (Patched[i].Length != Original.Length) throw new ArgumentException("region length");
            }
        }
        public bool Is(byte[] d, byte[] what)
        {
            if (Offset + what.Length > d.Length) return false;
            for (int i = 0; i < what.Length; i++) if (d[Offset + i] != what[i]) return false;
            return true;
        }
        public bool IsOriginal(byte[] d) { return Is(d, Original); }
        public bool IsPatched(byte[] d) { return Is(d, Patched[0]); }
        public bool IsKnown(byte[] d)
        {
            if (IsOriginal(d)) return true;
            foreach (byte[] p in Patched) if (Is(d, p)) return true;
            return false;
        }
        public void Write(byte[] d, bool patched) { Buffer.BlockCopy(patched ? Patched[0] : Original, 0, d, Offset, Original.Length); }
    }

    // Returns the original file bytes, or null if the file isn't the known game version (or holds unknown changes).
    static byte[] KnownOriginal(byte[] data, string originalHash, IEnumerable<Region[]> groups)
    {
        if (data == null) return null;
        byte[] copy = (byte[])data.Clone();
        foreach (Region[] g in groups)
            foreach (Region r in g)
            {
                if (!r.IsKnown(copy)) return null;
                r.Write(copy, false);
            }
        return Sha256(copy) == originalHash ? copy : null;
    }

    static bool GroupOn(byte[] data, Region[] g)
    {
        foreach (Region r in g) if (!r.IsPatched(data)) return false;
        return true;
    }

    static void SetGroup(byte[] data, Region[] g, bool on)
    {
        foreach (Region r in g) r.Write(data, on);
    }

    static byte[] ReadIfExists(string path) { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
}
