// f60_host.cpp - host of the 60 fps patch inside FFX_enh.exe / FFX-2_enh.exe (no RevoFX, no DLL), or with -DF60_DLL inside the
// optional dbghelp.dll: start, settings (ffx_enh.ini, the settings file of FFX Enhanced; FFX_60fps.ini before),
// log (FFX_enh.log / FFX-2_enh.log). It takes the place of module\ffx_fps60.cpp; the patch code itself is module\fps60 (the
// FFX-60fps mod, Apache-2.0, with the adaptive pacing of the RevoFX module; module\fps60\NOTICE.txt). docs\FPS60_EXE.md.
#include <windows.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include <wctype.h>
#include "../module/ffx_fps60.h"
#include "../module/fps60/f60_compat.h"

namespace fx { bool start(); void target(int fps); bool rate_set_fps(int fps); }
namespace x2 { bool start(); void target(int fps); }
extern "C" int f60_escmenu_install(int game);   // f60_escmenu.cpp: the Frame Rate row in the game's Esc menu

static F60Stats g_st; static const char *g_name = "FFX";
static char g_dir[MAX_PATH], g_ini[MAX_PATH + 32], g_logp[MAX_PATH + 32];
static int g_fps = 60, g_pacing = 0, g_verbose = 0, g_hotkeys = 0, g_cur = 60;   // g_cur: the rate in force (the Esc menu changes it)
static LARGE_INTEGER g_qf; static CRITICAL_SECTION g_cs; static FILE *g_log;

static void logf(const char *fmt, ...) {
    if (!g_log) return;
    char b[1800]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a);
    SYSTEMTIME t; GetLocalTime(&t);
    EnterCriticalSection(&g_cs);
    fprintf(g_log, "%02d:%02d:%02d.%03d %s\n", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, b); fflush(g_log);
    LeaveCriticalSection(&g_cs);
}
extern "C" void f60_log(const char *game, const char *text) {
    static const char *const periodic[] = { "in the last 600 invocations", "render-only OPP calls", "peak draw packets", "PPP pool", "held walks so far", "pacing stats:", "meshes extrapolated",
        "calls LOGIC", "per invocation kind", "last 600 drawn frames", "field particle steps paused", "LOOPDEFER: deferred", "models re-added so far", "logic windows=", "walks=", "rhcut=", 0 };
    if (!g_verbose) for (int i = 0; periodic[i]; i++) if (strstr(text, periodic[i])) return;
    logf("[%s] %s", game && *game ? game : g_name, text);
}
extern "C" int f60_opt(const char *key, int def) {
    if (!strcmp(key, "FrameRate")) return g_fps;
    if (!strcmp(key, "Fps60Rate")) return g_fps >= 60 ? g_fps : 60;
    if (!strcmp(key, "Fps60Pacing")) return g_pacing;
    return def;
}
// text settings: [fps60] soft_reset (both games; the mods' sections were [fps60] and [x2]) and the [rows] lists
extern "C" void f60_str(const char *section, const char *key, char *buf, int n, const char *def) {
    if (n <= 0) return;
    GetPrivateProfileStringA(!strcmp(section, "x2") ? "fps60" : section, key, def ? def : "", buf, (DWORD)n, g_ini);
}
extern "C" void f60_game_dir(wchar_t *out, size_t cap) { if (!cap) return; out[0] = 0; MultiByteToWideChar(CP_ACP, 0, g_dir, -1, out, (int)cap); out[cap - 1] = 0; }
// Row switches: the defaults of the RevoFX module (diagnostic, test, lab and gameplay-option rows off); [rows] ROW=value in the ini overrides.
extern "C" int f60_row(const char *id, int def) {
    int v = (int)GetPrivateProfileIntA("rows", id, -12345, g_ini); if (v != -12345) return v;
    const char *s = !strncmp(id, "X2-", 3) ? id + 3 : id;
    static const char *const rows[] = { "DIAG-FRAMETIME", "DIAG-BTLSIG", "DIAG-DRAWSTEP", "DIAG-DYNGEO", "DIAG-FURY", "DIAG-PAUSEWATCH", "DIAG-TEXTDUMP", "DEV-DMG", "DEV-STOCK", "DEV-TURBO",
        "QOL-AP", "QOL-BTLMENU", "QOL-FURY", "QOL-RETRY", "QOL-SAVEICON", "QOL-SENSOR", "LAB-001", "SR-003", "SAVE-001", "SAVE-002",
        "DIAG-PPPRATE", "DIAG-RATE", "DIAG-PPPOPS", "DIAG-FTBLINK", "DIAG-OP114", "DIAG-FLASH", "DIAG-PSET", "DIAG-HIT", "DIAG-FOCUS", "DIAG-PPPSLOT", "DIAG-PERF",
        "DEV-IMMORTAL", "DEV-TOPMOST", "DEV-EFFTEST", "DEV-BISECT", 0 };
    for (int i = 0; rows[i]; i++) if (!strcmp(s, rows[i])) return 0;
    return def;
}
#undef GetAsyncKeyState
extern "C" short f60_key(int vk) { return g_hotkeys ? GetAsyncKeyState(vk) : 0; }

