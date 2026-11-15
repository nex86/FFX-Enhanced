// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// QOL-FURY (gameplay option, default off; [rows] QOL-FURY = 0 | 2..5): Fast Fury. When Lulu's Fury reaches its spin
// state (Od_LuluFuryTick 0x891B80 state 4), the next invocation sets the cast level DAT_0133d71f (the number of
// spells, cap 16) and its gauge to the chosen number and runs the overdrive timer out, so the game's own state 4
// test (`_DAT_0133f790 <= 0`) ends the spin on that logic tick and casts (state 6: FUN_007b0470(5, level, ...)).
// The timer: FUN_00891ac0 0x891AC0 (same caller, before the Fury tick) computes left = total - elapsed / 30 (25 in
// one mode) while its mode is 2, so elapsed is set past total * 30 and left to 0.
// Details: docs/fps60/rows/QOL-FURY.md
#include "../fps60.h"
namespace fx {

static uint8_t* g_state = nullptr;               // DAT_0133d708 (short); level +0x17, count +0x16, gauge +0x14 (short),
                                                 // gauge scale +0x06 (short) - all checked against the tick's operands
static float*   g_elapsed = nullptr;             // _DAT_0133f788
static float*   g_total = nullptr;               // _DAT_0133f78c
static float*   g_left = nullptr;                // _DAT_0133f790
static int      g_casts = 0;                     // 0 = off, else 2..16
static volatile LONG g_applied = 0;

static int clamp_casts(int n) { return n <= 0 ? 0 : n < 2 ? 2 : n > 16 ? 16 : n; }

int  qol_fury_casts() { return row_QOLFURY.on ? g_casts : 0; }

bool qol_fury_set(int n) {
    n = clamp_casts(n);
    if (n == 0) { row_set(row_QOLFURY, false); return true; }
    if (!row_set(row_QOLFURY, true)) return false;
    g_casts = n;
    return true;
}

static bool install() {
    // Od_LuluFuryTick +6: cookie, PUSH EBX, MOV byte [EBP-0x15],0x80, MOV EDI,0x80FF... (rotation tables 04 05 06 07).
    // Matched past the 6-byte prologue: DIAG-FURY (or another mod) may have hooked the entry with a JMP.
    uint8_t* fury = sig_find_unique("QOL-FURY Od_LuluFuryTick (+6)",
        "A1 ?? ?? ?? ?? 33 C5 89 45 FC 53 C6 45 EB 80 B7 80 80 CB FF C7 45 F4 04 05 06 07",
        0x891B86);
    if (fury) fury -= 6;
    // FUN_00891ac0: CMP [mode],2; JNZ; FLD [elapsed]; FADD [1.0]; FSTP [elapsed]; CALL; FLD [total]; FLD [elapsed]
    uint8_t* tmr = sig_find_unique("QOL-FURY overdrive timer FUN_00891ac0",
        "55 8B EC 51 83 3D ?? ?? ?? ?? 02 0F 85 ?? ?? ?? ?? D9 05 ?? ?? ?? ?? DC 05 ?? ?? ?? ?? D9 1D ?? ?? ?? ?? E8 ?? ?? ?? ?? D9 05 ?? ?? ?? ?? D9 05",
        0x891AC0);
    if (!fury || !tmr) return false;
    // tick: MOV ECX,[state] +0x6A; MOV BL,[level] +0x283; MOV ESI,[gauge] +0x28A; MOVSX EAX,[scale] +0x356;
    // FCOMP [left] +0x128. timer: FSTP [left] +0x48.
    if (!sig_match(fury + 0x6A, "8B 0D") || !sig_match(fury + 0x283, "8A 1D") || !sig_match(fury + 0x28A, "8B 35") ||
        !sig_match(fury + 0x356, "0F BF 05") || !sig_match(fury + 0x128, "D8 1D") || !sig_match(tmr + 0x48, "D9 1D")) {
        log_line("  QOL-FURY: operand sites not found - refused");
        return false;
    }
    g_state = read_abs(fury + 0x6C);
    g_elapsed = (float*)read_abs(tmr + 0x13);
    g_total = (float*)read_abs(tmr + 0x2A);
    g_left = (float*)read_abs(tmr + 0x4A);
    if (read_abs(fury + 0x285) != g_state + 0x17 || read_abs(fury + 0x28C) != g_state + 0x14 ||
        read_abs(fury + 0x359) != g_state + 0x06 || (float*)read_abs(fury + 0x12A) != g_left) {
        log_line("  QOL-FURY: Fury globals not laid out as expected - refused");
        return false;
    }
    g_casts = clamp_casts(config_row("QOL-FURY", 0));
    log_line("  QOL-FURY: state gva=0x%08X (expected 0x0133D708), timer elapsed/total/left gva=0x%08X/0x%08X/0x%08X "
             "(expected 0x0133F788/0x0133F78C/0x0133F790); %d casts", rt_to_gva_exe(g_state), rt_to_gva_exe((uint8_t*)g_elapsed),
             rt_to_gva_exe((uint8_t*)g_total), rt_to_gva_exe((uint8_t*)g_left), g_casts);
    return true;
}

static void pre() {
    if (g_disabled || g_casts <= 0 || *(int16_t*)g_state != 4) return;
    int n = g_casts;
    uint8_t* s = g_state;
    s[0x17] = (uint8_t)n;                                        // level = casts
    s[0x16] = (uint8_t)n;                                        // rotation count (read only while spinning)
    int scale = *(int16_t*)(s + 0x06);
    *(int16_t*)(s + 0x14) = (int16_t)(n * ((scale + ((scale >> 31) & 0xF)) >> 4));   // the tick's gauge target
    float end = *g_total * 30.0f + 30.0f;
    if (*g_elapsed < end) *g_elapsed = end;
    *g_left = 0.0f;
    if (InterlockedIncrement(&g_applied) == 1 || (g_applied % 50) == 0)
        log_line("QOL-FURY: Fury spin ended at once with %d casts (applied %ld times)", n, g_applied);
}

Row row_QOLFURY = {
    "QOL-FURY",
    "gameplay option: Fast Fury - Lulu's Fury ends at once with the chosen number of casts (QOL-FURY = 2..5; 0 = off)",
    0, install, pre, nullptr, 0
};
} // namespace fx
