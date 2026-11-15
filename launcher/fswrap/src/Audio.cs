// Menu music and sound effects through the game's own fmodex.dll (FMOD Ex 4.44, stdcall C API), as the original:
// metamenu.ogg loops between 5487 ms and 117308 ms (a replacement: see the constructor); the two VAG effects share one channel (a new one replaces the last).
// Volumes: music = MusicVolume * master, effects = SFXVolume * master (INI values in percent); master = 0 when muted.
using System;
using System.Runtime.InteropServices;

namespace FFXLauncher
{
    sealed class Audio : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct CREATESOUNDEXINFO   // FMOD Ex 4.44 layout (34 x 4 bytes on x86); pointers and callbacks unused
        {
            public int cbsize; public uint length, fileoffset; public int numchannels, defaultfrequency, format; public uint decodebuffersize;
            public int initialsubsound, numsubsounds; public IntPtr inclusionlist; public int inclusionlistnum;
            public IntPtr pcmreadcallback, pcmsetposcallback, nonblockcallback, dlsname, encryptionkey; public int maxpolyphony; public IntPtr userdata;
            public int suggestedsoundtype; public IntPtr useropen, userclose, userread, userseek, userasyncread, userasynccancel;
            public int speakermap; public IntPtr initialsoundgroup; public uint initialseekposition; public int initialseekpostype;
            public int ignoresetfilesystem, cddaforceaspi; public uint audioqueuepolicy, minmidigranularity; public int nonblockthreadid;
        }
        const string DLL = "fmodex.dll";
        [DllImport(DLL)] static extern int FMOD_System_Create(out IntPtr sys);
        [DllImport(DLL)] static extern int FMOD_System_Init(IntPtr sys, int maxch, uint flags, IntPtr extra);
        [DllImport(DLL)] static extern int FMOD_System_Update(IntPtr sys);
        [DllImport(DLL)] static extern int FMOD_System_Close(IntPtr sys);
        [DllImport(DLL)] static extern int FMOD_System_Release(IntPtr sys);
        [DllImport(DLL)] static extern int FMOD_System_GetChannel(IntPtr sys, int id, out IntPtr ch);
        [DllImport(DLL)] static extern int FMOD_System_CreateSound(IntPtr sys, byte[] data, uint mode, ref CREATESOUNDEXINFO ex, out IntPtr snd);
        [DllImport(DLL)] static extern int FMOD_System_PlaySound(IntPtr sys, int chIndex, IntPtr snd, int paused, ref IntPtr ch);
        [DllImport(DLL)] static extern int FMOD_Sound_SetLoopCount(IntPtr snd, int count);
        [DllImport(DLL)] static extern int FMOD_Sound_SetLoopPoints(IntPtr snd, uint start, uint startType, uint end, uint endType);
        [DllImport(DLL)] static extern int FMOD_Sound_Release(IntPtr snd);
        [DllImport(DLL)] static extern int FMOD_Channel_SetVolume(IntPtr ch, float v);
        [DllImport(DLL)] static extern int FMOD_Channel_IsPlaying(IntPtr ch, out int playing);
        [DllImport(DLL)] static extern int FMOD_Channel_Stop(IntPtr ch);
        const uint OPENMEMORY = 0x800, SOFTWARE = 0x40, LOOP_NORMAL = 0x2, TIMEUNIT_MS = 1, TIMEUNIT_PCM = 2;
        const int CHANNEL_REUSE = -2;

        IntPtr sys, bgm, seMove, seConfirm, bgmCh, seCh;
        byte[] bgmData, moveData, confirmData;   // FMOD reads OPENMEMORY sounds in place: keep the bytes alive
        float master = 1, music = 1, sfx = 1;

