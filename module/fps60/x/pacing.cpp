// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// Pacing: hold the frame divider at 1 and run one logic tick every N Sg_MainLoop invocations, so N-1 of every
// N invocations are render-only. N comes from rate.cpp (2 at 60 fps, 4 at 120, 8 at 240) and is 2 unless
// RATE-001 has the host limiter at N * 29.97 Hz, so the game's speed is right at every rate. Game requests for
// 30 Hz are swallowed; requests for every-vsync (menus) and rows holding stock pacing pass through.
// Details: docs/fps60/pacing.md, README.md "Pacing policy"
#include "fps60.h"
#include "../../ffx_fps60.h"
namespace fx {

// ---- adaptive pacing (RevoFX module) ----
// The mod ran one logic tick in N invocations whatever the frame time: a PC that draws 48 fps ran the game at 0.8 x speed, and its
// PERF-002 row answered that by falling back to the game's own 30 fps pacing for 20 .. 640 s (the "drops straight from 60 to 30").
// Now the clock decides: every invocation the wall time since the last one is counted in passes (1 pass = tick / N = 16.68 ms at
// 60 fps). When a render-only pass is due and the loop is a whole pass behind the clock, that pass is left out and the next logic
// tick runs in its place - through the path the game's own catch-up already takes (kind INV_BACKLOG: clocks and steps count the whole
// tick, the period restarts) - but drawn. 60 fps: logic, render, logic, render. 48 fps: every 4th render pass is missing. 30 fps:
// every invocation is a logic tick, as in the game without the mod. Below 30 fps the game's own frame skip takes over (it counts in
// 1/30 s). Game speed is right at every frame rate; the frame rate falls as far as the PC needs and comes back at once.
static bool   g_adaptive = true;        // [Game] Fps60Pacing: 0 adaptive, 1 fixed (the mod's alternation; the game slows down below 60 fps)
static double g_lag = 0;                // passes the loop is behind the wall clock
static bool   g_lag_seeded = false;
static const double TICK_S = 1.0 / 29.97002997;
void pacing_adaptive(bool on) {
    if (g_adaptive == on) return;
    g_adaptive = on; g_lag = 0; g_lag_seeded = false;
    log_line("pacing: %s", on ? "adaptive (render-only passes left out when the PC is below 60 fps)" : "fixed (a logic tick every second invocation)");
}
static const char* g_holder = "";       // the last row that asked for stock pacing (diagnostics)
static bool   g_left_out = false;       // this invocation is a logic tick drawn in place of a render-only pass
bool pacing_left_out() { return g_left_out; }
bool pacing_adaptive_on() { return g_adaptive; }

static uint32_t g_wanted = 0;        // last divider value the GAME asked for
static bool     g_wanted_known = false;
static int      g_hold = 0;          // number of rows currently holding stock pacing
static int      g_force_div = 0;     // > 0: a logic tick every g_force_div invocations (game speed booster)
static bool     g_active = false;    // alternation applied this invocation
static bool     g_was_active = false;
static int      g_pass = 0;          // the pass the NEXT paced invocation runs
static int      g_pass_cur = 0;      // the pass THIS invocation is running: 0 = the logic tick
static int      g_period = 2;        // invocations per logic tick in force this invocation
static uint32_t g_gen = 0;           // rate.cpp generation the period was taken from
static int      g_kind = INV_STOCK;
static uint64_t g_count[4] = { 0, 0, 0, 0 };
static uint64_t g_inv = 0;
static int      g_log_budget = 200;  // rate limit for pacing event lines

#define PLOG(...) do { if (g_cfg.log_pacing_events && g_log_budget > 0) { g_log_budget--; log_line(__VA_ARGS__); } } while (0)

static void set_exec(int v) {
    if (g_cfg.exec_method == EXEC_BYTE) *g_exec_byte = (int8_t)v;
    else g_set_exec(v);
}

static void set_divider(uint32_t v) {
    if (g_set_divider_orig) g_set_divider_orig(v);   // trampoline once hooked: bypasses our hook
}

bool pacing_active() { return g_active; }
int  pacing_kind() { return g_kind; }
int  pacing_pass() { return g_pass_cur; }

// Where in the logic tick this invocation draws: pass 0 (the logic tick) draws 1/period of the way from the
// previous tick to this one, the last pass draws tick k itself. At N = 2 this is the old 0.5 / 1.0 pair.
// This is the pass being run now, not the one queued next - reading the queued one made the drawn fraction
// run 0.5, 0.75, 1.0, 0.25 instead of 0.25, 0.5, 0.75, 1.0, so every period ended by jumping backwards.
float pacing_t() {
    int p = g_period > 0 ? g_period : 1;
    int i = g_pass_cur;
    if (i < 0 || i >= p) i = 0;
    return (float)(i + 1) / (float)p;
}

void pacing_hold_stock(const char* who, bool hold) {
    g_hold += hold ? 1 : -1;
    if (g_hold < 0) g_hold = 0;
    if (hold) g_holder = who; else if (!g_hold) g_holder = "";
    PLOG("pacing: %s %s stock pacing (holders now %d)", who, hold ? "holds" : "releases", g_hold);
}

// The game speed booster asks for a logic tick every `every` invocations instead of every N, so its 2x / 4x
// stay 2x / 4x at every frame rate (BOOST-001). 0 = off.
void pacing_force_div(int every) { g_force_div = every > 0 ? every : 0; }

// Runtime 60/30 switch (CFG-MENU, game thread). pacing_pre picks it up at the next invocation: leaving the mod's
// pacing hands the game its divider back, entering re-asserts divider 1 (the same transitions as a stock holder).
void pacing_set(bool on) {
    if ((g_cfg.pacing != 0) == on) return;
    g_cfg.pacing = on ? 1 : 0;
    log_line("pacing: %s at runtime", on ? "mod pacing" : "stock 30");
}

bool pacing_divider_request(uint32_t req) {
    uint32_t prev = g_wanted;
    g_wanted = req;
    g_wanted_known = true;
    bool swallow = g_cfg.pacing && g_hold == 0 && req >= 2;
    if (req != prev) PLOG("pacing: game asks divider %u (was %u) -> %s", req, prev, swallow ? "swallowed, held at 1" : "passed");
    return swallow;
}

void pacing_pre() {
    g_inv++;
    double dt = f60_clock();                         // wall time since the previous invocation
    bool drawn = *g_backlog_count <= 1;              // the game's own catch-up invocations draw nothing
    if (!g_wanted_known && g_divider_hooked) {       // the game set its divider before our hook existed
        g_wanted = *g_divider;
        g_wanted_known = true;
        log_line("pacing: initial game divider %u", g_wanted);
    }

    bool want_active = g_cfg.pacing && g_divider_hooked && g_hold == 0 && g_wanted_known && g_wanted >= 2;

    // The host limiter follows the pacing state, so the loop never runs at the mod's rate with stock pacing.
    rate001_limiter(want_active);

    if (!want_active) {
        if (g_was_active) {                          // leaving the mod's pacing: give the game back its pacing
            set_divider(g_wanted ? g_wanted : 2);
            set_exec(1);
            g_pass = 0;
            PLOG("pacing: stock (divider -> %u, exec -> 1) at inv %llu", g_wanted, g_inv);
        }
        g_active = g_was_active = false;
        g_kind = INV_STOCK;
        g_count[INV_STOCK]++;
        g_lag = 0; g_lag_seeded = false; g_left_out = false;
        f60_state(0, g_hold > 0 ? g_holder : !g_cfg.pacing ? "target 30 fps" : (g_wanted_known && g_wanted < 2) ? "the game (menu: every refresh)" : "", (int)g_wanted);
        f60_lag(0);
        f60_frame(INV_STOCK, drawn, 0);
        return;
    }

    // The logic period: N from rate.cpp, or the booster's shorter one. A rate change restarts it, so a period
    // is never half counted at the old length.
    int n = rate_mult();
    int period = g_force_div > 0 ? g_force_div : n;
    if (period < 1) period = 1;
    if (period != g_period || rate_gen() != g_gen) {
        if (g_was_active && period != g_period)
            PLOG("pacing: logic period %d -> %d invocations at inv %llu", g_period, period, g_inv);
        g_period = period;
        g_gen = rate_gen();
        g_pass = 0;
    }

    if (!g_was_active) PLOG("pacing: on at inv %llu (%d fps, 1 logic tick in %d; game divider %u, frame counter %u)",
                            g_inv, n * 30, g_period, g_wanted, *g_frame_counter);
    if (*g_divider != 1) {
        if (g_was_active) PLOG("pacing: divider was %u at pre of inv %llu - re-asserted 1", *g_divider, g_inv);
        set_divider(1);
    }

#ifdef FPS60_DEV
    // Dev hotkey Num 1: shift the logic/render-only pattern by one invocation (U 12/10/2026: Echuilles light-ray
    // flicker and boss ghost come and go between sessions; test whether they follow the pattern's phase against a
    // per-invocation toggle in the game).
    static bool prev1 = false;
    if (dev_key(VK_NUMPAD1, &prev1)) {
        g_pass = (g_pass + 1) % g_period;
        log_line("Num1: pacing phase shifted by one invocation at inv %llu (next pass %d of %d, frame counter %u)",
                 g_inv, g_pass, g_period, *g_frame_counter);
    }
#endif
    bool backlog = *g_backlog_count > 1;
    int target;
    // Adaptive pacing: time accrued since the last invocation, in passes; what this invocation advances is taken off below.
    bool leave_out = false;
    bool adaptive = g_adaptive && g_force_div == 0 && g_period == 2;
    if (!g_lag_seeded || !adaptive) { g_lag = 0; g_lag_seeded = true; }
    else {
        g_lag += dt / (TICK_S / g_period);
        if (g_lag > 4.0) g_lag = 4.0;                // a hitch: the game's own frame skip covers frames of 33 ms and more
        leave_out = !backlog && g_pass != 0 && g_lag >= 2.0;   // a render-only pass is due and the loop is a whole pass behind
    }
    if ((backlog && g_cfg.alt_backlog == ALT_BACKLOG_LOGIC) || leave_out) {
        target = 1;                                  // catch-up tick: the period restarts from it
        g_kind = INV_BACKLOG;
        g_pass_cur = 0;
        g_pass = 1 % g_period;
        if (adaptive) g_lag -= g_period;             // a whole tick
    } else {
        if (adaptive) g_lag -= 1.0;
        g_pass_cur = g_pass;
        target = g_pass_cur == 0 ? 1 : 0;
        g_kind = target ? (g_force_div > 0 ? INV_BACKLOG : INV_LOGIC) : INV_BUILD;
        g_pass = (g_pass_cur + 1) % g_period;
    }
    if (adaptive && g_lag < 0) { g_lag *= 0.99; if (g_lag < -2.0) g_lag = -2.0; }   // ahead of the clock (after a hitch): fades out, or the next slow stretch would be answered late
    if (*g_exec_byte != target) set_exec(target);
    g_count[g_kind]++;
    g_active = g_was_active = true;
    f60_state(1, "", (int)g_wanted);
    f60_lag((float)g_lag);
    g_left_out = leave_out;
    f60_frame(g_kind, drawn, leave_out);
}

// Dev hot reload: the game gets its own divider, exec and frame limiter back before this copy goes passive.
void pacing_shutdown() {
    rate001_limiter(false);
    if (g_was_active) {
        set_divider(g_wanted ? g_wanted : 2);
        set_exec(1);
        log_line("pacing: shutdown, divider -> %u, exec -> 1", g_wanted ? g_wanted : 2);
    }
    g_active = g_was_active = false;
    g_kind = INV_STOCK;
}

void pacing_post() {
    if ((g_inv % (600 * (uint64_t)(rate_mult() * 30))) == 0) {       // ~10 min of drawn frames
        log_line("pacing stats: inv=%llu LOGIC=%llu BUILD=%llu BACKLOG=%llu STOCK=%llu (%d fps, 1 in %d)", g_inv,
                 g_count[INV_LOGIC], g_count[INV_BUILD], g_count[INV_BACKLOG], g_count[INV_STOCK],
                 rate_mult() * 30, g_period);
    }
}
} // namespace fx
