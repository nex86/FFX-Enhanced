/* ffx_fps60.h - the 60 fps patch of FINAL FANTASY X / X-2 HD inside the module (ffx_fps60.cpp, fps60\): what the module's C files
   and the patch code share. The patch is the FFX-60fps mod (github.com/peternguyenst/FFX-60fps, Apache-2.0, fps60\LICENSE and
   fps60\NOTICE.txt) with its pacing reworked: docs\FPS60.md. */
#ifndef FFX_FPS60_H
#define FFX_FPS60_H
#ifdef __cplusplus
extern "C" {
#endif
enum { F60_KIND_LOGIC = 0, F60_KIND_RENDER = 1, F60_KIND_BACKLOG = 2, F60_KIND_STOCK = 3 };   /* = InvKind of the patch */
enum { F60_HIST = 240 };
typedef struct F60Stats {
    int game;                    /* 0 = not installed, 1 = FFX / Eternal Calm, 2 = FFX-2 */
    int state;                   /* 0 off (setting), 1 waiting for the game code, 2 installed, 3 failed (reason in why) */
    char why[120];
    int target, pacing;          /* [Game] FrameRate (30 / 60), Fps60Pacing (0 adaptive, 1 fixed) */
    int mode;                    /* this frame: 0 = the game's own pacing (30 Hz), 1 = the patch's pacing (60 Hz) */
    char hold[64];               /* who holds the game's own pacing ("" = nobody): FMV, SyncData scene, menu ... */
    int divider;                 /* the frame divider the game asks for (2 = 30 Hz, 1 = every refresh: menus) */
    unsigned long long inv, n[4], drops;   /* invocations of the main loop: all, per kind, render-only passes left out (adaptive pacing) */
    float fps, tps;              /* frames drawn and logic ticks per second (last full second) */
    float dps;                   /* render-only passes left out per second (last full second) */
    float ms_avg, ms_p99, ms_max;   /* frame time over the last F60_HIST frames */
    unsigned spikes, stalls;     /* frames over 1.5 x the frame time of the limit in force (25 ms at 60 fps, 50 ms at 30) / over 100 ms, since the start */
    float hist[F60_HIST]; int at;   /* frame times in ms, ring */
    float lag;                   /* adaptive pacing: render passes behind the clock */
    int fmv;                     /* a movie plays */
    double fmv_wall, fmv_video;  /* seconds since it started: wall clock / movie frames shown at 29.97 fps */
    float fmv_last_drift;        /* video - wall of the last finished movie (s) */
} F60Stats;

/* module side (ffx.c, ffx_ui.c) */
int  ffx_fps60_start(int game, const char *name);   /* game 1 = FFX.exe, 2 = FFX-2.exe; 1 = the patch starts (its thread waits for the game code) */
void ffx_fps60_apply(const char *key, int val);     /* a Framerate option changed in the Game tab */
void ffx_fps60_frame(void);                         /* once per picture: diagnostics panel and log */
const F60Stats *ffx_fps60_stats(void);
int  ffx_fps60_lines(char L[][200], int max);       /* the diagnostics as text lines; number of lines */

/* patch side (fps60\x\pacing.cpp, fps60\x2\x2_pacing.cpp and rows) */
double f60_frame(int kind, int drawn, int dropped);   /* every invocation, after its kind is chosen: seconds since the last one are returned by f60_clock */
double f60_clock(void);                               /* seconds since the previous call (0 at the first): the pacing's wall clock */
void   f60_state(int mode, const char *hold, int divider);
void   f60_lag(float lag);
void   f60_fmv(int playing, int stepped);             /* per invocation while the movie row runs: a movie plays, it advanced a frame */
void   f60_installed(int ok, const char *why);
#ifdef __cplusplus
}
#endif
#endif
