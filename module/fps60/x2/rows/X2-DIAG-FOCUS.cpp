// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-DIAG-FOCUS (diagnostic, observe only): what the game does when its window loses and regains focus.
//
// Written for report R8 (paused in battle, click outside the window and back: the game freezes for a while and other
// programs slow down), which did not reproduce here. A watcher thread looks every 100 ms, also while the main loop is
// stopped (pause and load screens do not run it). It logs:
//   - each focus change (game window in front or not), with the time since the last main-loop invocation;
//   - for 20 s after each change, once per second: the game's CPU use (all threads, in % of one core), main-loop
//     invocations in that second, the longest gap between two invocations, and how long the window took to answer a
//     no-op message (a hung message loop shows here);
//   - any time, a second in which the game used more than 150% of a core or the window did not answer within 1 s.
// One log line per second at most; the game is never touched.
#include "../x2.h"

namespace x2 {

extern Row row_DIAGFOCUS;

static volatile LONG     g_invocations = 0;     // main-loop invocations (post())
static volatile LONGLONG g_last_inv_ms = 0;     // GetTickCount64 at the last invocation
static volatile LONGLONG g_max_gap_ms = 0;      // longest gap between invocations in the current second
static HANDLE            g_thread = nullptr;
static volatile LONG     g_stop = 0;

static BOOL CALLBACK find_wnd(HWND h, LPARAM lp) {
    DWORD pid = 0;
    GetWindowThreadProcessId(h, &pid);
    if (pid != GetCurrentProcessId() || !IsWindowVisible(h) || GetWindow(h, GW_OWNER)) return TRUE;
    *(HWND*)lp = h;
    return FALSE;
}

static ULONGLONG process_cpu_100ns() {
    FILETIME c, e, k, u;
    if (!GetProcessTimes(GetCurrentProcess(), &c, &e, &k, &u)) return 0;
    return (((ULONGLONG)k.dwHighDateTime << 32) | k.dwLowDateTime) + (((ULONGLONG)u.dwHighDateTime << 32) | u.dwLowDateTime);
}

static DWORD WINAPI watcher(void*) {
    HWND wnd = nullptr;
    int was_front = -1;
    ULONGLONG report_until = 0, sec_start = GetTickCount64(), cpu_start = process_cpu_100ns();
    LONG inv_start = g_invocations;
    while (!g_stop) {
        Sleep(100);
        if (!wnd || !IsWindow(wnd)) { wnd = nullptr; EnumWindows(find_wnd, (LPARAM)&wnd); }
        ULONGLONG now = GetTickCount64();
        if (wnd) {
            DWORD pid = 0;
            HWND fg = GetForegroundWindow();
            if (fg) GetWindowThreadProcessId(fg, &pid);
            int front = pid == GetCurrentProcessId();
            if (front != was_front) {
                if (was_front != -1)
                    log_line("X2-DIAG-FOCUS: game window %s; last main-loop invocation %llu ms ago", front ? "IN FRONT again" : "LOST focus",
                             now - (ULONGLONG)g_last_inv_ms);
                was_front = front;
                report_until = now + 20000;
            }
        }
        if (now - sec_start < 1000) continue;
        ULONGLONG cpu = process_cpu_100ns();
        double pct = (double)(cpu - cpu_start) / 100.0 / (double)(now - sec_start);   // 100 ns units -> % of one core
        LONG invs = g_invocations - inv_start;
        LONGLONG gap = InterlockedExchange64(&g_max_gap_ms, 0);
        if (invs == 0) gap = (LONGLONG)(now - (ULONGLONG)g_last_inv_ms);
        DWORD_PTR r = 0;
        ULONGLONG t0 = GetTickCount64();
        LRESULT ok = wnd ? SendMessageTimeoutW(wnd, WM_NULL, 0, 0, SMTO_ABORTIFHUNG, 5000, &r) : 1;
        ULONGLONG answer = GetTickCount64() - t0;
        if (now < report_until || pct > 150.0 || !ok || answer > 1000)
            log_line("X2-DIAG-FOCUS: cpu %.0f%% of a core, %ld invocations, longest gap %lld ms, window %s in %llu ms%s", pct, invs, gap,
                     ok ? "answered" : "DID NOT ANSWER", answer, pacing_kind() == INV_STOCK ? " (stock pacing)" : "");
        sec_start = now;
        cpu_start = cpu;
        inv_start = g_invocations;
    }
    return 0;
}

static void post() {
    LONGLONG now = (LONGLONG)GetTickCount64();
    LONGLONG gap = g_last_inv_ms ? now - g_last_inv_ms : 0;
    if (gap > g_max_gap_ms) g_max_gap_ms = gap;
    g_last_inv_ms = now;
    InterlockedIncrement(&g_invocations);
}

static bool install() {
    g_last_inv_ms = (LONGLONG)GetTickCount64();
    g_thread = CreateThread(nullptr, 0, watcher, nullptr, 0, nullptr);
    return g_thread != nullptr;
}

static void stop() {
    InterlockedExchange(&g_stop, 1);
    if (g_thread) { WaitForSingleObject(g_thread, 2000); CloseHandle(g_thread); g_thread = nullptr; }
}

Row row_DIAGFOCUS = {
    "X2-DIAG-FOCUS",
    "diagnostic: focus changes, CPU, main-loop gaps and window answers (report R8), from a watcher thread",
    1, install, nullptr, post, 0, 0, stop
};

} // namespace x2