extern "C" double f60_clock(void) {
    static LARGE_INTEGER last; LARGE_INTEGER t; QueryPerformanceCounter(&t);
    double d = last.QuadPart ? (double)(t.QuadPart - last.QuadPart) / (double)g_qf.QuadPart : 0; last = t; return d;
}
extern "C" double f60_frame(int kind, int drawn, int dropped) {
    static LARGE_INTEGER last, win, rep; static unsigned wf, wt, wd; LARGE_INTEGER t; QueryPerformanceCounter(&t);
    double d = last.QuadPart ? (double)(t.QuadPart - last.QuadPart) / (double)g_qf.QuadPart : 0; last = t;
    F60Stats &s = g_st; s.inv++; if (kind >= 0 && kind < 4) s.n[kind]++; if (dropped) { s.drops++; wd++; }
    if (kind != F60_KIND_RENDER) wt++;
    if (drawn) wf++;
    if (!win.QuadPart) win = rep = t;
    double w = (double)(t.QuadPart - win.QuadPart) / (double)g_qf.QuadPart;
    if (w >= 1.0) { s.fps = (float)(wf / w); s.tps = (float)(wt / w); s.dps = (float)(wd / w); wf = wt = wd = 0; win = t;
        if (g_verbose && (double)(t.QuadPart - rep.QuadPart) / (double)g_qf.QuadPart >= 5.0) { rep = t;
            logf("[%s] %s%s%s: drawn %.1f fps, logic %.1f ticks/s, left out %.1f passes/s (%llu), lag %.2f", g_name, s.mode ? "patch pacing" : "game pacing",
                 s.hold[0] ? " held by " : "", s.hold, s.fps, s.tps, s.dps, s.drops, s.lag); } }
    return d;
}
extern "C" void f60_state(int mode, const char *hold, int divider) {
    F60Stats &s = g_st; bool ch = s.mode != mode || strncmp(s.hold, hold ? hold : "", sizeof s.hold - 1) != 0;
    s.mode = mode; s.divider = divider; snprintf(s.hold, sizeof s.hold, "%s", hold ? hold : "");
    if (ch) logf("[%s] timing: %s%s%s (the game asks for divider %d)", g_name, mode ? "patch pacing" : divider == 1 ? "the game's own pacing, its menus run every refresh" : "the game's own pacing, 30 fps limit",
                 s.hold[0] && divider != 1 ? " - held by " : "", divider != 1 ? s.hold : "", divider);
}
extern "C" void f60_lag(float lag) { g_st.lag = lag; }
extern "C" void f60_fmv(int playing, int) { if (playing != g_st.fmv) { g_st.fmv = playing; logf("[%s] movie %s", g_name, playing ? "opened" : "over"); } }
extern "C" void f60_installed(int ok, const char *why) { g_st.state = ok ? 2 : 3; logf("[%s] 60 fps patch %s%s%s", g_name, ok ? "installed" : "NOT installed", why && *why ? ": " : "", why ? why : ""); }

