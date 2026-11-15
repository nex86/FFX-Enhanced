// Input actions shared by keyboard and controller, and the XInput reader.
// Controller: D-pad or left stick = directions (held: repeat after 400 ms every 110 ms), A = confirm, B = back,
// LB / RB = page, X = mute, Start = confirm. All four pads are read; new pads are looked for once a second.
// Under Proton the Steam Deck controls and other pads appear through XInput as well.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FFXLauncher
{
    enum Act { Up, Down, Left, Right, Confirm, Back, PageUp, PageDown, Mute }

    static class Pad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
        delegate int GetState(int user, out XState s);
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int Get14(int user, out XState s);
        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")] static extern int Get13(int user, out XState s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int Get910(int user, out XState s);
        static GetState get; static string dll; static readonly bool[] connected = new bool[4]; static int tick;
        static int prev, heldDir, heldSince, lastRepeat;
        // Raw mode (the Controls page waits for a button): Poll gives no actions and notes the first newly pressed button as the
        // game's controller code (KeyBind.cs: 11 X, 12 A, 13 B, 14 Y, 15 LB, 16 RB, 17 LT, 18 RT, 19 Back, 20 Start, 21 LS, 22 RS,
        // 23..26 D-pad up / right / down / left); TakeRaw fetches it. The sticks are not buttons there.
        public static bool Raw; static int prevRaw, rawCode;
        static readonly int[] RawBits = { 0x4000, 0x1000, 0x2000, 0x8000, 0x100, 0x200, 0x10000, 0x20000, 0x20, 0x10, 0x40, 0x80, 1, 8, 2, 4 };
        public static int TakeRaw() { int c = rawCode; rawCode = 0; return c; }
        public static void Inject(int code) { rawCode = code; }      // test aid (--keys padraw:<code>)
        const int UP = 1, DOWN = 2, LEFT = 4, RIGHT = 8, START = 0x10, LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000, X = 0x4000;

        static bool Try(GetState g, string name)
        {
            try { XState s; g(0, out s); get = g; dll = name; return true; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        public static void Init()
        {
            if (!Try(Get14, "xinput1_4") && !Try(Get13, "xinput1_3") && !Try(Get910, "xinput9_1_0")) { Log.Write("xinput: not available"); return; }
            string pads = ""; XState s;
            for (int i = 0; i < 4; i++) { connected[i] = get(i, out s) == 0; if (connected[i]) pads += " " + i; }
            Log.Write("xinput: " + dll + ", connected pads:" + (pads.Length > 0 ? pads : " none"));
        }

        public static void Reset() { prev = 0; heldDir = 0; }

        public static List<Act> Poll()
        {
            var acts = new List<Act>(); if (get == null) return acts;
            tick++;
            int b = 0, raw = 0; XState s;
            for (int i = 0; i < 4; i++)
            {
                if (!connected[i] && tick % 60 != 0) continue;
                bool was = connected[i]; connected[i] = get(i, out s) == 0;
                if (connected[i] != was) Log.Write("xinput: pad " + i + (connected[i] ? " connected" : " disconnected"));
                if (!connected[i]) continue;
                b |= s.Buttons; raw |= s.Buttons | (s.LT > 100 ? 0x10000 : 0) | (s.RT > 100 ? 0x20000 : 0);
                if (s.LY > 16000) b |= UP; else if (s.LY < -16000) b |= DOWN;
                if (s.LX < -16000) b |= LEFT; else if (s.LX > 16000) b |= RIGHT;
            }
            int rp = raw & ~prevRaw; prevRaw = raw;
            if (Raw)
            {
                if (rp != 0 && rawCode == 0) for (int i = 0; i < RawBits.Length; i++) if ((rp & RawBits[i]) != 0) { rawCode = 11 + i; break; }
                prev = b; heldDir = (b & UP) != 0 ? UP : (b & DOWN) != 0 ? DOWN : (b & LEFT) != 0 ? LEFT : (b & RIGHT) != 0 ? RIGHT : 0;
                heldSince = lastRepeat = Environment.TickCount;
                return acts;
            }
            int pressed = b & ~prev; prev = b;
            if ((pressed & (A | START)) != 0) acts.Add(Act.Confirm);
            if ((pressed & B) != 0) acts.Add(Act.Back);
            if ((pressed & LB) != 0) acts.Add(Act.PageUp);
            if ((pressed & RB) != 0) acts.Add(Act.PageDown);
            if ((pressed & X) != 0) acts.Add(Act.Mute);
            int dir = (b & UP) != 0 ? UP : (b & DOWN) != 0 ? DOWN : (b & LEFT) != 0 ? LEFT : (b & RIGHT) != 0 ? RIGHT : 0;
            int now = Environment.TickCount;
            if (dir != heldDir) { heldDir = dir; heldSince = lastRepeat = now; if (dir != 0) acts.Add(DirAct(dir)); }
            else if (dir != 0 && now - heldSince > 400 && now - lastRepeat > 110) { lastRepeat = now; acts.Add(DirAct(dir)); }
            return acts;
        }

        static Act DirAct(int d) { return d == UP ? Act.Up : d == DOWN ? Act.Down : d == LEFT ? Act.Left : Act.Right; }
    }
}
