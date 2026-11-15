// The launcher window: one borderless form over the monitor (black), the menu scaled into the centred rectangle with
// the menu's aspect ratio (scale = min(W / bgW, H / bgH)). Menu layout from MenuActiveRects.xml as the original;
// images are drawn into their layout rectangles whatever their pixel size (upscaled loose files simply look sharper).
// Settings and the 2014 credits are overlays inside this window. Input from mouse, keyboard and XInput.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;

namespace FFXLauncher
{
    sealed class LauncherForm : Form
    {
        sealed class Btn { public Rectangle R; public string Image, Click; public Bitmap Scaled; }

        readonly Options opt; readonly Audio audio; readonly Timer timer;
        int bgW = 1280, bgH = 720; string bgPath; readonly List<Btn> buttons = new List<Btn>();
        float scale = 1; Rectangle view; Bitmap bgScaled, versionScaled;
        int hover = -1; IOverlay overlay; bool closing; int tickCount, closingAt;
        public static bool GameStarted;                                        // a game was started: the process ends with the window (Program.Main)

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

        public LauncherForm(Rectangle area, Options opt)
        {
            this.opt = opt;
            Text = Loc.Title; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = area;
            BackColor = Color.Black; DoubleBuffered = true; KeyPreview = true; AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            LoadMenu();
            audio = new Audio(); audio.ReadVolumes();
            Pad.Init();
            timer = new Timer { Interval = 16 }; timer.Tick += Tick;
        }

        // ---- menu (MainForm.InitializeActiveRect of the original) ----
        static string Node(XmlNode n, string path) { var x = n.SelectSingleNode(path + Config.Cond + "/text()"); return x != null ? x.Value : null; }

        void LoadMenu()
        {
            var doc = new XmlDocument(); doc.LoadXml(Data.Text("MetaMenu/PS3Data/MenuMetaMenu/MenuActiveRects.xml"));
            bgW = int.Parse(Node(doc, "/FormConfig/Background/Width")); bgH = int.Parse(Node(doc, "/FormConfig/Background/Height"));
            string dir = "MetaMenu/PS3Data/MenuMetaMenu/" + Config.LanguagePath + "/";
            bgPath = dir + Node(doc, "/FormConfig/Background/Image");
            buttons.Clear();
            foreach (XmlNode n in doc.SelectNodes("//FormConfig/ActiveRect"))
            {
                int t = int.Parse(Node(n, "./Top")), l = int.Parse(Node(n, "./Left")), w = int.Parse(Node(n, "./Width")), h = int.Parse(Node(n, "./Height"));
                buttons.Add(new Btn { R = Rectangle.FromLTRB(l, t, l + w, t + h), Image = dir + Node(n, "./Image"), Click = Node(n, "./OnClick") });
            }
            Text = Loc.Title;
            Layout2();
        }

