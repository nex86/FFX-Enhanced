// x2_module.cpp - the FFX-2 part of the 60 fps patch inside the RevoFX module: what the mod's own DLL entry, log file, ini file and
// dev hot reload did (x2_main.cpp, x2_log.cpp, x2_config.cpp, x2_reload.cpp of FFX-60fps) is replaced by the module's start, RevoFX's
// log and RevoFX's settings. Written for the module (not part of FFX-60fps); see ../NOTICE.txt.
#include "x2.h"
#include "../../ffx_fps60.h"
#include <stdarg.h>

namespace x2 {

Config g_cfg;

// ---- log: RevoFX's log (category FPS60) ----
void game_dir(wchar_t* out, size_t cap) { f60_game_dir(out, cap); }
bool hot_copy() { return false; }
void log_open() {}
void log_close() {}
void log_reopen() {}
void log_line(const char* fmt, ...) { char b[1600]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a); f60_log("", b); }

// ---- settings: RevoFX's ([Game] FrameRate / Fps60Pacing; row switches keep their defaults, [Debug] FFXFps60Rows overrides) ----
void config_load() {
    g_cfg.enabled = 1; g_cfg.pacing = f60_opt("FrameRate", 30) >= 60; g_cfg.exec_method = 0; g_cfg.unpack_timeout_s = 60; g_cfg.log_pacing_events = 1;
    pacing_adaptive(!f60_opt("Fps60Pacing", 0));
    pacing_rate(f60_opt("Fps60Rate", 60));      // RevoFX has no such key: 60 (two invocations per tick, as before)
}
int  config_row(const char* row_id, int def) { return f60_row(row_id, def); }
#ifdef F60_EXE                                   // FFX-2_60fps.exe: text settings from FFX_60fps.ini
extern "C" void f60_str(const char* section, const char* key, char* buf, int n, const char* def);
void config_str(const char* s, const char* k, char* out, int size, const char* def) { f60_str(s, k, out, size, def); }
#else
void config_str(const char*, const char*, char* out, int size, const char* def) { if (size > 0) { strncpy(out, def ? def : "", size - 1); out[size - 1] = 0; } }
#endif
bool config_write(const char*, const char*, const char*) { return false; }
bool on_wine() {
    static int w = -1;
    if (w < 0) { HMODULE nt = GetModuleHandleW(L"ntdll.dll"); w = (nt && GetProcAddress(nt, "wine_get_version")) ? 1 : 0; }
    return w != 0;
}

// ---- start (was the init thread of the mod's DLL) ----
static bool probe_ready() {
    __try { return sigs_text_ready(); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool start() {
    config_load();
    log_line("init: FFX-2.exe base=%p, pacing=%d (target %d fps)", exe_base(), g_cfg.pacing, g_cfg.pacing ? 60 : 30);
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
    bool rows = true;
    __try { rows_install_all(); }
    __except (EXCEPTION_EXECUTE_HANDLER) { rows = false; }
    if (!rows) { log_line("FAULT during row install - main loop not hooked"); f60_installed(0, "fault while installing"); return false; }
    if (!hooks_install_mainloop()) { log_line("main loop hook FAILED - nothing installed"); f60_installed(0, "main loop hook failed"); return false; }
    log_line("ready (pacing %s)", g_cfg.pacing ? "60 fps: divider 1 + logic every second invocation, adaptive below 60" : "the game's own");
    f60_installed(1, "");
    return true;
}
void target(int fps) { if (fps >= 60) pacing_rate(fps); pacing_set(fps >= 60); }
void pacing_mode(int fixed) { pacing_adaptive(!fixed); }

} // namespace x2
