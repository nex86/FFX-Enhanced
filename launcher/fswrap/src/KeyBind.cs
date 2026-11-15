// Key bindings of the three games (GameSetting.ini: ffxKeyBinding, ffx2KeyBinding, lmKeyBinding), as FFX.exe / FFX-2.exe read
// them (docs\LAUNCHER.md 5.5): 81 numbers, comma separated. A number is a key or button code, 0 = not bound.
//   keyboard codes   28..119: A..Z 28..53, 1..9 0 54..63, Esc 64, F1..F12 65..76, Enter, Backspace, Tab, Space 77..80,
//                    - = [ ] \ ; ' , . / 81..90, Left Right Up Down 91..94, Home End PageUp PageDown Insert Delete 95..100,
//                    Numpad 1..9 0 101..110, Numpad / * - + 111..114, Numpad . 116, Control Shift Alt 117..119
//   controller codes 11..26: X A B Y LB RB LT RT Back Start LS RS Up Right Down Left (XInput names)
//   slots            0..13 keyboard 1 and 14..27 controller for the 14 basic actions, 28..48 further functions (keyboard,
//                    44 / 45 controller), 49..62 keyboard 2 of 0..13, 63..80 keyboard 2 of further functions
// Which slot a row of the game's Keyboard / Controller pages edits is in the tables below (FFX.exe 0xC399E8 / 0xC39EB8,
// FFX-2.exe 0xD42F7C / 0xD43588 for FFX-2 and 0xD430DC / 0xD43648 for Last Mission), with the text line of the row.
// The game's check (FFX.exe 0x405BB0 with 0x405CB0 / 0x405D00 / 0x405D80 = "slot is used by this game"): every used slot
// below 49 must hold a valid code, a used slot from 49 on may be 0, and no code may be on two used slots. If one of the
// three lists fails, the game resets all three to its defaults - so the launcher writes only lists that pass (Check).
using System;
using System.Collections.Generic;
using WK = System.Windows.Forms.Keys;

namespace FFXLauncher
{
    sealed class BindRow { public int K1 = -1, K2 = -1, Pad = -1, Text; }     // slots (keyboard 1, keyboard 2, controller), text line

    static class KeyBind
    {
        public const int N = 81;
        public static readonly string[] IniKeys = { "ffxKeyBinding", "ffx2KeyBinding", "lmKeyBinding" };
        public static readonly string[] GameNames = { "FINAL FANTASY X", "FINAL FANTASY X-2", "Last Mission" };

        // { keyboard 1 slot, keyboard 2 slot, text }, in the order of the game's page
        static readonly int[][][] KeyRows = {
            new[] { new[] { 0, 49, 632 }, new[] { 1, 50, 633 }, new[] { 2, 51, 634 }, new[] { 3, 52, 635 }, new[] { 4, 53, 636 }, new[] { 5, 54, 637 },
                    new[] { 6, 55, 638 }, new[] { 7, 56, 639 }, new[] { 8, 57, 897 }, new[] { 10, 59, 898 }, new[] { 9, 58, 899 }, new[] { 11, 60, 900 },
                    new[] { 38, 73, 901 }, new[] { 39, 74, 902 }, new[] { 36, 71, 903 }, new[] { 37, 72, 904 }, new[] { 12, 61, 644 }, new[] { 13, 62, 645 },
                    new[] { 28, 63, 646 }, new[] { 29, 64, 647 }, new[] { 31, 66, 649 }, new[] { 33, 68, 651 }, new[] { 34, 69, 652 } },
            new[] { new[] { 0, 49, 1372 }, new[] { 1, 50, 1373 }, new[] { 2, 51, 1374 }, new[] { 3, 52, 1375 }, new[] { 4, 53, 1376 }, new[] { 5, 54, 1377 },
                    new[] { 6, 55, 1378 }, new[] { 7, 56, 1379 }, new[] { 8, 57, 1380 }, new[] { 10, 59, 1381 }, new[] { 9, 58, 1382 }, new[] { 11, 60, 1383 },
                    new[] { 12, 61, 1384 }, new[] { 13, 62, 1695 }, new[] { 42, 76, 1386 }, new[] { 41, 75, 1385 }, new[] { 43, 77, 1387 },
                    new[] { 28, 63, 1388 }, new[] { 29, 64, 1389 }, new[] { 31, 66, 1391 }, new[] { 33, 68, 1393 }, new[] { 34, 69, 1394 } },
            new[] { new[] { 0, 49, 1397 }, new[] { 1, 50, 1398 }, new[] { 2, 51, 1399 }, new[] { 3, 52, 1400 }, new[] { 4, 53, 1401 }, new[] { 5, 54, 1402 },
                    new[] { 6, 55, 1403 }, new[] { 7, 56, 1404 }, new[] { 9, 58, 1405 }, new[] { 11, 60, 1406 }, new[] { 8, 57, 1407 }, new[] { 10, 59, 1408 },
                    new[] { 43, 77, 1409 }, new[] { 48, 80, 1410 }, new[] { 46, 78, 1411 }, new[] { 47, 79, 1412 }, new[] { 12, 61, 1413 }, new[] { 13, 62, 1415 },
                    new[] { 28, 63, 1416 }, new[] { 31, 66, 1418 }, new[] { 33, 68, 1420 } } };
        // { controller slot, text }
        static readonly int[][][] PadRows = {
            new[] { new[] { 14, 632 }, new[] { 15, 633 }, new[] { 16, 634 }, new[] { 17, 635 }, new[] { 18, 636 }, new[] { 19, 637 }, new[] { 20, 638 },
                    new[] { 21, 639 }, new[] { 22, 640 }, new[] { 23, 641 }, new[] { 24, 642 }, new[] { 25, 643 }, new[] { 26, 644 }, new[] { 27, 645 } },
            new[] { new[] { 14, 1372 }, new[] { 15, 1373 }, new[] { 16, 1374 }, new[] { 17, 1375 }, new[] { 18, 1376 }, new[] { 19, 1377 }, new[] { 20, 1378 },
                    new[] { 21, 1379 }, new[] { 22, 1380 }, new[] { 23, 1382 }, new[] { 24, 1381 }, new[] { 25, 1383 }, new[] { 26, 1384 }, new[] { 27, 1695 },
                    new[] { 44, 1385 }, new[] { 45, 1386 } },
            new[] { new[] { 14, 1397 }, new[] { 15, 1398 }, new[] { 16, 1399 }, new[] { 17, 1400 }, new[] { 18, 1401 }, new[] { 19, 1402 }, new[] { 20, 1403 },
                    new[] { 21, 1404 }, new[] { 23, 1405 }, new[] { 25, 1406 }, new[] { 22, 1407 }, new[] { 24, 1408 }, new[] { 26, 1413 }, new[] { 27, 1415 } } };

