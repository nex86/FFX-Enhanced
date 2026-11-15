// RevoFX's settings of this game: <game dir>\RevoFX\game\FINALFANTASYXX-2RevoFX.json
//   { "schemaVersion": 1, "settings": { "<section>": { "<key>": <value>, ... }, ... } }
// The launcher reads and writes single integer keys ([Game] FrameRate). The file is edited as text in place: every
// other key, the order, the indentation and the line endings stay as they are; missing objects are added. Nothing is
// written when RevoFX is not installed (no RevoFX\game folder) or the file cannot be understood.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FFXLauncher
{
    static class RevoFx
    {
        public static string JsonPath;                     // set by Config.Init (test aid --revofx <path>)

        public static bool ForceAbsent;                    // test aid --norevofx: the vanilla path in a folder that has RevoFX

        // RevoFX is installed in the game folder: its Direct3D 11 proxy (d3d11.dll) and RevoFX.dll next to the game EXE.
        // Then RevoFX's module draws the 60 fps; without it the launcher starts FFX_enh.exe / FFX-2_enh.exe.
        public static bool Installed
        {
            get
            {
                if (ForceAbsent || Config.GameDir == null) return false;
                return File.Exists(Path.Combine(Config.GameDir, "RevoFX.dll")) && File.Exists(Path.Combine(Config.GameDir, "d3d11.dll"));
            }
        }

        public static bool Present { get { return JsonPath != null && Directory.Exists(Path.GetDirectoryName(JsonPath)); } }

        // "Use RevoFX" (Advanced tab): RevoFX is switched by the name of its Direct3D 11 proxy - d3d11.dll (on) or
        // d3d11.dll.revofx-off (off; Windows and Wine load only "d3d11.dll", so the game then runs without RevoFX and the
        // launcher's own 60 fps paths apply). RevoFX.dll and the RevoFX folder stay. Offered when RevoFX.dll is there and one
        // of the two names; only a d3d11.dll that is RevoFX's proxy (it names RevoFX) is ever renamed. Every rename is
        // checked: the file under its new name must have the CRC-32 it had before.
        public const string OffName = "d3d11.dll.revofx-off";
        static string Dir(string f) { return Path.Combine(Config.GameDir, f); }
        public static bool Switchable
        {
            get { return Config.GameDir != null && File.Exists(Dir("RevoFX.dll")) && (File.Exists(Dir("d3d11.dll")) || File.Exists(Dir(OffName))); }
        }
        public static bool Enabled { get { return Config.GameDir != null && File.Exists(Dir("RevoFX.dll")) && File.Exists(Dir("d3d11.dll")); } }

        static bool IsRevoProxy(string path)
        {
            byte[] d = File.ReadAllBytes(path); byte[] k = Encoding.ASCII.GetBytes("RevoFX");
            for (int i = 0; i + k.Length <= d.Length; i++) { int j = 0; while (j < k.Length && d[i + j] == k[j]) j++; if (j == k.Length) return true; }
            return false;
        }

        // returns the line for the help panel; ok = the state is now the wanted one
        public static string SetEnabled(bool on, out bool ok)
        {
            ok = false; bool de = Config.LanguagePath == "de";
            string from = Dir(on ? OffName : "d3d11.dll"), to = Dir(on ? "d3d11.dll" : OffName);
            try
            {
                if (!File.Exists(from)) { ok = File.Exists(to); return null; }
                if (!IsRevoProxy(from))
                {
                    Log.Write("revofx: " + Path.GetFileName(from) + " is not RevoFX's proxy - not renamed");
                    return de ? "d3d11.dll geh\u00f6rt nicht zu RevoFX - nicht umbenannt." : "d3d11.dll is not RevoFX's - not renamed.";
                }
                if (on && Originals.CompatOn)                    // RevoFX brings its own 60 fps: the patched game EXEs must go first
                {
                    bool cok; string r = Originals.Switch(false, null, out cok);
                    Log.Write("revofx: Proton compatibility mode switched off before RevoFX is enabled: " + r);
                    if (!cok) return r;
                }
                uint crc = Originals.Crc(from);
                if (File.Exists(to))
                {
                    if (Originals.Crc(to) != crc)
                    {
                        Log.Write("revofx: both " + Path.GetFileName(from) + " and " + Path.GetFileName(to) + " exist and differ - not renamed");
                        return de ? "d3d11.dll und " + OffName + " sind beide vorhanden und verschieden - bitte eine entfernen." : "d3d11.dll and " + OffName + " both exist and differ - please remove one.";
                    }
                    File.Delete(from);                           // the same file twice: keep the one under the wanted name
                }
                else File.Move(from, to);
                uint after = Originals.Crc(to);
                if (after != crc) throw new IOException(Path.GetFileName(to) + ": CRC " + after.ToString("X8") + " after renaming, expected " + crc.ToString("X8"));
                Log.Write("revofx: " + Path.GetFileName(from) + " -> " + Path.GetFileName(to) + " (CRC " + crc.ToString("X8") + " checked): RevoFX " + (on ? "on" : "off"));
                ok = true;
                return on ? (de ? "RevoFX ist aktiviert (ab dem n\u00e4chsten Spielstart)." : "RevoFX is enabled (from the next game start).")
                          : (de ? "RevoFX ist deaktiviert: d3d11.dll hei\u00dft jetzt " + OffName + "." : "RevoFX is disabled: d3d11.dll is now " + OffName + ".");
            }
            catch (Exception e) { Log.Write("revofx: " + e.Message); return (de ? "RevoFX nicht umgeschaltet: " : "RevoFX not switched: ") + e.Message; }
        }

        public static int? GetInt(string section, string key)
        {
            try
            {
                if (!File.Exists(JsonPath)) return null;
                string s = File.ReadAllText(JsonPath); int vs, ve, o;
                if (!Section(s, section, out o) || !Member(s, o, key, out vs, out ve)) return null;
                int v; return int.TryParse(s.Substring(vs, ve - vs), out v) ? v : (int?)null;
            }
            catch (Exception e) { Log.Write("revofx: " + e.Message); return null; }
        }

        public static bool SetInt(string section, string key, int value)
        {
            if (!Present) return false;
            try
            {
                byte[] raw = File.Exists(JsonPath) ? File.ReadAllBytes(JsonPath) : null;
                bool bom = raw != null && raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
                string s = raw != null ? new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0)) : null;
                string v = value.ToString(), q = "\"";
                if (s == null || s.Trim().Length == 0)
                    s = "{\n  \"schemaVersion\": 1,\n  \"settings\": {\n    " + q + section + q + ": {\n      " + q + key + q + ": " + v + "\n    }\n  }\n}\n";
                else
                {
                    int root = SkipWs(s, 0); if (root >= s.Length || s[root] != '{') throw new FormatException("not a JSON object");
                    int set, sec, vs, ve;
                    if (!Member(s, root, "settings", out set, out ve)) s = Insert(s, root, "settings", "{ " + q + section + q + ": { " + q + key + q + ": " + v + " } }");
                    else if (s[set] != '{') throw new FormatException("\"settings\" is not an object");
                    else if (!Member(s, set, section, out sec, out ve)) s = Insert(s, set, section, "{ " + q + key + q + ": " + v + " }");
                    else if (s[sec] != '{') throw new FormatException("\"" + section + "\" is not an object");
                    else if (!Member(s, sec, key, out vs, out ve)) s = Insert(s, sec, key, v);
                    else s = s.Substring(0, vs) + v + s.Substring(ve);
                }
                var enc = new UTF8Encoding(false); byte[] outb = enc.GetBytes(s);
                if (bom) { var b2 = new byte[outb.Length + 3]; b2[0] = 0xEF; b2[1] = 0xBB; b2[2] = 0xBF; Buffer.BlockCopy(outb, 0, b2, 3, outb.Length); outb = b2; }
                string tmp = JsonPath + ".tmp"; File.WriteAllBytes(tmp, outb);
                Native.ReplaceFile(tmp, JsonPath);
                Log.Write("revofx: [" + section + "] " + key + " = " + v + " -> " + JsonPath);
                return true;
            }
            catch (Exception e) { Log.Write("revofx: " + JsonPath + ": " + e.Message + " (not written)"); return false; }
        }

        static bool Section(string s, string section, out int open)
        {
            open = -1; int root = SkipWs(s, 0), set, e;
            if (root >= s.Length || s[root] != '{' || !Member(s, root, "settings", out set, out e) || s[set] != '{') return false;
            return Member(s, set, section, out open, out e) && s[open] == '{';
        }

        // a member directly inside the object whose '{' is at 'open': start and end of its value
        static bool Member(string s, int open, string key, out int vs, out int ve)
        {
            vs = ve = -1; int i = SkipWs(s, open + 1);
            while (i < s.Length && s[i] != '}')
            {
                if (s[i] != '"') return false;
                int ke = StrEnd(s, i); if (ke < 0) return false;
                string k = s.Substring(i + 1, ke - i - 2);
                i = SkipWs(s, ke); if (i >= s.Length || s[i] != ':') return false;
                i = SkipWs(s, i + 1); int e = ValueEnd(s, i); if (e < 0) return false;
                if (k == key) { vs = i; ve = e; return true; }
                i = SkipWs(s, e); if (i < s.Length && s[i] == ',') i = SkipWs(s, i + 1);
            }
            return false;
        }

        // adds "key": value as the first member of the object at 'open', in the indentation of the existing members
        static string Insert(string s, int open, string key, string value)
        {
            int first = SkipWs(s, open + 1); string m = "\"" + key + "\": " + value;
            if (first < s.Length && s[first] == '}') return s.Insert(open + 1, " " + m + " ");
            return s.Insert(first, m + "," + s.Substring(open + 1, first - open - 1));
        }

        static int SkipWs(string s, int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; return i; }

        static int StrEnd(string s, int i)                 // i at the opening quote: index after the closing quote
        {
            for (i++; i < s.Length; i++) { if (s[i] == '\\') i++; else if (s[i] == '"') return i + 1; }
            return -1;
        }

        static int ValueEnd(string s, int i)
        {
            if (i >= s.Length) return -1;
            if (s[i] == '"') return StrEnd(s, i);
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0;
                for (; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '"') { int e = StrEnd(s, i); if (e < 0) return -1; i = e - 1; }
                    else if (c == '{' || c == '[') depth++;
                    else if ((c == '}' || c == ']') && --depth == 0) return i + 1;
                }
                return -1;
            }
            while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
            return i;
        }
    }
}

