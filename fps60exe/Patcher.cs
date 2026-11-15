// FFX_enh_Patcher.exe - writes FFX_enh.exe / FFX-2_enh.exe: copies of the user's own FFX.exe / FFX-2.exe with the 60 fps
// patch code added as two sections. The originals are only read, and only the Steam release's EXEs (CRC-32 checked before
// anything is written: CheckOriginal) are patched. No DLL is needed by the result. docs\FPS60_EXE.md.
// The launcher builds this file in too (without Main) and calls Patcher.Ensure when 60 fps are applied without RevoFX.
//   FFX_enh_Patcher.exe [<game dir>] [--out <dir>] [--quiet]        (game dir: default = the folder of the patcher)
// The patch code is the embedded resource "f60.img": one relocatable PE image (module\fps60 + f60_host.cpp, built by build.py).
// Per game:
//   1. the image is laid out as in memory and relocated to the address it gets inside the copy (the copy is loaded at its
//      preferred base: its DYNAMIC_BASE flag is cleared).
//   2. section ".f60" = that image (flagged as data, so the patch's signature scan of executable sections skips it), followed by
//      the new import table: the EXE's own descriptors (unchanged) and the patch code's. Section ".f60s" = a start stub.
//   3. the TLS directory of the patch code becomes the EXE's (the game EXEs have none); the entry point becomes the stub:
//      VirtualProtect(.f60, executable), the image's entry (C runtime start + DllMain), then a jump to the game's entry point.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

static class Patcher
{
    // the version of FFX Enhanced (launcher, patcher, patch): shown in the launcher's settings as "FFX_enh v. <Version>"
    public const string Version = "0.90 Beta";

