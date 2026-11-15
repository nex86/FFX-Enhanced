// The 2014 credits (CreditsView, a port of the original CreditForm) as an overlay of the launcher window: centred over
// the menu like the original window, rendered at screen resolution (sharp text, upscaled images if present), faded
// with the original's opacity. Mouse coordinates go to the view in its logical units; the controller maps to the
// original keys (up / down scroll, left / right / LB / RB page, confirm or back = Esc = fade out and close).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace FFXLauncher
{
    sealed class CreditsOverlay : IOverlay
    {
        readonly CreditsView v; readonly PointF origin; Bitmap back; float backScale; int acc; double lastOpacity = -1;
        public bool Dirty { get; set; }
        public bool Closed { get { return v.Closed; } }

        public CreditsOverlay(Audio audio, int menuW, int menuH)
        {
            v = new CreditsView(Data.Load, Data.LoadOriginal, Config.LanguagePath, Config.IsSmall);
            origin = new PointF((menuW - v.LogicalSize.Width) / 2, (menuH - v.LogicalSize.Height) / 2);   // CreditForm_Load centred it on the menu
            Dirty = true;
        }

        public void Paint(Graphics g, RectangleF view, float scale)
        {
            int bw = (int)Math.Ceiling(v.LogicalSize.Width * scale), bh = (int)Math.Ceiling(v.LogicalSize.Height * scale);
            if (back == null || back.Width != bw || back.Height != bh || backScale != scale || v.NeedsRepaint || Dirty)
            {
                if (back == null || back.Width != bw || back.Height != bh) { if (back != null) back.Dispose(); back = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb); }
                using (var gb = Graphics.FromImage(back))
                {
                    gb.Clear(Color.Transparent);
                    gb.InterpolationMode = InterpolationMode.HighQualityBicubic; gb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gb.SmoothingMode = SmoothingMode.AntiAlias; gb.TextRenderingHint = TextRenderingHint.AntiAlias;
                    gb.ScaleTransform(scale, scale);
                    v.Paint(gb, scale);
                }
                backScale = scale;
            }
            Dirty = false;
            float op = (float)Math.Max(0, Math.Min(1, v.Opacity)); lastOpacity = op;
            if (op <= 0) return;
            var dest = new Rectangle((int)Math.Round(view.X + origin.X * scale), (int)Math.Round(view.Y + origin.Y * scale), bw, bh);
            if (op >= 1) { g.DrawImage(back, dest.X, dest.Y, bw, bh); return; }
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix(); cm.Matrix33 = op; ia.SetColorMatrix(cm);
                g.DrawImage(back, dest, 0, 0, bw, bh, GraphicsUnit.Pixel, ia);
            }
        }

        int X(PointF p) { return (int)Math.Floor(p.X - origin.X); }
        int Y(PointF p) { return (int)Math.Floor(p.Y - origin.Y); }

        public void MouseMove(PointF p) { v.MouseMove(X(p), Y(p)); Sync(); }
        public void MouseDown(PointF p) { v.MouseDown(X(p), Y(p)); Sync(); }
        public void MouseUp(PointF p) { v.MouseUp(X(p), Y(p)); Sync(); }
        public void Wheel(PointF p, int delta) { v.MouseWheel(delta); Sync(); }

        public bool Act(Act a)
        {
            switch (a)
            {
                case FFXLauncher.Act.Up: v.KeyDown(Keys.Up); break;
                case FFXLauncher.Act.Down: v.KeyDown(Keys.Down); break;
                case FFXLauncher.Act.Left: case FFXLauncher.Act.PageUp: v.KeyDown(Keys.PageUp); break;
                case FFXLauncher.Act.Right: case FFXLauncher.Act.PageDown: v.KeyDown(Keys.PageDown); break;
                case FFXLauncher.Act.Confirm: case FFXLauncher.Act.Back: v.KeyDown(Keys.Escape); break;
                default: return false;
            }
            Sync(); return true;
        }

        public bool Key(Keys k)
        {
            if (k == Keys.Home || k == Keys.End) { v.KeyDown(k); Sync(); return true; }
            return false;
        }

        public void Tick(int ms)
        {
            if (!v.TimerEnabled) { acc = 0; return; }
            acc += ms;
            while (acc >= v.TimerIntervalMs && v.TimerEnabled) { v.Tick(); acc -= v.TimerIntervalMs; }
            Sync();
        }

        void Sync() { if (v.NeedsRepaint || Math.Abs(v.Opacity - lastOpacity) > 0.001) Dirty = true; }

        public string State() { return "credits, scroll " + v.ScrollPos + ", opacity " + v.Opacity.ToString("0.00") + (v.IsCloseButtonOver ? ", close button hover" : ""); }
    }
}
