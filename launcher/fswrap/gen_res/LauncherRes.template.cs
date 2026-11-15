// Menu graphics of the launcher in three resolutions, chosen by the monitor or by hand (Advanced settings):
//   data\metamenu_1440.vbf  the 144 menu PNGs at 2x (title 2560x1440)    tools\vbfbuild.py - data\metamenu_1440 ...
//   data\metamenu_2160.vbf  the same PNGs at 3x (title 3840x2160)        tools\vbfbuild.py - data\metamenu_2160 ...
//   data\metamenu_mod.vbf   the launcher's own files that are no upscaled image and not in metamenu.vbf or different
//                           from it (the menu music metamenu.ogg); expected next to metamenu.vbf, the launcher runs without
//   data\metamenu.vbf       the original archive, always the base (LoadOriginal reads it)
// Read order (Data.TryLoad): loose data\metamenu\..., the resolution archive, metamenu_mod.vbf, metamenu.vbf.
// Choice: [Launcher] Resources in ffx_enh.ini (game folder; FFX&X-2_LAUNCHER.ini before 2026-11-15): auto (monitor height below 1080 -> original,
// 1080 .. 1440 -> 1440, above -> 2160), 1440, 2160, original. A missing archive falls back to the other resolution, then to metamenu.vbf alone.
// The texts are in the games' eight languages (us de fr it sp jp kr ch); this file is ASCII (\u escapes).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FFXLauncher
{
    static class LauncherRes
    {
        public static readonly string[] Langs = { "us", "de", "fr", "it", "sp", "jp", "kr", "ch" };
        public static readonly string[] Values = { "auto", "1440", "2160", "original" };
        static readonly string[] Header = {{header}};
        static readonly string[] Label = {{label}};
        static readonly string[] Auto = {{auto}};
        static readonly string[] Orig = {{orig}};
        static readonly string[] Help = {{help}};
        static readonly string[] Used = {{used}};
        static readonly string[] CompatLabel = {{compat_label}};
        static readonly string[] CompatHelp = {{compat_help}};
        static readonly string[] TabControls = {{tab_controls}};
        static readonly string[] TabAdvanced = {{tab_advanced}};
        static readonly string[] RevoLabel = {{revo_label}};
        static readonly string[] RevoHelp = {{revo_help}};
        static readonly string[] DefLabel = {{defaults_label}};
        static readonly string[] DefHelp = {{defaults_help}};
        static readonly string[] DefDone = {{defaults_done}};
        static readonly string[] SkipLabel = {{skip_label}};
        static readonly string[] SkipHelp = {{skip_help}};
        static readonly string[] NoLaunchLabel = {{nolaunch_label}};
        static readonly string[] NoLaunchHelp = {{nolaunch_help}};
        static readonly string[] EflLabel = {{efl_label}};
        static readonly string[] EflHelp = {{efl_help}};
        static readonly string[] UwLabel = {{uw_label}};
        static readonly string[] UwHelp = {{uw_help}};

        static int Li() { int i = Array.IndexOf(Langs, Config.LanguagePath); return i < 0 ? 0 : i; }
        public static string HeaderText { get { return Header[Li()]; } }
        public static string LabelText { get { return Label[Li()]; } }
        public static string HelpText { get { return Help[Li()]; } }
        public static string CompatLabelText { get { return CompatLabel[Li()]; } }
        public static string CompatHelpText { get { return CompatHelp[Li()]; } }
        public static string ControlsTitle { get { return TabControls[Li()]; } }       // settings page tab (a placeholder for now)
        public static string AdvancedTitle { get { return TabAdvanced[Li()]; } }       // settings page tab: every advanced setting
        public static string RevoLabelText { get { return RevoLabel[Li()]; } }          // Advanced tab: Use RevoFX
        public static string RevoHelpText { get { return RevoHelp[Li()]; } }
        public static string DefaultsLabel { get { return DefLabel[Li()]; } }           // settings page: the fourth button
        public static string DefaultsHelp { get { return DefHelp[Li()]; } }
        public static string DefaultsDone { get { return DefDone[Li()]; } }
        public static string SkipLabelText { get { return SkipLabel[Li()]; } }          // Advanced tab: [start] skip_logos
        public static string SkipHelpText { get { return SkipHelp[Li()]; } }
        public static string NoLaunchLabelText { get { return NoLaunchLabel[Li()]; } }  // Advanced tab: [start] no_launcher_restart
        public static string NoLaunchHelpText { get { return NoLaunchHelp[Li()]; } }
        public static string EflLabelText { get { return EflLabel[Li()]; } }            // Advanced tab: [start] file_loader
        public static string EflHelpText { get { return EflHelp[Li()]; } }
        public static string UwLabelText { get { return UwLabel[Li()]; } }              // Advanced tab: [start] ultrawide (experimental)
        public static string UwHelpText { get { return UwHelp[Li()]; } }
        public static string[] Texts() { return new[] { Auto[Li()], "1440p (2x)", "2160p (3x)", Orig[Li()] }; }
        // bottom right of the settings page: the archive in use
        public static string UsedText() { return string.Format(Used[Li()], Data.ResUsed == "original" ? Orig[Li()] : Data.ResUsed); }

        // ---- the launcher's own settings: [Launcher] in ffx_enh.ini, the settings file of FFX Enhanced in the game folder ----
        public static string IniPath;                    // set in Main (test aid --launcherini <path>)

        // FFX&X-2_LAUNCHER.ini of the builds before 2026-11-15: its [Launcher] values go into ffx_enh.ini, the file is kept as .bak
        public static void MigrateOld(string old)
        {
            try
            {
                if (IniPath == null || old == null || !File.Exists(old) || string.Equals(Path.GetFullPath(old), Path.GetFullPath(IniPath), StringComparison.OrdinalIgnoreCase)) return;
                string sec = "";
                foreach (string raw in File.ReadAllLines(old))
                {
                    string t = raw.Trim();
                    if (t.StartsWith("[")) { sec = t.Trim('[', ']').Trim(); continue; }
                    int eq = t.IndexOf('='); if (eq <= 0 || t.StartsWith(";") || !sec.Equals("Launcher", StringComparison.OrdinalIgnoreCase)) continue;
                    WritePrivateProfileString("Launcher", t.Substring(0, eq).Trim(), t.Substring(eq + 1).Trim(), IniPath);
                }
                string bak = old + ".bak"; if (File.Exists(bak)) File.Delete(bak);
                File.Move(old, bak);
                Log.Write("launcher ini: " + Path.GetFileName(old) + " moved into " + Path.GetFileName(IniPath) + " (the old file is " + Path.GetFileName(bak) + ")");
            }
            catch (Exception e) { Log.Write("launcher ini: " + old + ": " + e.Message); }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool WritePrivateProfileString(string section, string key, string value, string file);

        public static string Choice()
        {
            try
            {
                if (IniPath == null || !File.Exists(IniPath)) return "auto";
                string sec = "";
                foreach (string raw in File.ReadAllLines(IniPath))
                {
                    string t = raw.Trim();
                    if (t.StartsWith("[")) { sec = t.Trim('[', ']').Trim().ToLowerInvariant(); continue; }
                    int eq = t.IndexOf('='); if (sec != "launcher" || eq < 0 || t.StartsWith(";")) continue;
                    if (t.Substring(0, eq).Trim().Equals("Resources", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = t.Substring(eq + 1).Trim().ToLowerInvariant();
                        return Array.IndexOf(Values, v) >= 0 ? v : "auto";
                    }
                }
            }
            catch (Exception e) { Log.Write("launcher ini: " + e.Message); }
            return "auto";
        }

        public static void SetChoice(string v)
        {
            if (IniPath == null) return;
            try
            {
                var lines = new List<string>(File.Exists(IniPath) ? File.ReadAllLines(IniPath) : new string[0]);
                int sec = -1, key = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.StartsWith("["))
                    {
                        if (sec >= 0) break;
                        if (t.Trim('[', ']').Trim().Equals("Launcher", StringComparison.OrdinalIgnoreCase)) sec = i;
                        continue;
                    }
                    int eq = t.IndexOf('=');
                    if (sec >= 0 && eq > 0 && t.Substring(0, eq).Trim().Equals("Resources", StringComparison.OrdinalIgnoreCase)) key = i;
                }
                if (key >= 0) lines[key] = "Resources=" + v;
                else if (sec >= 0) lines.Insert(sec + 1, "Resources=" + v);
                else
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                    lines.Add("[Launcher]"); lines.Add("; menu graphics: auto (by monitor height: below 1080 original, up to 1440 1440, above 2160), 1440, 2160, original"); lines.Add("Resources=" + v);
                }
                File.WriteAllText(IniPath, string.Join("\r\n", lines.ToArray()) + "\r\n", new UTF8Encoding(false));
                Log.Write("launcher ini: Resources=" + v + " -> " + IniPath);
            }
            catch (Exception e) { Log.Write("launcher ini: " + IniPath + ": " + e.Message + " (not written)"); }
        }
    }
}