// ---- the Esc menu's Frame Rate row (f60_escmenu.cpp) ----
extern "C" void f60_menu_log(const char *text) { logf("[%s] %s", g_name, text); }
extern "C" int f60_menu_fps(void) { return g_cur; }
// A rate chosen in the menu: at once when the patch is installed (FFX: 60 <-> 120 moves the limiter; 30 hands the timing
// back to the game), and saved in ffx_enh.ini for the next start (the launcher shows it too).
extern "C" void f60_menu_set(int fps) {
    if (fps != 30 && (fps < 60 || fps > 240 || fps % 30)) return;
    char b[16]; snprintf(b, sizeof b, "%d", fps);
    bool saved = WritePrivateProfileStringA("fps60", "fps", b, g_ini) != 0;
    if (g_st.state == 2) {
        if (g_st.game == 1 && fps >= 60 && !fx::rate_set_fps(fps)) { logf("[%s] Esc menu: %d fps refused by the rate row - unchanged", g_name, fps); return; }
        if (g_st.game == 2) x2::target(fps); else fx::target(fps);
        g_cur = fps;
        logf("[%s] Esc menu: %d fps in force%s", g_name, fps, saved ? ", saved" : ", NOT saved");
    } else {
        g_cur = fps;
        logf("[%s] Esc menu: %d fps saved - the patch is not installed in this session, it applies at the next start", g_name, fps);
    }
}


// ---- start options, [start] in ffx_enh.ini (both games, also at 30 fps and in Last Mission) ----
// skip_logos=1: the opening screen (OpeningScreen.swf: Square Enix / Virtuos / CRI / Dolby logos and the autosave notice) is
// skipped. Its "still playing" query (FFX 0x660400, FFX-2 0x4709F0 in the Steam builds: mov al, [flag]; ret) is found by its
// caller (xor bl, bl; mov ecx, [scene]; call query; test al, al; jnz back) and answers 0 from then on.
static bool skip_opening(void) {
    BYTE *b = (BYTE *)GetModuleHandleA(0); IMAGE_NT_HEADERS32 *nt = (IMAGE_NT_HEADERS32 *)(b + ((IMAGE_DOS_HEADER *)b)->e_lfanew);
    IMAGE_SECTION_HEADER *s = IMAGE_FIRST_SECTION(nt); BYTE *found = 0; int hits = 0;
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++) {
        if (!(s[i].Characteristics & IMAGE_SCN_MEM_EXECUTE) || !strncmp((const char *)s[i].Name, ".f60", 4)) continue;
        BYTE *p = b + s[i].VirtualAddress, *e = p + s[i].Misc.VirtualSize - 18;
        for (; p < e; p++) {
            if (p[0] != 0x32 || p[1] != 0xDB || p[2] != 0x8B || p[3] != 0x0D || p[8] != 0xE8 || p[13] != 0x84 || p[14] != 0xC0 || p[15] != 0x75 || p[16] != 0x9D) continue;
            BYTE *q = p + 13 + *(int *)(p + 9);
            if (q < b || q >= b + nt->OptionalHeader.SizeOfImage - 6 || q[0] != 0xA0 || q[5] != 0xC3) continue;
            if (found != q) { found = q; hits++; }
        }
    }
    if (hits != 1) { logf("[%s] skip logos: the opening screen's query was not found (%d candidates) - not skipped", g_name, hits); return false; }
    DWORD old; if (!VirtualProtect(found, 6, PAGE_EXECUTE_READWRITE, &old)) return false;
    found[0] = 0x30; found[1] = 0xC0; found[2] = 0xC3;   // xor al, al; ret
    VirtualProtect(found, 6, old, &old); FlushInstructionCache(GetCurrentProcess(), found, 6);
    logf("[%s] skip logos: opening screen (logos, autosave notice) skipped (query at 0x%08X)", g_name, (unsigned)(size_t)found);
    return true;
}
// no_launcher_restart=1: when the game is quit it starts FFX&X-2_LAUNCHER.exe through ShellExecuteW (both games import it from
// SHELL32). The import of the game EXE is redirected: a start of the launcher is left out, every other call goes through.
static HINSTANCE (WINAPI *o_shexec)(HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT);
static bool has_launcher(LPCWSTR f) {
    static const wchar_t k[] = L"LAUNCHER"; if (!f) return false;
    for (; *f; f++) { int i = 0; while (k[i] && f[i] && towupper(f[i]) == k[i]) i++; if (!k[i]) return true; }
    return false;
}
static HINSTANCE WINAPI h_shexec(HWND w, LPCWSTR op, LPCWSTR file, LPCWSTR par, LPCWSTR dir, INT show) {
    if (has_launcher(file)) { logf("[%s] no launcher restart: the game's start of the launcher was left out", g_name); return (HINSTANCE)42; }
    return o_shexec(w, op, file, par, dir, show);
}
static bool hook_shellexecute(void) {
    BYTE *b = (BYTE *)GetModuleHandleA(0); IMAGE_NT_HEADERS32 *nt = (IMAGE_NT_HEADERS32 *)(b + ((IMAGE_DOS_HEADER *)b)->e_lfanew);
    IMAGE_DATA_DIRECTORY dd = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT]; if (!dd.VirtualAddress) return false;
    for (IMAGE_IMPORT_DESCRIPTOR *d = (IMAGE_IMPORT_DESCRIPTOR *)(b + dd.VirtualAddress); d->Name; d++) {
        if (_stricmp((const char *)(b + d->Name), "SHELL32.dll") || !d->OriginalFirstThunk) continue;
        IMAGE_THUNK_DATA32 *n = (IMAGE_THUNK_DATA32 *)(b + d->OriginalFirstThunk), *a = (IMAGE_THUNK_DATA32 *)(b + d->FirstThunk);
        for (; n->u1.AddressOfData; n++, a++) {
            if (n->u1.Ordinal & IMAGE_ORDINAL_FLAG32) continue;
            if (strcmp((const char *)((IMAGE_IMPORT_BY_NAME *)(b + n->u1.AddressOfData))->Name, "ShellExecuteW")) continue;
            DWORD old; if (!VirtualProtect(&a->u1.Function, 4, PAGE_READWRITE, &old)) return false;
            o_shexec = (HINSTANCE (WINAPI *)(HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT))(size_t)a->u1.Function;
            a->u1.Function = (DWORD)(size_t)&h_shexec; VirtualProtect(&a->u1.Function, 4, old, &old);
            logf("[%s] no launcher restart: on", g_name); return true;
        }
    }
    logf("[%s] no launcher restart: the import ShellExecuteW was not found - off", g_name); return false;
}

