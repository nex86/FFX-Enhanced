// FFX&X-2_LAUNCHER.exe - standalone reimplementation of the FINAL FANTASY X/X-2 HD Remaster launcher.
//
// Own code only; it reads the original launcher data (data\metamenu.vbf, or loose files in data\metamenu\ first) and
// writes the same GameSetting.ini. One borderless window covers the monitor; the menu is scaled with its aspect ratio
// kept and black bars; settings and the 2014 credits are drawn inside the same window (no Windows dialogs).
// Mouse, keyboard and XInput controllers. Built for .NET Framework 4 (x86) and Wine Mono (Proton / Steam Deck).
//
// Files: Program.cs (start, config, INI, data, localisation), Audio.cs (FMOD Ex), LauncherForm.cs (window, menu, input
// dispatch), SettingsView.cs, Credits.cs (2014 credits), Input.cs (XInput). Analysis of the original: docs\LAUNCHER.md.
//
// Usage: FFX&X-2_LAUNCHER.exe [--monitor <index>]
// Test aids: [--ini <path>] (another GameSetting.ini) [--revofx <path>] (another RevoFX game JSON) [--fps60dir <dir>] (folder with fps60.ini / fps60\fps60.ini / ffx_enh.ini) [--norevofx] (treat RevoFX as not installed) [--start-test <exe>] (with --keys: start this harmless program in place of the game, to test the closing) [--size WxH] [--shot <png> [--shot-delay <ms>]] [--keys <list>] [--exit]
//   --size WxH   use a W x H area centred on the monitor instead of the whole monitor
//   --shot       capture the window to a PNG after --shot-delay ms (default 2500); --exit closes afterwards
//   --keys       comma separated input script, one step every 450 ms: key names (up down left right enter space esc
//                pgup pgdn m) are posted as WM_KEYDOWN; pad:<Up|Down|Left|Right|Confirm|Back|PageUp|PageDown|Mute>
//                takes the controller path; the state after each step is logged
// Unknown arguments are ignored (Steam launch option "<path>\FFX&X-2_LAUNCHER.exe" %command% works).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("FFX&X-2 Launcher (mod)")]
[assembly: AssemblyDescription("Reimplementation of the FFX/X-2 HD launcher: borderless fullscreen, controller support")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace FFXLauncher
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetProp(IntPtr hwnd, string name, IntPtr data);
        // settings files: ReplaceFile where it works, else overwrite (other runtimes / file systems under Wine)
        public static void ReplaceFile(string tmp, string dst)
        {
            try { if (File.Exists(dst)) File.Replace(tmp, dst, null); else File.Move(tmp, dst); return; } catch { }
            File.Copy(tmp, dst, true); try { File.Delete(tmp); } catch { }
        }
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attr, uint prot, uint hi, uint lo, string name);
        [DllImport("kernel32.dll")] public static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint hi, uint lo, uint bytes);
        [DllImport("kernel32.dll")] public static extern bool UnmapViewOfFile(IntPtr view);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern ushort GetUserDefaultLangID();
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr ShellExecute(IntPtr hwnd, string op, string file, string param, string dir, int show);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string device, int index, ref DISPLAY_DEVICE dd, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
    }

    static class Log
    {
        static StreamWriter w;
        public static void Open(string path) { try { w = new StreamWriter(path, false, Encoding.ASCII); w.AutoFlush = true; w.NewLine = "\r\n"; } catch { w = null; } }
        public static void Write(string s) { if (w != null) lock (w) w.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s); }
    }

    // ---- VBF archive (layout: tools\vbfextract.py) --------------------------------------------------------------------
    sealed class Vbf
    {
        struct Entry { public uint Block; public ulong Size, Offset; }
        readonly string path; readonly Dictionary<string, Entry> index = new Dictionary<string, Entry>(); ushort[] blocks;

        public static string Key(string name) { return name.Replace('\\', '/').ToLowerInvariant(); }

        public Vbf(string path)
        {
            this.path = path;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var r = new BinaryReader(f);
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "SRYK") throw new InvalidDataException("not a VBF archive: " + path);
                uint hsize = r.ReadUInt32(); ulong n = r.ReadUInt64();
                if (hsize > f.Length || 16 + n * 48 + 4 > hsize) throw new InvalidDataException("VBF header out of range");
                f.Position = 0; byte[] h = r.ReadBytes((int)hsize);
                int eo = 16 + (int)n * 16, o = eo + (int)n * 32;
                int nts = BitConverter.ToInt32(h, o), names = o + 4, bo = o + nts, nb = ((int)hsize - bo) / 2;
                blocks = new ushort[nb];
                for (int i = 0; i < nb; i++) blocks[i] = BitConverter.ToUInt16(h, bo + i * 2);
                for (int i = 0; i < (int)n; i++)
                {
                    int e = eo + i * 32; var x = new Entry();
                    x.Block = BitConverter.ToUInt32(h, e); x.Size = BitConverter.ToUInt64(h, e + 8); x.Offset = BitConverter.ToUInt64(h, e + 16);
                    int no = names + (int)BitConverter.ToUInt64(h, e + 24), end = Array.IndexOf<byte>(h, 0, no);
                    if (end < 0 || x.Block + ((x.Size + 0xFFFF) >> 16) > (ulong)nb) throw new InvalidDataException("VBF entry " + i + " out of range");
                    index[Key(Encoding.ASCII.GetString(h, no, end - no))] = x;
                }
            }
        }

        public int Count { get { return index.Count; } }

        public byte[] Read(string name)
        {
            Entry e;
            if (!index.TryGetValue(Key(name), out e)) return null;
            var o = new byte[e.Size];
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                f.Position = (long)e.Offset;
                int nblk = (int)((e.Size + 0xFFFF) >> 16);
                for (int k = 0; k < nblk; k++)
                {
                    int want = (int)Math.Min(0x10000UL, e.Size - (ulong)k * 0x10000), cs = blocks[e.Block + k] == 0 ? 0x10000 : blocks[e.Block + k];
                    byte[] d = new byte[cs];
                    if (f.Read(d, 0, cs) != cs) throw new IOException("VBF read failed: " + name);
                    if (cs == want) { Buffer.BlockCopy(d, 0, o, k * 0x10000, want); continue; }
                    using (var z = new DeflateStream(new MemoryStream(d, 2, cs - 2), CompressionMode.Decompress))   // zlib: skip the 2 byte header
                    {
                        int got = 0, m;
                        while (got < want && (m = z.Read(o, k * 0x10000 + got, want - got)) > 0) got += m;
                        if (got != want) throw new InvalidDataException("VBF block damaged: " + name);
                    }
                }
            }
            return o;
        }
    }

    // ---- data: loose files first (data\metamenu\..., data\ffx_data\...), then the archive --------------------------
    // MetaMenu/... comes from the resolution archive data\metamenu_1440.vbf or metamenu_2160.vbf (LauncherRes.cs, chosen by
    // SelectResources), then data\metamenu_mod.vbf (the launcher's own files, e.g. the menu music), then data\metamenu.vbf;
    // ffx_data/... (escmenu.swf, lockit texts) from data\FFX_Data.vbf. LoadOriginal always reads metamenu.vbf.
    // Loose files may be upscaled replacements: callers draw every image into its logical (original) rectangle.
    static class Data
    {
        static string dataDir; static string[] looseRoots; static Vbf metaVbf, modVbf, resVbf, gameVbf, game2Vbf; static bool gameVbfTried, game2VbfTried;
        static readonly Dictionary<string, Vbf> resArchives = new Dictionary<string, Vbf>();
        public static string ResUsed = "original";      // "1440", "2160" or "original" (metamenu.vbf alone)
        public static int MonitorHeight = 1080;          // the launcher's monitor in pixels (set in Main)
        static readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        static readonly HashSet<string> logged = new HashSet<string>();

        public static void Init(string gameDir)
        {
            dataDir = Path.GetFullPath(Path.Combine(gameDir, "data"));
            looseRoots = new[] { Path.Combine(dataDir, "metamenu") + Path.DirectorySeparatorChar, Path.Combine(dataDir, "ffx_data") + Path.DirectorySeparatorChar };
            metaVbf = Open("metamenu.vbf"); modVbf = Open("metamenu_mod.vbf");
            int loose = Directory.Exists(looseRoots[0]) ? Directory.GetFiles(looseRoots[0], "*", SearchOption.AllDirectories).Length : 0;
            Log.Write("data: " + loose + " loose file(s) in " + looseRoots[0] + ", metamenu_mod.vbf " + (modVbf != null ? modVbf.Count + " files" : "MISSING (the launcher's own files, e.g. its music, fall back to metamenu.vbf)")
                + ", metamenu.vbf " + (metaVbf != null ? metaVbf.Count + " files" : "missing"));
        }

        static Vbf Open(string file)
        {
            string p = Path.Combine(dataDir, file);
            try { return File.Exists(p) ? new Vbf(p) : null; }
            catch (Exception e) { Log.Write("data: " + file + ": " + e.Message); return null; }
        }

        // the menu graphics: choice "auto" (by MonitorHeight: below 1080 -> original, 1080 .. 1440 -> 1440, above -> 2160), "1440",
        // "2160" or
        // "original"; a missing archive falls back to the other resolution, then to metamenu.vbf alone. Images loaded
        // before are dropped from the cache, so the caller reloads the menu.
        public static void SelectResources(string choice)
        {
            string want = choice == "1440" || choice == "2160" || choice == "original" ? choice
                        : MonitorHeight < 1080 ? "original" : MonitorHeight <= 1440 ? "1440" : "2160";
            Vbf v = null; string used = "original";
            if (want != "original")
                foreach (string c in want == "1440" ? new[] { "1440", "2160" } : new[] { "2160", "1440" })
                {
                    if (!resArchives.TryGetValue(c, out v)) { v = Open("metamenu_" + c + ".vbf"); resArchives[c] = v; }
                    if (v != null) { used = c; break; }
                }
            resVbf = v; ResUsed = used;
            images.Clear(); lock (logged) logged.Clear();
            Log.Write("data: menu graphics " + choice + " (monitor height " + MonitorHeight + "): " + (v != null ? "metamenu_" + used + ".vbf " + v.Count + " files" : "metamenu.vbf (no resolution archive)"));
        }

        static Vbf ArchiveFor(string name)
        {
            if (Vbf.Key(name).StartsWith("ffx-2_data/"))               // FFX-2's texts (the Controls page): data\FFX2_Data.vbf, opened when first needed
            {
                if (!game2VbfTried)
                {
                    game2VbfTried = true; string p2 = Path.Combine(dataDir, "FFX2_Data.vbf");
                    try { if (File.Exists(p2)) { game2Vbf = new Vbf(p2); Log.Write("data: FFX2_Data.vbf " + game2Vbf.Count + " files"); } else Log.Write("data: FFX2_Data.vbf missing"); }
                    catch (Exception e) { Log.Write("data: FFX2_Data.vbf: " + e.Message); }
                }
                return game2Vbf;
            }
            if (!Vbf.Key(name).StartsWith("ffx_data/")) return metaVbf;
            if (!gameVbfTried)
            {
                gameVbfTried = true; string p = Path.Combine(dataDir, "FFX_Data.vbf");
                try { if (File.Exists(p)) { gameVbf = new Vbf(p); Log.Write("data: FFX_Data.vbf " + gameVbf.Count + " files"); } else Log.Write("data: FFX_Data.vbf missing"); }
                catch (Exception e) { Log.Write("data: FFX_Data.vbf: " + e.Message); }
            }
            return gameVbf;
        }

        static string LoosePath(string name)
        {
            string p;
            try { p = Path.GetFullPath(Path.Combine(dataDir, name.Replace('/', Path.DirectorySeparatorChar))); } catch { return null; }
            foreach (string r in looseRoots) if (p.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return p;   // nothing outside these folders
            return null;
        }

        public static byte[] TryLoad(string name)
        {
            string loose = LoosePath(name); byte[] d; string src;
            if (loose != null && File.Exists(loose)) { d = File.ReadAllBytes(loose); src = "loose " + loose; }
            else if (resVbf != null && !Vbf.Key(name).StartsWith("ffx_data/") && (d = resVbf.Read(name)) != null) src = "metamenu_" + ResUsed + ".vbf";
            else if (modVbf != null && !Vbf.Key(name).StartsWith("ffx_data/") && (d = modVbf.Read(name)) != null) src = "metamenu_mod.vbf";
            else { Vbf v = ArchiveFor(name); d = v != null ? v.Read(name) : null; src = "vbf"; }
            lock (logged) if (logged.Add(Vbf.Key(name))) Log.Write("file " + name + " <- " + (d == null ? "MISSING" : src + " (" + d.Length + " bytes)"));
            return d;
        }

        // the file as shipped in the archive (layout metrics of images that may be replaced by upscaled loose files)
        public static byte[] LoadOriginal(string name)
        {
            Vbf v = ArchiveFor(name); byte[] d = v != null ? v.Read(name) : null;
            return d ?? Load(name);
        }

        public static byte[] Load(string name)
        {
            byte[] d = TryLoad(name);
            if (d == null) throw new FileNotFoundException("not in data\\metamenu and not in metamenu.vbf: " + name);
            return d;
        }

        public static string Text(string name)
        {
            string s = Encoding.UTF8.GetString(Load(name));
            return s.Length > 0 && s[0] == '\ufeff' ? s.Substring(1) : s;
        }

        public static Image Image(string name)
        {
            Image img; string k = Vbf.Key(name);
            if (images.TryGetValue(k, out img)) return img;
            using (var ms = new MemoryStream(Load(name))) using (var tmp = System.Drawing.Image.FromStream(ms)) img = new Bitmap(tmp);   // own copy: no stream kept open
            images[k] = img;
            return img;
        }
    }

    // ---- configuration: the original's Program class ---------------------------------------------------------------
    static class Config
    {
        public static string GameDir, IniPath, Language = "en", Version = "1.0.0";
        public static bool IsSmall;
        public static Dictionary<string, string> Ini;   // insertion order is kept, like the original (Dictionary, no removals)
        static string iniNewLine = "\r\n";              // the game writes LF, the original launcher CRLF: keep what the file has
        public const string FFX = "FFX.exe", FFX2 = "FFX-2.exe", Credit = "FFX&X-2_Will.exe";
        public const string SideStoryParam = "_ECalm", LastMissionParam = "FFX2_LASTMISSION";

        public static void Init(string gameDir, Rectangle monitor, string iniOverride, string revoFxOverride, string fps60Override)
        {
            GameDir = gameDir;
            RevoFx.JsonPath = revoFxOverride ?? Path.Combine(gameDir, @"RevoFX\game\FINALFANTASYXX-2RevoFX.json");
            Fps60Ini.BaseDir = fps60Override ?? gameDir;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), @"SQUARE ENIX\FINAL FANTASY X&X-2 HD Remaster");
            try { Directory.CreateDirectory(dir); } catch { }
            IniPath = iniOverride ?? Path.Combine(dir, "GameSetting.ini");
            Log.Write("ini: " + IniPath);
            int lang = Native.GetUserDefaultLangID();
            switch (lang & 0x3F)
            {
                case 0x04: Language = "ch"; break;    // the original: 0x404 / 0xC04 / 0x1404 (traditional Chinese) only
                case 0x0C: Language = "fr"; break;
                case 0x07: Language = "de"; break;
                case 0x10: Language = "it"; break;
                case 0x0A: Language = "es"; break;
                case 0x11: Language = "jp"; break;
                case 0x12: Language = "kr"; break;
                default: Language = "en"; break;
            }
            if ((lang & 0x3F) == 0x04 && lang != 0x404 && lang != 0xC04 && lang != 0x1404) Language = "en";
            IsSmall = monitor.Height < 720;
            LoadIni();
        }

        public static string LanguagePath { get { return Language == "en" ? "us" : Language == "es" ? "sp" : Language; } }

        // XPath condition of the original (Program.XmlNodeSelectorCondition)
        public static string Cond { get { return "[(not(@language) or @language='" + Language + "')" + (IsSmall ? "" : " and not(@small)") + "]"; } }

        public static void LoadIni()
        {
            Ini = null;
            if (File.Exists(IniPath))
            {
                try
                {
                    var re = new Regex(@"^[ \t]*(.+?)[\t ]*=[\t ]*(.+?)[\t ]*$"); var d = new Dictionary<string, string>();
                    string all = File.ReadAllText(IniPath); iniNewLine = all.Contains("\r\n") || !all.Contains("\n") ? "\r\n" : "\n";
                    foreach (string line in File.ReadAllLines(IniPath)) { var m = re.Match(line); if (m.Success) d.Add(m.Groups[1].Value, m.Groups[2].Value); }
                    if (d.ContainsKey("Resolution")) d["Resolution"] = d["Resolution"].Replace("*", "x");
                    Ini = d;
                }
                catch (Exception e) { Log.Write("ini: " + e.Message); Ini = null; }
            }
            string l = Get("Language", "");
            if (l != "") Language = l; else Set("Language", Language);
        }

        public static string Get(string key, string def) { string v; return Ini != null && Ini.TryGetValue(key, out v) ? v : def; }
        public static void Set(string key, string val) { if (Ini == null) Ini = new Dictionary<string, string>(); Ini[key] = val; }

        public static void SaveIni()
        {
            if (Ini == null) return;
            var lines = new List<string>();
            foreach (var kv in Ini) lines.Add(string.Format("{0}={1}", kv.Key, kv.Key == "Resolution" ? kv.Value.Replace("x", "*") : kv.Value));
            File.WriteAllText(IniPath, string.Join(iniNewLine, lines.ToArray()) + iniNewLine);
            Log.Write("ini saved: " + IniPath);
        }

        // every mode of every monitor attached to the desktop (the original: a fixed list cut to the primary monitor's modes)
        public static List<string> Resolutions()
        {
            var set = new HashSet<string>(); var list = new List<Size>();
            var dd = new Native.DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            for (int i = 0; Native.EnumDisplayDevices(null, i, ref dd, 0); i++, dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE)))
            {
                if ((dd.StateFlags & 1) == 0) continue;                                       // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                var dm = new Native.DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
                for (int m = 0; Native.EnumDisplaySettings(dd.DeviceName, m, ref dm); m++)
                    if (dm.dmPelsWidth >= 640 && dm.dmPelsHeight >= 480 && set.Add(dm.dmPelsWidth + "x" + dm.dmPelsHeight)) list.Add(new Size(dm.dmPelsWidth, dm.dmPelsHeight));
            }
            foreach (Screen s in Screen.AllScreens) if (set.Add(s.Bounds.Width + "x" + s.Bounds.Height)) list.Add(s.Bounds.Size);
            string cur = Get("Resolution", "");
            var mc = Regex.Match(cur, @"^(\d+)x(\d+)$");
            if (mc.Success && set.Add(cur)) list.Add(new Size(int.Parse(mc.Groups[1].Value), int.Parse(mc.Groups[2].Value)));
            list.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
            return list.ConvertAll(s => s.Width + "x" + s.Height);
        }
    }

    // ---- localisation (SettingsLoc.xml) and layout (MenuActiveRects.xml) ---------------------------------------------
    static class Loc
    {
        public static string Title = "FINAL FANTASY X/X-2 LAUNCHER", ConfigTitle = "SETTING", Font = "";
        static readonly Dictionary<string, string> texts = new Dictionary<string, string>();

        public static void Load()
        {
            texts.Clear();
            var doc = new XmlDocument(); doc.LoadXml(Data.Text("MetaMenu/PS3Data/MenuMetaMenu/SettingsLoc.xml"));
            var lang = doc.SelectSingleNode("/Localization/Lang" + Config.Cond);
            if (lang == null) return;
            Title = Inner(lang, "./Title", Title); ConfigTitle = Inner(lang, "./ConfigTitle", ConfigTitle); Font = Inner(lang, "./Font", "");
            foreach (XmlNode n in lang.SelectNodes("./Text"))
            {
                var a = n.Attributes["Control"] ?? n.Attributes["Key"];
                if (a != null) texts[a.Value] = n.InnerText;
            }
        }

        static string Inner(XmlNode n, string path, string def) { var x = n.SelectSingleNode(path); return x != null ? x.InnerText : def; }

        public static string T(string key, string def) { string v; return texts.TryGetValue(key, out v) ? v : def; }

        // a font family that exists; the localised one first ("MS Shell Dlg" is a GDI alias, not a GDI+ family)
        public static FontFamily Family(params string[] prefer)
        {
            var names = new List<string>();
            if (Font != "" && Font != "MS Shell Dlg") names.Add(Font);
            // Japanese / Korean / Chinese: families with those glyphs first. Windows GDI+ substitutes missing glyphs by itself;
            // Wine does so only in newer versions, and Proton ships these names as replacements (Source Han Sans).
            string lp = Config.LanguagePath;
            if (lp == "jp") names.AddRange(new[] { "Meiryo", "Yu Gothic UI", "MS UI Gothic", "MS Gothic", "Microsoft YaHei", "SimSun" });
            else if (lp == "kr") names.AddRange(new[] { "Malgun Gothic", "Gulim", "Microsoft YaHei", "SimSun" });
            else if (lp == "ch") names.AddRange(new[] { "Microsoft JhengHei", "Microsoft YaHei", "SimSun", "MingLiU" });
            names.AddRange(prefer);
            foreach (string n in names) { try { return new FontFamily(n); } catch { } }
            return FontFamily.GenericSansSerif;
        }
    }

    // ---- single instance (same mapping and message as the original, so only one launcher of either kind runs) -------
    static class Instance
    {
        public static int FocusMessage; static IntPtr mapping;

        public static bool Acquire()
        {
            FocusMessage = Native.RegisterWindowMessage("822CE147-7153-40B5-B587-10B8D0A05D15"); if (FocusMessage == 0) FocusMessage = 0x444;
            mapping = Native.CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 4, 0, 4, "2BB0BCBC-A134-44E0-8DCC-8FC3FAE4A7E9");
            if (mapping != IntPtr.Zero && Marshal.GetLastWin32Error() == 183)
            {
                Native.SendMessage(MainWindow, FocusMessage, IntPtr.Zero, IntPtr.Zero);
                Native.CloseHandle(mapping); mapping = IntPtr.Zero;
                return false;
            }
            return true;
        }

        public static IntPtr MainWindow
        {
            get
            {
                if (mapping == IntPtr.Zero) return IntPtr.Zero;
                IntPtr v = Native.MapViewOfFile(mapping, 4, 0, 0, 4); if (v == IntPtr.Zero) return IntPtr.Zero;
                IntPtr h = Marshal.ReadIntPtr(v); Native.UnmapViewOfFile(v); return h;
            }
            set
            {
                if (mapping == IntPtr.Zero) return;
                IntPtr v = Native.MapViewOfFile(mapping, 2, 0, 0, 4); if (v == IntPtr.Zero) return;
                Marshal.WriteIntPtr(v, value); Native.UnmapViewOfFile(v);
            }
        }

        public static void Release() { if (mapping != IntPtr.Zero) Native.CloseHandle(mapping); mapping = IntPtr.Zero; }
    }

    sealed class Options
    {
        public int Monitor = -1, ShotDelay = 2500, MonitorHeight; public Size Size = Size.Empty;
        public string Shot, Keys, Ini, RevoFx, Fps60Dir, StartTest, LauncherIni; public bool Exit, NoRevoFx;

        static Size ParseSize(string s) { string[] v = s.ToLowerInvariant().Split('x'); return new Size(int.Parse(v[0]), int.Parse(v[1])); }

        public static Options Parse(string[] a)
        {
            var o = new Options();
            for (int i = 0; i < a.Length; i++)
            {
                string s = a[i].ToLowerInvariant(); bool more = i + 1 < a.Length;
                try
                {
                    if (s == "--monitor" && more) o.Monitor = int.Parse(a[++i]);
                    else if (s == "--size" && more) o.Size = ParseSize(a[++i]);
                    else if (s == "--shot" && more) o.Shot = Path.GetFullPath(a[++i]);
                    else if (s == "--shot-delay" && more) o.ShotDelay = int.Parse(a[++i]);
                    else if (s == "--keys" && more) o.Keys = a[++i];
                    else if (s == "--exit") o.Exit = true;
                    else if (s == "--ini" && more) o.Ini = Path.GetFullPath(a[++i]);
                    else if (s == "--revofx" && more) o.RevoFx = Path.GetFullPath(a[++i]);
                    else if (s == "--fps60dir" && more) o.Fps60Dir = Path.GetFullPath(a[++i]);
                    else if (s == "--norevofx") o.NoRevoFx = true;
                    else if (s == "--launcherini" && more) o.LauncherIni = Path.GetFullPath(a[++i]);
                    else if (s == "--monitorheight" && more) o.MonitorHeight = int.Parse(a[++i]);   // test: as if the monitor were this high
                    else if (s == "--start-test" && more) o.StartTest = a[++i];
                }
                catch { }
            }
            return o;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }                            // real monitor pixels
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            Log.Open(Path.ChangeExtension(Application.ExecutablePath, ".log"));
            Options opt = Options.Parse(args);
            try
            {
                Environment.CurrentDirectory = dir;
                if (!Instance.Acquire()) { Log.Write("another launcher instance is running; focused it"); return 0; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Screen[] all = Screen.AllScreens;
                Screen scr = opt.Monitor >= 0 && opt.Monitor < all.Length ? all[opt.Monitor] : Screen.PrimaryScreen;
                Rectangle area = scr.Bounds;
                if (opt.Size != Size.Empty) area = new Rectangle(area.X + (area.Width - opt.Size.Width) / 2, area.Y + (area.Height - opt.Size.Height) / 2, opt.Size.Width, opt.Size.Height);
                Log.Write("FFX Enhanced " + Patcher.Version);
                Log.Write("monitor " + scr.DeviceName + " " + scr.Bounds + ", area " + area + ", runtime " + Environment.Version + (Type.GetType("Mono.Runtime") != null ? " (Mono)" : ""));
                Data.Init(dir);
                RevoFx.ForceAbsent = opt.NoRevoFx; Config.Init(dir, scr.Bounds, opt.Ini, opt.RevoFx, opt.Fps60Dir);
                // ffx_enh.ini: FFX_60fps.ini renamed / brought to the current layout, the old FFX&X-2_LAUNCHER.ini moved in
                if (Patcher.Available) { string im = Patcher.EnsureIni(Path.GetDirectoryName(Fps60Exe.IniPath)); if (im != null) Log.Write("settings: " + im); }
                if (Patcher.Available) foreach (string l in Patcher.MigrateNames(dir)) Log.Write("fps60exe: " + l);   // FFX_60fps.exe -> FFX_enh.exe
                LauncherRes.IniPath = opt.LauncherIni ?? Fps60Exe.IniPath;
                LauncherRes.MigrateOld(Path.ChangeExtension(Application.ExecutablePath, ".ini"));
                Data.MonitorHeight = opt.MonitorHeight > 0 ? opt.MonitorHeight : scr.Bounds.Height;
                Data.SelectResources(LauncherRes.Choice());
                Loc.Load();
                Originals.EnsureBackups();                                        // every start: backup of the originals (CRC checked)
                Patcher.SourceOf = Originals.SourceFor;
                Fps60Exe.RefreshAtStart();
                Originals.SyncAtStart();
                Application.Run(new LauncherForm(area, opt));
                return 0;
            }
            catch (Exception e)
            {
                Log.Write("error: " + e);
                MessageBox.Show(e.Message, "FFX&X-2 Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally
            {
                Instance.Release();
                // after a game start nothing of the launcher may stay in memory: no thread or library keeps the process alive
                if (LauncherForm.GameStarted) { Log.Write("exit: game started, launcher process ends"); Environment.Exit(0); }
            }
        }
    }
}