        void Layout2()
        {
            Size cs = ClientSize.Width > 0 ? ClientSize : Bounds.Size;
            scale = Math.Min((float)cs.Width / bgW, (float)cs.Height / bgH);
            int vw = (int)Math.Round(bgW * scale), vh = (int)Math.Round(bgH * scale);
            view = new Rectangle((cs.Width - vw) / 2, (cs.Height - vh) / 2, vw, vh);
            if (bgScaled != null) bgScaled.Dispose();
            bgScaled = Resample(Data.Image(bgPath), vw, vh);
            foreach (var b in buttons) { if (b.Scaled != null) b.Scaled.Dispose(); b.Scaled = Resample(Data.Image(b.Image), S(b.R.Width), S(b.R.Height)); }
            if (versionScaled != null) versionScaled.Dispose();
            versionScaled = VersionLabel();
            Log.Write(string.Format("layout: area {0}x{1}, menu {2}x{3} -> {4} (scale {5:0.###}), language {6}", cs.Width, cs.Height, bgW, bgH, view, scale, Config.Language));
            Invalidate();
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        static Bitmap Resample(Image src, int w, int h)
        {
            var b = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return b;
        }

        // "Version x.y.z" as the original: Arial 16 (12 small), outline and fill of a GraphicsPath, drawn at the menu scale
        Bitmap VersionLabel()
        {
            float fs = Config.IsSmall ? 12 : 16, pw = Config.IsSmall ? 1.5f : 2f;
            using (var path = new GraphicsPath())
            {
                FontFamily fam; try { fam = new FontFamily("Arial"); } catch { fam = FontFamily.GenericSansSerif; }
                path.AddString("Version " + Config.Version, fam, 0, fs, new Point(2, 2), StringFormat.GenericTypographic);
                RectangleF bb = path.GetBounds();
                var b = new Bitmap(Math.Max(1, S(bb.Right + 4)), Math.Max(1, S(bb.Bottom + 4)), PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b))
                using (var m = new Matrix())
                {
                    m.Scale(scale, scale); path.Transform(m);
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.CompositingQuality = CompositingQuality.HighQuality; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var pen = new Pen(Color.FromArgb(Config.IsSmall ? 0xC8 : 0x80, Color.Black), pw * scale))
                    using (var br = new SolidBrush(Color.FromArgb(Config.IsSmall ? 0xF0 : 0xC8, Color.White))) { g.DrawPath(pen, path); g.FillPath(br, path); }
                }
                return b;
            }
        }