namespace FFXLauncher
{
    // The external 60 fps mods' settings, each file only if it exists: fps60.ini and fps60\fps60.ini (FFX, section
    // [fps60]), fps60x2.ini and fps60\fps60x2.ini (FFX-2, section [x2]), all relative to the game folder.
    // Both files have the same layout ([fps60] / [x2]: enabled, pacing, ...). The launcher sets enabled (0 at 30 fps, 1 from
    // 60 fps on) and fps (the selected rate) in both; the FFX-2 mod of 2026-11 reads no fps key yet (x2_config.cpp:
    // enabled and pacing; it draws 60 fps), the key is written for it all the same. pacing is left as it is.
    // The files are edited line by line: comments, other keys, sections and line endings stay; missing keys are added
    // below the section header, a missing section at the end. Bytes are read and written as Latin-1 (unchanged).
    static class Fps60Ini
    {
        public static string BaseDir;                      // set by Config.Init (test aid --fps60dir <dir>)

        sealed class Target { public string File, Section; public bool AddFps; }

        static System.Collections.Generic.List<Target> Targets()
        {
            var l = new System.Collections.Generic.List<Target>();
            if (BaseDir == null) return l;
            foreach (string f in new[] { "fps60.ini", @"fps60\fps60.ini", "fps60x2.ini", @"fps60\fps60x2.ini" })
            {
                string p = Path.Combine(BaseDir, f); bool x2 = f.EndsWith("x2.ini");
                if (File.Exists(p)) l.Add(new Target { File = p, Section = x2 ? "x2" : "fps60", AddFps = true });
            }
            return l;
        }

