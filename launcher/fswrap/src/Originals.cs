// The game's original EXEs: a backup at every launcher start, and the Proton compatibility mode.
//
// Backup (EnsureBackups, every start, also with RevoFX): FFX.exe, FFX-2.exe and FFX&X-2_Will.exe are copied to
// <game dir>\backup_original\ - only a file with the CRC-32 of the Steam release (Crcs, given by the user) is backed up, a
// backup that already exists with that CRC is kept as it is, and every copy is read back and compared (CRC) before it is
// put in place (through a .tmp file). A file with another CRC (a patched copy, a game update) is never backed up.
//
// Proton compatibility mode ([Launcher] ProtonCompat=1 in FFX&X-2_LAUNCHER.ini; Video > Advanced settings: launcher):
//   on   FFX.exe / FFX-2.exe are replaced by their 60 fps copies (FFX_enh.exe / FFX-2_enh.exe, made from the verified
//        backups), so Steam / Proton start the game under its own file names. Needs a valid backup of each; the copies are
//        CRC checked; if a file disappears right after (anti-virus), the original is put back and the mode stays off.
//   off  the originals come back from backup_original (CRC = the Steam release's).
//   A failure while switching on puts the originals back. At every start with the mode on (SyncAtStart) each game EXE is
//   compared with its 60 fps copy: an original put back by a Steam file check, or an older copy, is replaced again; any
//   other file (e.g. after a game update) is left alone and logged.
// Proton's own fix for this game (crash in battle, Proton 8.0-5) is keyed on the Steam app id (SteamGameId 359870 in
// Wine's ntdll: wine_allocs_2g_limit), not on the EXE name, and patches nothing in the EXE; it applies in both modes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FFXLauncher
{
    static class Originals
    {
        public static readonly string[] Names = { "FFX.exe", "FFX-2.exe", "FFX&X-2_Will.exe" };
        static readonly uint[] Crcs = { 0xFC9264CD, 0x6C2274E0, 0x40E21BB6 };   // the Steam release (2026-11)
        static readonly string[][] Pairs = { new[] { "FFX.exe", "FFX_enh.exe" }, new[] { "FFX-2.exe", "FFX-2_enh.exe" } };
        public const string BackupFolder = "backup_original";

        static uint Known(string name) { int i = Array.IndexOf(Names, name); return i < 0 ? 0 : Crcs[i]; }
        static string GamePath(string n) { return Path.Combine(Config.GameDir, n); }
        static string BackupPath(string n) { return Path.Combine(Config.GameDir, BackupFolder, n); }
        static string Hex(uint c) { return c.ToString("X8"); }

        static uint[] table;
        public static uint Crc(string path)
        {
            if (table == null)
            {
                var t = new uint[256];
                for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[i] = c; }
                table = t;
            }
            uint r = 0xFFFFFFFF; var buf = new byte[1 << 20];
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int n;
                while ((n = f.Read(buf, 0, buf.Length)) > 0) for (int i = 0; i < n; i++) r = table[(r ^ buf[i]) & 0xFF] ^ (r >> 8);
            }
            return ~r;
        }

        static bool BackupValid(string n) { string b = BackupPath(n); return File.Exists(b) && Crc(b) == Known(n); }

        // src -> dst through dst.tmp: the copy must have the CRC 'want' before it replaces dst, and dst is read back afterwards
        static void CopyChecked(string src, string dst, uint want)
        {
            string tmp = dst + ".tmp";
            File.Copy(src, tmp, true);
            uint got = Crc(tmp);
            if (got != want) { try { File.Delete(tmp); } catch { } throw new IOException(Path.GetFileName(dst) + ": the copy has CRC " + Hex(got) + ", expected " + Hex(want) + " - not replaced"); }
            Native.ReplaceFile(tmp, dst);
            got = Crc(dst);
            if (got != want) throw new IOException(Path.GetFileName(dst) + ": CRC " + Hex(got) + " after replacing, expected " + Hex(want));
        }

        // every launcher start: the originals that have no valid backup yet are backed up - only with the Steam release's CRC
        public static void EnsureBackups()
        {
            foreach (string n in Names)
            {
                try
                {
                    string b = BackupPath(n), g = GamePath(n);
                    if (File.Exists(b))
                    {
                        uint c = Crc(b);
                        if (c == Known(n)) { Log.Write("backup: " + BackupFolder + "\\" + n + " present, CRC " + Hex(c) + " ok - kept"); continue; }
                        Log.Write("backup: " + BackupFolder + "\\" + n + " has CRC " + Hex(c) + ", expected " + Hex(Known(n)) + " - replaced if the game folder has the original");
                    }
                    if (!File.Exists(g)) { Log.Write("backup: " + n + " is not in the game folder"); continue; }
                    uint gc = Crc(g);
                    if (gc != Known(n)) { Log.Write("backup: " + n + " has CRC " + Hex(gc) + ", not the original's " + Hex(Known(n)) + " - no backup made"); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(b));
                    CopyChecked(g, b, gc);
                    Log.Write("backup: " + n + " -> " + BackupFolder + "\\" + n + " (CRC " + Hex(gc) + ", copy checked)");
                }
                catch (Exception e) { Log.Write("backup: " + n + ": " + e.Message); }
            }
        }

        // the source of the 60 fps patcher: the game's EXE, or its backup when the game folder holds a patched copy (mode on)
        public static string SourceFor(string dir, string exe)
        {
            string g = Path.Combine(dir, exe), b = Path.Combine(dir, BackupFolder, exe);
            if (File.Exists(g) && Patcher.IsPatched(g) && File.Exists(b)) return b;
            return g;
        }

        // ---- the mode ----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPrivateProfileInt(string section, string key, int def, string file);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool WritePrivateProfileString(string section, string key, string value, string file);

        public static bool CompatOn
        {
            get
            {
                try { return LauncherRes.IniPath != null && File.Exists(LauncherRes.IniPath) && GetPrivateProfileInt("Launcher", "ProtonCompat", 0, LauncherRes.IniPath) == 1; }
                catch { return false; }
            }
        }
        static void SetFlag(bool on)
        {
            if (WritePrivateProfileString("Launcher", "ProtonCompat", on ? "1" : "0", LauncherRes.IniPath)) Log.Write("compat: [Launcher] ProtonCompat=" + (on ? 1 : 0) + " -> " + LauncherRes.IniPath);
            else Log.Write("compat: " + LauncherRes.IniPath + " not written");
        }

        // the row is offered without RevoFX, with the patch code built in and a game EXE in the folder
        public static bool Available { get { return !RevoFx.Installed && !Fps60Dll.Present && Patcher.Available && (File.Exists(GamePath(Config.FFX)) || File.Exists(GamePath(Config.FFX2))); } }

        // puts back every original whose game file differs from it (used after a failure while switching on)
        static void RestoreAll()
        {
            foreach (var p in Pairs)
            {
                try
                {
                    if (!BackupValid(p[0])) continue;
                    if (File.Exists(GamePath(p[0])) && Crc(GamePath(p[0])) == Known(p[0])) continue;
                    CopyChecked(BackupPath(p[0]), GamePath(p[0]), Known(p[0]));
                    Log.Write("compat: " + p[0] + " put back from " + BackupFolder + " (CRC checked)");
                }
                catch (Exception e) { Log.Write("compat: " + p[0] + " could not be put back: " + e.Message); }
            }
        }

        // switches the mode; runs on the settings page's worker thread. progress: the file being handled and 0 .. 1.
        public static string Switch(bool on, Action<string, double> progress, out bool ok)
        {
            ok = false; bool de = Config.LanguagePath == "de";
            if (progress == null) progress = delegate { };
            try
            {
                progress(null, 0.02);
                EnsureBackups();
                foreach (var p in Pairs)
                    if (File.Exists(GamePath(p[0])) && !BackupValid(p[0]))
                        throw new IOException((de ? "kein gepr\u00fcftes Backup von " : "no verified backup of ") + p[0] + " (" + BackupFolder + ")");
                if (on)
                {
                    var made = Patcher.Ensure(Config.GameDir, (f, x) => progress(f, 0.1 + 0.4 * x)); made.AddRange(Patcher.Refresh(Config.GameDir));
                    foreach (string l in made) Log.Write("fps60exe: " + l);
                    foreach (string l in made) if (l.EndsWith(" - not patched") || l.EndsWith(" - not updated")) throw new IOException(l);   // e.g. not the original EXE (CRC)
                    int i = 0;
                    foreach (var p in Pairs)
                    {
                        if (!File.Exists(BackupPath(p[0]))) continue;
                        string s = GamePath(p[1]);
                        if (!File.Exists(s)) throw new IOException(p[1] + (de ? " fehlt (vom Virenschutz entfernt?)" : " is missing (removed by an anti-virus program?)"));
                        progress(p[0], 0.55 + 0.15 * i++);
                        uint want = Crc(s); CopyChecked(s, GamePath(p[0]), want);
                        Log.Write("compat: " + p[1] + " -> " + p[0] + " (CRC " + Hex(want) + ", copy checked)");
                    }
                    for (int k = 1; k <= 15; k++) { progress(null, 0.85 + 0.15 * k / 15); System.Threading.Thread.Sleep(100); }
                    foreach (var p in Pairs)
                        if (File.Exists(BackupPath(p[0])) && !File.Exists(GamePath(p[0])))
                            throw new IOException(p[0] + (de ? " wurde direkt nach dem Schreiben entfernt (Virenschutz?)" : " was removed right after it was written (anti-virus?)"));
                    SetFlag(true); ok = true;
                    return de ? "Proton-Kompatibilit\u00e4tsmodus an: FFX.exe und FFX-2.exe sind jetzt die 60-fps-Fassungen. Originale in " + BackupFolder + " (CRC gepr\u00fcft)."
                              : "Proton compatibility mode on: FFX.exe and FFX-2.exe are now the 60 fps builds. Originals in " + BackupFolder + " (CRC checked).";
                }
                int j = 0;
                foreach (var p in Pairs)
                {
                    if (!File.Exists(BackupPath(p[0]))) continue;
                    progress(p[0], 0.2 + 0.35 * j++);
                    CopyChecked(BackupPath(p[0]), GamePath(p[0]), Known(p[0]));
                    Log.Write("compat: " + BackupFolder + "\\" + p[0] + " -> " + p[0] + " (original, CRC " + Hex(Known(p[0])) + " checked)");
                }
                progress(null, 1); SetFlag(false); ok = true;
                return de ? "Proton-Kompatibilit\u00e4tsmodus aus: die Original-EXEs sind wiederhergestellt (CRC gepr\u00fcft)."
                          : "Proton compatibility mode off: the original EXE files are back (CRC checked).";
            }
            catch (Exception e)
            {
                Log.Write("compat: " + e.Message);
                if (on) { RestoreAll(); SetFlag(false); }
                return (de ? "Nicht umgeschaltet: " : "Not switched: ") + e.Message + (on ? (de ? ". Die Originale sind wieder an ihrem Platz." : ". The originals are in place.") : "");
            }
        }

        // launcher start with the mode on: each game EXE must be its current 60 fps copy
        public static void SyncAtStart()
        {
            if (!CompatOn) return;
            if (Fps60Dll.Present)                                   // the DLL patches the original EXEs: they must be in place
            {
                bool ok; string r = Switch(false, null, out ok);
                Log.Write("compat: dbghelp.dll in the game folder - the mode is switched off: " + r);
                return;
            }
            if (RevoFx.Installed)                                   // RevoFX patches the original EXEs itself: they must be in place
            {
                bool ok; string r = Switch(false, null, out ok);
                Log.Write("compat: RevoFX is installed - the mode is switched off: " + r);
                return;
            }
            foreach (var p in Pairs)
            {
                try
                {
                    string g = GamePath(p[0]), s = GamePath(p[1]);
                    if (!File.Exists(g) || !File.Exists(s)) { Log.Write("compat: " + p[0] + " or " + p[1] + " missing - left as it is"); continue; }
                    uint gc = Crc(g), sc = Crc(s);
                    if (gc == sc) { Log.Write("compat: " + p[0] + " is the current 60 fps build (CRC " + Hex(gc) + ")"); continue; }
                    if (gc == Known(p[0]) || Patcher.IsPatched(g))
                    {
                        EnsureBackups();
                        if (!BackupValid(p[0])) { Log.Write("compat: no verified backup of " + p[0] + " - not replaced"); continue; }
                        CopyChecked(s, g, sc);
                        Log.Write("compat: " + p[0] + " replaced by the current " + p[1] + " (was CRC " + Hex(gc) + ", now " + Hex(sc) + ")");
                    }
                    else Log.Write("compat: " + p[0] + " has CRC " + Hex(gc) + ": neither the original nor a 60 fps build (game update?) - left alone");
                }
                catch (Exception e) { Log.Write("compat: " + p[0] + ": " + e.Message); }
            }
        }
    }
}