        public static List<BindRow> Keys(int game) { var l = new List<BindRow>(); foreach (var r in KeyRows[game]) l.Add(new BindRow { K1 = r[0], K2 = r[1], Text = r[2] }); return l; }
        public static List<BindRow> Pads(int game) { var l = new List<BindRow>(); foreach (var r in PadRows[game]) l.Add(new BindRow { Pad = r[0], Text = r[1] }); return l; }

        // slots this game does not use (the three functions of the EXE)
        static readonly int[][] Unused = {
            new[] { 41, 42, 43, 44, 45, 46, 47, 48, 75, 76, 77, 78, 79, 80, 35, 70, 30, 32, 65, 67 },
            new[] { 46, 47, 48, 78, 79, 80, 35, 70, 36, 37, 38, 39, 71, 72, 73, 74, 30, 32, 65, 67 },
            new[] { 44, 45, 29, 34, 64, 69, 41, 42, 75, 76, 35, 70, 36, 37, 38, 39, 71, 72, 73, 74, 30, 32, 65, 67 } };
        public static bool Used(int game, int slot) { return Array.IndexOf(Unused[game], slot) < 0; }
        public static bool PadSlot(int slot) { return slot >= 14 && slot <= 27 || slot == 44 || slot == 45; }
        static bool ValidPad(int c) { return c >= 11 && c <= 28; }
        static bool ValidKey(int c) { return c >= 28 && c <= 119 && c != 115; }

        // a slot the game's pages show (the others, e.g. 40 = Esc menu, cannot be changed)
        public static bool Shown(int game, int slot)
        {
            foreach (var r in KeyRows[game]) if (r[0] == slot || r[1] == slot) return true;
            foreach (var r in PadRows[game]) if (r[0] == slot) return true;
            return false;
        }

        // the game's defaults (FFX.exe 0xC8A538, 0xC8A3F0, 0xC8A2A8); confirm / cancel on the controller are B / A in the
        // Japanese, Korean and Chinese versions and A / B in the others (0x402120)
        public static int[] Defaults(int game, string language)
        {
            var a = new int[N];
            int[] b = { 93, 94, 91, 92, 30, 51, 49, 53, 46, 97, 31, 98, 29, 41, 23, 25, 26, 24, 13, 12, 14, 11, 15, 17, 16, 18, 20, 19 };
            int[] f = game == 0 ? new[] { 65, 68, 0, 69, 0, 66, 67, 0, 95, 96, 28, 44, 64 }
                    : game == 1 ? new[] { 65, 68, 0, 69, 0, 66, 67, 0, 0, 0, 0, 0, 64, 34, 35, 50, 21, 22 }
                    : new[] { 65, 0, 0, 69, 0, 66, 0, 0, 0, 0, 0, 0, 64, 0, 0, 99, 0, 0, 95, 96, 100 };
            Array.Copy(b, a, b.Length); Array.Copy(f, 0, a, 28, f.Length); a[53] = 77; a[54] = 78;
            if (language != "jp" && language != "kr" && language != "ch") { a[18] = 12; a[19] = 13; }
            return a;
        }