        public static System.Collections.Generic.List<string> Files() { return Targets().ConvertAll(x => x.File); }

        // the frame rate the first file selects: 30 when the mod is off, else its fps (60 without an fps key)
        public static int? Get()
        {
            foreach (Target tg in Targets())
            {
                try
                {
                    string en = null, fps = null, sec = "";
                    foreach (string raw in File.ReadAllLines(tg.File, Encoding.GetEncoding(28591)))
                    {
                        string t = raw.Trim();
                        if (t.StartsWith("[")) { sec = t.Trim('[', ']').Trim().ToLowerInvariant(); continue; }
                        if (sec != tg.Section || t.StartsWith(";") || t.IndexOf('=') < 0) continue;
                        string k = t.Substring(0, t.IndexOf('=')).Trim().ToLowerInvariant(), v = t.Substring(t.IndexOf('=') + 1).Trim();
                        if (k == "enabled") en = v; else if (k == "fps") fps = v;
                    }
                    int f; if (en == "0") return 30;
                    return int.TryParse(fps, out f) ? f : 60;
                }
                catch (Exception e) { Log.Write("fps60: " + tg.File + ": " + e.Message); }
            }
            return null;
        }

        public static void Set(int fps)
        {
            foreach (Target tg in Targets())
            {
                try
                {
                    var lat = Encoding.GetEncoding(28591);
                    string s = File.ReadAllText(tg.File, lat), nl = s.Contains("\r\n") ? "\r\n" : "\n";
                    var lines = new System.Collections.Generic.List<string>(s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
                    string en = fps >= 60 ? "1" : "0", fv = fps.ToString();
                    int header = -1, after = -1; bool haveEn = false, haveFps = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        string t = lines[i].Trim();
                        if (t.StartsWith("["))
                        {
                            if (header >= 0) break;
                            if (t.Trim('[', ']').Trim().ToLowerInvariant() == tg.Section) header = i;
                            continue;
                        }
                        if (header < 0 || t.StartsWith(";") || t.IndexOf('=') < 0) continue;
                        string k = t.Substring(0, t.IndexOf('=')).Trim().ToLowerInvariant();
                        if (k == "enabled") { lines[i] = Replace(lines[i], en); haveEn = true; after = Math.Max(after, i + 1); }
                        else if (k == "pacing") after = Math.Max(after, i + 1);
                        else if (k == "fps") { lines[i] = Replace(lines[i], fv); haveFps = true; }
                    }
                    if (header < 0)
                    {
                        while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
                        if (lines.Count > 0) lines.Add("");
                        lines.Add("[" + tg.Section + "]"); lines.Add("; 1 = mod on, 0 = mod off"); lines.Add("enabled=" + en);
                        if (tg.AddFps) lines.Add("fps=" + fv);
                        lines.Add("");
                    }
                    else
                    {
                        // a missing fps goes below pacing / enabled (as in fps60.ini), a missing enabled below the header
                        if (!haveFps && tg.AddFps) lines.Insert(after > header ? after : header + 1, "fps=" + fv);
                        if (!haveEn) { lines.Insert(header + 1, "; 1 = mod on, 0 = mod off"); lines.Insert(header + 2, "enabled=" + en); }
                    }
                    string outText = string.Join(nl, lines.ToArray());
                    if (outText == s) continue;
                    string tmp = tg.File + ".tmp"; File.WriteAllText(tmp, outText, lat); Native.ReplaceFile(tmp, tg.File);
                    Log.Write("fps60: [" + tg.Section + "] enabled=" + en + (tg.AddFps || haveFps ? " fps=" + fv : "") + " -> " + tg.File);
                }
                catch (Exception e) { Log.Write("fps60: " + tg.File + ": " + e.Message + " (not written)"); }
            }
        }