// file_loader=1: ffgriever's External File Loader (EFL, github.com/ffgriever-pl; the unchanged release DLLs) is loaded
// directly, without its dinput8.dll module loader: <base>\modules\ff10-file-loader.dll, where <base> is the game folder or
// its EFL subfolder (the release unpacked there). The module installs its hooks in its DllMain, keeps the game folder as its
// main folder (data\mods) and reads <base>\modules\config\ff10-file-loader.ini. It imports simpleLog.dll, which is loaded
// first by its full path (<base>\simpleLog.dll, else the game folder's). simpleLog reads ".\hook.ini" from the current
// directory and opens a console window without it, so the current directory is <base> while the two DLLs load (hook.log
// is then written there too) and the game folder again afterwards. Not loaded twice: skipped when the module is already
// in the process (EFL's own dinput8.dll in the game folder).
static void load_efl(void) {
    if (GetModuleHandleA("ff10-file-loader.dll")) { logf("[%s] file loader: ff10-file-loader.dll is already loaded (EFL's own dinput8.dll) - left to it", g_name); return; }
    static const char *const sub[] = { "", "EFL\\" };
    char base[MAX_PATH + 8], mod[MAX_PATH + 64], sl[MAX_PATH + 64], ini[MAX_PATH + 64], cwd[MAX_PATH]; int found = 0;
    for (int i = 0; i < 2 && !found; i++) {
        snprintf(base, sizeof base, "%s%s", g_dir, sub[i]); snprintf(mod, sizeof mod, "%smodules\\ff10-file-loader.dll", base);
        if (GetFileAttributesA(mod) != INVALID_FILE_ATTRIBUTES) found = 1;
    }
    if (!found) { logf("[%s] file loader: modules\\ff10-file-loader.dll not found (game folder or EFL\\) - not loaded", g_name); return; }
    snprintf(sl, sizeof sl, "%ssimpleLog.dll", base);
    if (GetFileAttributesA(sl) == INVALID_FILE_ATTRIBUTES) snprintf(sl, sizeof sl, "%ssimpleLog.dll", g_dir);
    snprintf(ini, sizeof ini, "%shook.ini", base);
    bool moved = GetFileAttributesA(ini) != INVALID_FILE_ATTRIBUTES && GetCurrentDirectoryA(sizeof cwd, cwd) && SetCurrentDirectoryA(base);
    HMODULE s = LoadLibraryA(sl); DWORD es = GetLastError();
    HMODULE m = s ? LoadLibraryA(mod) : 0; DWORD em = GetLastError();
    if (moved) SetCurrentDirectoryA(cwd);
    if (!s) { logf("[%s] file loader: %s did not load (error %lu) - not loaded", g_name, sl, es); return; }
    if (!m) { logf("[%s] file loader: %s did not load (error %lu; it needs the Visual C++ 2015-2022 x86 runtime)", g_name, mod, em); return; }
    // the module's exports (ff10-file-loader 1.2.0.0): const char *__cdecl FF10HgetName(void) and
    // void __cdecl FF10HgetVer(int out[3]) - it writes major, minor, patch into the caller's array (called without one it
    // writes through a stray stack value: the crash at the game's start in FFX Enhanced 0.90 Beta)
    typedef const char *(__cdecl *name_fn)(void); typedef void (__cdecl *ver_fn)(int *);
    name_fn nm = (name_fn)GetProcAddress(m, "FF10HgetName"); ver_fn vf = (ver_fn)GetProcAddress(m, "FF10HgetVer");
    int v[8] = { 0 }; char vs[48] = ""; if (vf) { vf(v); snprintf(vs, sizeof vs, "%d.%d.%d", v[0], v[1], v[2]); }
    logf("[%s] file loader: %s %s loaded from %s (its log: %shook.log)", g_name, nm ? nm() : "ff10-file-loader", vs, mod, moved ? base : g_dir);
}

