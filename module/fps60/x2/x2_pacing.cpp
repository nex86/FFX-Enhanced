// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// 60 Hz pacing: the heart of the mod (same idea as the FFX mod's pacing.cpp).
//
// The stock game runs its main loop (Sg_MainLoop) once every second screen refresh: its frame divider is 2. We keep
// the divider at 1, so the loop runs on every refresh, and tell the game on every other run to skip its logic and only
// draw (the exec flag: 1 = run logic, 0 = draw only). The game logic still runs 30 times a second and the screen
// shows 60 frames. Each run is an "invocation"; pacing_pre() picks its kind (logic, render-only, backlog, stock;
// x2.h) just before it starts.
// Rules:
//   - the game asking for divider 2 (30 Hz) is swallowed while 60 Hz is on; asking for 1 passes
//   - backlog invocations (the game catching up, no draw) always run logic, without moving the alternation
//   - any row can hold stock pacing for a while (FMV, menu, load: pacing_hold_stock); holds nest
//   - the game's booster (X2-PACE-TURBO): every invocation runs logic
//   - X2-PACE-MENUDIV2 (Steam Deck "Fast Results"): see its comment further down
// Tested by tests/x2/test_pacing.cpp.
#include "x2.h"
#include "../../ffx_fps60.h"
#include <math.h>
#include <string.h>

