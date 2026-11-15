// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-DIAG-PPPRATE (diagnostic, observe only): which particle effects still run at double speed, and who steps them.
//
// A particle program's clock (+8) advances once per step unless the step runs paused. At 60 fps that should happen on
// logic invocations only; a program whose clock also advances on render-only invocations runs 2x. X2-MAG-006 calls
// back after every particle step (spell DLL, game and field programs) with the clock before and after. The steps are
// counted per call site (the step's return address): logic steps, render-only steps, and how many of each advanced
// the clock. Every 10 s each call site that advanced on render-only invocations is logged as "2x", with its module
// (magic_NNNN.dll + offset, or an FFX-2.exe address), so one play session names every effect that still runs fast.
#include "../x2.h"
#include <stdio.h>
#include <string.h>

namespace x2 {

extern Row row_DIAGPPPRATE;

enum { MAX_SITES = 96 };
struct StepSite {
    const void* ret;            // return address of the step call
    int         cls;            // 1 spell DLL, 2 game (FFX-2.exe), 3 field step
    char        where[48];      // "magic_0866.dll+0x1234" or "FFX-2.exe 0x006A5FD7"
    LONG        logic_steps, logic_advanced, render_steps, render_advanced;   // this 10 s window
    LONG        total_render_advanced;
    const void* last_prog;
    LONG        programs;       // program changes seen in this window (a rough count of programs)
};
static StepSite g_sites[MAX_SITES];
static int      g_site_count = 0;
static LONG     g_sites_full = 0;

// Counts one step into its site. Pure bookkeeping (tested without the game).
static void count_step(StepSite& s, int invocation_kind, const void* prog, int32_t clock_before, int32_t clock_after) {
    bool advanced = clock_after != clock_before;
    if (invocation_kind == INV_RENDER_ONLY) {
        s.render_steps++;
        if (advanced) { s.render_advanced++; s.total_render_advanced++; }
    } else {
        s.logic_steps++;
        if (advanced) s.logic_advanced++;
    }
    if (prog != s.last_prog) { s.last_prog = prog; s.programs++; }
}

// A site runs 2x when its clocks advance on render-only invocations about as often as on logic ones.
static bool site_runs_double(const StepSite& s) {
    return s.render_advanced > 0 && s.render_advanced * 4 >= s.logic_advanced;
}

static void describe(const void* ret, int cls, char* out, size_t cap) {
    if (in_image(ret)) { _snprintf_s(out, cap, _TRUNCATE, "FFX-2.exe 0x%08X", rt_to_gva(ret)); return; }
    HMODULE mod = nullptr;
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCSTR)ret, &mod) && mod) {
        char path[MAX_PATH] = {};
        GetModuleFileNameA(mod, path, MAX_PATH);
        const char* name = strrchr(path, '\\');
        _snprintf_s(out, cap, _TRUNCATE, "%s+0x%X", name ? name + 1 : path, (unsigned)((const uint8_t*)ret - (const uint8_t*)mod));
        return;
    }
    _snprintf_s(out, cap, _TRUNCATE, "%s %p", cls == 1 ? "dll?" : "?", ret);
}

static StepSite* find_site(const void* ret, int cls) {
    static int last = -1;
    if (last >= 0 && last < g_site_count && g_sites[last].ret == ret) return &g_sites[last];
    for (int i = 0; i < g_site_count; i++)
        if (g_sites[i].ret == ret) { last = i; return &g_sites[i]; }
    if (g_site_count >= MAX_SITES) { g_sites_full++; return nullptr; }
    StepSite& s = g_sites[g_site_count];
    memset(&s, 0, sizeof s);
    s.ret = ret;
    s.cls = cls;
    describe(ret, cls, s.where, sizeof s.where);
    last = g_site_count++;
    return &s;
}

// Game thread, after every particle step.
static void rate_note(void* prog, int cls, const void* ret, int32_t clock_before, int32_t clock_after) {
    if (!row_DIAGPPPRATE.on) return;
    int kind = pacing_kind();
    if (kind != INV_LOGIC && kind != INV_RENDER_ONLY) return;     // stock / backlog steps say nothing about 60 Hz
    StepSite* s = find_site(ret, cls);
    if (s) count_step(*s, kind, prog, clock_before, clock_after);
}

static bool install() {
    if (!mag006_entry()) {
        log_line("  X2-DIAG-PPPRATE: needs X2-MAG-006 (its step hooks) - off");
        return false;
    }
    mag006_set_rate_note(&rate_note);
    return true;
}

static void post() {
    static DWORD last = 0;
    DWORD now = GetTickCount();
    if (now - last < 10000) return;
    last = now;
    static const char* const k_cls[4] = { "?", "spell", "game", "field" };
    int reported = 0;
    for (int i = 0; i < g_site_count; i++) {
        StepSite& s = g_sites[i];
        if (s.logic_steps + s.render_steps == 0) continue;
        if (site_runs_double(s)) {
            log_line("X2-DIAG-PPPRATE: 2x %s step at %s: clock advanced on render-only %ld/%ld, on logic %ld/%ld (programs ~%ld)",
                     k_cls[s.cls & 3], s.where, s.render_advanced, s.render_steps, s.logic_advanced, s.logic_steps, s.programs);
            reported++;
        }
        s.logic_steps = s.logic_advanced = s.render_steps = s.render_advanced = 0;
        s.programs = 0;
        s.last_prog = nullptr;
    }
    static int quiet_logged = 0;
    if (!reported && g_site_count && !quiet_logged) {
        quiet_logged = 1;
        log_line("X2-DIAG-PPPRATE: no particle step advanced on render-only invocations in the last 10 s (%d call sites seen)",
                 g_site_count);
    }
    if (reported) quiet_logged = 0;
    if (g_sites_full == 1) log_line("X2-DIAG-PPPRATE: more than %d call sites, the rest not counted", MAX_SITES);
}

Row row_DIAGPPPRATE = {
    "X2-DIAG-PPPRATE",
    "particle steps that advance on render-only invocations (2x), per call site, every 10 s (observe only)",
    1, install, nullptr, post, 0
};

} // namespace x2