// ultrawide=1 (experimental): the ultrawide patch set of the RevoFX FFX module (module\ffx_uw.c + ffx_uw_data.h: SUWSF patches
// ported to the current Steam FFX.exe and FFX-2.exe; SUWSF by PhantomGamers, MIT) is applied to the game's image before the
// game starts - all or nothing. The aspect ratio is the primary screen's, or [start] ultrawide_size=WxH. Screens that are
// not wider than 16:9 are left alone. A SUWSF.ini in the game folder is used instead of the built-in patches.
extern "C" int ffx_uw_apply(int on, int shadows);
static char g_exe[MAX_PATH];
extern "C" void rfx_log(const char *fmt, ...) { char b[1500]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a); logf("[%s] %s", g_name, !strncmp(b, "FFX: ", 5) ? b + 5 : b); }
extern "C" const char *rfx_work_dir(void) { return g_dir; }
extern "C" const char *rfx_dir(void) { return g_dir; }
extern "C" const char *rfx_exe_path(void) { return g_exe; }
extern "C" void rfx_ini_str(const char *sec, const char *key, const char *def, char *out, int n) {
    if (n <= 0) return;
    if (!strcmp(key, "OutputSize")) GetPrivateProfileStringA("start", "ultrawide_size", def ? def : "", out, (DWORD)n, g_ini);
    else snprintf(out, (size_t)n, "%s", def ? def : "");
}
extern "C" void rfx_write_code(void *dst, const void *src, int n) {
    DWORD old; if (n <= 0 || !VirtualProtect(dst, (SIZE_T)n, PAGE_EXECUTE_READWRITE, &old)) return;
    memcpy(dst, src, (size_t)n); VirtualProtect(dst, (SIZE_T)n, old, &old); FlushInstructionCache(GetCurrentProcess(), dst, (SIZE_T)n);
}
extern "C" size_t f60_uw_image_size(size_t n) {     // the game's own part of the image: up to the patch's first section (.f60)
    BYTE *b = (BYTE *)GetModuleHandleA(0); IMAGE_NT_HEADERS32 *nt = (IMAGE_NT_HEADERS32 *)(b + ((IMAGE_DOS_HEADER *)b)->e_lfanew); IMAGE_SECTION_HEADER *s = IMAGE_FIRST_SECTION(nt);
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++) if (!strncmp((const char *)s[i].Name, ".f60", 4) && s[i].VirtualAddress < n) n = s[i].VirtualAddress;
    return n;
}
static void ultrawide(void) {
    int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN); char os[32]; int a, b;
    GetPrivateProfileStringA("start", "ultrawide_size", "", os, sizeof os, g_ini); if (sscanf(os, "%dx%d", &a, &b) == 2 && a > 0 && b > 0) { w = a; h = b; }
    if (h <= 0 || (double)w / h < 1.80) { logf("[%s] ultrawide: the screen is %dx%d, not wider than 16:9 - nothing changed", g_name, w, h); return; }
    logf("[%s] ultrawide (experimental): %s", g_name, ffx_uw_apply(1, 0) ? "applied" : "NOT applied (see above)");
}

