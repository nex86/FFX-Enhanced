// Settings in the style of the game's own option menu (escmenu.swf bitmaps, the game's localised texts), drawn
// fullscreen inside the launcher window (no Windows dialog). Pages:
//   Audio    voice / music / SFX / master volume (+ mute on the master row)       INI VoiceVolume MusicVolume SFXVolume MasterVolume Mute
//   Video    resolution, screen mode, quality preset (launcher), brightness, VSync, frame rate (RevoFX JSON, only with
//            RevoFX installed: [Game] FrameRate 30 / 60 / 120), anisotropic filtering, shadows,
//            texture quality, anti-aliasing (MSAA), colour correction, post AA, unsharp mask, ambient occlusion
//   Controls key bindings of the three games (INI ffxKeyBinding / ffx2KeyBinding / lmKeyBinding, KeyBind.cs): a Game row, then
//            the rows of the game's Keyboard page (Keyboard 1 / Keyboard 2) and of its Controller page. Left / right = column,
//            confirm or a click = wait for a key / button (Esc = keep), Del = clear Keyboard 2. A key that is in use
//            elsewhere swaps places with the old one, as the game allows no key twice.
//   Language text language of the launcher and the games (INI Language)
//   Advanced the 60 fps patch's settings (ffx_enh.ini, without RevoFX) and the launcher's own: menu graphics,
//            Proton compatibility mode (last row)
// Values and their INI strings are the tables of FFX.exe (see docs\LAUNCHER.md): e.g. Shadow 0 = Off, 1 = High,
// 2 = Medium, 3 = Low; MSAA 0/2/4/8; PostAA PAA_OFF/FXAA/SMAA1..4; HDAO Off/Low/Medium/High. As in the game, MSAA
// and ambient occlusion exclude each other, and changing a detail sets the quality preset to Custom. Game keys that
// are not in the INI yet are only written when they are changed. Unknown INI values are kept and shown as they are.
// Input: up / down = row (then the buttons), left / right = value, LB / RB or Page Up / Down = page, confirm = button
// or next row, back = Cancel, mute = speaker. Mouse: hover, click arrows / value / tabs / buttons, drag bars, wheel.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace FFXLauncher
{
    interface IOverlay
    {
        void Paint(Graphics g, RectangleF view, float scale);      // view: the menu area on screen, scale: screen px per menu px
        void MouseMove(PointF p); void MouseDown(PointF p); void MouseUp(PointF p); void Wheel(PointF p, int delta);   // p in menu coordinates
        bool Act(Act a);
        bool Key(Keys k);                                            // raw keys not covered by Act (true = handled)
        void Tick(int ms);
        bool Closed { get; }
        bool Dirty { get; set; }                                     // needs a repaint
        string State();
    }

    sealed class SettingsView : IOverlay
    {
        sealed class Opt
        {
            public string Key, Label, Help; public string[] Values, Texts; public bool Bar, Detail, Changed, Existed, InRevoFx;
            public bool Header;                  // a section title: not selectable, no value
            public bool InLauncher;              // a setting of the launcher itself (FFX&X-2_LAUNCHER.ini)
            public bool Locked;                  // set by the quality preset: shown dimmed, not changeable
            public BindRow Bind;                 // a key binding row of the Controls page (KeyBind.cs)
            public string[] Cols;                // a section title with column captions (Keyboard 1 / Keyboard 2)
            public string Def; public int DefValue = -1;   // default (the Defaults button): value of a list, of a bar; none = not reset
            public string IniSec;                // a setting of ffx_enh.ini ([fps60] / [rows]); null = GameSetting.ini or RevoFX
            public int Index, Value = 100;
            public string Text { get { return Bar ? Value.ToString() : Texts[Index]; } }
        }
        sealed class Page { public string Title; public List<Opt> Opts = new List<Opt>(); }

        readonly Audio audio; readonly float k; Bitmap back, frame;
        List<Page> pages; int page, sel, top; bool closed, dragging, mute, dirty, contentDirty = true; string message;
        // 60 fps patch on Apply / OK (Fps60Exe.Ensure): a worker thread writes the EXEs, the page shows a progress panel and takes
        // no input until it is done; OK closes the page afterwards (or stays open when something went wrong).
        System.Threading.Thread worker; readonly object busyLock = new object();
        string busyFile, busyResult; double busyFrac; bool busyOk, closeAfter; int busyShown = -1; string busyShownFile;
        bool Busy { get { return worker != null; } }
        // Dirty: the page changed, so the content (all but the pointer) is drawn again into 'frame'
        public bool Dirty { get { return dirty; } set { dirty = value; if (value) contentDirty = true; } }

        // The selection pointer sways like the cursor of FFX-2's menus (sprite 772 of saveloadx2_jp.swf, stage 1280x720,
        // 24 fps): 23 frames per cycle, from rest linearly 10 px to the left in 11 frames and back in 11, stepped per
        // frame as Flash plays it. Its tip at rest is on the selected item. Only the pointer's area is repainted for it
        // (AnimDirty / AnimRect, in window pixels).
        const float SwayAmp = 10, CursorH = 26; const int SwayFps = 24, SwayFrames = 23, SwayHalf = 11;
        PointF? cursorTip; RectangleF lastView; float lastScale; int swayDrawn;
        public bool AnimDirty; public Rectangle AnimRect;
        public bool Closed { get { return closed; } }
        public string SavedLanguage;
        public bool ResourcesChanged;                                    // the menu graphics were switched: the form reloads its menu

        // layout (1280x720)
        const float ListX = 80, ListW = 1120, ListY = 196, RowH = 42, ValX = 760, ValW = 420;
        const int Visible = 8;
        static readonly RectangleF HelpR = new RectangleF(60, 562, 800, 140), TitleR = new RectangleF(0, 26, 1060, 64);
        static RectangleF TabR(int i) { return new RectangleF(80 + i * 194, 112, 184, 56); }   // five tabs: Audio, Video, Controls, Language, Advanced
        static RectangleF BtnR(int i) { return new RectangleF(920, 556 + i * 38, 270, 34); }   // OK, Cancel, Apply, Defaults
        RectangleF RowR(int vis) { return new RectangleF(ListX, ListY + vis * RowH, ListW, RowH - 4); }

        public SettingsView(Audio audio, int menuWidth)
        {
            this.audio = audio; k = menuWidth / 1280f;
            Config.LoadIni();
            Build();
            Dirty = true;
        }

        Opt Bar(string key, int label, string defLabel, int help, int def)
        {
            var o = new Opt { Key = key, Label = GameUi.T(label, defLabel), Help = GameUi.T(help, ""), Bar = true };
            string v = Config.Get(key, null); int n;
            o.Existed = v != null; o.Value = v != null && int.TryParse(v, out n) ? Math.Max(0, Math.Min(100, n)) : def; o.DefValue = def;
            return o;
        }

        Opt Choice(string key, string label, string help, string[] values, string[] texts, string def)
        {
            var o = new Opt { Key = key, Label = label, Help = help, Values = values, Texts = texts, Def = def };
            string v = Config.Get(key, null); o.Existed = v != null; if (v == null) v = def;
            o.Index = Array.IndexOf(values, v);
            if (o.Index < 0)                                                                          // keep an unknown value
            {
                var vs = new List<string>(values); vs.Add(v); o.Values = vs.ToArray();
                var ts = new List<string>(texts); ts.Add(v); o.Texts = ts.ToArray(); o.Index = o.Values.Length - 1;
            }
            return o;
        }

        Opt G(string key, int label, string defLabel, string[] values, int[] textIds, string[] defTexts, string def)
        {
            var t = new string[values.Length];
            for (int i = 0; i < t.Length; i++) t[i] = textIds != null ? GameUi.T(textIds[i], defTexts[i]) : defTexts[i];
            var o = Choice(key, GameUi.T(label, defLabel), GameUi.T(label + 1, ""), values, t, def); return o;
        }

        // Frame rate: [Game] FrameRate of RevoFX (RevoFX\game\FINALFANTASYXX-2RevoFX.json) and [fps60] enabled / fps of the
        // external 60 fps mod (fps60.ini, fps60\fps60.ini); shown when either is installed, written to every one present.
        // Also shown when FFX_enh.exe / FFX-2_enh.exe exist: then ffx_enh.ini ([fps60] fps) is written as well, and without
        // RevoFX the launcher starts those EXEs from 60 fps on (Fps60Exe in RevoFx.cs).
        // Shown value: Fps60Exe.FrameRate() - with RevoFX the JSON first, without it ffx_enh.ini first; 30 when no store has it.
        Opt FrameRate()
        {
            bool de = Config.LanguagePath == "de";
            var o = new Opt
            {
                Key = "FrameRate", InRevoFx = true, Label = "Framerate",
                Help = de ? "Bilder pro Sekunde (RevoFX: FINALFANTASYXX-2RevoFX.json [Game] FrameRate; fps60.ini: enabled, fps). 30 fps ist das Original;\n60 und 120 fps zeichnet der Framerate-Patch. Gilt ab dem n\u00e4chsten Spielstart.\nOhne RevoFX: FFX bis 240 FPS (ab 90 FPS VSync aus), FFX-2 zeichnet h\u00f6chstens 60 FPS."
                          : "Frames per second (RevoFX: FINALFANTASYXX-2RevoFX.json [Game] FrameRate; fps60.ini: enabled, fps). 30 fps is the original;\n60 and 120 fps are drawn by the frame rate patch. Takes effect at the next start of the game.\nWithout RevoFX: FFX up to 240 FPS (VSync off from 90 FPS on), FFX-2 draws 60 FPS at most.",
                Values = new[] { "30", "60", "90", "120", "240" }, Texts = new[] { "30 FPS (Original)", "60 FPS", "90 FPS", "120 FPS", "240 FPS" }
            };
            int? cur = RevoFx.GetInt("Game", "FrameRate") ?? Fps60Ini.Get() ?? Fps60Exe.Get(); o.Existed = cur != null;
            if (RevoFx.Present || Fps60Ini.Files().Count > 0 || Fps60Exe.Present || Fps60Exe.CanPatch || Fps60Dll.Present) cur = Fps60Exe.FrameRate();
            o.Def = "60";                                           // the 60 fps patch's optimized setup
            string v = (cur ?? 30).ToString(); o.Index = Array.IndexOf(o.Values, v);
            if (o.Index < 0) { var vl = new List<string>(o.Values); vl.Add(v); var tl = new List<string>(o.Texts); tl.Add(v + " FPS"); o.Values = vl.ToArray(); o.Texts = tl.ToArray(); o.Index = o.Values.Length - 1; }   // keep an unknown value
            return o;
        }

        void Build()
        {
            pages = new List<Page>();
            var audioP = new Page { Title = GameUi.T(545, "Audio") };
            audioP.Opts.Add(Bar("VoiceVolume", 553, "Voice volume", 554, 100));
            audioP.Opts.Add(Bar("MusicVolume", 555, "Music volume", 556, 100));
            audioP.Opts.Add(Bar("SFXVolume", 557, "SFX volume", 558, 100));
            audioP.Opts.Add(Bar("MasterVolume", 559, "Master volume", 560, 100));
            mute = Config.Get("Mute", "Off") != "Off";
            pages.Add(audioP);

            var video = new Page { Title = GameUi.T(547, "Video") };
            var res = Config.Resolutions(); string r0 = Config.Get("Resolution", "1280x720"); if (!res.Contains(r0)) res.Add(r0);
            var rt = res.ConvertAll(x => x.Replace("x", " x "));
            video.Opts.Add(Choice("Resolution", GameUi.T(561, "Resolution"), GameUi.T(562, ""), res.ToArray(), rt.ToArray(), r0));
            video.Opts.Add(Choice("ScreenMode", GameUi.T(563, "Screen Mode"), GameUi.T(564, ""), new[] { "SM_WINDOW", "SM_FULLSCREEN", "SM_BORDERLESS" },
                new[] { GameUi.T(565, Loc.T("SM_WINDOW", "Windowed Mode")), GameUi.T(566, Loc.T("SM_FULLSCREEN", "Full Screen Mode")), GameUi.T(567, Loc.T("SM_BORDERLESS", "Borderless Mode")) }, "SM_WINDOW"));
            video.Opts.Add(Choice("Quality", Loc.T("label9", "Video Quality Setting"), "", new[] { "VQ_LOW", "VQ_MEDIUM", "VQ_HIGH", "VQ_CUSTOM" },
                new[] { Loc.T("VQ_LOW", "Low"), Loc.T("VQ_MEDIUM", "Medium"), Loc.T("VQ_HIGH", "High"), Loc.T("VQ_CUSTOM", "Custom") }, "VQ_MEDIUM"));
            var br = Bar("Brightness", 568, "Brightness", 569, 50); video.Opts.Add(br);
            video.Opts.Add(G("VSync", 570, "Vsync", new[] { "On", "Off" }, new[] { 572, 573 }, new[] { "On", "Off" }, "Off"));
            if (RevoFx.Present || Fps60Ini.Files().Count > 0 || Fps60Exe.Present || Fps60Exe.CanPatch || Fps60Dll.Present) video.Opts.Add(FrameRate());
            video.Opts.Add(G("Anisotropic", 574, "Anisotropic Filtering", new[] { "1", "2", "4", "8", "16" }, null, new[] { "1x", "2x", "4x", "8x", "16x" }, "1"));
            video.Opts.Add(G("Shadow", 580, "Shadows", new[] { "0", "3", "2", "1" }, new[] { 579, 582, 583, 584 }, new[] { "Off", "Low (1024x1024)", "Medium (2048x2048)", "High (4096x4096)" }, "0"));
            video.Opts.Add(G("TextureQuality", 585, "Texture Quality", new[] { "TQ_LOW", "TQ_MEDIUM", "TQ_HIGH" }, new[] { 587, 588, 589 }, new[] { "Low", "Medium", "High" }, "TQ_HIGH"));
            video.Opts.Add(G("MSAA", 590, "Anti-Aliasing", new[] { "0", "2", "4", "8" }, new[] { 592, 593, 594, 595 }, new[] { "Off", "MSAA 2x", "MSAA 4x", "MSAA 8x" }, "0"));
            video.Opts.Add(G("ColorCorrection", 596, "Color Correction", new[] { "On", "Off" }, new[] { 598, 599 }, new[] { "On", "Off" }, "On"));
            video.Opts.Add(G("PostAA", 600, "Anti-Aliasing (Post Filter)", new[] { "PAA_OFF", "PAA_FXAA", "PAA_SMAA1", "PAA_SMAA2", "PAA_SMAA3", "PAA_SMAA4" },
                new[] { 602, 603, 604, 605, 606, 607 }, new[] { "Off", "FXAA", "SMAA Low", "SMAA Medium", "SMAA High", "SMAA Ultra" }, "PAA_OFF"));
            video.Opts.Add(G("UnSharpMask", 608, "Unsharp Mask", new[] { "On", "Off" }, new[] { 610, 611 }, new[] { "On", "Off" }, "Off"));
            video.Opts.Add(G("HDAO", 612, "Ambient Occlusion", new[] { "Off", "Low", "Medium", "High" }, new[] { 614, 615, 616, 617 }, new[] { "Off", "HDAO Low", "HDAO Medium", "HDAO High" }, "Off"));
            pages.Add(video);
            foreach (var o in video.Opts) o.Detail = Presets.ContainsKey(o.Key);
            ApplyPreset(false);

            // Controls: the key bindings (ffxKeyBinding / ffx2KeyBinding / lmKeyBinding)
            var ctl = new Page { Title = LauncherRes.ControlsTitle };
            ctl.Opts.Add(new Opt { Key = "BindGame", Label = Config.LanguagePath == "de" ? "Spiel" : "Game", Help = GameUi.T(623, "") + "\n" + GameUi.T(625, ""),
                Values = new[] { "0", "1", "2" }, Texts = KeyBind.GameNames, Index = bindGame, Existed = true });
            BuildControls(ctl);
            pages.Add(ctl);

            var lang = new Page { Title = Loc.T("label1", "Language") };
            lang.Opts.Add(Choice("Language", Loc.T("label2", "Text"), "", new[] { "en", "fr", "de", "it", "es", "jp", "ch", "kr" },
                new[] { "English", "Fran\u00e7ais", "Deutsch", "Italiano", "Espa\u00f1ol", "\u65e5\u672c\u8a9e", "\u7e41\u9ad4\u4e2d\u6587", "\ud55c\uad6d\uc5b4" }, "en"));
            lang.Opts.Add(Voice());
            pages.Add(lang);

            // Advanced (last tab): the 60 fps patch's settings (ffx_enh.ini, without RevoFX) and the launcher's own
            var adv = new Page { Title = LauncherRes.AdvancedTitle };
            if (!RevoFx.Installed && (Fps60Exe.Present || Fps60Exe.CanPatch || Fps60Dll.Present))   // the very first row: Ultrawide (experimental)
            {
                string uv = Fps60Exe.IniGet("start", "ultrawide");
                adv.Opts.Add(new Opt { Key = "ultrawide", IniSec = "start", Label = LauncherRes.UwLabelText, Help = LauncherRes.UwHelpText, Values = new[] { "0", "1" },
                    Texts = new[] { GameUi.T(573, "Off"), GameUi.T(572, "On") }, Index = uv == "1" ? 1 : 0, Def = "0", Existed = uv != null });
            }
            if (!RevoFx.Installed && (Fps60Exe.Present || Fps60Exe.CanPatch || Fps60Dll.Present)) Advanced(adv);
            // the launcher's own advanced settings: which menu graphics (LauncherRes.cs), always present
            adv.Opts.Add(new Opt { Header = true, Key = "#launcher", Label = LauncherRes.HeaderText, Help = "", Values = new[] { "" }, Texts = new[] { "" } });
            if (RevoFx.Switchable)                                      // Use RevoFX: the name of the RevoFX proxy d3d11.dll
                adv.Opts.Add(new Opt { Key = "UseRevoFX", InLauncher = true, Label = LauncherRes.RevoLabelText, Help = LauncherRes.RevoHelpText,
                    Values = new[] { "0", "1" }, Texts = new[] { GameUi.T(573, "Off"), GameUi.T(572, "On") }, Index = RevoFx.Enabled ? 1 : 0, Existed = true });
            if (!RevoFx.Installed && (Fps60Exe.Present || Fps60Exe.CanPatch || Fps60Dll.Present))   // start options: through the patch
            {
                foreach (var so in new[] { new[] { "skip_logos", LauncherRes.SkipLabelText, LauncherRes.SkipHelpText }, new[] { "no_launcher_restart", LauncherRes.NoLaunchLabelText, LauncherRes.NoLaunchHelpText } })
                {
                    string sv = Fps60Exe.IniGet("start", so[0]);
                    adv.Opts.Add(new Opt { Key = so[0], IniSec = "start", Label = so[1], Help = so[2], Values = new[] { "0", "1" }, Texts = new[] { GameUi.T(573, "Off"), GameUi.T(572, "On") },
                        Index = sv == "1" ? 1 : 0, Def = "0", Existed = sv != null });
                }
                if (Fps60Exe.EflPresent)                                // External File Loader: only when its DLL is there; no default (Defaults keeps it)
                {
                    string ev = Fps60Exe.IniGet("start", "file_loader");
                    adv.Opts.Add(new Opt { Key = "file_loader", IniSec = "start", Label = LauncherRes.EflLabelText, Help = LauncherRes.EflHelpText, Values = new[] { "0", "1" },
                        Texts = new[] { GameUi.T(573, "Off"), GameUi.T(572, "On") }, Index = ev == "1" ? 1 : 0, Existed = ev != null });
                }
            }
            var resOpt = new Opt { Key = "Resources", InLauncher = true, Def = "auto", Label = LauncherRes.LabelText, Help = LauncherRes.HelpText, Values = LauncherRes.Values, Texts = LauncherRes.Texts(), Existed = true };
            resOpt.Index = Math.Max(0, Array.IndexOf(resOpt.Values, LauncherRes.Choice())); adv.Opts.Add(resOpt);
            if (Originals.Available)                                    // last row: Proton compatibility mode (Originals.cs)
                adv.Opts.Add(new Opt { Key = "ProtonCompat", InLauncher = true, Label = LauncherRes.CompatLabelText, Help = LauncherRes.CompatHelpText,
                    Values = new[] { "0", "1" }, Texts = new[] { GameUi.T(573, "Off"), GameUi.T(572, "On") }, Index = Originals.CompatOn ? 1 : 0, Existed = true });
            pages.Add(adv);
            page = Math.Min(page, pages.Count - 1); sel = Math.Min(sel, Rows - 1);
        }

        // Advanced settings: every setting of ffx_enh.ini (the 60 fps EXEs, no RevoFX), at the end of the Video page under
        // their own titles, with the texts of AdvancedText.cs (gen_advanced.py) in the game's language. They are read from the
        // INI (its default when a key is missing) and written to it on Apply / OK; the EXEs read them at the next game start.
        static int AdvLang() { int i = Array.IndexOf(AdvancedText.Langs, Config.LanguagePath); return i < 0 ? 0 : i; }
        void Advanced(Page p)
        {
            int li = AdvLang();
            foreach (var a in AdvancedText.Items)
            {
                if (a.Header) { p.Opts.Add(new Opt { Header = true, Key = "#" + a.Label[0], Label = a.Label[li], Help = "", Values = new[] { "" }, Texts = new[] { "" } }); continue; }
                var t = new string[a.Values.Length];
                for (int i = 0; i < t.Length; i++)
                    t[i] = a.Texts != null ? a.Texts[i][li] : a.Values[i] == "1" ? GameUi.T(572, "On") : a.Values[i] == "0" ? GameUi.T(573, "Off") : a.Values[i];
                string v = Fps60Exe.IniGet(a.Sec, a.Key);
                var o = new Opt { Key = a.Key, IniSec = a.Sec, Label = a.Label[li], Help = a.Help[li], Values = a.Values, Texts = t, Existed = v != null };
                if (v == null) v = a.Def;
                if (a.Key == "pacing") v = v == "1" || v.Equals("fixed", StringComparison.OrdinalIgnoreCase) ? "original" : v == "0" ? "optimized" : v.ToLowerInvariant();
                o.Index = Array.IndexOf(a.Values, v); o.Def = a.Def;                         // the optimized default of ffx_enh.ini
                if (o.Index < 0)                                                                  // keep an unknown value
                {
                    var vs = new List<string>(a.Values); vs.Add(v); o.Values = vs.ToArray();
                    var ts = new List<string>(t); ts.Add(v); o.Texts = ts.ToArray(); o.Index = o.Values.Length - 1;
                }
                p.Opts.Add(o);
            }
        }

        // ---- Controls page: key bindings (KeyBind.cs) ----
        int bindGame, bindCol, captureAt; int[][] binds; readonly bool[] bindsChanged = new bool[3]; bool capturing;

        // the rows below the Game row: the game's Keyboard page, then its Controller page, with the game's own texts
        void BuildControls(Page p)
        {
            if (binds == null) { binds = new int[3][]; for (int g = 0; g < 3; g++) binds[g] = KeyBind.Load(g, Config.Language); }
            if (p.Opts.Count > 1) p.Opts.RemoveRange(1, p.Opts.Count - 1);
            bool de = Config.LanguagePath == "de";
            string hintK = GameUi.T(630, "") + "\n" + (de ? "Enter / A: Taste w\u00e4hlen (Esc: behalten). Links / Rechts: Tastatur 1 / 2. Entf: Tastatur 2 leeren."
                                                           : "Enter / A: choose a key (Esc: keep). Left / right: Keyboard 1 / 2. Del: clear Keyboard 2.");
            string hintP = de ? "Enter / A: danach die gew\u00fcnschte Taste am Controller dr\u00fccken (Esc: behalten)." : "Enter / A: then press the button on the controller (Esc: keep).";
            p.Opts.Add(new Opt { Header = true, Key = "#keyboard", Label = GameUi.T(622, "Keyboard"), Help = "", Values = new[] { "" }, Texts = new[] { "" },
                Cols = new[] { GameUi.T(628, "Keyboard 1"), GameUi.T(629, "Keyboard 2") } });
            int i = 0;
            foreach (var r in KeyBind.Keys(bindGame)) p.Opts.Add(BindOpt("bindk" + i++, r, hintK));
            p.Opts.Add(new Opt { Header = true, Key = "#controller", Label = GameUi.T(624, "Controller"), Help = "", Values = new[] { "" }, Texts = new[] { "" } });
            i = 0;
            foreach (var r in KeyBind.Pads(bindGame)) p.Opts.Add(BindOpt("bindp" + i++, r, hintP));
        }

        Opt BindOpt(string key, BindRow r, string hint)
        {
            string t = (bindGame == 0 ? GameUi.T(r.Text, "#" + r.Text) : GameUi.T2(r.Text, "#" + r.Text)).Replace("\n", " ");
            return new Opt { Key = key, Bind = r, Label = t, Help = t + "\n" + hint, Values = new[] { "" }, Texts = new[] { "" }, Existed = true };
        }

        int BindSlot(Opt o) { return o.Bind.Pad >= 0 ? o.Bind.Pad : bindCol == 1 ? o.Bind.K2 : o.Bind.K1; }

        void StartCapture()
        {
            capturing = true; captureAt = Environment.TickCount; Pad.Raw = true; Pad.TakeRaw();
            message = GameUi.T(631, "Press required button to confirm keybinding"); audio.Confirm(); Dirty = true;
        }

        void EndCapture(string msg) { capturing = false; Pad.Raw = false; Pad.TakeRaw(); message = msg; Dirty = true; }

        // the pressed key / button goes to the selected cell. The game allows no code on two slots: a code that is in use
        // moves away - the other cell gets this cell's old code (a swap); refused when that would empty a cell that must
        // be bound, or when the other slot is not on the pages (Esc menu).
        void Assign(int code)
        {
            Opt o = Opts[sel]; int slot = BindSlot(o); int[] a = binds[bindGame]; int old = a[slot]; bool de = Config.LanguagePath == "de";
            if (old == code) { EndCapture(null); return; }
            int other = -1;
            for (int i = 0; i < KeyBind.N; i++) if (i != slot && a[i] == code && KeyBind.Used(bindGame, i)) other = i;
            string msg = null;
            if (other >= 0)
            {
                string name = KeyBind.Name(code, o.Bind.Pad >= 0);
                if (!KeyBind.Shown(bindGame, other) || old == 0 && other < 49)
                {
                    EndCapture(name + (de ? ": bereits fest vergeben, nicht ge\u00e4ndert." : ": already in use and cannot be moved, not changed.")); return;
                }
                a[other] = old;
                string where = ""; foreach (var x in Opts) if (x.Bind != null && (x.Bind.K1 == other || x.Bind.K2 == other || x.Bind.Pad == other)) where = x.Label;
                msg = name + (de ? ": getauscht mit \u201e" + where + "\u201c." : ": swapped with \"" + where + "\".");
            }
            a[slot] = code; bindsChanged[bindGame] = true; audio.Move();
            EndCapture(msg);
        }

        void CaptureKey(Keys key)
        {
            if (Environment.TickCount - captureAt < 200) return;       // the key that started the capture, repeating
            if (key == Keys.Escape) { EndCapture(null); return; }
            if (Opts[sel].Bind.Pad >= 0) return;                         // a controller row waits for a button
            int code = KeyBind.FromKey(key);
            if (code != 0) Assign(code);
        }

        bool IsHeader(int row) { return row >= 0 && row < Opts.Count && Opts[row].Header; }
        int Skip(int row, int dir)                                       // the next selectable row from 'row' in direction dir
        {
            for (int n = 0; n < Rows && IsHeader(row); n++) row = (row + dir + Rows) % Rows;
            return row;
        }

        List<Opt> Opts { get { return pages[page].Opts; } }
        int Rows { get { return Opts.Count + 4; } }                    // + OK / Cancel / Apply / Defaults
        Opt Find(string key) { foreach (var p in pages) foreach (var o in p.Opts) if (o.Key == key) return o; return null; }

        // ---- values ----
        // The quality presets, as FFX.exe applies them when it loads GameSetting.ini (table at 0xB7CBA8, loop at 0x4020F0; FFX-2.exe
        // has the same): with Quality Low / Medium / High the eight settings below take these values, whatever the INI says; with
        // Custom the INI's values count. VSync and brightness are not part of a preset. The launcher shows the same: the eight
        // rows take the preset's values and are locked (dimmed) unless Quality is Custom.
        static readonly Dictionary<string, string[]> Presets = new Dictionary<string, string[]> {   // Low, Medium, High
            { "Anisotropic", new[] { "1", "4", "16" } }, { "Shadow", new[] { "0", "3", "1" } }, { "TextureQuality", new[] { "TQ_LOW", "TQ_MEDIUM", "TQ_HIGH" } },
            { "MSAA", new[] { "0", "2", "8" } }, { "ColorCorrection", new[] { "On", "On", "On" } }, { "PostAA", new[] { "PAA_OFF", "PAA_FXAA", "PAA_SMAA4" } },
            { "UnSharpMask", new[] { "Off", "On", "On" } }, { "HDAO", new[] { "Off", "Low", "High" } } };
        void ApplyPreset(bool mark)
        {
            Opt q = Find("Quality"); if (q == null) return;
            int level = Array.IndexOf(new[] { "VQ_LOW", "VQ_MEDIUM", "VQ_HIGH" }, q.Values[q.Index]);
            foreach (var kv in Presets)
            {
                Opt o = Find(kv.Key); if (o == null) continue;
                o.Locked = level >= 0; if (level < 0) continue;
                int k = Array.IndexOf(o.Values, kv.Value[level]);
                if (k >= 0 && k != o.Index) { o.Index = k; if (mark) o.Changed = true; }
            }
        }

        void Change(Opt o, int dir, bool wrap)
        {
            if (o.Bind != null) return;                                 // key bindings: not a list of values
            if (o.Locked) return;                                       // part of the quality preset
            if (o.Header) return;
            if (o.Bar) { int v = Math.Max(0, Math.Min(100, o.Value + dir * 5)); if (v == o.Value) return; o.Value = v; }
            else
            {
                int n = o.Values.Length, i = o.Index + dir;
                if (wrap) i = (i + n) % n; else i = Math.Max(0, Math.Min(n - 1, i));
                if (i == o.Index) return; o.Index = i;
            }
            Changed(o);
        }

        // Voice language. The games have no setting of their own for it: FFX.exe takes it from the text language
        // (language table at 0x6db090: jp -> type 0, kr / ch -> type 3, en fr de it es -> type 1; FmodVoice 0x70b3f0:
        // types 0, 3, 4 load Voice/JP, types 1, 2 Voice/US). So the row only shows and switches the text language:
        // Japanese voice = Japanese text; English voice = the last Western text language, else English. Not an INI key.
        static readonly string[] JpVoiceLangs = { "jp", "kr", "ch" };
        string lastWestern = "en";

        Opt Voice()
        {
            string l = Config.LanguagePath == "de" ? "de" : "en";
            var o = new Opt
            {
                Key = "Voice", Label = l == "de" ? "Sprachausgabe" : "Voice",
                Help = l == "de" ? "Sprache der Stimmen. Die Spiele w\u00e4hlen sie \u00fcber die Textsprache: japanische Stimmen gibt es nur mit japanischem, koreanischem oder chinesischem Text. Japanisch stellt den Text auf Japanisch um."
                                 : "Language of the voices. The games choose it by the text language: Japanese voices come only with Japanese, Korean or Chinese text. Japanese switches the text to Japanese.",
                Values = new[] { "us", "jp" }, Texts = l == "de" ? new[] { "Englisch", "Japanisch" } : new[] { "English", "Japanese" }
            };
            string cur = Config.Get("Language", "en");
            if (Array.IndexOf(JpVoiceLangs, cur) < 0) lastWestern = cur;
            o.Index = Array.IndexOf(JpVoiceLangs, cur) >= 0 ? 1 : 0;
            return o;
        }

        void CoupleVoice(Opt o)
        {
            Opt lang = Find("Language"), voice = Find("Voice"); if (lang == null || voice == null) return;
            string l = lang.Values[lang.Index]; bool jpVoice = Array.IndexOf(JpVoiceLangs, l) >= 0;
            if (o == lang) { if (!jpVoice) lastWestern = l; voice.Index = jpVoice ? 1 : 0; }
            else if (o == voice)
            {
                bool wantJp = voice.Values[voice.Index] == "jp"; if (wantJp == jpVoice) return;
                int i = Array.IndexOf(lang.Values, wantJp ? "jp" : lastWestern); if (i < 0) i = Array.IndexOf(lang.Values, "en");
                if (i >= 0) { lang.Index = i; lang.Changed = true; }
            }
        }

        void Changed(Opt o)
        {
            o.Changed = true; message = null; audio.Move(); Dirty = true;
            if (o.Key == "BindGame") { o.Changed = false; bindGame = o.Index; bindCol = 0; BuildControls(pages[page]); return; }
            CoupleVoice(o);
            if (o.Key == "Quality") ApplyPreset(true);
            if (o.Detail) { var q = Find("Quality"); int c = Array.IndexOf(q.Values, "VQ_CUSTOM"); if (c >= 0 && q.Index != c) { q.Index = c; q.Changed = true; } }
            var msaa = Find("MSAA"); var hdao = Find("HDAO");
            if (o == msaa && msaa.Values[msaa.Index] != "0" && hdao.Values[hdao.Index] != "Off") { hdao.Index = Array.IndexOf(hdao.Values, "Off"); hdao.Changed = true; message = GameUi.T(911, "Ambient Occlusion will be set to Off."); }
            if (o == hdao && hdao.Values[hdao.Index] != "Off" && msaa.Values[msaa.Index] != "0") { msaa.Index = Array.IndexOf(msaa.Values, "0"); msaa.Changed = true; message = GameUi.T(912, "Anti-aliasing will be set to Off."); }
            Preview();
        }

        void Preview()   // live audio, as the original volume slider
        {
            audio.SetLevels(Find("MusicVolume").Value, Find("SFXVolume").Value, mute ? 0 : Find("MasterVolume").Value);
        }

        void ToggleMute() { mute = !mute; Find("MasterVolume").Changed = true; Preview(); audio.Move(); Dirty = true; }

        void Press(int b)
        {
            if (Busy) return;
            if (b == 3) { ResetDefaults(); return; }
            audio.Confirm();
            if (b == 0) { Save(); if (Busy) closeAfter = true; else { closed = true; audio.ReadVolumes(); } }
            else if (b == 1) { closed = true; audio.ReadVolumes(); }
            else Save();
            Dirty = true;
        }

        // The Defaults button: every row with a default goes back to it (the game's / launcher's defaults, the 60 fps patch's
        // optimized setup at 60 FPS), mute off. Rows without one stay: resolution, screen mode, text and voice language, Use
        // RevoFX, Proton compatibility mode. Nothing is saved here - OK / Apply save, Cancel keeps the old values.
        void ResetDefaults()
        {
            audio.Confirm(); int n = 0;
            foreach (var p in pages) foreach (var o in p.Opts)
                {
                    if (o.Header || o.Key == "Resolution" || o.Key == "ScreenMode" || o.Key == "Language") continue;
                    if (o.Bar) { if (o.DefValue >= 0 && o.Value != o.DefValue) { o.Value = o.DefValue; o.Changed = true; n++; } continue; }
                    if (o.Def == null || o.Values == null) continue;
                    int k = Array.IndexOf(o.Values, o.Def);
                    if (k >= 0 && k != o.Index) { o.Index = k; o.Changed = true; n++; }
                }
            if (mute) { mute = false; Find("MasterVolume").Changed = true; n++; }
            if (binds != null)                                            // key bindings: the games' defaults for the chosen language
            {
                var lo = Find("Language"); string bl = lo != null ? lo.Values[lo.Index] : Config.Language;
                for (int g = 0; g < 3; g++)
                {
                    int[] d = KeyBind.Defaults(g, bl);
                    if (KeyBind.Format(d) != KeyBind.Format(binds[g])) { binds[g] = d; bindsChanged[g] = true; n++; }
                }
            }
            ApplyPreset(true);
            Preview();
            Log.Write("settings: defaults set (" + n + " rows changed, not saved yet)");
            message = LauncherRes.DefaultsDone; Dirty = true;
        }

        void Save()
        {
            string oldLang = Config.Get("Language", "en");
            // the original launcher's keys first, in its order, then the game keys
            foreach (string key in new[] { "Language", "Resolution", "ScreenMode", "Quality" }) { var o = Find(key); Config.Set(key, o.Values[o.Index]); }
            Config.Set("Mute", mute ? "On" : "Off");
            Config.Set("MasterVolume", Find("MasterVolume").Value.ToString());
            bool adv = false;
            foreach (var p in pages) foreach (var o in p.Opts) if (o.IniSec != null && o.Changed) adv = true;
            if (adv) { string im = Patcher.EnsureIni(Path.GetDirectoryName(Fps60Exe.IniPath)); if (im != null) Log.Write("fps60exe: " + im); }
            foreach (var p in pages) foreach (var o in p.Opts)
                {
                    if (o.Header || o.Bind != null || o.Key == "BindGame") continue;
                    if (o.IniSec != null) { if (o.Changed) { Fps60Exe.IniSet(o.IniSec, o.Key, o.Values[o.Index]); o.Changed = false; o.Existed = true; } continue; }
                    if (o.InLauncher)
                    {
                        if (o.Changed && o.Key == "ProtonCompat") { compatPending = o.Values[o.Index] == "1"; o.Changed = false; }
                        else if (o.Changed && o.Key == "UseRevoFX") { revoPending = o.Values[o.Index] == "1"; o.Changed = false; }
                        else if (o.Changed) { string v = o.Values[o.Index]; LauncherRes.SetChoice(v); Data.SelectResources(v); ResourcesChanged = true; o.Changed = false; }
                        continue;
                    }
                    if (o.Key == "Language" || o.Key == "Resolution" || o.Key == "ScreenMode" || o.Key == "Quality" || o.Key == "MasterVolume" || o.Key == "Voice") continue;
                    if (o.InRevoFx) { if (o.Changed) { int f = int.Parse(o.Values[o.Index]); RevoFx.SetInt("Game", o.Key, f); Fps60Ini.Set(f); if (Fps60Exe.NeedsPatch(f)) StartPatch(f); else if (Fps60Exe.Present || Fps60Dll.Present) Fps60Exe.Set(f); o.Changed = false; o.Existed = true; } continue; }
                    if (o.Existed || o.Changed) { Config.Set(o.Key, o.Bar ? o.Value.ToString() : o.Values[o.Index]); o.Existed = true; }
                }
            if (binds != null && (bindsChanged[0] || bindsChanged[1] || bindsChanged[2]))
            {
                // key bindings: all three lists (the game resets all of them when one fails its check), each only if it passes
                string bad = null;
                for (int g = 0; g < 3; g++) { string e = KeyBind.Check(g, binds[g]); if (e != null) bad = KeyBind.IniKeys[g] + ": " + e; }
                if (bad != null) { Log.Write("keys: not written, " + bad); message = bad; }
                else
                {
                    for (int g = 0; g < 3; g++) { Config.Set(KeyBind.IniKeys[g], KeyBind.Format(binds[g])); bindsChanged[g] = false; }
                    Log.Write("keys: key bindings written");
                }
            }
            Config.SaveIni();
            { int fr0 = Fps60Exe.FrameRate(); if (!Busy && Fps60Exe.NeedsPatch(fr0)) StartPatch(fr0); }   // e.g. a start option on at 30 fps: the patch is needed
            if (revoPending != null)
            {
                // RevoFX on / off: rename d3d11.dll, then rebuild the pages (the 60 fps rows depend on it); without RevoFX and
                // with 60 fps or more the 60 fps EXEs are written when missing
                bool on = revoPending.Value, rok; revoPending = null;
                string r = RevoFx.SetEnabled(on, out rok);
                if (on) compatPending = null;
                Relocalise(); if (r != null) message = r;
                int fr = Fps60Exe.FrameRate();
                if (rok && !on && !Busy && Fps60Exe.NeedsPatch(fr)) StartPatch(fr);
            }
            if (compatPending != null && !Busy) { bool on = compatPending.Value; compatPending = null; StartCompat(on); }
            string nl = Find("Language").Values[Find("Language").Index];
            if (nl != oldLang || nl != Config.Language) { Config.Language = nl; SavedLanguage = nl; }
        }

        void StartPatch(int fps)
        {
            lock (busyLock) { busyFile = null; busyFrac = 0; busyResult = null; busyOk = true; }
            busyShown = -1; busyShownFile = null; message = null; busyKind = 0;
            worker = new System.Threading.Thread(() =>
            {
                string r; bool ok;
                try { r = Fps60Exe.Ensure(fps, (f, x) => { lock (busyLock) { busyFile = f; busyFrac = Math.Max(0, Math.Min(1, x)); } }, out ok); Fps60Exe.Set(fps); }
                catch (Exception e) { r = e.Message; ok = false; Log.Write("fps60exe: " + e); }
                lock (busyLock) { busyResult = r ?? ""; busyOk = ok; }
            }) { IsBackground = true, Name = "fps60 patch" };
            Log.Write("fps60exe: writing the 60 fps EXEs (" + fps + " fps)");
            worker.Start(); Dirty = true;
        }

        // Proton compatibility mode: switched on the worker thread (Originals.Switch), with the progress panel; after a 60 fps
        // patch run of the same Apply it follows that one
        bool? compatPending, revoPending; int busyKind;                               // 0 = 60 fps EXEs, 1 = compatibility mode on, 2 = off
        void StartCompat(bool on)
        {
            lock (busyLock) { busyFile = null; busyFrac = 0; busyResult = null; busyOk = true; }
            busyShown = -1; busyShownFile = null; message = null; busyKind = on ? 1 : 2;
            worker = new System.Threading.Thread(() =>
            {
                string r; bool ok;
                try { r = Originals.Switch(on, (f, x) => { lock (busyLock) { busyFile = f; busyFrac = Math.Max(0, Math.Min(1, x)); } }, out ok); }
                catch (Exception e) { r = e.Message; ok = false; Log.Write("compat: " + e); }
                lock (busyLock) { busyResult = r ?? ""; busyOk = ok; }
            }) { IsBackground = true, Name = "proton compat" };
            Log.Write("compat: switching " + (on ? "on" : "off"));
            worker.Start(); Dirty = true;
        }

        void PollPatch()
        {
            string res, f; int pct; bool ok;
            lock (busyLock) { res = busyResult; pct = (int)(busyFrac * 100); f = busyFile; ok = busyOk; }
            if (res != null)
            {
                worker = null; message = res.Length > 0 ? res : null; Dirty = true;
                if (busyKind != 0) { var pc = Find("ProtonCompat"); if (pc != null) pc.Index = Originals.CompatOn ? 1 : 0; }
                busyKind = 0;
                if (compatPending != null && ok) { bool on = compatPending.Value; compatPending = null; StartCompat(on); return; }
                compatPending = null;
                Log.Write("fps60exe: done" + (ok ? "" : " with a problem") + (res.Length > 0 ? ": " + res.Replace("\n", " ") : ""));
                if (closeAfter) { closeAfter = false; if (ok) { closed = true; audio.ReadVolumes(); } }
                return;
            }
            if (pct != busyShown || f != busyShownFile) { busyShown = pct; busyShownFile = f; Dirty = true; }
        }

        public void Relocalise()
        {
            var keep = new Dictionary<string, Opt>(); foreach (var p in pages) foreach (var o in p.Opts) keep[o.Key] = o;
            Build();
            foreach (var p in pages) foreach (var o in p.Opts) { Opt old; if (keep.TryGetValue(o.Key, out old)) { o.Index = Math.Min(old.Index, o.Values == null ? 0 : o.Values.Length - 1); o.Value = old.Value; o.Changed = old.Changed; o.Existed = old.Existed; } }
            SavedLanguage = null; Dirty = true;
        }

        // ---- input ----
        void Select(int row)
        {
            sel = Math.Max(0, Math.Min(Rows - 1, row));
            if (sel < Opts.Count)
            {
                int first = IsHeader(sel - 1) ? sel - 1 : sel;           // a section title above the row stays in view with it
                if (first < top) top = first; if (sel >= top + Visible) top = sel - Visible + 1;
            }
            Dirty = true;
        }
        void SetPage(int p) { if (p == page || p < 0 || p >= pages.Count) return; page = p; top = 0; sel = Skip(0, 1); message = null; audio.Move(); Dirty = true; }   // a page may start with a section title

        public bool Act(Act a)
        {
            if (Busy) return true;                                     // the progress panel takes no input
            if (capturing) return true;                                // waiting for a key / button (Key, Tick)
            if (sel < Opts.Count && Opts[sel].Bind != null && (a == FFXLauncher.Act.Left || a == FFXLauncher.Act.Right || a == FFXLauncher.Act.Confirm))
            {
                if (a == FFXLauncher.Act.Confirm) StartCapture();
                else if (Opts[sel].Bind.Pad < 0) { int c = a == FFXLauncher.Act.Right ? 1 : 0; if (c != bindCol) { bindCol = c; audio.Move(); Dirty = true; } }
                return true;
            }
            message = a == FFXLauncher.Act.Left || a == FFXLauncher.Act.Right ? message : null;
            switch (a)
            {
                case FFXLauncher.Act.Up: Select(Skip((sel + Rows - 1) % Rows, -1)); audio.Move(); return true;
                case FFXLauncher.Act.Down: Select(Skip((sel + 1) % Rows, 1)); audio.Move(); return true;
                case FFXLauncher.Act.Left: case FFXLauncher.Act.Right:
                    if (sel < Opts.Count) Change(Opts[sel], a == FFXLauncher.Act.Right ? 1 : -1, false);
                    else { Select(Opts.Count + Math.Max(0, Math.Min(2, sel - Opts.Count + (a == FFXLauncher.Act.Right ? 1 : -1)))); audio.Move(); }
                    return true;
                case FFXLauncher.Act.PageUp: SetPage((page + pages.Count - 1) % pages.Count); return true;
                case FFXLauncher.Act.PageDown: SetPage((page + 1) % pages.Count); return true;
                case FFXLauncher.Act.Confirm:
                    if (sel >= Opts.Count) Press(sel - Opts.Count);
                    else if (Opts[sel].Key == "MasterVolume") ToggleMute();
                    else { Select(Skip(Math.Min(sel + 1, Rows - 1), 1)); audio.Move(); }
                    return true;
                case FFXLauncher.Act.Back: Press(1); return true;
                case FFXLauncher.Act.Mute: ToggleMute(); return true;
            }
            return false;
        }

        public bool Key(Keys key)
        {
            if (Busy) return true;
            if (capturing) { CaptureKey(key); return true; }
            if (key == Keys.Delete && sel < Opts.Count && Opts[sel].Bind != null)   // clear Keyboard 2 (Keyboard 1 and the controller must stay bound)
            {
                var bo = Opts[sel];
                if (bo.Bind.K2 >= 0 && bindCol == 1 && binds[bindGame][bo.Bind.K2] != 0) { binds[bindGame][bo.Bind.K2] = 0; bindsChanged[bindGame] = true; audio.Move(); Dirty = true; }
                return true;
            }
            if (key == Keys.Tab) { SetPage((page + 1) % pages.Count); return true; }
            return false;
        }

        PointF L(PointF p) { return new PointF(p.X / k, p.Y / k); }

        int HitRow(PointF q)
        {
            for (int v = 0; v < Visible && top + v < Opts.Count; v++) if (RowR(v).Contains(q)) return top + v;
            for (int i = 0; i < 4; i++) if (BtnR(i).Contains(q)) return Opts.Count + i;
            return -1;
        }

        static RectangleF CellR(RectangleF val, int c, int n)           // cell c of n in a row's value area (key bindings)
        {
            float w = n == 1 ? 204 : (val.Width - 12) / 2;
            return n == 1 ? new RectangleF(val.X + (val.Width - w) / 2, val.Y + 1, w, val.Height - 2) : new RectangleF(val.X + c * (w + 12), val.Y + 1, w, val.Height - 2);
        }
        RectangleF ValR(int vis) { RectangleF r = RowR(vis); return new RectangleF(ValX, r.Y, ValW, r.Height); }
        RectangleF TrackR(int vis) { RectangleF v = ValR(vis); return new RectangleF(v.X + 44, v.Y + v.Height / 2 - 5, v.Width - 44 - 64, 10); }
        RectangleF SpeakerR(int vis) { RectangleF v = ValR(vis); return new RectangleF(v.X, v.Y + (v.Height - 30) / 2, 30, 30); }

        public void MouseMove(PointF p)
        {
            if (Busy || capturing) return;
            PointF q = L(p);
            if (dragging) { SetBarAt(q.X); return; }
            int r = HitRow(q);
            if (r >= 0 && r != sel && !IsHeader(r)) { Select(r); audio.Move(); }
        }

        void SetBarAt(float x)
        {
            if (sel >= Opts.Count || !Opts[sel].Bar) return;
            RectangleF t = TrackR(sel - top); var o = Opts[sel];
            int v = (int)Math.Round(Math.Max(0, Math.Min(1, (x - t.X) / t.Width)) * 100);
            if (v != o.Value) { o.Value = v; Changed(o); }
        }

        public void MouseDown(PointF p)
        {
            if (Busy) return;
            if (capturing) { EndCapture(null); return; }
            PointF q = L(p);
            for (int i = 0; i < pages.Count; i++) if (TabR(i).Contains(q)) { SetPage(i); return; }
            int r = HitRow(q); if (r < 0 || IsHeader(r)) return;
            Select(r);
            if (r >= Opts.Count) { Press(r - Opts.Count); return; }
            var o = Opts[r]; int vis = r - top; RectangleF v = ValR(vis);
            if (o.Bind != null)                                         // a click on a cell: wait for its key / button
            {
                int nc = o.Bind.Pad >= 0 ? 1 : 2;
                for (int c = 0; c < nc; c++) if (CellR(v, c, nc).Contains(q)) { if (nc == 2) bindCol = c; StartCapture(); }
                return;
            }
            if (o.Bar)
            {
                if (o.Key == "MasterVolume" && SpeakerR(vis).Contains(q)) { ToggleMute(); return; }
                RectangleF t = TrackR(vis); t.Inflate(10, 12);
                if (t.Contains(q)) { dragging = true; SetBarAt(q.X); }
                return;
            }
            if (!v.Contains(q)) return;
            if (q.X < v.X + 44) Change(o, -1, false); else if (q.X > v.Right - 44) Change(o, 1, false); else Change(o, 1, true);
        }

        public void MouseUp(PointF p) { dragging = false; }

        public void Wheel(PointF p, int delta)
        {
            if (Busy || capturing) return;
            int r = HitRow(L(p));
            if (r >= 0 && r < Opts.Count && ValR(r - top).Contains(L(p))) { Change(Opts[r], delta > 0 ? 1 : -1, false); return; }
            int nt = Math.Max(0, Math.Min(Math.Max(0, Opts.Count - Visible), top + (delta > 0 ? -1 : 1)));
            if (nt != top) { top = nt; Dirty = true; }
        }

        public void Tick(int ms)
        {
            if (Busy) { PollPatch(); return; }
            if (capturing)                                              // a controller button: for a controller row the new binding, else B = keep
            {
                int c = Pad.TakeRaw();
                if (c != 0 && sel < Opts.Count && Opts[sel].Bind != null) { if (Opts[sel].Bind.Pad >= 0) Assign(c); else if (c == 13) EndCapture(null); }
            }
            if (cursorTip == null || frame == null || dirty) return;
            int sp = SwayPx();
            if (sp == swayDrawn) return;
            RectangleF r = CursorRect(0); r = RectangleF.Union(r, CursorRect(-SwayAmp));
            float f = lastScale * k;
            AnimRect = Rectangle.Inflate(Rectangle.FromLTRB((int)Math.Floor(lastView.X + r.X * f), (int)Math.Floor(lastView.Y + r.Y * f),
                (int)Math.Ceiling(lastView.X + r.Right * f), (int)Math.Ceiling(lastView.Y + r.Bottom * f)), 3, 3);
            AnimDirty = true;
        }

        // sway offset in whole window pixels (keeps the pointer sharp while it moves)
        int SwayPx()
        {
            int f = (int)((long)(Environment.TickCount & int.MaxValue) * SwayFps / 1000 % SwayFrames);
            float x = f <= SwayHalf ? -SwayAmp * f / SwayHalf : -SwayAmp + SwayAmp * (f - SwayHalf) / SwayHalf;
            return (int)Math.Round(x * lastScale * k);
        }

        RectangleF CursorRect(float sway)
        {
            PointF tip = cursorTip.Value; Bitmap c = GameUi.MenuCursor();
            if (c == null) return new RectangleF(tip.X - 42 + sway, tip.Y - 19, 38, 38);
            float w = CursorH * c.Width / c.Height;
            return new RectangleF(tip.X - w + sway, tip.Y - CursorH / 2, w, CursorH);
        }

        void PaintCursor(Graphics g, RectangleF view, float scale)
        {
            if (cursorTip == null) return;
            swayDrawn = SwayPx();
            RectangleF r = CursorRect(swayDrawn / (scale * k));
            var state = g.Save();
            g.TranslateTransform(view.X, view.Y); g.ScaleTransform(scale * k, scale * k);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Bitmap c = GameUi.MenuCursor();
            if (c != null) g.DrawImage(c, r); else Img(g, GameUi.Cursor, r, 1);
            g.Restore(state);
        }

        public string State()
        {
            string s = "settings, page " + page + " (" + pages[page].Title + "), row " + sel;
            if (sel < Opts.Count && Opts[sel].Bind != null)
            {
                var b = Opts[sel].Bind; int[] a = binds[bindGame];
                s += " " + Opts[sel].Key + " game " + bindGame + (b.Pad >= 0 ? " pad " + KeyBind.Name(a[b.Pad], true) : " keys " + KeyBind.Name(a[b.K1], false) + " | " + KeyBind.Name(a[b.K2], false) + ", column " + bindCol)
                    + (capturing ? ", waiting" : "");
            }
            else if (sel < Opts.Count) s += " " + Opts[sel].Key + " = " + (Opts[sel].Bar ? Opts[sel].Value.ToString() : Opts[sel].Values[Opts[sel].Index]);
            else s += " button " + (sel - Opts.Count);
            if (mute) s += ", muted"; if (message != null) s += ", message";
            if (Busy) s += ", patching " + Math.Max(0, busyShown) + " %" + (busyShownFile != null ? " " + busyShownFile : "");
            return s;
        }

        // ---- drawing ----
        static readonly Color TextC = Color.FromArgb(236, 240, 248), ValC = Color.FromArgb(206, 226, 255), DimC = Color.FromArgb(120, 140, 170, 200);

        static void Img(Graphics g, int id, RectangleF r, float alpha)
        {
            Bitmap b = GameUi.Bmp(id); if (b == null) return;
            if (alpha >= 1) { g.DrawImage(b, r); return; }
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix(); cm.Matrix33 = alpha; ia.SetColorMatrix(cm);
                g.DrawImage(b, new[] { r.Location, new PointF(r.Right, r.Y), new PointF(r.X, r.Bottom) }, new RectangleF(0, 0, b.Width, b.Height), GraphicsUnit.Pixel, ia);
            }
        }

        static void ImgMirror(Graphics g, int id, RectangleF r)
        {
            Bitmap b = GameUi.Bmp(id); if (b == null) return;
            g.DrawImage(b, new[] { new PointF(r.Right, r.Y), new PointF(r.X, r.Y), new PointF(r.Right, r.Bottom) }, new RectangleF(0, 0, b.Width, b.Height), GraphicsUnit.Pixel);
        }

        static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, bool shadow)
        {
            using (var sf = new StringFormat { Alignment = h, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                if (shadow) using (var sb = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.DrawString(s, f, sb, new RectangleF(r.X + 2, r.Y + 2, r.Width, r.Height), sf);
                using (var br = new SolidBrush(c)) g.DrawString(s, f, br, r, sf);
            }
        }

        public void Paint(Graphics g, RectangleF view, float scale)
        {
            dirty = false;
            int fw = Math.Max(1, (int)Math.Round(view.Width)), fh = Math.Max(1, (int)Math.Round(view.Height));
            if (frame == null || frame.Width != fw || frame.Height != fh || contentDirty)
            {
                if (frame == null || frame.Width != fw || frame.Height != fh) { if (frame != null) frame.Dispose(); frame = new Bitmap(fw, fh, PixelFormat.Format32bppPArgb); }
                using (var gf = Graphics.FromImage(frame)) PaintContent(gf, new RectangleF(0, 0, fw, fh), scale);
                contentDirty = false;
            }
            lastView = view; lastScale = scale;
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(frame, (int)Math.Round(view.X), (int)Math.Round(view.Y), fw, fh);
            g.Restore(state);
            PaintCursor(g, new RectangleF((float)Math.Round(view.X), (float)Math.Round(view.Y), view.Width, view.Height), scale);
        }

        // everything but the pointer; records where the pointer points (cursorTip)
        void PaintContent(Graphics g, RectangleF view, float scale)
        {
            cursorTip = null;
            // static layer (background, title bar, help panel) rendered once per size: keeps navigation fast on slow CPUs
            int vw = (int)Math.Round(view.Width), vh = (int)Math.Round(view.Height);
            if (back == null || back.Width != vw || back.Height != vh)
            {
                if (back != null) back.Dispose();
                back = new Bitmap(Math.Max(1, vw), Math.Max(1, vh), PixelFormat.Format32bppPArgb);
                using (var gb = Graphics.FromImage(back))
                {
                    gb.ScaleTransform(scale * k, scale * k);
                    gb.InterpolationMode = InterpolationMode.HighQualityBicubic; gb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gb.FillRectangle(Brushes.Black, 0, 0, 1280, 720);   // the in-game menu is translucent over the game; here it is a page of its own
                    if (GameUi.Bmp(GameUi.Background) != null) Img(gb, GameUi.Background, new RectangleF(0, 0, 1280, 720), 1);
                    else using (var b = new LinearGradientBrush(new RectangleF(0, 0, 1280, 720), Color.FromArgb(14, 20, 40), Color.FromArgb(2, 3, 8), 90f)) gb.FillRectangle(b, 0, 0, 1280, 720);
                    Img(gb, GameUi.TitleBar, TitleR, 1);
                    Img(gb, GameUi.HelpPanel, HelpR, 0.95f);
                    Bitmap hb = GameUi.Bmp(GameUi.HelpText);
                    if (hb != null) Img(gb, GameUi.HelpText, new RectangleF(HelpR.X + 30, HelpR.Y + 14, hb.Width * 0.62f, hb.Height * 0.62f), 1);
                }
            }
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(back, (int)Math.Round(view.X), (int)Math.Round(view.Y), back.Width, back.Height);
            g.TranslateTransform(view.X, view.Y); g.ScaleTransform(scale * k, scale * k);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SetClip(new RectangleF(0, 0, 1280, 720));

            FontFamily fam = Loc.Family("Segoe UI", "Arial", "Tahoma", "Liberation Sans", "DejaVu Sans");
            using (var fTitle = new Font(fam, 32, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fTab = new Font(fam, 23, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fRow = new Font(fam, 23, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fHelp = new Font(fam, 17, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                // title and page tabs
                Text(g, GameUi.T(537, "System setting"), fTitle, TextC, new RectangleF(80, TitleR.Y, 900, TitleR.Height), StringAlignment.Near, true);
                using (var fVer = new Font(fam, 18, FontStyle.Regular, GraphicsUnit.Pixel))                // the version of FFX Enhanced, only here
                    Text(g, "FFX_enh v. " + Patcher.Version, fVer, DimC, new RectangleF(780, TitleR.Y, 420, TitleR.Height), StringAlignment.Far, false);
                for (int i = 0; i < pages.Count; i++)
                {
                    RectangleF t = TabR(i); bool on = i == page;
                    Img(g, GameUi.Button, t, on ? 1f : 0.55f);
                    if (on) Img(g, GameUi.Ornament, new RectangleF(t.X + 4, t.Y + 4, t.Width - 8, t.Height - 8), 0.85f);
                    Text(g, pages[i].Title, fTab, on ? Color.White : DimC, t, StringAlignment.Center, true);
                }
                using (var f2 = new Font(fam, 16, FontStyle.Regular, GraphicsUnit.Pixel))
                    Text(g, "LB / RB", f2, DimC, new RectangleF(TabR(pages.Count).X, 112, 140, 56), StringAlignment.Near, false);

                // rows
                for (int v = 0; v < Visible && top + v < Opts.Count; v++)
                {
                    var o = Opts[top + v]; RectangleF r = RowR(v); bool on = top + v == sel;
                    if (o.Header)                                                // section title: gold text over a thin rule, no value
                    {
                        using (var pen = new Pen(Color.FromArgb(150, 214, 190, 120), 1.5f)) g.DrawLine(pen, r.X, r.Bottom - 3, r.Right, r.Bottom - 3);
                        Text(g, o.Label, fTab, Color.FromArgb(255, 226, 196, 120), new RectangleF(r.X + 6, r.Y, r.Width - 12, r.Height), StringAlignment.Near, true);
                        if (o.Cols != null) for (int c = 0; c < o.Cols.Length; c++) Text(g, o.Cols[c], fHelp, DimC, CellR(ValR(v), c, o.Cols.Length), StringAlignment.Center, false);
                        continue;
                    }
                    Img(g, GameUi.RowBar, new RectangleF(r.X - 20, r.Y - 10, r.Width + 40, r.Height + 20), 0.9f);
                    if (on) Img(g, GameUi.Ornament, r, 0.9f);
                    if (on) cursorTip = new PointF(r.X - 4, r.Y + r.Height / 2);
                    Text(g, o.Label, fRow, o.Locked ? DimC : TextC, new RectangleF(r.X + 26, r.Y, ValX - r.X - 40, r.Height), StringAlignment.Near, true);
                    RectangleF val = ValR(v);
                    if (o.Bind != null)                                          // key binding cells: Keyboard 1 | Keyboard 2, or the controller button
                    {
                        bool pad = o.Bind.Pad >= 0; int nc = pad ? 1 : 2;
                        for (int c = 0; c < nc; c++)
                        {
                            RectangleF cr = CellR(val, c, nc); bool cur = on && (pad || c == bindCol);
                            Img(g, GameUi.Button, cr, cur ? 1f : 0.45f);
                            if (cur) Img(g, GameUi.Ornament, new RectangleF(cr.X + 3, cr.Y + 3, cr.Width - 6, cr.Height - 6), 0.8f);
                            int code = binds[bindGame][pad ? o.Bind.Pad : c == 0 ? o.Bind.K1 : o.Bind.K2];
                            Text(g, cur && capturing ? "..." : KeyBind.Name(code, pad), fRow, cur && capturing ? Color.FromArgb(255, 214, 160) : cur ? Color.White : ValC, cr, StringAlignment.Center, true);
                        }
                    }
                    else if (o.Bar) PaintBar(g, o, v, fRow);
                    else
                    {
                        bool l = o.Index > 0 && !o.Locked, rr = o.Index < o.Values.Length - 1 && !o.Locked;
                        if (l) ImgMirrorArrow(g, new RectangleF(val.X + 6, val.Y + (val.Height - 26) / 2, 22, 26), false);
                        if (rr) ImgMirrorArrow(g, new RectangleF(val.Right - 28, val.Y + (val.Height - 26) / 2, 22, 26), true);
                        Text(g, o.Text, fRow, o.Locked ? DimC : ValC, new RectangleF(val.X + 34, val.Y, val.Width - 68, val.Height), StringAlignment.Center, true);
                    }
                }
                if (top > 0) Tri(g, ListX + ListW / 2, ListY - 14, -1);
                if (top + Visible < Opts.Count) Tri(g, ListX + ListW / 2, ListY + Visible * RowH + 2, 1);

                // help (panel and title are in the static layer)
                string help = message ?? (sel < Opts.Count ? Opts[sel].Help : sel == Opts.Count + 3 ? LauncherRes.DefaultsHelp : GameUi.T(sel == Opts.Count ? 532 : sel == Opts.Count + 1 ? 534 : 532, ""));
                if (sel < Opts.Count && Opts[sel].Key == "MasterVolume") help = (help.Length > 0 ? help + "\n" : "") + "\u266a  A / Enter / M";
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                using (var b = new SolidBrush(message != null ? Color.FromArgb(255, 214, 160) : TextC))
                    g.DrawString(help ?? "", fHelp, b, new RectangleF(HelpR.X + 34, HelpR.Y + 44, HelpR.Width - 68, HelpR.Height - 48), sf);

                // buttons
                string[] bt = { GameUi.T(531, Loc.T("button1", "OK")), GameUi.T(533, Loc.T("button2", "Cancel")), Loc.T("button3", "Apply"), LauncherRes.DefaultsLabel };
                for (int i = 0; i < 4; i++)
                {
                    RectangleF b = BtnR(i); bool on = sel == Opts.Count + i;
                    Img(g, GameUi.ButtonWide, b, on ? 1f : 0.7f);
                    if (on) { Img(g, GameUi.Ornament, new RectangleF(b.X + 3, b.Y + 3, b.Width - 6, b.Height - 6), 0.8f); cursorTip = new PointF(b.X - 4, b.Y + b.Height / 2); }
                    Text(g, bt[i], fTab, on ? Color.White : DimC, b, StringAlignment.Center, true);
                }
                using (var fRes = new Font(fam, 14, FontStyle.Regular, GraphicsUnit.Pixel))         // bottom right: the menu graphics in use
                    Text(g, LauncherRes.UsedText(), fRes, DimC, new RectangleF(BtnR(3).X, BtnR(3).Bottom, BtnR(3).Width, 720 - BtnR(3).Bottom), StringAlignment.Far, false);
                if (Busy) PaintBusy(g, fTab, fHelp);
            }
            g.Restore(state);
        }

        // progress panel while the 60 fps EXEs are written (in 1280 x 720 page units)
        void PaintBusy(Graphics g, Font fTitle, Font fText)
        {
            double x; string f; lock (busyLock) { x = busyFrac; f = busyFile; }
            bool de = Config.LanguagePath == "de";
            cursorTip = null;                                           // no pointer over the panel
            using (var b = new SolidBrush(Color.FromArgb(160, 0, 0, 0))) g.FillRectangle(b, 0, 0, 1280, 720);
            var box = new RectangleF(320, 250, 640, 200);
            using (var b = new LinearGradientBrush(box, Color.FromArgb(245, 22, 34, 66), Color.FromArgb(245, 6, 10, 24), 90f)) g.FillRectangle(b, box);
            if (GameUi.Bmp(GameUi.Marble) != null) Img(g, GameUi.Marble, box, 0.15f);
            using (var p = new Pen(Color.FromArgb(200, 170, 190, 230), 1.5f)) g.DrawRectangle(p, box.X, box.Y, box.Width, box.Height);
            string title = busyKind == 1 ? (de ? "Proton-Kompatibilit\u00e4tsmodus wird eingeschaltet" : "Switching the Proton compatibility mode on")
                         : busyKind == 2 ? (de ? "Original-EXEs werden wiederhergestellt" : "Restoring the original EXE files")
                         : (de ? "60-fps-Patch wird angewendet" : "Applying the 60 fps patch");
            Text(g, title, fTitle, TextC, new RectangleF(box.X + 30, box.Y + 18, box.Width - 60, 40), StringAlignment.Near, true);
            string what = f == null ? (x > 0 ? (de ? "Dateien werden gepr\u00fcft ..." : "Checking the files ...") : (de ? "Vorbereitung ..." : "Preparing ..."))
                                    : (de ? f + " wird geschrieben ..." : "Writing " + f + " ...");
            Text(g, what, fText, ValC, new RectangleF(box.X + 30, box.Y + 66, box.Width - 60, 30), StringAlignment.Near, true);
            var t = new RectangleF(box.X + 30, box.Y + 110, box.Width - 60, 20);
            using (var b = new SolidBrush(Color.FromArgb(220, 8, 12, 24))) g.FillRectangle(b, t);
            var fill = new RectangleF(t.X, t.Y, (float)(t.Width * x), t.Height);
            if (fill.Width > 0.5f) using (var b = new LinearGradientBrush(t, Color.FromArgb(60, 110, 220), Color.FromArgb(150, 210, 255), 0f)) g.FillRectangle(b, fill);
            using (var p = new Pen(Color.FromArgb(160, 170, 190, 230), 1f)) g.DrawRectangle(p, t.X, t.Y, t.Width, t.Height);
            Text(g, (int)Math.Round(x * 100) + " %", fText, ValC, new RectangleF(box.X + 30, box.Y + 140, box.Width - 60, 34), StringAlignment.Far, true);
            string foot = busyKind == 0 ? (de ? "Die Original-EXEs werden nicht ver\u00e4ndert." : "The original EXE files are not changed.")
                        : (de ? "Jede Kopie wird per CRC gepr\u00fcft." : "Every copy is checked by CRC.");
            Text(g, foot, fText, DimC, new RectangleF(box.X + 30, box.Y + 140, box.Width - 160, 34), StringAlignment.Near, false);
        }

        void ImgMirrorArrow(Graphics g, RectangleF r, bool right)
        {
            if (GameUi.Bmp(GameUi.Arrow) == null) { Tri(g, r.X + r.Width / 2, r.Y + r.Height / 2, right ? 2 : -2); return; }
            if (right) ImgMirror(g, GameUi.Arrow, r); else Img(g, GameUi.Arrow, r, 1);
        }

        static void Tri(Graphics g, float x, float y, int dir)
        {
            PointF[] pts;
            if (dir == -1) pts = new[] { new PointF(x, y), new PointF(x - 10, y + 9), new PointF(x + 10, y + 9) };
            else if (dir == 1) pts = new[] { new PointF(x, y + 9), new PointF(x - 10, y), new PointF(x + 10, y) };
            else pts = new[] { new PointF(x + dir * 4, y), new PointF(x - dir * 4, y - 8), new PointF(x - dir * 4, y + 8) };
            using (var b = new SolidBrush(Color.FromArgb(220, 210, 225, 255))) g.FillPolygon(b, pts);
        }

        void PaintBar(Graphics g, Opt o, int v, Font f)
        {
            RectangleF t = TrackR(v), val = ValR(v);
            bool muted = o.Key == "MasterVolume" && mute;
            if (o.Key == "MasterVolume")
            {
                var img = Data.Image(muted ? "MetaMenu/PS3Data/MenuMetaMenu/speaker_off.png" : "MetaMenu/PS3Data/MenuMetaMenu/speaker_on.png");
                g.DrawImage(img, SpeakerR(v));
            }
            using (var b = new SolidBrush(Color.FromArgb(210, 8, 12, 24))) g.FillRectangle(b, t);
            if (GameUi.Bmp(GameUi.Marble) != null) Img(g, GameUi.Marble, t, 0.35f);
            var fill = new RectangleF(t.X, t.Y, t.Width * o.Value / 100f, t.Height);
            if (fill.Width > 0.5f)
                using (var b = new LinearGradientBrush(new RectangleF(t.X, t.Y, t.Width, t.Height), muted ? Color.FromArgb(90, 96, 110) : Color.FromArgb(60, 110, 220), muted ? Color.FromArgb(130, 136, 150) : Color.FromArgb(150, 210, 255), 0f))
                    g.FillRectangle(b, fill);
            using (var p = new Pen(Color.FromArgb(160, 170, 190, 230), 1f)) g.DrawRectangle(p, t.X, t.Y, t.Width, t.Height);
            var knob = new RectangleF(t.X + t.Width * o.Value / 100f - 5, t.Y - 7, 10, t.Height + 14);
            using (var b = new LinearGradientBrush(knob, Color.White, Color.FromArgb(160, 176, 200), 90f)) g.FillRectangle(b, knob);
            Text(g, o.Value.ToString(), f, muted ? DimC : ValC, new RectangleF(t.Right + 6, val.Y, val.Right - t.Right - 6, val.Height), StringAlignment.Far, true);
        }
    }
}