        // ---- painting ----
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.CompositingQuality = CompositingQuality.HighSpeed;
            g.DrawImage(bgScaled, view.X, view.Y, bgScaled.Width, bgScaled.Height);
            if (overlay == null && hover >= 0 && hover < buttons.Count) { var b = buttons[hover]; g.DrawImage(b.Scaled, view.X + S(b.R.X), view.Y + S(b.R.Y), b.Scaled.Width, b.Scaled.Height); }
            // no version label on the menu (the original showed "Version 1.0.0" here): the version of FFX Enhanced is shown in the settings only
            if (overlay != null) overlay.Paint(g, view, scale);
        }

        // ---- menu actions ----
        void Activate2(int i)
        {
            if (i < 0 || i >= buttons.Count || closing) return;
            audio.Confirm();
            switch (buttons[i].Click)
            {
                case "OnFFXClicked": Launch(Config.FFX, ""); break;
                case "OnSideStoryClicked": Launch(Config.FFX, Config.SideStoryParam); break;
                case "OnFFX2Clicked": Launch(Config.FFX2, ""); break;
                case "OnLastMissionClicked": Launch(Config.FFX2, Config.LastMissionParam); break;
                case "OnCreditClicked": Launch(Config.Credit, ""); break;
                case "OnCredit2014Clicked": Open(new CreditsOverlay(audio, bgW, bgH)); break;
                case "OnConfigClicked": Open(new SettingsView(audio, bgW)); break;
            }
        }

        void Open(IOverlay o) { overlay = o; hover = -1; Log.Write("open " + o.GetType().Name); Invalidate(); }

        void Launch(string exe, string param)
        {
            string path = Path.Combine(Config.GameDir, Fps60Exe.Pick(exe));   // FFX_enh.exe / FFX-2_enh.exe without RevoFX at 60 fps and more
            Log.Write("launch " + path + " " + param);
            if (Scripted)                                                            // a test script never starts a game
            {
                if (opt.StartTest == null) { Log.Write("  test run (--keys): not started"); return; }
                Log.Write("  test run (--keys): " + opt.StartTest + " started in place of the game (--start-test)"); path = opt.StartTest; param = "";
            }
            int r = Native.ShellExecute(Handle, null, path, param, Config.GameDir, 1).ToInt32();
            if (r > 32)                                                              // close after the confirm sound, as the original
            {
                closing = true; closingAt = Environment.TickCount; GameStarted = true;
                // Watchdog: the launcher must not stay in memory next to the game. Should closing hang anywhere (the sound
                // library, a window message, Wine), the process is ended 8 s after the start, whatever it is doing.
                new System.Threading.Thread(() => { System.Threading.Thread.Sleep(8000); Log.Write("exit: still running 8 s after the game start - process terminated"); Native.TerminateProcess(Native.GetCurrentProcess(), 0); }) { IsBackground = true, Name = "exit watchdog" }.Start();
                return;
            }
            if (r == 2 || r == 3 || r == 11) MessageBox.Show(this, new System.ComponentModel.Win32Exception(r).Message + "\n" + path, Loc.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        void SetHover(int i, bool sound)
        {
            if (i == hover) return;
            hover = i; if (sound && i >= 0) audio.Move(); Invalidate();
        }

        void MenuAct(Act a)
        {
            int n = buttons.Count; if (n == 0) return;
            switch (a)
            {
                case Act.Up: case Act.Left: SetHover(hover < 0 ? 0 : (hover + n - 1) % n, true); break;
                case Act.Down: case Act.Right: SetHover(hover < 0 ? 0 : (hover + 1) % n, true); break;
                case Act.Confirm: if (hover < 0) SetHover(0, true); else Activate2(hover); break;
            }
        }

        public void Do(Act a)
        {
            if (overlay != null) { overlay.Act(a); if (overlay.Dirty) Invalidate(); return; }
            MenuAct(a);
        }

        PointF ToMenu(Point p) { return new PointF((p.X - view.X) / scale, (p.Y - view.Y) / scale); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (Scripted) return;
            // Windows also sends WM_MOUSEMOVE without a real move (window shown, repaints): only a changed position
            // counts, so a keyboard / controller selection is kept until the mouse is really moved
            bool moved = mouseKnown && e.Location != lastMouse; lastMouse = e.Location; mouseKnown = true;
            if (!moved) return;
            PointF m = ToMenu(e.Location);
            if (overlay != null) { overlay.MouseMove(m); if (overlay.Dirty) Invalidate(); return; }
            int h = -1;
            for (int i = 0; i < buttons.Count; i++) if (buttons[i].R.Contains((int)Math.Floor(m.X), (int)Math.Floor(m.Y))) h = i;
            SetHover(h, true);
        }
        Point lastMouse; bool mouseKnown, shown;
        bool Scripted { get { return opt.Keys != null; } }                    // --keys test run: no mouse input (it may rest over the window), no game start

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (Scripted) return;
            if (overlay != null && e.Button == MouseButtons.Left) { overlay.MouseDown(ToMenu(e.Location)); if (overlay.Dirty) Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (Scripted) return;
            if (overlay != null) { overlay.MouseUp(ToMenu(e.Location)); if (overlay.Dirty) Invalidate(); return; }
            if (e.Button == MouseButtons.Left && hover >= 0 && buttons[hover].R.Contains(Point.Truncate(ToMenu(e.Location)))) Activate2(hover);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e); if (Scripted) return;
            if (overlay != null) { overlay.Wheel(ToMenu(e.Location), e.Delta); if (overlay.Dirty) Invalidate(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (overlay != null && overlay.Key(k)) { if (overlay.Dirty) Invalidate(); return true; }
            switch (k)
            {
                case Keys.Up: Do(Act.Up); return true;
                case Keys.Down: Do(Act.Down); return true;
                case Keys.Left: Do(Act.Left); return true;
                case Keys.Right: Do(Act.Right); return true;
                case Keys.Enter: case Keys.Space: Do(Act.Confirm); return true;
                case Keys.PageUp: Do(Act.PageUp); return true;
                case Keys.PageDown: Do(Act.PageDown); return true;
                case Keys.M: Do(Act.Mute); return true;
                case Keys.Escape: if (overlay != null) Do(Act.Back); else Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- timer: controller, audio, overlays, closing after a game start ----
        void Tick(object sender, EventArgs e)
        {
            tickCount++;
            audio.Update();
            if (GetForegroundWindow() == Handle) { foreach (Act a in Pad.Poll()) Do(a); } else Pad.Reset();
            if (overlay != null)
            {
                overlay.Tick(timer.Interval);
                var sv = overlay as SettingsView;
                if (sv != null && sv.SavedLanguage != null) { Loc.Load(); LoadMenu(); sv.Relocalise(); }
                if (sv != null && sv.ResourcesChanged) { sv.ResourcesChanged = false; LoadMenu(); sv.Dirty = true; }   // other menu graphics
                if (overlay.Closed) { overlay = null; Invalidate(); }
                else if (overlay.Dirty) Invalidate();
                else if (sv != null && sv.AnimDirty) { sv.AnimDirty = false; Invalidate(sv.AnimRect); }   // swaying pointer only
            }
            if (closing && (!audio.EffectPlaying || Environment.TickCount - closingAt > 3000)) { closing = false; Close(); }   // at most 3 s for the sound
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            shown = true; if (view.Width != ClientSize.Width && view.Height != ClientSize.Height) Layout2();
            Instance.MainWindow = Handle;
            audio.PlayMusic();
            Activate();
            timer.Start();
            if (opt.Keys != null) RunKeys();
            if (opt.Shot != null) StartShot();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (shown && bgScaled != null && ClientSize.Width > 0) Layout2(); }   // the window passes through other sizes while it is created

        protected override void WndProc(ref Message m)
        {
            if (Instance.FocusMessage != 0 && m.Msg == Instance.FocusMessage) { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); return; }
            base.WndProc(ref m);
        }

        // Proton starts Xalia (a gamepad UI for mouse-only launchers; it knows the original launcher of this game by a hard-coded
        // layout). This launcher reads the controller itself: the window property tells Xalia to leave the window alone
        // (Xalia main.gudl: win32_has_window_property("__xalia__supports_gamepad")). Without Xalia it has no effect.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Native.SetProp(Handle, "__xalia__supports_gamepad", (IntPtr)1); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); audio.Dispose(); base.OnFormClosed(e); }

        // ---- test aids ----
        void RunKeys()
        {
            string[] steps = opt.Keys.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries); int i = 0;
            var keys = new Dictionary<string, Keys> { { "up", Keys.Up }, { "down", Keys.Down }, { "left", Keys.Left }, { "right", Keys.Right }, { "enter", Keys.Enter },
                { "space", Keys.Space }, { "esc", Keys.Escape }, { "pgup", Keys.PageUp }, { "pgdn", Keys.PageDown }, { "m", Keys.M }, { "tab", Keys.Tab } };
            var t = new Timer { Interval = 450 };
            t.Tick += delegate
            {
                if (i > 0) Log.Write("  after " + steps[i - 1] + ": " + State());
                if (i >= steps.Length) { t.Stop(); return; }
                string s = steps[i++].ToLowerInvariant();
                if (s.StartsWith("pad:")) Do((Act)Enum.Parse(typeof(Act), s.Substring(4), true));
                else if (s.StartsWith("padraw:")) Pad.Inject(int.Parse(s.Substring(7)));                 // a controller button for the Controls page
                else if (s.StartsWith("key:")) Native.PostMessage(Handle, 0x0100, (IntPtr)(int)(Keys)Enum.Parse(typeof(Keys), s.Substring(4), true), IntPtr.Zero);
                else if (keys.ContainsKey(s)) Native.PostMessage(Handle, 0x0100, (IntPtr)(int)keys[s], IntPtr.Zero);
            };
            t.Start();
        }

        string State()
        {
            if (overlay != null) return overlay.State();
            return hover >= 0 ? "menu, selected " + hover + " (" + buttons[hover].Click + ")" : "menu, nothing selected";
        }

        void StartShot()
        {
            var t = new Timer { Interval = Math.Max(100, opt.ShotDelay) };
            t.Tick += delegate
            {
                t.Stop();
                var b = ClientRectangle;                                              // the window's own drawing only: never other windows on the screen
                using (var bmp = new Bitmap(b.Width, b.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) { g.Clear(BackColor); OnPaint(new PaintEventArgs(g, b)); }
                    bmp.Save(opt.Shot, ImageFormat.Png);
                }
                Log.Write("screenshot " + opt.Shot + " " + b.Width + "x" + b.Height);
                if (opt.Exit) Close();
            };
            t.Start();
        }
    }
}