        // null = the list passes the game's check
        public static string Check(int game, int[] a)
        {
            if (a == null || a.Length != N) return "not " + N + " values";
            for (int i = 0; i < N; i++)
            {
                if (a[i] == 0 && i >= 49) continue;
                if (!Used(game, i)) continue;
                if (PadSlot(i) ? !ValidPad(a[i]) : !ValidKey(a[i])) return "slot " + i + ": code " + a[i] + " is not valid";
            }
            for (int i = 0; i < N; i++)
            {
                if (a[i] == 0 || !Used(game, i)) continue;
                for (int j = i + 1; j < N; j++) if (a[j] == a[i] && Used(game, j)) return "code " + a[i] + " is on the slots " + i + " and " + j;
            }
            return null;
        }

        // as the game parses the line: digits and commas, at most 81 values
        public static int[] Parse(string s)
        {
            if (s == null) return null;
            var a = new int[N]; int n = 0, v = 0; bool any = false;
            foreach (char c in s + ",")
            {
                if (c >= '0' && c <= '9') { v = Math.Min(100000, v * 10 + (c - '0')); any = true; }
                else if (c == ',') { if (n < N) a[n] = v; n++; v = 0; }
                else if (c != ' ') return null;
            }
            return any && n >= N ? a : null;
        }

        public static string Format(int[] a) { var p = new string[a.Length]; for (int i = 0; i < p.Length; i++) p[i] = a[i].ToString(); return string.Join(",", p); }

        // the INI's list, or the defaults when it is missing or would not pass the game's check
        public static int[] Load(int game, string language)
        {
            string s = Config.Get(IniKeys[game], null); int[] a = Parse(s); string e = a == null ? (s == null ? "missing" : "not readable") : Check(game, a);
            if (e == null) return a;
            Log.Write("keys: " + IniKeys[game] + " " + e + ", showing the defaults");
            return Defaults(game, language);
        }

        static readonly string[] KeyNames = BuildNames();
        static string[] BuildNames()
        {
            var n = new List<string>();
            for (char c = 'A'; c <= 'Z'; c++) n.Add(c.ToString());
            n.AddRange("1 2 3 4 5 6 7 8 9 0 Esc F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 Enter Backspace Tab Space - = [ ] \\ ; ' , . / Left Right Up Down Home End PageUp PageDown Insert Delete".Split(' '));
            foreach (char c in "1234567890/*-+") n.Add("Numpad " + c);
            n.Add("?"); n.Add("Numpad ."); n.Add("Control"); n.Add("Shift"); n.Add("Alt");
            return n.ToArray();                                   // index = code - 28
        }
        static readonly string[] PadNames = { "X", "A", "B", "Y", "LB", "RB", "LT", "RT", "Back", "Start", "LS", "RS", "D-Pad Up", "D-Pad Right", "D-Pad Down", "D-Pad Left", "TouchPad" };

        public static string Name(int code, bool pad)
        {
            if (code == 0) return "-";
            if (pad) return code >= 11 && code - 11 < PadNames.Length ? PadNames[code - 11] : "#" + code;
            return code >= 28 && code - 28 < KeyNames.Length ? KeyNames[code - 28] : "#" + code;
        }

        // a pressed key -> the game's code (0 = a key the games do not know). The punctuation keys are assigned by their
        // US positions (virtual keys OEM_*), as the game's names suggest.
        public static int FromKey(WK k)
        {
            if (k >= WK.A && k <= WK.Z) return 28 + (k - WK.A);
            if (k >= WK.D1 && k <= WK.D9) return 54 + (k - WK.D1);
            if (k >= WK.F1 && k <= WK.F12) return 65 + (k - WK.F1);
            if (k >= WK.NumPad1 && k <= WK.NumPad9) return 101 + (k - WK.NumPad1);
            switch (k)
            {
                case WK.D0: return 63;
                case WK.Enter: return 77;
                case WK.Back: return 78;
                case WK.Tab: return 79;
                case WK.Space: return 80;
                case WK.OemMinus: return 81;
                case WK.Oemplus: return 82;
                case WK.OemOpenBrackets: return 83;
                case WK.OemCloseBrackets: return 84;
                case WK.OemPipe: return 85;
                case WK.OemSemicolon: return 86;
                case WK.OemQuotes: return 87;
                case WK.Oemcomma: return 88;
                case WK.OemPeriod: return 89;
                case WK.OemQuestion: return 90;
                case WK.Left: return 91;
                case WK.Right: return 92;
                case WK.Up: return 93;
                case WK.Down: return 94;
                case WK.Home: return 95;
                case WK.End: return 96;
                case WK.PageUp: return 97;
                case WK.PageDown: return 98;
                case WK.Insert: return 99;
                case WK.Delete: return 100;
                case WK.NumPad0: return 110;
                case WK.Divide: return 111;
                case WK.Multiply: return 112;
                case WK.Subtract: return 113;
                case WK.Add: return 114;
                case WK.Decimal: return 116;
                case WK.ControlKey: return 117;
                case WK.ShiftKey: return 118;
                case WK.Menu: return 119;
            }
            return 0;
        }
    }
}