        public Audio()
        {
            try
            {
                bgmData = Data.Load("MetaMenu/PS3Data/MenuMetaMenu/metamenu.ogg");
                confirmData = Data.Load("MetaMenu/PS3Data/MenuMetaMenu/metaMenu_SFX_Comfirme.VAG");
                moveData = Data.Load("MetaMenu/PS3Data/MenuMetaMenu/metaMenu_SFX_Move.VAG");
                if (FMOD_System_Create(out sys) != 0 || FMOD_System_Init(sys, 2, 0, IntPtr.Zero) != 0) { Log.Write("audio: FMOD init failed"); sys = IntPtr.Zero; return; }
                FMOD_System_GetChannel(sys, 0, out bgmCh); FMOD_System_GetChannel(sys, 1, out seCh);
                bgm = Create(bgmData, OPENMEMORY | SOFTWARE | LOOP_NORMAL);
                if (bgm != IntPtr.Zero)
                {
                    FMOD_Sound_SetLoopCount(bgm, -1);
                    // the original track loops between 5487 and 117308 ms; a replacement (loose metamenu.ogg that differs
                    // from the archive) loops between its LOOPSTART / LOOPEND (or LOOPLENGTH) tags in samples, else as a whole
                    byte[] orig = Data.LoadOriginal("MetaMenu/PS3Data/MenuMetaMenu/metamenu.ogg");
                    uint ls, le;
                    if (Same(orig, bgmData)) FMOD_Sound_SetLoopPoints(bgm, 5487, TIMEUNIT_MS, 117308, TIMEUNIT_MS);
                    else if (LoopTags(bgmData, out ls, out le)) { FMOD_Sound_SetLoopPoints(bgm, ls, TIMEUNIT_PCM, le, TIMEUNIT_PCM); Log.Write("audio: replacement music, loop " + ls + ".." + le + " samples"); }
                    else Log.Write("audio: replacement music, whole track loops");
                }
                seMove = Create(moveData, OPENMEMORY | SOFTWARE);
                seConfirm = Create(confirmData, OPENMEMORY | SOFTWARE);
                Log.Write("audio: FMOD ready (music " + (bgm != IntPtr.Zero) + ", effects " + (seMove != IntPtr.Zero && seConfirm != IntPtr.Zero) + ")");
            }
            catch (Exception e) { Log.Write("audio: " + e.Message); sys = IntPtr.Zero; }
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // LOOPSTART=n and LOOPEND=n or LOOPLENGTH=n (samples) from the Vorbis comment header at the start of the file
        static bool LoopTags(byte[] ogg, out uint start, out uint end)
        {
            start = end = 0;
            string head = System.Text.Encoding.ASCII.GetString(ogg, 0, Math.Min(ogg.Length, 65536));
            long s = Tag(head, "LOOPSTART="), e = Tag(head, "LOOPEND="), l = Tag(head, "LOOPLENGTH=");
            if (s < 0 || (e < 0 && l <= 0)) return false;
            start = (uint)s; end = (uint)(e >= 0 ? e : s + l - 1);
            return end > start;
        }

        static long Tag(string head, string key)
        {
            int i = head.IndexOf(key, StringComparison.OrdinalIgnoreCase); if (i < 0) return -1;
            i += key.Length; int j = i; while (j < head.Length && char.IsDigit(head[j])) j++;
            long v; return j > i && long.TryParse(head.Substring(i, j - i), out v) ? v : -1;
        }

        IntPtr Create(byte[] data, uint mode)
        {
            var ex = new CREATESOUNDEXINFO(); ex.cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)); ex.length = (uint)data.Length;
            IntPtr s; int r = FMOD_System_CreateSound(sys, data, mode, ref ex, out s);
            if (r != 0) { Log.Write("audio: createSound error " + r); return IntPtr.Zero; }
            return s;
        }

        public void ReadVolumes()
        {
            float m, s;
            music = float.TryParse(Config.Get("MusicVolume", "100"), out m) ? m / 100f : 1f;
            sfx = float.TryParse(Config.Get("SFXVolume", "100"), out s) ? s / 100f : 1f;
            int v; int.TryParse(Config.Get("MasterVolume", "100"), out v);
            SetMaster(Config.Get("Mute", "Off") == "Off" ? v : 0);
        }

        // live preview from the settings (percent values)
        public void SetLevels(int musicPercent, int sfxPercent, int masterPercent)
        {
            music = musicPercent / 100f; sfx = sfxPercent / 100f; SetMaster(masterPercent);
        }

        public void SetMaster(int percent)
        {
            master = percent / 100f;
            if (sys == IntPtr.Zero) return;
            if (seCh != IntPtr.Zero) FMOD_Channel_SetVolume(seCh, sfx * master);
            if (bgmCh != IntPtr.Zero) FMOD_Channel_SetVolume(bgmCh, music * master);
        }

        public void PlayMusic()
        {
            if (sys == IntPtr.Zero || bgm == IntPtr.Zero) return;
            FMOD_System_PlaySound(sys, CHANNEL_REUSE, bgm, 0, ref bgmCh);
            FMOD_Channel_SetVolume(bgmCh, music * master);
        }

        void Play(IntPtr snd)
        {
            if (sys == IntPtr.Zero || snd == IntPtr.Zero) return;
            FMOD_System_PlaySound(sys, CHANNEL_REUSE, snd, 0, ref seCh);
            FMOD_Channel_SetVolume(seCh, sfx * master);
        }
        public void Move() { Play(seMove); }
        public void Confirm() { Play(seConfirm); }

        public bool EffectPlaying
        {
            get { int p = 0; if (sys != IntPtr.Zero && seCh != IntPtr.Zero) FMOD_Channel_IsPlaying(seCh, out p); return p != 0; }
        }

        public void Update() { if (sys != IntPtr.Zero) FMOD_System_Update(sys); }

        public void Dispose()
        {
            if (sys == IntPtr.Zero) return;
            if (bgmCh != IntPtr.Zero) FMOD_Channel_Stop(bgmCh);
            if (seCh != IntPtr.Zero) FMOD_Channel_Stop(seCh);
            if (seConfirm != IntPtr.Zero) FMOD_Sound_Release(seConfirm);
            if (seMove != IntPtr.Zero) FMOD_Sound_Release(seMove);
            if (bgm != IntPtr.Zero) FMOD_Sound_Release(bgm);
            FMOD_System_Close(sys); FMOD_System_Release(sys); sys = IntPtr.Zero;
        }
    }
}