    static uint U32(byte[] b, long o) { return BitConverter.ToUInt32(b, (int)o); }
    static ushort U16(byte[] b, long o) { return BitConverter.ToUInt16(b, (int)o); }
    static void W32(byte[] b, long o, uint v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, (int)o, 4); }
    static void W16(byte[] b, long o, ushort v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, (int)o, 2); }
    static uint Align(uint v, uint a) { return (v + a - 1) / a * a; }

    sealed class Pe
    {
        public byte[] Raw; public int Nt, Opt, Dd, Sec, NSec;
        public uint ImageBase, SecAlign, FileAlign, SizeOfImage, SizeOfHeaders, Entry;
        public Pe(byte[] raw)
        {
            Raw = raw;
            if (raw.Length < 0x200 || U16(raw, 0) != 0x5A4D) throw new Exception("not an EXE");
            Nt = (int)U32(raw, 0x3C);
            if (U32(raw, Nt) != 0x4550 || U16(raw, Nt + 4) != 0x14C || U16(raw, Nt + 24) != 0x10B) throw new Exception("not a 32 bit Windows EXE");
            NSec = U16(raw, Nt + 6); Opt = Nt + 24; Dd = Opt + 96; Sec = Opt + U16(raw, Nt + 20);
            Entry = U32(raw, Opt + 16); ImageBase = U32(raw, Opt + 28); SecAlign = U32(raw, Opt + 32); FileAlign = U32(raw, Opt + 36);
            SizeOfImage = U32(raw, Opt + 56); SizeOfHeaders = U32(raw, Opt + 60);
        }
        public uint DirRva(int i) { return U32(Raw, Dd + 8 * i); }
        public uint DirSize(int i) { return U32(Raw, Dd + 8 * i + 4); }
        public string SecName(int i) { return Encoding.ASCII.GetString(Raw, Sec + 40 * i, 8).TrimEnd('\0'); }
        public uint SecVa(int i) { return U32(Raw, Sec + 40 * i + 12); }
        public uint SecVSize(int i) { return U32(Raw, Sec + 40 * i + 8); }
        public uint SecRawSize(int i) { return U32(Raw, Sec + 40 * i + 16); }
        public uint SecRawPtr(int i) { return U32(Raw, Sec + 40 * i + 20); }
        public byte[] Map()                                   // the image as the loader lays it out
        {
            var m = new byte[Align(SizeOfImage, SecAlign)];
            Buffer.BlockCopy(Raw, 0, m, 0, (int)Math.Min(SizeOfHeaders, (uint)Raw.Length));
            for (int i = 0; i < NSec; i++)
            {
                uint n = Math.Min(SecRawSize(i), Math.Max(SecVSize(i), 1u) > 0 ? Align(SecVSize(i), SecAlign) : 0);
                if (SecRawPtr(i) != 0 && n != 0) Buffer.BlockCopy(Raw, (int)SecRawPtr(i), m, (int)SecVa(i), (int)Math.Min(n, (uint)Raw.Length - SecRawPtr(i)));
            }
            return m;
        }
        public long Off(uint rva)                             // file offset of an RVA
        {
            for (int i = 0; i < NSec; i++) if (rva >= SecVa(i) && rva < SecVa(i) + Math.Max(SecVSize(i), SecRawSize(i))) return rva - SecVa(i) + SecRawPtr(i);
            if (rva < SizeOfHeaders) return rva;
            throw new Exception("address outside the file");
        }
    }

    static string CStr(byte[] b, long o) { int e = (int)o; while (b[e] != 0) e++; return Encoding.ASCII.GetString(b, (int)o, e - (int)o); }

    // step (optional): progress of this file, 0 .. 1
    public static string Patch(string src, string dst, byte[] img, Action<double> step = null)
    {
        if (step == null) step = delegate { };
        var pe = new Pe(File.ReadAllBytes(src)); step(0.15);
        CheckOriginal(src, pe.Raw);
        for (int i = 0; i < pe.NSec; i++)
        {
            if (pe.SecName(i).StartsWith(".f60")) throw new Exception("is already a patched copy");
            if (pe.SecName(i).StartsWith(".bind")) throw new Exception("carries Steam DRM (.bind section): not supported");
        }
        if (pe.DirSize(9) != 0) throw new Exception("has a TLS directory: not supported");
        int hdrEnd = pe.Sec + 40 * pe.NSec;
        if (hdrEnd + 80 > pe.SizeOfHeaders || hdrEnd + 80 > pe.SecRawPtr(0)) throw new Exception("no room for two section headers");
        int last = pe.NSec - 1;
        uint rva = Align(pe.SecVa(last) + pe.SecVSize(last), pe.SecAlign), bas = pe.ImageBase + rva;

        // 1. the patch image, relocated to bas
        var pl = new Pe(img); byte[] mem = pl.Map(); uint delta = bas - pl.ImageBase;
        uint rr = pl.DirRva(5), rs = pl.DirSize(5);
        if (rs == 0) throw new Exception("the patch image has no relocations (build error)");
        for (uint o = rr; o < rr + rs; )
        {
            uint page = U32(mem, o), size = U32(mem, o + 4); if (size < 8) break;
            for (uint k = 8; k < size; k += 2)
            {
                int e = U16(mem, o + k), type = e >> 12;
                if (type == 3) W32(mem, page + (e & 0xFFF), U32(mem, page + (e & 0xFFF)) + delta);
                else if (type != 0) throw new Exception("relocation type " + type + " in the patch image (build error)");
            }
            o += size;
        }
        W32(mem, pl.Opt + 28, bas); step(0.35);
        // 2. import table: the EXE's descriptors, then the patch image's with their RVAs moved by rva
        var table = new List<byte>(); uint vp = 0; int nOwn = 0, nExe = 0;
        for (long o = pe.Off(pe.DirRva(1)); U32(pe.Raw, o + 12) != 0 || U32(pe.Raw, o + 16) != 0; o += 20) { for (int k = 0; k < 20; k++) table.Add(pe.Raw[o + k]); nExe++; }
        for (uint o = pl.DirRva(1); U32(mem, o + 12) != 0 || U32(mem, o + 16) != 0; o += 20)
        {
            uint ilt = U32(mem, o), name = U32(mem, o + 12), iat = U32(mem, o + 16);
            if (ilt == 0) throw new Exception("import without a lookup table in the patch image (build error)");
            for (uint t = 0; U32(mem, ilt + t) != 0; t += 4)
            {
                uint v = U32(mem, ilt + t);
                if ((v & 0x80000000) == 0)
                {
                    if (CStr(mem, v + 2) == "VirtualProtect") vp = bas + iat + t;
                    W32(mem, ilt + t, v + rva); W32(mem, iat + t, v + rva);
                }
            }
            table.AddRange(BitConverter.GetBytes(ilt + rva)); table.AddRange(BitConverter.GetBytes(0u)); table.AddRange(BitConverter.GetBytes(0u));
            table.AddRange(BitConverter.GetBytes(name + rva)); table.AddRange(BitConverter.GetBytes(iat + rva)); nOwn++;
        }
        if (vp == 0) throw new Exception("the patch image does not import VirtualProtect (build error)");
        table.AddRange(new byte[20]); step(0.45);
        uint impRva = rva + (uint)mem.Length, f60Size = Align((uint)(mem.Length + table.Count), pe.SecAlign);
        var f60 = new byte[f60Size]; Buffer.BlockCopy(mem, 0, f60, 0, mem.Length); table.CopyTo(f60, mem.Length);
        // 3. the stub
        uint srva = rva + f60Size, sva = pe.ImageBase + srva;
        var s = new List<byte>();
        s.Add(0x68); s.AddRange(BitConverter.GetBytes(sva + 0x80));                 // push &old protection
        s.Add(0x6A); s.Add(0x40);                                                   // push PAGE_EXECUTE_READWRITE
        s.Add(0x68); s.AddRange(BitConverter.GetBytes(f60Size));
        s.Add(0x68); s.AddRange(BitConverter.GetBytes(bas));
        s.Add(0xFF); s.Add(0x15); s.AddRange(BitConverter.GetBytes(vp));            // call [VirtualProtect]
        s.Add(0x6A); s.Add(0x00); s.Add(0x6A); s.Add(0x01);                         // entry(image, DLL_PROCESS_ATTACH, 0)
        s.Add(0x68); s.AddRange(BitConverter.GetBytes(bas));
        s.Add(0xE8); s.AddRange(BitConverter.GetBytes(bas + pl.Entry - (sva + (uint)s.Count + 4)));
        s.Add(0xE9); s.AddRange(BitConverter.GetBytes(pe.ImageBase + pe.Entry - (sva + (uint)s.Count + 4)));   // jmp game entry
        var stub = new byte[Align(0x100, pe.FileAlign)]; s.CopyTo(stub);
        MakeStamp(img, pe.Raw).CopyTo(stub, StampAt);
        // 4. the copy
        int len = pe.Raw.Length; if (pe.DirRva(4) != 0 && pe.DirRva(4) < len) len = (int)pe.DirRva(4);   // the Authenticode signature (end of the file) is not valid for the copy
        uint p1 = Align((uint)len, pe.FileAlign), p2 = p1 + f60Size;
        var o2 = new byte[p2 + stub.Length];
        Buffer.BlockCopy(pe.Raw, 0, o2, 0, len); Buffer.BlockCopy(f60, 0, o2, (int)p1, f60.Length); Buffer.BlockCopy(stub, 0, o2, (int)p2, stub.Length); step(0.55);
        Action<int, string, uint, uint, uint, uint, uint> hdr = (at, name, vs, va, rsz, rp, ch) =>
        {
            Array.Clear(o2, at, 40); Encoding.ASCII.GetBytes(name, 0, name.Length, o2, at);
            W32(o2, at + 8, vs); W32(o2, at + 12, va); W32(o2, at + 16, rsz); W32(o2, at + 20, rp); W32(o2, at + 36, ch);
        };
        hdr(hdrEnd, ".f60", f60Size, rva, f60Size, p1, 0xC0000040);
        hdr(hdrEnd + 40, ".f60s", 0x100, srva, (uint)stub.Length, p2, 0xE0000020);
        W16(o2, pe.Nt + 6, (ushort)(pe.NSec + 2));
        W32(o2, pe.Opt + 16, srva);                                                 // AddressOfEntryPoint
        W32(o2, pe.Opt + 56, Align(srva + 0x100, pe.SecAlign));                     // SizeOfImage
        W32(o2, pe.Opt + 64, 0);                                                    // CheckSum
        W16(o2, pe.Opt + 70, (ushort)(U16(o2, pe.Opt + 70) & ~0x40));               // no DYNAMIC_BASE
        W32(o2, pe.Dd + 8, impRva); W32(o2, pe.Dd + 12, (uint)table.Count);
        W32(o2, pe.Dd + 32, 0); W32(o2, pe.Dd + 36, 0);                             // no signature
        if (pl.DirSize(9) != 0) { W32(o2, pe.Dd + 72, pl.DirRva(9) + rva); W32(o2, pe.Dd + 76, pl.DirSize(9)); }
        string tmp = dst + ".tmp"; const int Chunk = 1 << 20;
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            for (int o = 0; o < o2.Length; o += Chunk) { fs.Write(o2, o, Math.Min(Chunk, o2.Length - o)); step(0.55 + 0.4 * Math.Min(1.0, (o + Chunk) / (double)o2.Length)); }
        if (File.Exists(dst)) File.Delete(dst);
        File.Move(tmp, dst); step(1.0);
        return string.Format("{0} -> {1}: {2:N0} KB, patch code at 0x{3:X}, {4} + {5} import DLLs", Path.GetFileName(src), Path.GetFileName(dst), o2.Length / 1024, bas, nExe, nOwn);
    }

    // Only the Steam release's EXEs are patched (CRC-32 of the whole file; the patch's signatures were made on them). Another
    // file - a game update, a modified or already patched EXE - stops the patch with an error, nothing is written.
    public static readonly string[] OriginalNames = { "FFX.exe", "FFX-2.exe" };
    public static readonly uint[] OriginalCrcs = { 0xFC9264CD, 0x6C2274E0 };
    static void CheckOriginal(string src, byte[] raw)
    {
        string name = Path.GetFileName(src); int i = -1;
        for (int k = 0; k < OriginalNames.Length; k++) if (string.Equals(OriginalNames[k], name, StringComparison.OrdinalIgnoreCase)) i = k;
        uint crc = Crc32(raw);
        if (i < 0) throw new Exception(name + ": not FFX.exe / FFX-2.exe - not patched");
        if (crc != OriginalCrcs[i])
            throw new Exception(name + " has CRC-32 " + crc.ToString("X8") + ", expected " + OriginalCrcs[i].ToString("X8") + " (the Steam release): not the original EXE, patch stopped");
    }

    // Version stamp of a patched copy, 16 bytes at offset 0xC0 of its last section (.f60s): "F60V", CRC-32 of the patch image,
    // CRC-32 and length of the EXE it was made from. A copy made by another build of the patcher (another patch image), or
    // from another game EXE (a game update), has another stamp and is written again (Refresh).
    const int StampAt = 0xC0;
    static uint[] crcTable;
    static uint Crc32(byte[] d)
    {
        if (crcTable == null) { var t = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[i] = c; } crcTable = t; }
        uint r = 0xFFFFFFFF; for (int i = 0; i < d.Length; i++) r = crcTable[(r ^ d[i]) & 0xFF] ^ (r >> 8); return ~r;
    }
    static byte[] MakeStamp(byte[] img, byte[] src)
    {
        var b = new byte[16]; b[0] = (byte)'F'; b[1] = (byte)'6'; b[2] = (byte)'0'; b[3] = (byte)'V';
        W32(b, 4, Crc32(img)); W32(b, 8, Crc32(src)); W32(b, 12, (uint)src.Length); return b;
    }
    static byte[] ReadStamp(string exe)                   // null: not a patched copy, or one without a stamp (older patcher)
    {
        try
        {
            var pe = new Pe(File.ReadAllBytes(exe)); int last = pe.NSec - 1;
            if (pe.SecName(last) != ".f60s" || pe.SecRawSize(last) < StampAt + 16) return null;
            var b = new byte[16]; Buffer.BlockCopy(pe.Raw, (int)pe.SecRawPtr(last) + StampAt, b, 0, 16);
            return b[0] == 'F' && b[1] == '6' && b[2] == '0' && b[3] == 'V' ? b : null;
        }
        catch { return null; }
    }

    // For the launcher's start: every existing 60 fps EXE whose stamp is not the one this patcher would write (older
    // patch code, or the game EXE changed) is written again from the game's EXE. One line per file; empty = all current.
    public static List<string> Refresh(string dir)
    {
        var log = MigrateNames(dir); byte[] img = null;
        foreach (string[] g in Games)
        {
            string src = SourceOf(dir, g[0]), dst = Path.Combine(dir, g[1]);
            if (!File.Exists(src) || !File.Exists(dst)) continue;
            try
            {
                if (img == null) img = Resource("f60.img");
                byte[] have = ReadStamp(dst), want = MakeStamp(img, File.ReadAllBytes(src));
                bool same = have != null; for (int i = 0; same && i < 16; i++) same = have[i] == want[i];
                if (same) continue;
                string why = have == null ? "no version stamp" : U32(have, 4) != U32(want, 4) ? "older patch code" : "the game EXE changed";
                log.Add(g[1] + " is outdated (" + why + "): " + Patch(src, dst, img));
            }
            catch (Exception e) { log.Add(g[1] + ": " + e.Message + " - not updated"); }
        }
        string im = EnsureIni(dir); if (im != null) log.Add(im);
        return log;
    }

    // ffx_enh.ini (the settings file of FFX Enhanced; FFX_60fps.ini before 2026-11-15, renamed here when it is the only one):
    // written when missing; a file of another layout (no "; settings v3" line) gets the current layout with every value it
    // had (keys of the template keep their place, other keys are added to their section, other sections at the end; the
    // pacing value 0 / 1 of the first build becomes optimized / original). The file before stays as ffx_enh.ini.bak.
    public const string IniName = "ffx_enh.ini", OldIniName = "FFX_60fps.ini";
    public static string EnsureIni(string dir)
    {
        try
        {
            string ini = Path.Combine(dir, IniName), old = Path.Combine(dir, OldIniName), msg = "";
            if (!File.Exists(ini) && File.Exists(old)) { File.Move(old, ini); msg = OldIniName + " renamed to " + IniName + "; "; }
            string tpl = Encoding.ASCII.GetString(Resource(IniName));
            if (!File.Exists(ini)) { File.WriteAllText(ini, tpl, Encoding.ASCII); return msg + IniName + " written"; }
            string cur = Encoding.ASCII.GetString(File.ReadAllBytes(ini));
            if (cur.Contains("; settings v3")) return msg.Length > 0 ? msg.TrimEnd(' ', ';') : null;
            // the values of the file: section -> key -> value (case-insensitive), in their order
            var vals = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase); string sec = "";
            foreach (string raw in cur.Split('\n'))
            {
                string l = raw.Trim(); if (l.Length == 0 || l.StartsWith(";")) continue;
                if (l.StartsWith("[")) { sec = l.Trim('[', ']').Trim(); continue; }
                int eq = l.IndexOf('='); if (eq <= 0) continue;
                string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                if (sec.Equals("fps60", StringComparison.OrdinalIgnoreCase) && k.Equals("pacing", StringComparison.OrdinalIgnoreCase) && (v == "0" || v == "1")) v = v == "1" ? "original" : "optimized";
                if (!vals.ContainsKey(sec)) vals[sec] = new List<KeyValuePair<string, string>>();
                vals[sec].RemoveAll(x => x.Key.Equals(k, StringComparison.OrdinalIgnoreCase)); vals[sec].Add(new KeyValuePair<string, string>(k, v));
            }
            var outL = new List<string>(); var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase); sec = "";
            Action<string> flush = s =>                              // keys of section s the template does not have, at its end
            {
                List<KeyValuePair<string, string>> kv; if (s.Length == 0 || !vals.TryGetValue(s, out kv)) return;
                foreach (var x in kv) if (used.Add(s + "|" + x.Key)) outL.Add(x.Key + "=" + x.Value);
            };
            foreach (string raw in tpl.Replace("\r\n", "\n").Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("[")) { while (outL.Count > 0 && outL[outL.Count - 1].Length == 0) outL.RemoveAt(outL.Count - 1); flush(sec); outL.Add(""); sec = l.Trim('[', ']').Trim(); outL.Add(raw); continue; }
                int eq = l.IndexOf('=');
                if (!l.StartsWith(";") && eq > 0 && sec.Length > 0)
                {
                    string k = l.Substring(0, eq).Trim(); List<KeyValuePair<string, string>> kv;
                    if (vals.TryGetValue(sec, out kv)) { int at = kv.FindIndex(x => x.Key.Equals(k, StringComparison.OrdinalIgnoreCase)); if (at >= 0) { outL.Add(k + "=" + kv[at].Value); used.Add(sec + "|" + k); continue; } }
                    used.Add(sec + "|" + k);
                }
                outL.Add(raw);
            }
            while (outL.Count > 0 && outL[outL.Count - 1].Length == 0) outL.RemoveAt(outL.Count - 1);
            flush(sec);
            foreach (var s in vals.Keys)                             // sections the template does not have
            {
                bool inTpl = tpl.IndexOf("[" + s + "]", StringComparison.OrdinalIgnoreCase) >= 0; if (inTpl || s.Length == 0) continue;
                outL.Add(""); outL.Add("[" + s + "]"); flush(s);
            }
            File.Copy(ini, ini + ".bak", true);
            File.WriteAllText(ini, string.Join("\r\n", outL.ToArray()).TrimStart('\r', '\n') + "\r\n", Encoding.ASCII);
            return msg + IniName + " brought to the current layout (all values kept; the file before is " + IniName + ".bak)";
        }
        catch (Exception e) { return IniName + ": " + e.Message; }
    }

    // the file a 60 fps EXE is made from (game dir, game EXE name): the game EXE itself; the launcher returns the backed-up
    // original instead while the game folder holds a patched copy under the original name (Proton compatibility mode)
    public static Func<string, string, string> SourceOf = (dir, exe) => Path.Combine(dir, exe);
    public static bool IsPatched(string exe)
    {
        try { var pe = new Pe(File.ReadAllBytes(exe)); for (int i = 0; i < pe.NSec; i++) if (pe.SecName(i).StartsWith(".f60")) return true; }
        catch { }
        return false;
    }

    public static bool Available { get { return Assembly.GetExecutingAssembly().GetManifestResourceInfo("f60.img") != null; } }

    // For the launcher: writes the 60 fps EXEs that are missing in the game folder (and ffx_enh.ini when missing); one line per file.
    // progress (optional): the file being written and the progress of all of them, 0 .. 1.
    public static List<string> Ensure(string dir, Action<string, double> progress = null)
    {
        var log = new List<string>(); byte[] img = null;
        var todo = new List<string[]>();
        foreach (string[] g in Games) if (File.Exists(Path.Combine(dir, g[0])) && !File.Exists(Path.Combine(dir, g[1]))) todo.Add(g);
        for (int i = 0; i < todo.Count; i++)
        {
            string[] g = todo[i]; string src = SourceOf(dir, g[0]), dst = Path.Combine(dir, g[1]); int n = i;
            Action<double> step = x => { if (progress != null) progress(g[1], (n + x) / todo.Count); };
            step(0);
            try { if (img == null) img = Resource("f60.img"); log.Add(Patch(src, dst, img, step)); }
            catch (Exception e) { log.Add(g[0] + ": " + e.Message + " - not patched"); }
        }
        string im = EnsureIni(dir); if (im != null) log.Add(im);
        return log;
    }

    // The patched EXEs were FFX_60fps.exe / FFX-2_60fps.exe before 2026-11-15: such a file is renamed to its new name (the
    // version stamp check that follows writes it again when it is not current); next to an existing new one it is removed.
    public static List<string> MigrateNames(string dir)
    {
        var log = new List<string>();
        foreach (string[] g in Games)
        {
            string oldName = Path.GetFileNameWithoutExtension(g[0]) + "_60fps.exe", o = Path.Combine(dir, oldName), n = Path.Combine(dir, g[1]);
            try
            {
                if (!File.Exists(o)) continue;
                if (File.Exists(n)) { File.Delete(o); log.Add(oldName + " removed (" + g[1] + " is the patched EXE now)"); }
                else { File.Move(o, n); log.Add(oldName + " renamed to " + g[1]); }
            }
            catch (Exception e) { log.Add(oldName + ": " + e.Message); }
        }
        return log;
    }

    static readonly string[][] Games = { new[] { "FFX.exe", "FFX_enh.exe" }, new[] { "FFX-2.exe", "FFX-2_enh.exe" } };

    static byte[] Resource(string name)
    {
        using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (st == null) throw new Exception("resource " + name + " missing (build error)");
            var b = new byte[st.Length]; int n = 0; while (n < b.Length) n += st.Read(b, n, b.Length - n); return b;
        }
    }