        static string Replace(string line, string value)   // keeps the key's spelling and spacing, and a trailing comment
        {
            int eq = line.IndexOf('='); string rest = line.Substring(eq + 1);
            int lead = rest.Length - rest.TrimStart().Length; string after = rest.TrimStart();
            int c = after.IndexOf(';'); string tail = c >= 0 ? " " + after.Substring(c) : "";
            return line.Substring(0, eq + 1) + rest.Substring(0, lead) + value + tail;
        }
    }
}

namespace FFXLauncher
{
    // The 60 fps EXEs (FFX_enh.exe / FFX-2_enh.exe, written by FFX_enh_Patcher.exe from the game's own EXEs; they need
    // no RevoFX and no DLL) and their settings file ffx_enh.ini ([fps60] fps) in the game folder. The launcher starts them
    // in place of FFX.exe / FFX-2.exe when RevoFX is not installed and the frame rate is 60 or more.
    // The optional DLL build of the 60 fps patch (fps60exe\bin\dbghelp.dll, docs\FPS60_EXE.md section 12): with a dbghelp.dll in
    // the game folder the launcher writes no 60 fps EXEs, offers no Proton compatibility mode and starts the original EXEs;
    // the frame rate is still ffx_enh.ini [fps60] fps, which the DLL reads. Any dbghelp.dll counts (also the FFX-60fps mod's
    // own loader): with one there the EXE route would patch twice.
    static class Fps60Dll
    {
        public static bool Present { get { return Config.GameDir != null && File.Exists(Path.Combine(Config.GameDir, "dbghelp.dll")); } }
    }

