// fx_module.cpp - the FFX part of the 60 fps patch inside the RevoFX module: what the mod's own DLL entry, log file, ini file and
// dev tools did (proxy.cpp, log.cpp, config.cpp, reload.cpp, capture.cpp of FFX-60fps) is replaced by the module's start, RevoFX's
// log and RevoFX's settings. Written for the module (not part of FFX-60fps); see ../NOTICE.txt.
#include "fps60.h"
#include "../../ffx_fps60.h"
#include <stdarg.h>
namespace fx {

Config g_cfg;

// ---- log: RevoFX's log (category FPS60) ----
void game_dir(wchar_t* out, size_t cap) { f60_game_dir(out, cap); }
bool hot_copy() { return false; }
void log_open() {}
void log_close() {}
DWORD log_cs_owner() { return 0; }
void log_line(const char* fmt, ...) { char b[1600]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a); f60_log("", b); }
bool log_line_try(const char* fmt, ...) { char b[1600]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a); f60_log("", b); return true; }
static char g_ring[128][240]; static LONG g_ring_n;
void log_ring(const char* fmt, ...) { LONG i = InterlockedIncrement(&g_ring_n) - 1; va_list a; va_start(a, fmt); vsnprintf(g_ring[i & 127], sizeof g_ring[0], fmt, a); va_end(a); }
void log_ring_dump(const char* why) {
    LONG n = g_ring_n; log_line("ring: %ld line(s) kept in memory (%s)", n > 128 ? 128L : n, why);
    for (LONG i = n > 128 ? n - 128 : 0; i < n; i++) f60_log("", g_ring[i & 127]);
    InterlockedExchange(&g_ring_n, 0);
}

// ---- settings: RevoFX's ([Game] FrameRate / Fps60Pacing; row switches keep their defaults, [Debug] FFXFps60Rows overrides) ----
void config_load() {
    Config& c = g_cfg;
    c.enabled = 1; c.pacing = f60_opt("FrameRate", 30) >= 60; c.fps = f60_opt("Fps60Rate", 60); c.rollback = 1; c.unpack_timeout_s = 60; c.log_pacing_events = 1; c.crash_dump_mem = 0;
    c.alt_backlog = ALT_BACKLOG_LOGIC; c.exec_method = EXEC_SETTER;
    pacing_adaptive(!f60_opt("Fps60Pacing", 0));
}
int  config_row(const char* row_id, int def) { return f60_row(row_id, def); }
#ifdef F60_EXE                                   // FFX_60fps.exe: text settings from FFX_60fps.ini (soft_reset, row lists)
extern "C" void f60_str(const char* section, const char* key, char* buf, int n, const char* def);
void config_str(const char* s, const char* k, char* buf, int n, const char* def) { f60_str(s, k, buf, n, def); }
#else
void config_str(const char*, const char*, char* buf, int n, const char* def) { if (n > 0) { strncpy(buf, def ? def : "", n - 1); buf[n - 1] = 0; } }
#endif
bool config_write(const char*, const char*, const char*) { return false; }

// ---- parts of the mod that are not in the module ----
void capture_init() {}
void capture_point(int) {}
void capture_divider_event(uint32_t, bool) {}
void capture_on_fault(const char*, DWORD) {}
void capture_abort(const char*) {}
#ifndef F60_EXE                                  // FFX_60fps.exe has the VSYNC row (fps60exe\rows\VSYNC.cpp)
bool vsync_hook_for_diag() { return false; }   // the swap chain is RevoFX's: nothing of the patch touches Present
void vsync_present_stats(double* ms, uint32_t* n, uint32_t* synced, unsigned* flags, unsigned long* tid) { *ms = 0; *n = 0; *synced = 0; *flags = 0; *tid = 0; }
#endif
void* proxy_real_minidump() { return nullptr; }
HMODULE reload_next_module() { return nullptr; }

// ---- start (was the init thread of the mod's DLL): waits for the Steam DRM to unpack the code, resolves every signature (one required
//      miss = nothing installed), installs the rows and hooks the runner's call of the main loop ----
static bool probe_ready() {
    __try { return sigs_text_ready(); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool start() {
    config_load();
    rate_init();
    log_line("init: exe base=%p size=0x%X, pacing=%d (target %d fps), rollback=%d", exe_base(), exe_size(), g_cfg.pacing, g_cfg.pacing ? 60 : 30, g_cfg.rollback);
    const int budget_ms = g_cfg.unpack_timeout_s * 1000;
    int waited = 0;
    while (!probe_ready()) {
        if (waited >= budget_ms) { log_line("gave up waiting for the unpacked code (%d s) - nothing installed", g_cfg.unpack_timeout_s); f60_installed(0, "the game code did not unpack"); return false; }
        Sleep(250);
        waited += 250;
    }
    log_line("code ready after ~%d ms", waited);
    bool ok = false;
    __try { ok = sigs_resolve_all(); }
    __except (EXCEPTION_EXECUTE_HANDLER) { ok = false; log_line("FAULT during signature resolution"); }
    if (!ok) { log_line("a required signature failed verification - NOTHING INSTALLED (fail-safe)"); f60_installed(0, "another game version: a required signature failed"); return false; }
    if (!g_set_divider_orig) { log_line("SetFlipVSyncInterval (frame divider) not resolved - NOTHING INSTALLED (fail-safe)"); f60_installed(0, "frame divider not found"); return false; }
    hook_init();
    bool rows = true;
    __try { rows_install_all(); }
    __except (EXCEPTION_EXECUTE_HANDLER) { rows = false; }
    if (!rows) { log_line("FAULT during row install - main loop not hooked"); f60_installed(0, "fault while installing"); return false; }
    rollback_report();
    if (!hooks_install_mainloop()) { log_line("main loop hook FAILED - nothing installed"); f60_installed(0, "main loop hook failed"); return false; }
    log_line("ready (pacing %s)", g_cfg.pacing ? "60 fps: divider 1 + logic every second invocation, adaptive below 60" : "the game's own");
    f60_installed(1, "");
    return true;
}
void target(int fps) { pacing_set(fps >= 60); }
void pacing_mode(int fixed) { pacing_adaptive(!fixed); }

} // namespace fx