namespace x2 {

// ---- adaptive pacing (RevoFX module; the same scheme as the FFX part, ../x/pacing.cpp) ----
// The mod ran logic on every second invocation whatever the frame time: a PC that draws 48 fps ran the game at 0.8 x speed. Now the
// wall clock decides: time is counted in passes (1 pass = half a tick = 16.68 ms). When a render-only invocation is due and the loop is
// a whole pass behind the clock, it is left out and the next logic tick runs in its place, as the game's own catch-up does (kind
// INV_BACKLOG: clocks and steps count the whole tick; the alternation does not move) - but drawn. At 30 fps every invocation is a logic
// tick, as in the game without the mod; below that the game's own frame skip takes over. Game speed is right at every frame rate.
static bool   g_adaptive = true;            // [Game] Fps60Pacing: 0 adaptive, 1 fixed (the mod's alternation; the game slows down below 60 fps)
static double g_lag = 0;                    // passes the loop is behind the wall clock
static bool   g_lag_seeded = false;
static const double PASS_S = 0.5 / 29.97002997;

// ---- more than two passes per logic tick (FFX-2_60fps.exe: 90 / 120 / 240 fps; 2026-11) ----
// The mod ran exactly two invocations per logic tick. Now N of them (N = fps / 30): pass 0 is the logic invocation, passes
// 1 .. N-1 are render-only. N = 2 is the mod as it was, and what the RevoFX module runs. For N > 2 the game's own frame
// limiter is retargeted the way the FFX part's RATE-001 does it: SKS_WinMainLoop waits `divider * K` with K = 1 / 59.94 s
// (a double with one reference in .text, the same code as in FFX.exe); with the divider held at 1, K = 2 / (N * 59.94)
// runs the loop at N * 29.97 Hz. K goes back to the game's value whenever the mod's pacing is not applied.
// What follows the passes: the camera (X2-SMOOTH-001) and the actor roots (X2-SMOOTH-002) are drawn (pass + 1) / N of the
// way from tick k-1 to k; the limbs (X2-SMOOTH-003) keep their two poses per tick, the second from the middle pass on;
// rows that hold or replay something on a render-only invocation do so on every one of them. The adaptive pacing is for
// N = 2 only: above 60 fps a PC that does not hold the rate runs the game slow (as in the FFX part).
static int      g_period = 2;               // passes per logic tick in force
static int      g_period_want = 2;          // asked for (pacing_rate); taken over when a tick starts
static int      g_pass_cur = 0;             // the pass this invocation runs (0 = logic)
static double*  g_limit_k = nullptr;        // the limiter's period constant
static double   g_limit_stock = 0;
static bool     g_limit_tried = false, g_limit_applied = false;
static int      g_limit_n = 2;
static bool limiter_find() {
    if (g_limit_tried) return g_limit_k != nullptr;
    g_limit_tried = true;
    uint8_t* at = sig_find("frame limiter period", "DD 9D ?? ?? ?? ?? DD 85 ?? ?? ?? ?? DC 0D ?? ?? ?? ?? D9 C1 DC 25 ?? ?? ?? ?? D8 E1 D8 D1 DF E0 F6 C4 41", 0xABEBEC);
    if (!at) return false;
    double* k = (double*)abs_operand("limiter period", at + 0x0E);
    double v = 0; DWORD old;
    if (!k || !safe_copy(&v, k, sizeof(v)) || !(fabs(1.0 / v - 59.94005994) < 0.01)) { log_line("  limiter constant is %g, not 1 / 59.94 - rates above 60 fps refused", v); return false; }
    if (!VirtualProtect(k, sizeof(double), PAGE_READWRITE, &old)) { log_line("  limiter constant not writable - rates above 60 fps refused"); return false; }
    g_limit_k = k; g_limit_stock = v;
    log_line("  limiter period gva=0x%08X = %.9f s (59.94 Hz)", rt_to_gva(k), v);
    return true;
}
// game thread, between invocations (the limiter runs on the same thread): K follows the pacing state
static void limiter(bool want) {
    bool on = want && g_period > 2 && g_limit_k;
    if (on == g_limit_applied && (!on || g_period == g_limit_n)) return;
    if (!g_limit_k) return;
    *g_limit_k = on ? g_limit_stock * 2.0 / g_period : g_limit_stock;
    g_limit_applied = on; g_limit_n = g_period;
    log_line("pacing: host limiter at %.2f Hz", 1.0 / *g_limit_k);
}
int   pacing_pass() { return g_pass_cur; }
int   pacing_period() { return g_period; }
float pacing_t() { int n = g_period > 0 ? g_period : 2, i = g_pass_cur; if (i < 0 || i >= n) i = 0; return (float)(i + 1) / (float)n; }
int   pacing_limb_pass() { return g_period / 2 > 0 ? g_period / 2 : 1; }      // the pass from which the limbs draw tick k
bool  pacing_rate(int fps) {
    int n = fps / 30; if (n < 2) n = 2; if (n > 8) n = 8;
    if (n > 2 && !limiter_find()) { log_line("pacing: %d fps refused (the frame limiter was not found) - 60 fps", n * 30); g_period_want = 2; return false; }
    if (n != g_period_want) log_line("pacing: %d fps asked for (%d invocations per logic tick)", n * 30, n);
    g_period_want = n;
    return true;
}
static const char* g_holder = "";
static bool   g_left_out = false;           // this invocation is a logic tick drawn in place of a render-only invocation
bool pacing_left_out() { return g_left_out; }
void pacing_adaptive(bool on) {
    if (g_adaptive == on) return;
    g_adaptive = on; g_lag = 0; g_lag_seeded = false;
    log_line("pacing: %s", on ? "adaptive (render-only invocations left out when the PC is below 60 fps)" : "fixed (logic on every second invocation)");
}

static uint32_t g_game_divider = 0;        // last divider value the GAME asked for
static bool     g_game_divider_known = false;
static bool     g_was_60hz = false;
static int      g_stock_holds = 0;          // rows currently holding stock pacing
static int      g_pass = 0;                  // the pass the next paced invocation runs: 0 = LOGIC
static int      g_inv_kind = INV_STOCK;
static uint64_t g_kind_counts[4] = { 0, 0, 0, 0 };
static uint64_t g_inv_count = 0;
static int      g_log_lines_left = 200;

#define PACING_LOG(...) do { if (g_cfg.log_pacing_events && g_log_lines_left > 0) { g_log_lines_left--; log_line(__VA_ARGS__); } } while (0)

static void set_exec(int v) {
    if (g_cfg.exec_method == 1 || !g_set_exec) *g_exec_byte = (int8_t)v;
    else g_set_exec(v);
}

static void set_divider(uint32_t v) {
    if (g_set_divider) g_set_divider(v);   // trampoline once hooked: bypasses our hook
}

int pacing_kind() { return g_inv_kind; }

void pacing_hold_stock(const char* who, bool hold) {
    g_stock_holds += hold ? 1 : -1;
    if (g_stock_holds < 0) g_stock_holds = 0;
    if (hold) g_holder = who; else if (!g_stock_holds) g_holder = "";
    PACING_LOG("pacing: %s %s stock pacing (holders now %d)", who, hold ? "holds" : "releases", g_stock_holds);
}

// Give the game its stock pacing back now (divider, exec 1), e.g. when it enters load mode at the end of an
// invocation: no Sg_MainLoop runs during a load, so pacing_pre cannot do it. The next invocation re-enters 60 Hz.
void pacing_leave_now(const char* why) {
    if (!g_was_60hz) return;
    set_divider(g_game_divider ? g_game_divider : 2);
    set_exec(1);
    g_pass = 0;
    g_was_60hz = false;
    PACING_LOG("pacing: stock now for %s (divider -> %u, exec -> 1) at inv %llu", why, g_game_divider, g_inv_count);
}

static bool g_turbo_on = false;
void pacing_turbo(bool on) { g_turbo_on = on; }

// X2-DIAG-CAPTURE-PHASEFLIP: one repeated kind (two logic or two render-only invocations in a row) moves the logic /
// render-only alternation against the presented-frame count, to tell which one a blink follows.
static volatile LONG g_flip_requested = 0;
void pacing_flip_phase() { InterlockedExchange(&g_flip_requested, 1); }
// X2-DIAG-CAPTURE-BACKLOG: one no-draw logic invocation (backlog count 2, as the game's own catch-up) without moving
// the logic / render-only alternation (capture 19:10: the blink's sign changed at a hitch with a BACKLOG invocation,
// never at a forced repeat). A magic DLL that runs its effect from Mag_Draw does not step on it.
static volatile LONG g_backlog_requested = 0;
void pacing_inject_backlog() { InterlockedExchange(&g_backlog_requested, 1); }

void pacing_set(bool on) {
    if ((g_cfg.pacing != 0) == on) return;
    g_cfg.pacing = on ? 1 : 0;
    log_line("pacing: %s at runtime", on ? "60 Hz" : "stock 30 Hz");
}

// X2-PACE-MENUDIV2 ([rows], default on under Wine / Proton, off on Windows): while a row holds stock pacing (PS2
// menus), keep the game at divider 2 when it asks for 1. The battle results screen asks for 60 fps; on the Steam Deck
// (Proton) it then takes 6-10 s (stock ~4 s; PC near-instant): it ends when background work finishes, and that work
// needs main-thread idle time between frames (D 18/10 LOADWAITS: 8.5 ms per frame in the command-ring wait). At divider
// 2 it takes ~1 s, but alone its animation plays at half speed (the menu steps once per host frame). Skipping every second HD render
// or present at divider 1 did not help (8.3 s / 15 s): the frame loop rate itself is what matters. re/ledger/LOAD-DECK.md.
// Full-speed animation at 30 fps (U 18/10: works, ~1 s and normal animation): the menu steps in Menu_CoroutineIterate 0x7645E0 only when
// Sg_GetTurboFactor() <= the host-frame counter [0xDF6414], which the runner resets to 0 after each Sg_MainLoop. Each
// host frame starts with backlog count 2 (Sg_MainLoop: count >= 2 -> skip flags, count--: a no-draw invocation, then a
// drawn one) and the counter is set to 1 before the second, so the menu steps twice per rendered frame. The pad is
// latched per invocation, so one press is seen once.
static int g_opt_menudiv2 = -1;
static bool g_menu_kept_at_30 = false;                    // the game asked for divider 1 under a hold and we kept 2
static volatile int32_t* g_host_frame_counter = nullptr;    // [0xDF6414], from the runner (call site + 0xD: MOV ECX,[ctr])
// Config page "Fast Results" (X2-CFG-MENU): the default, and the runtime switch (the next divider request follows it).
int  pacing_menudiv2_default() { return on_wine() ? 1 : 0; }
bool pacing_menudiv2_get() {
    if (g_opt_menudiv2 < 0) g_opt_menudiv2 = config_row("X2-PACE-MENUDIV2", pacing_menudiv2_default());
    return g_opt_menudiv2 != 0;
}
void pacing_menudiv2_set(bool on) { g_opt_menudiv2 = on ? 1 : 0; }
bool pacing_divider_request(uint32_t req) {
    uint32_t prev = g_game_divider;
    if (req != 1) g_menu_kept_at_30 = false;
    if (pacing_menudiv2_get() && g_stock_holds > 0 && req == 1) {         // g_game_divider stays 2, so the stock path keeps divider 2
        g_menu_kept_at_30 = true;
        PACING_LOG("pacing: game asks divider 1 (wanted %u) under a stock hold -> swallowed, kept at 2 (X2-PACE-MENUDIV2)", prev);
        return true;
    }
    g_game_divider = req;
    g_game_divider_known = true;
    bool swallow = g_cfg.pacing && g_stock_holds == 0 && req >= 2;
    if (req != prev) PACING_LOG("pacing: game asks divider %u (was %u) -> %s", req, prev, swallow ? "swallowed, held at 1" : "passed");
    return swallow;
}

void pacing_pre() {
    g_inv_count++;
    double dt = f60_clock();                               // wall time since the previous invocation
    bool drawn = *g_backlog_count <= 1;                    // the game's own catch-up invocations draw nothing
    if (!g_game_divider_known && g_divider_hooked) {       // the game set its divider before our hook existed
        g_game_divider = *g_divider;
        g_game_divider_known = true;
        log_line("pacing: initial game divider %u", g_game_divider);
    }

    bool want_active = g_cfg.pacing && g_divider_hooked && g_stock_holds == 0 && g_game_divider_known && g_game_divider >= 2;
    if (g_period_want != g_period && (g_pass == 0 || !want_active)) {      // a new rate starts with a logic tick
        log_line("pacing: %d -> %d invocations per logic tick (%d fps)", g_period, g_period_want, g_period_want * 30);
        g_period = g_period_want; g_pass = 0;
    }
    limiter(want_active);
    if (!want_active) {
        if (g_opt_menudiv2 && g_menu_kept_at_30 && g_stock_holds > 0) {
            if (!g_host_frame_counter && g_mainloop_callsite && sig_match(g_mainloop_callsite + 0xD, "8B 0D"))
                g_host_frame_counter = (volatile int32_t*)abs_operand("host frame counter", g_mainloop_callsite + 0xF);
            if (*g_backlog_count == 0) *g_backlog_count = 2;
            else if (*g_backlog_count == 1 && g_host_frame_counter && *g_host_frame_counter < 1) *g_host_frame_counter = 1;
        }
        if (g_was_60hz) {                          // leaving 60 Hz: give the game back its pacing
            set_divider(g_game_divider ? g_game_divider : 2);
            set_exec(1);
            g_pass = 0;
            PACING_LOG("pacing: stock (divider -> %u, exec -> 1) at inv %llu", g_game_divider, g_inv_count);
        }
        g_was_60hz = false;
        g_inv_kind = INV_STOCK;
        g_pass_cur = 0;
        g_kind_counts[INV_STOCK]++;
        g_lag = 0; g_lag_seeded = false; g_left_out = false;
        f60_state(0, g_stock_holds > 0 ? g_holder : !g_cfg.pacing ? "target 30 fps" : (g_game_divider_known && g_game_divider < 2) ? "the game (menu: every refresh)" : "", (int)g_game_divider);
        f60_lag(0);
        f60_frame(INV_STOCK, drawn, 0);
        return;
    }

    if (!g_was_60hz) PACING_LOG("pacing: 60 Hz on at inv %llu (game divider %u, frame counter %u)", g_inv_count, g_game_divider,
                            g_frame_counter ? *g_frame_counter : 0);
    if (*g_divider != 1) {
        if (g_was_60hz) PACING_LOG("pacing: divider was %u at pre of inv %llu - re-asserted 1", *g_divider, g_inv_count);
        set_divider(1);
    }

    int target;
    if (InterlockedExchange(&g_backlog_requested, 0) && *g_backlog_count == 0) *g_backlog_count = 2;
    bool leave_out = false;
    bool adaptive = g_adaptive && !g_turbo_on && g_period == 2;
    if (!g_lag_seeded || !adaptive) { g_lag = 0; g_lag_seeded = true; }
    else {
        g_lag += dt / PASS_S;
        if (g_lag > 4.0) g_lag = 4.0;                // a hitch: the game's own frame skip covers frames of 33 ms and more
        leave_out = *g_backlog_count <= 1 && g_pass != 0 && g_lag >= 2.0;   // a render-only invocation is due and the loop is a whole pass behind
    }
    if (*g_backlog_count > 1 || leave_out) {
        target = 1;
        g_pass_cur = 0;
        g_inv_kind = INV_BACKLOG;
        if (adaptive) g_lag -= 2.0;                  // a whole tick
    } else if (g_turbo_on) {                         // X2-PACE-TURBO: count = booster / 2 per host frame, all logic
        target = 1;
        g_pass = 0; g_pass_cur = 0;
        g_inv_kind = INV_LOGIC;
    } else {
        if (adaptive) g_lag -= 1.0;
        if (InterlockedExchange(&g_flip_requested, 0)) g_pass = (g_pass + g_period - 1) % g_period;   // repeat the last kind
        g_pass_cur = g_pass;
        target = g_pass_cur == 0 ? 1 : 0;
        g_pass = (g_pass_cur + 1) % g_period;
        g_inv_kind = target ? INV_LOGIC : INV_RENDER_ONLY;
    }
    if (adaptive && g_lag < 0) { g_lag *= 0.99; if (g_lag < -2.0) g_lag = -2.0; }   // ahead of the clock (after a hitch): fades out, or the next slow stretch would be answered late
    if (*g_exec_byte != target) set_exec(target);
    g_kind_counts[g_inv_kind]++;
    g_was_60hz = true;
    f60_state(1, "", (int)g_game_divider);
    f60_lag((float)g_lag);
    g_left_out = leave_out;
    f60_frame(g_inv_kind, drawn, leave_out);
}

void pacing_post() {
    if ((g_inv_count % 36000) == 0) {                      // ~10 min at 60 Hz
        log_line("pacing stats: inv=%llu LOGIC=%llu BUILD=%llu BACKLOG=%llu STOCK=%llu", g_inv_count,
                 g_kind_counts[INV_LOGIC], g_kind_counts[INV_RENDER_ONLY], g_kind_counts[INV_BACKLOG], g_kind_counts[INV_STOCK]);
    }
}

} // namespace x2