#if PATCHER_EXE
    static int Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory, outDir = null; bool quiet = false; int rc = 0, done = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            else if (args[i] == "--quiet") quiet = true;
            else dir = args[i];
        }
        if (outDir == null) outDir = dir;
        Console.WriteLine("FFX Enhanced " + Version + " - FFX / FFX-2 60 fps patcher (patch code: FFX-60fps by Peter Nguyen, Apache-2.0; MinHook, BSD 2-Clause)");
        Console.WriteLine("game folder: " + dir);
        try
        {
            byte[] img = Resource("f60.img");
            Directory.CreateDirectory(outDir);
            foreach (string[] g in Games)
            {
                string src = Path.Combine(dir, g[0]);
                if (!File.Exists(src)) { Console.WriteLine(g[0] + ": not found"); continue; }
                try { Console.WriteLine(Patch(src, Path.Combine(outDir, g[1]), img)); done++; }
                catch (Exception e) { Console.WriteLine(g[0] + ": " + e.Message + " - not patched"); rc = 1; }
            }
            if (done > 0) { string im = EnsureIni(outDir); if (im != null) Console.WriteLine(im); }
            if (done == 0) { Console.WriteLine("Nothing written. Put the patcher next to FFX.exe / FFX-2.exe or pass the game folder."); rc = 1; }
            else Console.WriteLine("The original EXE files were not changed.");
        }
        catch (Exception e) { Console.WriteLine("error: " + e.Message); rc = 1; }
        if (!quiet) { Console.WriteLine("Press Enter to close."); Console.ReadLine(); }
        return rc;
    }
#endif
}
