// Credits.cs - the "2014 credits" screen of the original launcher (GameLauncher.CreditForm), ported from its IL.
//
// Translated 1:1 from GameLauncher.CreditForm, ICreditElement, CreditElementBase, CreditTextElement, CreditImgElement,
// TextElement and StaffBinParser (launcher.il). Same constants, coordinates, fonts, colors, alpha math and scroll limits.
// The WinForms Form and Timer are replaced by a host-driven view:
//   - the host calls Tick() every TimerIntervalMs while TimerEnabled is true (timer1, Interval 25), applies Opacity to
//     whatever shows the view, and drops the view once Closed is true (the original called Form.Close())
//   - the host forwards left-button mouse events in logical coordinates (0,0)-(LogicalSize) and keeps forwarding moves
//     while the button is held, also outside the view (WinForms captures the mouse on MouseDown the same way)
//   - NeedsRepaint is set wherever the original called Invalidate(); Paint() clears it
//   - CreditForm_Load centred the window over the main form's client area; positioning is left to the host
//
// Hi-res images: every image is loaded twice. loadOriginal returns the shipped file (metamenu.vbf); ALL metrics come
// from it (LogicalSize, element sizes, the DPI based physical size of DrawImage(Image, Point), positions, hit areas).
// load returns the file actually shown (a loose, possibly upscaled replacement); it is only the pixel source and is
// drawn into the original's logical rectangle. When both return the same bytes the original image is used directly
// and the output is identical to drawing the shipped files.
//
// Paint(Graphics g, float pixelScale) contract: g ALREADY CARRIES the transform. The host sets
// g.ScaleTransform(pixelScale, pixelScale) (optionally preceded by an integer translation, e.g. to centre the view in
// a back buffer of size ceil(LogicalSize * pixelScale)) and Paint draws in logical coordinates. pixelScale must equal
// the scale of that transform: the text layer is rendered at LogicalSize * pixelScale device pixels (sharp text, edge
// fade rows scaled accordingly) and copied 1:1 to the device position of logical (0,0). Paint(g) == Paint(g, 1f).
//
// Original values (non-small / IsSmall):
//   InitializeComponent: ClientSize 239x194, FormBorderStyle.None, Opacity 0.0, timer1.Interval 25,
//                        AutoScaleMode.Font with AutoScaleDimensions 6x13 (no scaling at 96 DPI)
//   ctor:  mScrollBarX 1072 / 536, mScrollBarYBegin 52 / 26, mScrollBarYEnd 478 / 239,
//          mLastLinePosY starts at this.Height / 3 = 194 / 3 = 64 (the designer size; Load resizes the window later),
//          each line adds max element height + 10 / + 4, finally mLastLinePosY -= background height -> max scroll pos
//          text: Arial 18pt / 12pt bold, GraphicsPath.AddString with emSize = Font.Size (pixels), black outline
//          pen 3 / 2 with LineJoin.Round, filled white, centred (StringFormat Center/Center)
//          columns: element i of n is centred at (int)(i * bgW / n + bgW / n / 2)
//          IsSmall: background, logo and the three close button images are drawn at half size (HighQuality)
//   OnPaint: background, then the text layer: rows [0, 52) alpha 0, rows [52, 52 + 150) alpha * i / 150,
//            rows [H - 52 - 150, H - 52) alpha * (150 - i) / 150, rows [H - 52, H) alpha 0 (80 instead of 150 when
//            IsSmall, 26 instead of 52), then the scroll block at
//            (mScrollBarX - w / 2, YBegin + (YEnd - YBegin) * pos / mLastLinePosY - h / 2), then the close button
//            (down > over > up image) at (bgW - w, 0, w, h)
//   Tick:  Opacity += 0.05 (fade in) or -= 0.05 (exiting); > 1 -> 1 and timer off; < 0 -> 0, timer off, Close if exiting
//   Keys:  Up/Down -/+ Height / 10, PageUp/PageDown -/+ Height * 8 / 10, Home, End, Escape -> fade out and close
//   Wheel: -delta / 5
//   ChangeScrollPos clamps to [0, mLastLinePosY]; dragging in the column mScrollBarX-10 < x < mScrollBarX+15 sets the
//   position from y (y <= YBegin and y > YBegin-5 -> 0, y >= YEnd -> mLastLinePosY)
//
// DPI: the original was not DPI aware, so all of its drawing ran at 96 DPI. Graphics.DrawImage(Image, Point) and
// DrawImageUnscaled draw an image at its physical size (pixels * 96 / image DPI); the shipped PNGs carry 72 DPI
// (scollbar.png 6x12 is drawn 8x16) and 95.99 DPI (credits_1280.png). This port reproduces those sizes explicitly so
// the result does not depend on the DPI of the Graphics passed to Paint, and measures text on a 96 DPI surface (the
// original measured on Graphics.FromHwnd of its own window, 96 DPI).
//
// No unsafe code: the alpha pass copies the locked text layer to a managed buffer and back (the original used byte
// pointers on the same LockBits data).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FFXLauncher
{
    public sealed class CreditsView : IDisposable
    {
        // ---- constants of the original ------------------------------------------------------------------------------
        const int DesignerClientHeight = 194;          // InitializeComponent: ClientSize = new Size(239, 194)
        const int TimerInterval = 25;                  // InitializeComponent: timer1.Interval = 25
        const double OpacityStep = 0.05;               // timer1_Tick
        const float OriginalDpi = 96f;                 // the original process was not DPI aware

        const string PathBg = "MetaMenu/PS3Data/MenuCredit/credits_1280.png";
        const string PathScrollBlock = "MetaMenu/PS3Data/MenuCredit/scollbar.png";
        const string PathLogoPrefix = "MetaMenu/PS3Data/MenuCredit/";
        const string PathLogoSuffix = "/credits_logo.png";
        const string PathButtonDown = "MetaMenu/PS3Data/MenuCredit/button_down.png";
        const string PathButtonOver = "MetaMenu/PS3Data/MenuCredit/button_over.png";
        const string PathButtonUp = "MetaMenu/PS3Data/MenuCredit/button_up.png";
        const string PathCreditData = "MetaMenu/GameData/PS3Data/MenuCreditsText/credit.dat";

        [DllImport("Kernel32.dll", EntryPoint = "RtlZeroMemory")]
        static extern void ZeroMemory(IntPtr dest, UIntPtr size);

        // ---- fields of GameLauncher.CreditForm -----------------------------------------------------------------------
        readonly bool mIsSmall;
        readonly List<ICreditElement> mCreditElements = new List<ICreditElement>();
        int mLastLinePosY;
        int mScrollPos;
        int mScrollBarX = 0x430;                       // 1072
        int mScrollBarYBegin = 52;
        int mScrollBarYEnd = 0x1de;                    // 478
        bool mIsMouseDown;
        bool mIsExiting;
        Sprite mFFXLogo;
        Sprite mScrollBlock;
        Bitmap mTextLayer;
        Sprite mXButtonUp;
        Sprite mXButtonDown;
        Sprite mXButtonOver;
        bool mIsXButtonOver;
        bool mIsXButtonDown;
        Sprite mBg;

        // replacements for the Form / Timer state
        bool mTimerEnabled;
        double mOpacity;
        bool mClosed;
        bool mNeedsRepaint;
        readonly int mWindowHeight;

        // resources the port owns (the original leaked most of them or relied on finalizers)
        readonly List<IDisposable> mOwned = new List<IDisposable>();
        readonly Pen mPen;
        readonly StringFormat mStrFmt;
        Font mFont;
        byte[] mLayerBuffer;
        bool mDisposed;

        // ---- public surface ------------------------------------------------------------------------------------------
        public Size LogicalSize { get { return new Size(mBg.Width, mBg.Height); } }

        // this.Height of the original after CreditForm_Load. The form uses FormBorderStyle.None, so the window has no
        // frame: Program.AdjustWindowRect (Win32 AdjustWindowRect with the window's GWL_STYLE, no WS_CAPTION, WS_BORDER
        // or WS_THICKFRAME) returns the client rectangle unchanged and Height == client height == background height
        // (560, or 280 when IsSmall). Two PageDown presses then give 2 * (560 * 8 / 10) = 896.
        public int OriginalWindowHeight { get { return mWindowHeight; } }

        public int TimerIntervalMs { get { return TimerInterval; } }
        public bool TimerEnabled { get { return mTimerEnabled && !mClosed; } }
        public double Opacity { get { return mOpacity; } }
        public bool Closed { get { return mClosed; } }
        public bool NeedsRepaint { get { return mNeedsRepaint; } }
        public int ScrollPos { get { return mScrollPos; } }
        public bool IsCloseButtonOver { get { return mIsXButtonOver; } }

        // ---- ctor (CreditForm..ctor, InitializeComponent and CreditForm_Load) ------------------------------------
        // load: the file to show (loose replacement first); loadOriginal: the shipped file, source of all metrics.
        // Both take archive paths like "MetaMenu/PS3Data/MenuCredit/credits_1280.png" and throw if the file is missing.
        public CreditsView(Func<string, byte[]> load, Func<string, byte[]> loadOriginal, string languagePath, bool isSmall)
        {
            if (load == null) throw new ArgumentNullException("load");
            if (loadOriginal == null) throw new ArgumentNullException("loadOriginal");
            mIsSmall = isSmall;

            // InitializeComponent: Opacity = 0.0; the form is FormBorderStyle.None, so this.Height == 194 here
            mOpacity = 0.0;
            mLastLinePosY = DesignerClientHeight / 3;
            mIsMouseDown = false;

            mPen = new Pen(Brushes.Black, isSmall ? 2f : 3f);
            mPen.LineJoin = LineJoin.Round;
            mStrFmt = new StringFormat();
            mStrFmt.Alignment = StringAlignment.Center;
            mStrFmt.LineAlignment = StringAlignment.Center;

            mBg = LoadSprite(load, loadOriginal, PathBg);
            mScrollBlock = LoadSprite(load, loadOriginal, PathScrollBlock);
            mFFXLogo = LoadSprite(load, loadOriginal, PathLogoPrefix + languagePath + PathLogoSuffix);
            mXButtonDown = LoadSprite(load, loadOriginal, PathButtonDown);
            mXButtonOver = LoadSprite(load, loadOriginal, PathButtonOver);
            mXButtonUp = LoadSprite(load, loadOriginal, PathButtonUp);

            if (isSmall)
            {
                mBg = HalfSize(mBg);
                mScrollBarX /= 2;
                mScrollBarYBegin /= 2;
                mScrollBarYEnd /= 2;
                mFFXLogo = HalfSize(mFFXLogo);
                mXButtonUp = HalfSize(mXButtonUp);
                mXButtonOver = HalfSize(mXButtonOver);
                mXButtonDown = HalfSize(mXButtonDown);
            }

            AddLine(new CreditImgElement(mFFXLogo));

            StaffBinParser parser = new StaffBinParser();
            parser.Load(load(PathCreditData));

            mFont = isSmall ? new Font("Arial", 12f, FontStyle.Bold) : new Font("Arial", 18f, FontStyle.Bold);

            // the original measured with Graphics.FromHwnd(this.Handle); a 96 DPI bitmap gives the same metrics
            using (Bitmap measureBmp = new Bitmap(1, 1))
            {
                measureBmp.SetResolution(OriginalDpi, OriginalDpi);
                using (Graphics gMeasure = Graphics.FromImage(measureBmp))
                {
                    int rowCount = parser.RowCount;
                    for (int row = 0; row < rowCount; row++)
                    {
                        int columns = parser.ColumnCount(row);
                        ICreditElement[] elems = null;
                        if (columns == 0)
                        {
                            elems = new ICreditElement[] { NewText(" ", gMeasure) };
                        }
                        else
                        {
                            elems = new ICreditElement[columns];
                            for (int col = 0; col < columns; col++)
                                elems[col] = NewText(parser[row, col].text, gMeasure);
                        }
                        AddLine(elems);
                    }
                }
            }

            mLastLinePosY -= mBg.Height;

            // CreditForm_Load: the window is resized to the background size (AdjustWindowRect adds no frame for
            // FormBorderStyle.None), centred over the main form, and timer1 is started (fade in)
            mWindowHeight = mBg.Height;
            mTimerEnabled = true;
            mNeedsRepaint = true;
        }

        // An image as the original saw it (Width, Height, physical size) plus the pixel source actually drawn.
        sealed class Sprite
        {
            public Image Src;          // pixel source
            public bool IsOriginal;    // Src is the original's own image: draw with the original's exact calls
            public int Width;          // Image.Width / Height of the original
            public int Height;
            public float PhysWidth;    // size of DrawImage(Image, Point) at 96 DPI: pixels * 96 / image DPI
            public float PhysHeight;
        }

        Image DecodeImage(byte[] data, string path)
        {
            // Program.LoadVBFImage: Image.FromStream over the archive bytes (keeps the PNG's DPI, which matters for
            // DrawImage(Image, Point)). The stream stays open for the image's lifetime as GDI+ requires.
            if (data == null) throw new FileNotFoundException("missing launcher data: " + path, path);
            MemoryStream ms = new MemoryStream(data);
            Image img = Image.FromStream(ms);
            mOwned.Add(img);
            mOwned.Add(ms);
            return img;
        }

        Sprite LoadSprite(Func<string, byte[]> load, Func<string, byte[]> loadOriginal, string path)
        {
            byte[] origData = loadOriginal(path);
            byte[] data = load(path);
            Image orig = DecodeImage(origData, path);
            Sprite s = new Sprite();
            s.Width = orig.Width;
            s.Height = orig.Height;
            s.PhysWidth = orig.Width * OriginalDpi / orig.HorizontalResolution;
            s.PhysHeight = orig.Height * OriginalDpi / orig.VerticalResolution;
            if (SameBytes(origData, data))
            {
                s.Src = orig;
                s.IsOriginal = true;
            }
            else
            {
                s.Src = DecodeImage(data, path);
                s.IsOriginal = false;
            }
            return s;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        // IsSmall: the original replaced the image by new Bitmap(w / 2, h / 2) (96 DPI) drawn with HighQuality settings
        Sprite HalfSize(Sprite s)
        {
            Sprite r = new Sprite();
            r.Width = s.Width / 2;
            r.Height = s.Height / 2;
            r.PhysWidth = r.Width;
            r.PhysHeight = r.Height;
            if (s.IsOriginal)
            {
                Bitmap b = NewBitmap(s.Width / 2, s.Height / 2);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.DrawImage(s.Src, 0, 0, s.Width / 2, s.Height / 2);
                    g.Flush();
                }
                r.Src = b;
                r.IsOriginal = true;
            }
            else
            {
                r.Src = s.Src;                         // hi-res source, drawn into the half size rectangle
                r.IsOriginal = false;
            }
            return r;
        }

        Bitmap NewBitmap(int w, int h)
        {
            // new Bitmap(w, h) of the original: 32bpp ARGB at the (96 DPI) screen resolution
            Bitmap b = new Bitmap(w, h);
            b.SetResolution(OriginalDpi, OriginalDpi);
            mOwned.Add(b);
            return b;
        }

        CreditTextElement NewText(string text, Graphics gMeasure)
        {
            return new CreditTextElement(text, mFont, gMeasure, mPen, mStrFmt);
        }

        // draws a replacement image into a logical rectangle with high quality filtering (edges clamped)
        static void DrawHiRes(Graphics g, Image src, RectangleF dest)
        {
            InterpolationMode oldMode = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                PointF[] pts = new PointF[] { new PointF(dest.Left, dest.Top), new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom) };
                g.DrawImage(src, pts, new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel, ia);
            }
            g.InterpolationMode = oldMode;
        }

        // Graphics.DrawImage(Image, Point) / DrawImageUnscaled of the original (96 DPI form graphics)
        static void DrawSpritePhysical(Graphics g, Sprite s, int x, int y)
        {
            RectangleF dest = new RectangleF(x, y, s.PhysWidth, s.PhysHeight);
            if (s.IsOriginal) g.DrawImage(s.Src, dest);
            else DrawHiRes(g, s.Src, dest);
        }

        // Graphics.DrawImage(Image, x, y, w, h) of the original
        static void DrawSpriteRect(Graphics g, Sprite s, int x, int y, int w, int h)
        {
            if (s.IsOriginal) g.DrawImage(s.Src, x, y, w, h);
            else DrawHiRes(g, s.Src, new RectangleF(x, y, w, h));
        }

        // ---- CreditForm.AddLine --------------------------------------------------------------------------------------
        void AddLine(params ICreditElement[] elems)
        {
            int count = elems.Length;
            int width = mBg.Width;
            double step = (double)width / (double)count;
            double half = step / 2.0;
            int maxHeight = 0;
            foreach (ICreditElement e in elems)
            {
                if (e.Height > maxHeight) maxHeight = e.Height;
            }
            for (int i = 0; i < count; i++)
                elems[i].SetPosition((int)((double)i * step + half) - elems[i].Width / 2, mLastLinePosY);
            foreach (ICreditElement e in elems)
                mCreditElements.Add(e);
            if (mIsSmall)
                mLastLinePosY += maxHeight + 4;
            else
                mLastLinePosY += maxHeight + 10;
        }

        // ---- CreditForm.ChangeScrollPos ------------------------------------------------------------------------------
        void ChangeScrollPos(int delta)
        {
            int pos = mScrollPos + delta;
            if (pos >= mLastLinePosY)
            {
                mScrollPos = mLastLinePosY;
                return;
            }
            if (pos < 0)
            {
                mScrollPos = 0;
                return;
            }
            mScrollPos = pos;
        }

        void Invalidate()
        {
            mNeedsRepaint = true;
        }

        // ---- CreditForm.FadeOutAndExiting ----------------------------------------------------------------------------
        void FadeOutAndExiting()
        {
            mIsExiting = true;
            mTimerEnabled = true;
        }

        // ---- CreditForm.OnPaint (preceded by the form's OnPaintBackground) -------------------------------------------
        public void Paint(Graphics g)
        {
            Paint(g, 1f);
        }

        // g carries ScaleTransform(pixelScale) (see the file header); drawing is in logical coordinates
        public void Paint(Graphics g, float pixelScale)
        {
            if (g == null) throw new ArgumentNullException("g");
            if (!(pixelScale > 0f)) throw new ArgumentOutOfRangeException("pixelScale");
            mNeedsRepaint = false;
            if (mDisposed) return;

            int width = mBg.Width;
            int height = mBg.Height;
            bool unscaled = pixelScale == 1f;

            // Form.OnPaintBackground (AllPaintingInWmPaint): BackColor, the default SystemColors.Control
            using (SolidBrush back = new SolidBrush(SystemColors.Control))
                g.FillRectangle(back, 0, 0, width, height);

            // e.Graphics.DrawImageUnscaled(mBg, Point.Empty)
            DrawSpritePhysical(g, mBg, 0, 0);

            // the text layer, logical size * pixelScale device pixels
            int layerW = unscaled ? width : (int)Math.Ceiling(width * (double)pixelScale);
            int layerH = unscaled ? height : (int)Math.Ceiling(height * (double)pixelScale);
            if (mTextLayer == null || mTextLayer.Width != layerW || mTextLayer.Height != layerH)
            {
                if (mTextLayer != null) mTextLayer.Dispose();
                mTextLayer = new Bitmap(layerW, layerH);
                mTextLayer.SetResolution(OriginalDpi, OriginalDpi);
            }

            // clear the text layer: LockBits(WriteOnly, Format32bppArgb) + ZeroMemory(Scan0, Height * Stride)
            Rectangle layerRect = Rectangle.FromLTRB(0, 0, mTextLayer.Width, mTextLayer.Height);
            BitmapData data = mTextLayer.LockBits(layerRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            ZeroMemory(data.Scan0, new UIntPtr((uint)(data.Height * data.Stride)));
            mTextLayer.UnlockBits(data);

            using (Graphics tg = Graphics.FromImage(mTextLayer))
            {
                tg.SmoothingMode = SmoothingMode.AntiAlias;
                tg.TextRenderingHint = TextRenderingHint.AntiAlias;
                foreach (ICreditElement e in mCreditElements)
                    e.Draw(tg, width, height, -mScrollPos, pixelScale);
            }

            // edge fade of the text layer: LockBits(ReadWrite, Format32bppArgb), alpha byte = offset 3 of each pixel.
            // Row boundaries are the original's logical rows times pixelScale (identical at pixelScale 1).
            data = mTextLayer.LockBits(layerRect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int fade = 150;
                if (mIsSmall) fade = 80;
                int topZeroEnd = ScaleRow(mScrollBarYBegin, pixelScale, layerH);
                int topFadeEnd = ScaleRow(mScrollBarYBegin + fade, pixelScale, layerH);
                int bottomFadeBegin = ScaleRow(height - mScrollBarYBegin - fade, pixelScale, layerH);
                int bottomZeroBegin = ScaleRow(height - mScrollBarYBegin, pixelScale, layerH);
                int topFade = topFadeEnd - topZeroEnd;
                int bottomFade = bottomZeroBegin - bottomFadeBegin;

                int stride = data.Stride;
                int w = data.Width;
                int size = stride * data.Height;
                if (mLayerBuffer == null || mLayerBuffer.Length != size) mLayerBuffer = new byte[size];
                byte[] p = mLayerBuffer;
                Marshal.Copy(data.Scan0, p, 0, size);

                // rows [0, YBegin): alpha 0
                int line = 3;
                for (int y = 0; y < topZeroEnd; y++)
                {
                    for (int x = 0; x < w; x++) p[line + x * 4] = 0;
                    line += stride;
                }
                // rows [YBegin, YBegin + fade): alpha * i / fade
                line = stride * topZeroEnd + 3;
                for (int i = 0; i < topFade; i++)
                {
                    for (int x = 0; x < w; x++) p[line + x * 4] = (byte)(p[line + x * 4] * i / topFade);
                    line += stride;
                }
                // rows [H - YBegin - fade, H - YBegin): alpha * (fade - i) / fade
                line = stride * bottomFadeBegin + 3;
                for (int i = 0; i < bottomFade; i++)
                {
                    for (int x = 0; x < w; x++) p[line + x * 4] = (byte)(p[line + x * 4] * (bottomFade - i) / bottomFade);
                    line += stride;
                }
                // rows [H - YBegin, H): alpha 0
                line = stride * bottomZeroBegin + 3;
                int rows = data.Height - bottomZeroBegin;
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < w; x++) p[line + x * 4] = 0;
                    line += stride;
                }

                Marshal.Copy(p, 0, data.Scan0, size);
            }
            finally
            {
                mTextLayer.UnlockBits(data);
            }

            // e.Graphics.DrawImage(mTextLayer, Point.Empty)
            if (unscaled)
            {
                g.DrawImage(mTextLayer, new RectangleF(0, 0, mTextLayer.Width * OriginalDpi / mTextLayer.HorizontalResolution,
                    mTextLayer.Height * OriginalDpi / mTextLayer.VerticalResolution));
            }
            else
            {
                // 1:1 copy to the device position of logical (0,0)
                Matrix m = g.Transform;
                float[] el = m.Elements;
                g.ResetTransform();
                g.DrawImage(mTextLayer, new Rectangle((int)Math.Round(el[4]), (int)Math.Round(el[5]), layerW, layerH));
                g.Transform = m;
                m.Dispose();
            }

            // scroll block
            int blockW = mScrollBlock.Width;
            int blockH = mScrollBlock.Height;
            double ratio = (double)mScrollPos / (double)mLastLinePosY;
            double blockY = (double)mScrollBarYBegin + (double)(mScrollBarYEnd - mScrollBarYBegin) * ratio - (double)(blockH / 2);
            double blockX = (double)(mScrollBarX - blockW / 2);
            DrawSpritePhysical(g, mScrollBlock, (int)blockX, (int)blockY);

            // close button
            Sprite button;
            if (mIsXButtonDown) button = mXButtonDown;
            else if (mIsXButtonOver) button = mXButtonOver;
            else button = mXButtonUp;
            DrawSpriteRect(g, button, mBg.Width - button.Width, 0, button.Width, button.Height);
        }

        static int ScaleRow(int logicalRow, float pixelScale, int layerH)
        {
            int r = pixelScale == 1f ? logicalRow : (int)Math.Round(logicalRow * (double)pixelScale);
            if (r < 0) r = 0;
            if (r > layerH) r = layerH;
            return r;
        }

        // ---- CreditForm.timer1_Tick ----------------------------------------------------------------------------------
        public void Tick()
        {
            if (mClosed || mDisposed || !mTimerEnabled) return;
            double v = 0.0;
            if (mIsExiting)
                v = mOpacity - OpacityStep;
            else
                v = mOpacity + OpacityStep;
            if (!(v <= 1.0))
            {
                v = 1.0;
                mTimerEnabled = false;
            }
            if (!(v >= 0.0))
            {
                v = 0.0;
                mTimerEnabled = false;
                if (mIsExiting) mClosed = true;   // this.Close()
            }
            mOpacity = v;                          // Form.Opacity setter clamps to [0, 1] as well
        }

        // ---- CreditForm_MouseDown / MouseUp / MouseMove --------------------------------------------------------------
        public void MouseDown(int x, int y)
        {
            if (mClosed || mDisposed) return;
            mIsMouseDown = true;
            if (mIsXButtonOver) mIsXButtonDown = true;
            MouseMove(x, y);
        }

        public void MouseUp(int x, int y)
        {
            if (mClosed || mDisposed) return;
            mIsMouseDown = false;
            if (mIsXButtonDown)
            {
                mIsXButtonDown = false;
                Invalidate();
            }
            if (mIsXButtonOver) FadeOutAndExiting();
        }

        public void MouseMove(int x, int y)
        {
            if (mClosed || mDisposed) return;
            if (mIsMouseDown && x > mScrollBarX - 10 && x < mScrollBarX + 15)
            {
                if (y > mScrollBarYBegin && y < mScrollBarYEnd)
                {
                    double ratio = (double)(y - mScrollBarYBegin) / (double)(mScrollBarYEnd - mScrollBarYBegin);
                    mScrollPos = (int)(ratio * (double)mLastLinePosY);
                }
                else if (y <= mScrollBarYBegin && y > mScrollBarYBegin - 5)
                {
                    mScrollPos = 0;
                }
                else if (y >= mScrollBarYEnd)
                {
                    mScrollPos = mLastLinePosY;
                }
                Invalidate();
            }

            // close button hit test uses the up image size, edges inclusive
            if (x >= mBg.Width - mXButtonUp.Width && x <= mBg.Width && y >= 0 && y <= mXButtonUp.Height)
            {
                if (!mIsXButtonOver)
                {
                    mIsXButtonOver = true;
                    Invalidate();
                }
                return;
            }
            if (mIsXButtonOver)
            {
                mIsXButtonOver = false;
                Invalidate();
            }
        }

        // ---- CreditForm.OnMouseWheel ---------------------------------------------------------------------------------
        public void MouseWheel(int delta)
        {
            if (mClosed || mDisposed) return;
            ChangeScrollPos(-delta / 5);
            Invalidate();
        }

        // ---- CreditForm.OnKeyDown (e.KeyCode) ------------------------------------------------------------------------
        public void KeyDown(Keys key)
        {
            if (mClosed || mDisposed) return;
            Keys code = key & Keys.KeyCode;
            int h = mWindowHeight;
            if (code == Keys.Up) ChangeScrollPos(-h / 10);
            else if (code == Keys.Down) ChangeScrollPos(h / 10);
            else if (code == Keys.Home) ChangeScrollPos(-mScrollPos);
            else if (code == Keys.End) ChangeScrollPos(mLastLinePosY - mScrollPos);
            else if (code == Keys.PageUp) ChangeScrollPos(-h * 8 / 10);
            else if (code == Keys.PageDown) ChangeScrollPos(h * 8 / 10);
            else if (code == Keys.Escape) FadeOutAndExiting();
            else return;                            // base.OnKeyDown, no Invalidate
            Invalidate();
        }

        public void Dispose()
        {
            if (mDisposed) return;
            mDisposed = true;
            foreach (ICreditElement e in mCreditElements)
            {
                IDisposable d = e as IDisposable;
                if (d != null) d.Dispose();
            }
            mCreditElements.Clear();
            for (int i = mOwned.Count - 1; i >= 0; i--) mOwned[i].Dispose();
            mOwned.Clear();
            if (mTextLayer != null) { mTextLayer.Dispose(); mTextLayer = null; }
            if (mFont != null) { mFont.Dispose(); mFont = null; }
            mPen.Dispose();
            mStrFmt.Dispose();
            mLayerBuffer = null;
        }

        // ==== credit elements ==========================================================================================
        // Draw gets the logical area and offset of the original plus the device scale of the text layer graphics (which
        // has no transform); at scale 1 the original's exact drawing calls are used.

        interface ICreditElement
        {
            int Width { get; }
            int Height { get; }
            void SetPosition(int x, int y);
            void Draw(Graphics g, int rectW, int rectH, int offsetY, float scale);
        }

        abstract class CreditElementBase : ICreditElement
        {
            protected int mWidth;
            protected int mHeight;
            protected int mX;
            protected int mY;

            public int Width { get { return mWidth; } }
            public int Height { get { return mHeight; } }

            public void SetPosition(int x, int y)
            {
                mX = x;
                mY = y;
            }

            protected abstract void DrawImpl(Graphics g, Point pt, float scale);

            public void Draw(Graphics g, int rectW, int rectH, int offsetY, float scale)
            {
                // skipped when the vertical centre is above the top or below the bottom of the area
                if (mY + offsetY + mHeight / 2 < 0) return;
                if (mY + offsetY + mHeight / 2 > rectH) return;
                DrawImpl(g, new Point(mX, mY + offsetY), scale);
            }
        }

        sealed class CreditTextElement : CreditElementBase, IDisposable
        {
            readonly Font mFont;
            readonly string mText;
            readonly SizeF mTextSize;
            readonly Pen mPen;
            readonly StringFormat mStrFmt;
            Image mBuffer;
            Bitmap mScaledBuffer;          // the same text rendered at mScaledFor device pixels per logical pixel
            float mScaledFor;

            public CreditTextElement(string text, Font font, Graphics gMeasure, Pen pen, StringFormat strFmt)
            {
                mText = text;
                mFont = font;
                mPen = pen;
                mStrFmt = strFmt;
                mTextSize = gMeasure.MeasureString(mText, mFont);
                int w = (int)mTextSize.Width;
                int h = (int)mTextSize.Height;
                if (w > 0 && h > 0)
                {
                    mBuffer = Render(w, h, 1f);
                    mWidth = (int)mTextSize.Width;
                    mHeight = (int)mTextSize.Height;
                }
                else
                {
                    mWidth = 0;
                    mHeight = 0;
                }
            }

            Bitmap Render(int w, int h, float scale)
            {
                Bitmap b = new Bitmap(w, h);
                b.SetResolution(OriginalDpi, OriginalDpi);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (scale != 1f) g.ScaleTransform(scale, scale);
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        Rectangle r = Rectangle.FromLTRB(0, 0, (int)mTextSize.Width, (int)mTextSize.Height);
                        // emSize = Font.Size (points) used as world units (pixels), like the original
                        path.AddString(mText, mFont.FontFamily, (int)FontStyle.Bold, mFont.Size, r, mStrFmt);
                        g.DrawPath(mPen, path);
                        g.FillPath(Brushes.White, path);
                        g.Flush();
                    }
                }
                return b;
            }

            protected override void DrawImpl(Graphics g, Point pt, float scale)
            {
                if (mWidth <= 0 || mHeight <= 0) return;
                if (scale == 1f)
                {
                    // both the buffer and the text layer are 96 DPI bitmaps, so DrawImage(Image, Point) is unscaled
                    g.DrawImage(mBuffer, pt);
                    return;
                }
                if (mScaledBuffer == null || mScaledFor != scale)
                {
                    if (mScaledBuffer != null) mScaledBuffer.Dispose();
                    mScaledBuffer = Render((int)Math.Ceiling(mWidth * (double)scale), (int)Math.Ceiling(mHeight * (double)scale), scale);
                    mScaledFor = scale;
                }
                g.DrawImage(mScaledBuffer, new Point((int)Math.Round(pt.X * (double)scale), (int)Math.Round(pt.Y * (double)scale)));
            }

            public void Dispose()
            {
                if (mBuffer != null)
                {
                    mBuffer.Dispose();
                    mBuffer = null;
                }
                if (mScaledBuffer != null)
                {
                    mScaledBuffer.Dispose();
                    mScaledBuffer = null;
                }
            }
        }

        sealed class CreditImgElement : CreditElementBase
        {
            readonly Sprite mImg;

            public CreditImgElement(Sprite img)
            {
                mImg = img;
                mWidth = img.Width;
                mHeight = img.Height;
            }

            protected override void DrawImpl(Graphics g, Point pt, float scale)
            {
                if (scale == 1f)
                {
                    DrawSpriteRect(g, mImg, pt.X, pt.Y, mWidth, mHeight);
                }
                else
                {
                    RectangleF dest = new RectangleF(pt.X * scale, pt.Y * scale, mWidth * scale, mHeight * scale);
                    DrawHiRes(g, mImg.Src, dest);
                }
                g.Flush();
            }
        }

        // ==== credit.dat (GameLauncher.TextElement / StaffBinParser) ===================================================
        //   u16 base offset of the string table, u16 row count, then per row: u8 column count, column count * s32
        //   string offsets relative to the base; string: u8 isBold, u16 length + 1, length bytes UTF-8 XOR 0x5B

        sealed class TextElement
        {
            public byte isBold;
            public ushort length;
            public string text;
        }

        sealed class StaffBinParser
        {
            ushort mNumRows;
            byte[] mNumColumns;
            Dictionary<uint, TextElement> mTexts;

            static TextElement GetTextElement(byte[] buffer, int startIndex)
            {
                TextElement e = new TextElement();
                e.isBold = buffer[startIndex];
                startIndex++;
                e.length = (ushort)(BitConverter.ToUInt16(buffer, startIndex) - 1);
                startIndex += 2;
                byte[] bytes = new byte[e.length];
                Array.Copy(buffer, startIndex, bytes, 0, e.length);
                for (int i = 0; i < e.length; i++) bytes[i] = (byte)(bytes[i] ^ 91);
                e.text = Encoding.UTF8.GetString(bytes);
                return e;
            }

            public bool Load(byte[] buffer)
            {
                int pos = 0;
                mTexts = new Dictionary<uint, TextElement>();
                int baseOffset = BitConverter.ToUInt16(buffer, pos);
                pos += 2;
                mNumRows = BitConverter.ToUInt16(buffer, pos);
                pos += 2;
                mNumColumns = new byte[mNumRows];
                for (int row = 0; row < mNumRows; row++)
                {
                    mNumColumns[row] = buffer[pos];
                    pos++;
                    for (int col = 0; col < mNumColumns[row]; col++)
                    {
                        int offset = BitConverter.ToInt32(buffer, pos);
                        pos += 4;
                        TextElement e = GetTextElement(buffer, baseOffset + offset);
                        mTexts.Add((uint)((row << 8) | col), e);
                    }
                }
                return true;
            }

            public TextElement this[int row, int column]
            {
                get { return mTexts[(uint)((row << 8) | column)]; }
            }

            public int RowCount { get { return mNumRows; } }

            public int ColumnCount(int row)
            {
                return mNumColumns[row];
            }
        }
    }
}