    static class Fps60Exe
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetPrivateProfileInt(string section, string key, int def, string file);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool WritePrivateProfileString(string section, string key, string value, string file);

        public static string For(string exe) { return Path.GetFileNameWithoutExtension(exe) + "_enh.exe"; }   // FFX.exe -> FFX_enh.exe
        public static bool Has(string exe) { return File.Exists(Path.Combine(Config.GameDir, For(exe))); }
        public static bool Present { get { return Has(Config.FFX) || Has(Config.FFX2); } }
        // the launcher can write the 60 fps EXEs itself: built with the patch code and RevoFX is not installed
        public static bool CanPatch { get { return !RevoFx.Installed && !Fps60Dll.Present && Patcher.Available && (File.Exists(Path.Combine(Config.GameDir, Config.FFX)) || File.Exists(Path.Combine(Config.GameDir, Config.FFX2))); } }

        // the 60 fps EXEs that Apply would write now: 60 fps or more, no RevoFX, the game EXE there and its 60 fps EXE not
        static List<string> Missing(int fps)
        {
            var l = new List<string>();
            if ((fps < 60 && !StartOptionsOn) || !CanPatch) return l;   // the start options need the patch at 30 fps too
            foreach (string e in new[] { Config.FFX, Config.FFX2 }) if (File.Exists(Path.Combine(Config.GameDir, e)) && !Has(e)) l.Add(For(e));
            return l;
        }
        public static bool NeedsPatch(int fps) { return Missing(fps).Count > 0; }
        // [start] skip_logos / no_launcher_restart of ffx_enh.ini: they work through the patch, so they need the 60 fps EXEs
        public static bool StartOptionsOn { get { return IniGet("start", "skip_logos") == "1" || IniGet("start", "no_launcher_restart") == "1" || IniGet("start", "file_loader") == "1" || IniGet("start", "ultrawide") == "1"; } }
        // ffgriever's External File Loader is there: modules\ff10-file-loader.dll in the game folder or in its EFL subfolder
        public static bool EflPresent
        {
            get
            {
                foreach (string b in new[] { "", "EFL" }) if (File.Exists(Path.Combine(Path.Combine(Config.GameDir, b), @"modules\ff10-file-loader.dll"))) return true;
                return false;
            }
        }

