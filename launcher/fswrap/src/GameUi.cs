// Graphics and texts of the game's own option menu, read at run time from data\FFX_Data.vbf:
//   ffx_data/gamedata/ps3data/flash/escmenu.swf          (Flash 9 UI of the in-game "Esc" menu; its bitmaps only)
//   ffx_data/gamedata/ps3data/lockit/ffx_loc_kit_ps3_<language>.bin   (localised UI texts, one per line, UTF-8)
// The SWF is only used as a bitmap source: its pages and items are filled by FFX.exe at run time, so the launcher
// draws its own layout with these bitmaps. Character ids are those of the Steam release's escmenu.swf; missing ids
// fall back to plain drawing.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace FFXLauncher
{
    static class Swf
    {
        static byte[] Inflate(byte[] d, int offset, int count)
        {
            using (var z = new DeflateStream(new MemoryStream(d, offset + 2, count - 2), CompressionMode.Decompress))   // zlib: skip the header
            using (var o = new MemoryStream()) { z.CopyTo(o); return o.ToArray(); }
        }

        // DefineBitsLossless(2) format 5 and DefineBitsJPEG2 / 3 of the given character ids
        public static Dictionary<int, Bitmap> Bitmaps(byte[] swf, ICollection<int> ids)
        {
            var res = new Dictionary<int, Bitmap>();
            byte[] body;
            if (swf[0] == 'C') body = Inflate(swf, 8, swf.Length - 8);
            else { body = new byte[swf.Length - 8]; Buffer.BlockCopy(swf, 8, body, 0, body.Length); }
            int nbits = body[0] >> 3, p = (5 + 4 * nbits + 7) / 8 + 4;
            while (p + 2 <= body.Length)
            {
                int h = BitConverter.ToUInt16(body, p); p += 2; int code = h >> 6, len = h & 63;
                if (len == 63) { len = BitConverter.ToInt32(body, p); p += 4; }
                if (code == 0) break;
                if ((code == 20 || code == 36 || code == 21 || code == 35) && ids.Contains(BitConverter.ToUInt16(body, p)))
                {
                    int id = BitConverter.ToUInt16(body, p);
                    try { Bitmap b = code == 20 || code == 36 ? Lossless(body, p, len, code == 36) : Jpeg(body, p, len, code == 35); if (b != null) res[id] = b; }
                    catch (Exception e) { Log.Write("swf: bitmap " + id + ": " + e.Message); }
                }
                p += len;
            }
            return res;
        }

        static Bitmap Lossless(byte[] d, int p, int len, bool alpha)
        {
            int fmt = d[p + 2], w = BitConverter.ToUInt16(d, p + 3), h = BitConverter.ToUInt16(d, p + 5);
            if (fmt != 5) return null;
            byte[] px = Inflate(d, p + 7, len - 7);                                   // A R G B per pixel (premultiplied for v2)
            var b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int s = (y * w + x) * 4;
                    row[x * 4] = px[s + 3]; row[x * 4 + 1] = px[s + 2]; row[x * 4 + 2] = px[s + 1]; row[x * 4 + 3] = alpha ? px[s] : (byte)255;
                }
                Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, w * 4);
            }
            b.UnlockBits(bd);
            return b;
        }

        static Bitmap Jpeg(byte[] d, int p, int len, bool withAlpha)
        {
            int jpgOff = p + 2, jpgLen = len - 2, alphaOff = 0;
            if (withAlpha) { jpgLen = BitConverter.ToInt32(d, p + 2); jpgOff = p + 6; alphaOff = jpgOff + jpgLen; }
            if (jpgLen > 4 && d[jpgOff] == 0xFF && d[jpgOff + 1] == 0xD9 && d[jpgOff + 2] == 0xFF && d[jpgOff + 3] == 0xD8) { jpgOff += 4; jpgLen -= 4; }   // erroneous header
            Bitmap b;
            using (var ms = new MemoryStream(d, jpgOff, jpgLen)) using (var img = Image.FromStream(ms)) b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var ms = new MemoryStream(d, jpgOff, jpgLen)) using (var img = Image.FromStream(ms)) using (var g = Graphics.FromImage(b)) g.DrawImage(img, 0, 0, img.Width, img.Height);
            if (withAlpha && alphaOff < p + len)
            {
                byte[] a = Inflate(d, alphaOff, p + len - alphaOff);
                if (a.Length >= b.Width * b.Height)
                {
                    var bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    var row = new byte[b.Width * 4];
                    for (int y = 0; y < b.Height; y++)
                    {
                        Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, row.Length);
                        for (int x = 0; x < b.Width; x++)
                        {
                            int al = a[y * b.Width + x]; row[x * 4 + 3] = (byte)al;
                            if (al > 0 && al < 255) for (int c = 0; c < 3; c++) row[x * 4 + c] = (byte)Math.Min(255, row[x * 4 + c] * 255 / al);   // stored premultiplied
                        }
                        Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, row.Length);
                    }
                    b.UnlockBits(bd);
                }
            }
            return b;
        }
    }

    static class GameUi
    {
        // escmenu.swf character ids
        public const int Background = 3547, TitleBar = 3545, RowBar = 3542, HelpPanel = 3483, Frame = 3526, Ornament = 3561,
            Arrow = 3466, ButtonWide = 19, Button = 3504, HelpText = 3550, Cursor = 3579, Marble = 3572;
        static Dictionary<int, Bitmap> bmps; static string[] lines; static string linesLang;

        public static Bitmap Bmp(int id)
        {
            if (bmps == null)
            {
                bmps = new Dictionary<int, Bitmap>();
                try
                {
                    byte[] swf = Data.TryLoad("ffx_data/gamedata/ps3data/flash/escmenu.swf");
                    if (swf != null) bmps = Swf.Bitmaps(swf, new[] { Background, TitleBar, RowBar, HelpPanel, Frame, Ornament, Arrow, ButtonWide, Button, HelpText, Cursor, Marble });
                    Log.Write("escmenu.swf: " + bmps.Count + " bitmaps");
                }
                catch (Exception e) { Log.Write("escmenu.swf: " + e.Message); }
            }
            Bitmap b; return bmps.TryGetValue(id, out b) ? b : null;
        }

        // The golden pointer of the main game's menus, cut from the menu atlas
        // ffx_data/gamedata/ps3data/menu/d3d11/meswin.dds.phyre: a PhyreEngine texture, DXT5 1024x1024 without mipmaps,
        // the block data at the end of the file, rows stored bottom-up. The pointer is at (613, 534) - (711, 590) of the
        // upright image; one transparent pixel around it is taken along. null if the file is missing or not of this layout.
        static Bitmap menuCursor; static bool menuCursorTried;
        const int AtlasSize = 1024, CurX = 612, CurY = 533, CurW = 100, CurH = 58;

        public static Bitmap MenuCursor()
        {
            if (menuCursorTried) return menuCursor;
            menuCursorTried = true;
            try
            {
                byte[] d = Data.TryLoad("ffx_data/gamedata/ps3data/menu/d3d11/meswin.dds.phyre")
                    ?? Data.TryLoad("ffx_data/gamedata/ps3data/menu_us/d3d11/meswin.dds.phyre");
                int size = AtlasSize * AtlasSize;                                   // DXT5: one byte per pixel
                if (d == null || d.Length < size + 64 || Encoding.ASCII.GetString(d, 0, Math.Min(d.Length - size, 0x1000)).IndexOf("DXT5", StringComparison.Ordinal) < 0)
                { Log.Write("meswin.dds.phyre: not found or not DXT5 " + AtlasSize + "x" + AtlasSize); return null; }
                menuCursor = Dxt5Crop(d, d.Length - size, AtlasSize, CurX, CurY, CurW, CurH);
                Log.Write("meswin.dds.phyre: menu cursor " + CurW + "x" + CurH);
            }
            catch (Exception e) { Log.Write("meswin.dds.phyre: " + e.Message); }
            return menuCursor;
        }

        // pixels (x, y, w, h) of the upright image of a bottom-up DXT5 texture of width texW
        static Bitmap Dxt5Crop(byte[] d, int off, int texW, int x0, int y0, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var row = new byte[w * 4]; var alpha = new int[8]; var col = new int[4, 3];
            int texH = (d.Length - off) / texW;
            for (int y = 0; y < h; y++)
            {
                int ry = texH - 1 - (y0 + y);
                for (int x = 0; x < w; x++)
                {
                    int rx = x0 + x, p = off + ((ry >> 2) * (texW >> 2) + (rx >> 2)) * 16, i = (ry & 3) * 4 + (rx & 3);
                    int a0 = d[p], a1 = d[p + 1];
                    alpha[0] = a0; alpha[1] = a1;
                    if (a0 > a1) for (int j = 1; j < 7; j++) alpha[j + 1] = ((7 - j) * a0 + j * a1) / 7;
                    else { for (int j = 1; j < 5; j++) alpha[j + 1] = ((5 - j) * a0 + j * a1) / 5; alpha[6] = 0; alpha[7] = 255; }
                    long abits = 0; for (int j = 5; j >= 0; j--) abits = (abits << 8) | d[p + 2 + j];
                    int c0 = d[p + 8] | d[p + 9] << 8, c1 = d[p + 10] | d[p + 11] << 8;
                    for (int c = 0; c < 2; c++)
                    {
                        int v = c == 0 ? c0 : c1;
                        col[c, 0] = (v >> 11 & 31) * 255 / 31; col[c, 1] = (v >> 5 & 63) * 255 / 63; col[c, 2] = (v & 31) * 255 / 31;
                    }
                    for (int c = 0; c < 3; c++) { col[2, c] = (2 * col[0, c] + col[1, c]) / 3; col[3, c] = (col[0, c] + 2 * col[1, c]) / 3; }
                    int ci = (int)((uint)(d[p + 12] | d[p + 13] << 8 | d[p + 14] << 16 | d[p + 15] << 24) >> (2 * i)) & 3;
                    row[x * 4] = (byte)col[ci, 2]; row[x * 4 + 1] = (byte)col[ci, 1]; row[x * 4 + 2] = (byte)col[ci, 0];
                    row[x * 4 + 3] = (byte)alpha[(int)(abits >> (3 * i)) & 7];
                }
                Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, w * 4);
            }
            b.UnlockBits(bd);
            return b;
        }

        // text line n of the game's localisation (the ids FFX.exe uses), '@el@' = line break
        public static string T(int n, string def)
        {
            if (linesLang != Config.LanguagePath)
            {
                linesLang = Config.LanguagePath; lines = null;
                try
                {
                    byte[] d = Data.TryLoad("ffx_data/gamedata/ps3data/lockit/ffx_loc_kit_ps3_" + linesLang + ".bin");
                    if (d != null) lines = Encoding.UTF8.GetString(d).Split(new[] { "\r\n" }, StringSplitOptions.None);
                }
                catch (Exception e) { Log.Write("lockit: " + e.Message); }
            }
            if (lines == null || n >= lines.Length || lines[n].Length == 0) return def;
            return lines[n].Replace("@el@", "\n");
        }

        // text line n of FFX-2's localisation (the ids FFX-2.exe uses; FFX-2 and Last Mission rows of the Controls page)
        static string[] lines2; static string lines2Lang;
        public static string T2(int n, string def)
        {
            if (lines2Lang != Config.LanguagePath)
            {
                lines2Lang = Config.LanguagePath; lines2 = null;
                try
                {
                    byte[] d = Data.TryLoad("ffx-2_data/gamedata/ps3data/lockit/ffx2_loc_kit_ps3_" + lines2Lang + ".bin");
                    if (d != null) lines2 = Encoding.UTF8.GetString(d).Split(new[] { "\r\n" }, StringSplitOptions.None);
                }
                catch (Exception e) { Log.Write("lockit (FFX-2): " + e.Message); }
            }
            if (lines2 == null || n >= lines2.Length || lines2[n].Length == 0) return def;
            return lines2[n].Replace("@el@", "\n");
        }
    }
}
