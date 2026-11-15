// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// Diagnostic DIAG-FURY (default off): logs every Od_LuluFuryTick 0x891B80 pass while Lulu's Fury is spinning (state 4):
// invocation kind and pass, the stick samples it reads (ring -1 and 0, x/y), the direction history, the rotation count
// and level before/after. Written for U 01/11/2026: Fury spins don't count at 120 fps, fine at 60.
// Details: docs/fps60/rows/DIAG-FURY.md
#include "../fps60.h"
#include <stdio.h>
namespace fx {

typedef void(__cdecl* fury_t)(void);
typedef uint8_t(__cdecl* pad_t)(int port, int p2, int axis, int ring);

static uint8_t*  g_entry = nullptr;
static fury_t    g_tramp = nullptr;
static pad_t     g_pad = nullptr;                // FUN_00888c20: pad ring analog byte
static uint8_t*  g_state = nullptr;              // DAT_0133d708 (short state; cluster below)
static uint8_t*  g_analog = nullptr;             // DAT_00c58ef0: analog available
static bool      g_patched = false;
static volatile LONG g_lines = 0;
static uint64_t  g_inv = 0, g_last_inv = 0;

enum { MAX_LINES = 3000 };

// the cluster around the state (S, decompile 0x891B80): count +0x16, level +0x17, history +0x19 (16 bytes), length +0x29
static void snap(char* out, size_t n) {
    uint8_t* s = g_state;
    char h[40] = {};
    int len = (int8_t)s[0x29];
    for (int i = 0; i < len && i < 16; i++) h[i] = (char)('0' + s[0x19 + i]);
    _snprintf_s(out, n, _TRUNCATE, "cnt=%u lv=%u hist=%s(%d)", s[0x16], s[0x17], h, len);
}

// void(void), plain RET (0x89208E / 0x8920A7).
static void __cdecl h_fury() {
    int st = *(int16_t*)g_state;          // MOVSX ECX,word [state] (0x891BE9)
    static int prev = -1;
    if (!g_disabled && row_DIAGFURY.on && st != prev && g_lines <= MAX_LINES) {
        InterlockedIncrement(&g_lines);
        log_line("DIAG-FURY: state %d -> %d at inv %llu (kind %d, pass %d/%d)", prev, st, g_inv, pacing_kind(), pacing_pass(), rate_mult());
        prev = st;
    }
    if (g_disabled || !row_DIAGFURY.on || st != 4 || g_lines > MAX_LINES) { g_tramp(); return; }
    static const char* const K[] = { "LOGIC", "BUILD", "BACKLOG", "STOCK" };
    int k = pacing_kind();
    uint8_t x1 = g_pad(0, 0, 0, -1), y1 = g_pad(0, 0, 1, -1), x0 = g_pad(0, 0, 0, 0), y0 = g_pad(0, 0, 1, 0);
    char a[80], b[80];
    snap(a, sizeof(a));
    g_tramp();
    snap(b, sizeof(b));
    InterlockedIncrement(&g_lines);
    log_line("DIAG-FURY: %-7s pass %d/%d inv %llu (+%llu) analog=%u ring-1 (%3u,%3u) ring0 (%3u,%3u) | %s -> %s state %d",
             (k >= 0 && k < 4) ? K[k] : "?", pacing_pass(), rate_mult(), g_inv, g_inv - g_last_inv, *g_analog,
             x1, y1, x0, y0, a, b, *(int16_t*)g_state);
    g_last_inv = g_inv;
}

static bool install() {
    // Od_LuluFuryTick: prologue, cookie, PUSH EBX, MOV byte [EBP-0x15],0x80, MOV EDI,0x80FF... (rotation tables 04 05 06 07)
    g_entry = sig_find_unique("DIAG-FURY Od_LuluFuryTick",
        "55 8B EC 83 EC 1C A1 ?? ?? ?? ?? 33 C5 89 45 FC 53 C6 45 EB 80 B7 80 80 CB FF C7 45 F4 04 05 06 07",
        0x891B80);
    if (!g_entry) return false;
    // MOV ECX,[state] at +0x6A, CMP [analog],0 at +0x16C, CALL pad reader at +0x163
    if (!sig_match(g_entry + 0x6A, "8B 0D") || !sig_match(g_entry + 0x16C, "83 3D") || !sig_match(g_entry + 0x163, "E8")) {
        log_line("  DIAG-FURY: operand sites not found - refused");
        return false;
    }
    g_state  = (uint8_t*)read_abs(g_entry + 0x6C);
    g_analog = (uint8_t*)read_abs(g_entry + 0x16E);
    g_pad    = (pad_t)call_target(g_entry + 0x163);
    log_line("  DIAG-FURY: Od_LuluFuryTick gva=0x%08X, state gva=0x%08X (expected 0x0133D708), analog flag gva=0x%08X "
             "(expected 0x00C58EF0), pad reader gva=0x%08X (expected 0x00888C20)", rt_to_gva_exe(g_entry),
             rt_to_gva_exe(g_state), rt_to_gva_exe(g_analog), rt_to_gva_exe((uint8_t*)g_pad));
    return true;
}

static void pre() {
    g_inv++;
    if (g_patched) return;
    g_patched = true;
    if (!hook_entry("DIAG-FURY", g_entry, (void*)&h_fury, (void**)&g_tramp)) {
        log_line("DIAG-FURY: entry hook failed - row off");
        row_DIAGFURY.on = 0;
        return;
    }
    log_line("DIAG-FURY: Od_LuluFuryTick entry hooked on the game thread (tramp %p)", (void*)g_tramp);
}

Row row_DIAGFURY = {
    "DIAG-FURY",
    "diagnostic: logs Lulu's Fury spin passes (stick samples, history, count) while spinning",
    0, install, pre, nullptr, 0
};
} // namespace fx