        // Launcher start: 60 fps EXEs written by another build of the patcher, or from another game EXE (game update), are
        // written again (Patcher.Refresh: version stamp = CRC of the patch code + CRC of the game EXE). Not with RevoFX.
        public static void RefreshAtStart()
        {
            try
            {
                if (RevoFx.Installed || !Patcher.Available || !Present) return;
                if (Fps60Dll.Present) { Log.Write("fps60exe: dbghelp.dll in the game folder: the 60 fps EXEs are not written or refreshed (the DLL carries the patch)"); return; }
                var l = Patcher.Refresh(Config.GameDir);
                foreach (string s in l) Log.Write("fps60exe: " + s);
                if (l.Count == 0) Log.Write("fps60exe: the 60 fps EXEs are current");
            }
            catch (Exception e) { Log.Write("fps60exe: " + e.Message); }
        }

        // Apply / OK with 60 fps or more and no RevoFX: the 60 fps EXEs that do not exist yet are written from the game's own EXEs.
        // Runs on the settings page's worker thread; progress: the file being written (null: checking) and 0 .. 1.
        // Afterwards every new file is looked for once more: an anti-virus program that removes a new file does so within a
        // second, and the launcher would then start the original EXE without saying why. Returns the line for the help panel.
        public static string Ensure(int fps, Action<string, double> progress, out bool ok)
        {
            ok = true; List<string> todo = Missing(fps);
            if (todo.Count == 0) return null;
            bool de = Config.LanguagePath == "de";
            // writing: 0 .. 80 %, then 1.5 s of checking: 80 .. 100 %
            var lines = Patcher.Ensure(Config.GameDir, progress == null ? null : (Action<string, double>)((f, x) => progress(f, 0.8 * x)));
            foreach (string l in lines) Log.Write("fps60exe: " + l);
            for (int i = 1; i <= 15; i++) { if (progress != null) progress(null, 0.8 + 0.2 * i / 15); System.Threading.Thread.Sleep(100); }
            var gone = todo.FindAll(f => !File.Exists(Path.Combine(Config.GameDir, f)));
            var failed = lines.FindAll(l => l.EndsWith(" - not patched"));
            if (failed.Count > 0)
            {
                ok = false;
                return (de ? "60-fps-Patch abgebrochen: " : "60 fps patch stopped: ") + string.Join("; ", failed.ConvertAll(l => Explain(l, de)).ToArray()) + (de ? " (Details: FFX&X-2_LAUNCHER.log)" : " (details: FFX&X-2_LAUNCHER.log)");
            }
            if (gone.Count > 0)
            {
                ok = false; string names = string.Join(", ", gone.ToArray());
                Log.Write("fps60exe: missing right after writing: " + names + " - removed by an anti-virus program? The original EXE is started instead.");
                return de ? names + " wurde direkt nach dem Schreiben entfernt, vermutlich vom Virenschutz. Bis dahin startet das Original mit 30 fps.\nAbhilfe: Spielordner im Virenschutz ausnehmen und erneut \"Apply\" w\u00e4hlen."
                          : names + " was removed right after it was written, probably by the anti-virus program. Until then the original EXE starts at 30 fps.\nFix: exclude the game folder in the anti-virus program and choose \"Apply\" again.";
            }
            return (de ? "60-fps-Patch angewendet: " : "60 fps patch applied: ") + string.Join(", ", todo.ToArray());
        }
        // a patcher line for the help panel: the CRC check (Patcher.CheckOriginal) in the launcher's language, others as they are
        static string Explain(string line, bool de)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^(\S+?): \S+ has CRC-32 ([0-9A-F]{8}), expected ([0-9A-F]{8})");
            if (!m.Success) return line;
            return de ? m.Groups[1].Value + " ist nicht die Original-EXE der Steam-Version (CRC-32 " + m.Groups[2].Value + " statt " + m.Groups[3].Value + ") - nicht gepatcht"
                      : m.Groups[1].Value + " is not the Steam release's original EXE (CRC-32 " + m.Groups[2].Value + ", expected " + m.Groups[3].Value + ") - not patched";
        }

        public static string IniPath { get { return Path.Combine(Fps60Ini.BaseDir ?? Config.GameDir, Patcher.IniName); } }   // ffx_enh.ini

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder buf, int size, string file);
        // one setting of ffx_enh.ini (advanced settings page); null = the file or the key does not exist
        public static string IniGet(string section, string key)
        {
            try
            {
                if (!File.Exists(IniPath)) return null;
                var b = new StringBuilder(256); GetPrivateProfileString(section, key, "\u0001", b, b.Capacity, IniPath);
                string v = b.ToString().Trim(); return v == "\u0001" ? null : v;
            }
            catch (Exception e) { Log.Write("fps60exe: " + e.Message); return null; }
        }
        public static void IniSet(string section, string key, string value)
        {
            try { if (WritePrivateProfileString(section, key, value, IniPath)) Log.Write("fps60exe: [" + section + "] " + key + "=" + value); else Log.Write("fps60exe: " + key + " not written"); }
            catch (Exception e) { Log.Write("fps60exe: " + e.Message); }
        }

        public static int? Get()
        {
            try { if (!File.Exists(IniPath)) return null; int v = GetPrivateProfileInt("fps60", "fps", -1, IniPath); return v < 0 ? (int?)null : v; }
            catch (Exception e) { Log.Write("fps60exe: " + e.Message); return null; }
        }

        public static void Set(int fps)                    // the file is created when missing (the EXEs default to 60 without it)
        {
            try
            {
                if (WritePrivateProfileString("fps60", "fps", fps.ToString(), IniPath)) Log.Write("fps60exe: [fps60] fps=" + fps + " -> " + IniPath);
                else Log.Write("fps60exe: " + IniPath + " not written");
            }
            catch (Exception e) { Log.Write("fps60exe: " + e.Message); }
        }

        // The frame rate setting in force. With RevoFX: its JSON; without: ffx_enh.ini; then the other stores; 30 when none has it.
        public static int FrameRate()
        {
            int? v = RevoFx.Installed ? (RevoFx.GetInt("Game", "FrameRate") ?? Fps60Ini.Get() ?? Get())
                                      : ((Present || Fps60Dll.Present ? Get() : null) ?? Fps60Ini.Get() ?? RevoFx.GetInt("Game", "FrameRate"));
            return v ?? 30;
        }

        // The EXE to start for a game EXE: the 60 fps EXE when RevoFX is not installed and it exists, at every frame rate: at 30 fps
        // it runs with the game's own timing and its Esc menu (Video > Frame Rate) can switch to 60 during play. Else the original.
        public static string Pick(string exe)
        {
            if (exe != Config.FFX && exe != Config.FFX2) return exe;
            if (Fps60Dll.Present && !RevoFx.Installed) { Log.Write("start: dbghelp.dll in the game folder (the 60 fps patch as a DLL) -> " + exe + " (the original EXE; the DLL patches it)"); return exe; }
            if (Originals.CompatOn && !RevoFx.Installed) { Log.Write("start: Proton compatibility mode on -> " + exe + " (the 60 fps build under the original name)"); return exe; }
            bool revo = RevoFx.Installed; int fps = FrameRate(); bool has = Has(exe);
            string pick = !revo && has ? For(exe) : exe;
            Log.Write("start: RevoFX " + (revo ? "installed" : "not installed") + ", frame rate " + fps + ", " + For(exe) + (has ? " present" : " missing") + " -> " + pick);
            return pick;
        }
    }
}