static DWORD WINAPI start_thread(LPVOID) { bool ok = g_st.game == 2 ? x2::start() : fx::start(); if (!ok && g_st.state != 3) f60_installed(0, "see above"); return 0; }

// Called once at the start of the patched EXE copy (after the C runtime of this code is up), before the game's own entry point runs.
static void exe_start(void) {
    char exe[MAX_PATH]; GetModuleFileNameA(0, exe, sizeof exe);
    char *sl = strrchr(exe, '\\'); const char *base = sl ? sl + 1 : exe;
    int game = strstr(base, "-2") ? 2 : 1; g_name = game == 2 ? "FFX-2" : "FFX";
    snprintf(g_dir, sizeof g_dir, "%.*s", (int)(base - exe), exe);
    snprintf(g_ini, sizeof g_ini, "%sffx_enh.ini", g_dir); snprintf(g_logp, sizeof g_logp, "%s%s_enh.log", g_dir, g_name);
    { char old[MAX_PATH + 32]; snprintf(old, sizeof old, "%sFFX_60fps.ini", g_dir);   // the settings file before FFX Enhanced 2026-11-15
      if (GetFileAttributesA(g_ini) == INVALID_FILE_ATTRIBUTES && GetFileAttributesA(old) != INVALID_FILE_ATTRIBUTES) MoveFileA(old, g_ini); }
    QueryPerformanceFrequency(&g_qf); InitializeCriticalSection(&g_cs);
    g_fps = (int)GetPrivateProfileIntA("fps60", "fps", 60, g_ini); { char pm[32]; GetPrivateProfileStringA("fps60", "pacing", "optimized", pm, sizeof pm, g_ini);   // optimized (adaptive) | original (the mod's fixed pacing); 0 / 1 of older files
      g_pacing = !_stricmp(pm, "original") || !_stricmp(pm, "fixed") || !strcmp(pm, "1") ? 1 : 0; }
    g_verbose = (int)GetPrivateProfileIntA("fps60", "verbose", 0, g_ini); g_hotkeys = (int)GetPrivateProfileIntA("fps60", "hotkeys", 0, g_ini);
    if (GetPrivateProfileIntA("fps60", "log", 1, g_ini)) g_log = fopen(g_logp, "w");
    if (g_fps > 240) g_fps = 240; g_fps -= g_fps % 30;
    logf("[%s] FFX Enhanced 0.90 Beta, %s (60 fps patch: FFX-60fps b541a6a, Apache-2.0, adaptive pacing): fps=%d pacing=%s, ini %s", g_name, base, g_fps, g_pacing ? "original (fixed)" : "optimized (adaptive)", g_ini);
    snprintf(g_exe, sizeof g_exe, "%s", exe);
    if (GetPrivateProfileIntA("start", "ultrawide", 0, g_ini)) ultrawide();   // first: it works on a copy of the unhooked image
    if (GetPrivateProfileIntA("start", "file_loader", 0, g_ini)) load_efl();
    if (GetPrivateProfileIntA("start", "skip_logos", 0, g_ini)) skip_opening();
    if (GetPrivateProfileIntA("start", "no_launcher_restart", 0, g_ini)) hook_shellexecute();
    if (strstr(GetCommandLineA(), "FFX2_LASTMISSION")) { logf("[%s] Last Mission: no patch (by design) - original timing", g_name); return; }
    if (g_fps < 60) g_fps = 30;
    g_cur = g_fps;
    // The patch is installed also at 30 fps, with the game's own timing in force, so the Esc menu can switch to 60 later.
    // [fps60] menu=0: no Frame Rate row in the Esc menu.
    if (GetPrivateProfileIntA("fps60", "menu", 1, g_ini)) f60_escmenu_install(game);
    g_st.game = game; g_st.state = 1; g_st.target = g_fps; g_st.pacing = g_pacing;
    if (g_fps < 60) logf("[%s] 30 fps: the patch is installed with the game's own timing (the Esc menu can switch to 60)", g_name);
    HANDLE t = CreateThread(0, 0, start_thread, 0, 0, 0); if (t) CloseHandle(t);
}
#ifndef F60_DLL
BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) exe_start(); return TRUE; }
#else
// The optional DLL build (dbghelp.dll in the game folder; on Proton with WINEDLLOVERRIDES="dbghelp=n,b"). Every process that
// loads it gets a working dbghelp (dll\f60_dbghelp.cpp forwards to the system DLL). The patch starts only in FFX.exe /
// FFX-2.exe themselves - not in the launcher or other tools, not in a 60 fps EXE (FFX_enh.exe, or the Proton compatibility
// mode's copy under the original name: it carries the patch in its ".f60" section) and not with RevoFX, which installs the
// same patch from its own module (RevoFX.dll and its d3d11.dll there; the launcher's "Use RevoFX = Off" renames d3d11.dll).
extern "C" void f60_dbghelp_init(HINSTANCE self);
extern "C" int f60_dbghelp_resolved;
static unsigned file_crc(const char *path) {          // CRC-32 (zlib polynomial) of a file; 0 when it cannot be read
    static unsigned t[256]; if (!t[1]) for (unsigned i = 0; i < 256; i++) { unsigned c = i; for (int k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; }
    FILE *f = fopen(path, "rb"); if (!f) return 0;
    static unsigned char b[1 << 16]; unsigned r = 0xFFFFFFFFu; size_t n;
    while ((n = fread(b, 1, sizeof b, f)) > 0) for (size_t i = 0; i < n; i++) r = t[(r ^ b[i]) & 0xFF] ^ (r >> 8);
    fclose(f); return ~r;
}
static bool main_is_patched(void) {
    BYTE *b = (BYTE *)GetModuleHandleA(0); IMAGE_DOS_HEADER *d = (IMAGE_DOS_HEADER *)b;
    IMAGE_NT_HEADERS32 *nt = (IMAGE_NT_HEADERS32 *)(b + d->e_lfanew); IMAGE_SECTION_HEADER *s = IMAGE_FIRST_SECTION(nt);
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++) if (!strncmp((const char *)s[i].Name, ".f60", 4)) return true;
    return false;
}
BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(self);
    f60_dbghelp_init(self);
    char exe[MAX_PATH]; GetModuleFileNameA(0, exe, sizeof exe);
    char *sl = strrchr(exe, '\\'); const char *base = sl ? sl + 1 : exe;
    if (_stricmp(base, "FFX.exe") && _stricmp(base, "FFX-2.exe")) return TRUE;
    if (main_is_patched()) return TRUE;
    char revo[MAX_PATH + 16]; snprintf(revo, sizeof revo, "%.*sRevoFX.dll", (int)(base - exe), exe);
    char d3d[MAX_PATH + 16]; snprintf(d3d, sizeof d3d, "%.*sd3d11.dll", (int)(base - exe), exe);   // RevoFX active: RevoFX.dll and its d3d11.dll
    if (GetFileAttributesA(revo) != INVALID_FILE_ATTRIBUTES && GetFileAttributesA(d3d) != INVALID_FILE_ATTRIBUTES) return TRUE;
    // only the Steam release's EXEs are patched (CRC-32 of the file, as the launcher's patcher checks)
    unsigned crc = file_crc(exe), want = !_stricmp(base, "FFX-2.exe") ? 0x6C2274E0u : 0xFC9264CDu;
    if (crc != want) {
        char lp[MAX_PATH + 32]; snprintf(lp, sizeof lp, "%.*s%s_enh.log", (int)(base - exe), exe, !_stricmp(base, "FFX-2.exe") ? "FFX-2" : "FFX");
        FILE *f = fopen(lp, "w");
        if (f) { fprintf(f, "%s has CRC-32 %08X, expected %08X (the Steam release): not the original EXE - 60 fps patch NOT installed, dbghelp.dll only forwards\n", base, crc, want); fclose(f); }
        return TRUE;
    }
    exe_start();
    logf("[%s] loaded as dbghelp.dll; dbghelp forwards: %d of 250 resolved from the system DLL", g_name, f60_dbghelp_resolved);
    return TRUE;
}
#endif
