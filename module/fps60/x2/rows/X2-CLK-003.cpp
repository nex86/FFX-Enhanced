// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-CLK-003: the draw frame counter some effects animate from counts game ticks, not drawn frames.
//
// Background. FUN_0069fc50, called from Sg_MainLoop's tail on every invocation, starts with two counter increments
// (0x1B417B4 and 0x1B417B0). The counter is read only by drawing code, as an animation phase: a battle effect object's
// curve of 50 points (FUN_00680b70: brightness pulses on a 32-step triangle wave, the points slide on a 16-step phase)
// and two HD-layer draws (FUN_00b68280 / FUN_00b689e0: scroll phase). At stock 30 fps it counts once per tick; at
// 60 fps it counted every drawn frame, so these pulses and scrolls ran at double speed.
//
// The fix. On render-only invocations both counters are read before and written back after the invocation, so they
// count once per game tick. The render-only frame still draws with the next value (each value is shown for two frames).
// Evidence: re/ledger/UI018-X2.md (counter rates read from the running game: 60/s; consumers, decompiles).
#include "../x2.h"

namespace x2 {

extern Row row_CLK003;

static volatile int32_t* g_counter = nullptr;    // 0x1B417B4
static volatile int32_t* g_counter2 = nullptr;   // 0x1B417B0
static int32_t g_saved = 0, g_saved2 = 0;
static bool    g_saving = false;                 // this invocation is render-only and its values were saved
static volatile LONG g_held = 0;

static bool install() {
    // FUN_0069fc50 after its aligned-stack prologue: SUB ESP,0x1F8; MOV EAX,[cookie]; XOR EAX,EBP; MOV [EBP-4],EAX;
    // INC [counter]; INC [counter2]
    uint8_t* m = sig_find("X2-CLK-003 frame counter increments (FUN_0069fc50)",
        "81 EC F8 01 00 00 A1 ?? ?? ?? ?? 33 C5 89 45 FC FF 05 ?? ?? ?? ?? FF 05 ?? ?? ?? ??", 0x69FC66);
    if (!m) return false;
    g_counter = (volatile int32_t*)abs_operand("draw frame counter", m + 0x12);
    g_counter2 = (volatile int32_t*)abs_operand("draw frame counter 2", m + 0x18);
    if (!g_counter || !g_counter2) return false;
    log_line("  X2-CLK-003: counters gva=0x%08X / 0x%08X (0x01B417B4 / 0x01B417B0) held on render-only invocations",
             rt_to_gva((const void*)g_counter), rt_to_gva((const void*)g_counter2));
    return true;
}

static void pre() {
    g_saving = !g_disabled && render_only();
    if (g_saving) { g_saved = *g_counter; g_saved2 = *g_counter2; }
}

static void post() {
    if (!g_saving) return;
    g_saving = false;
    if (*g_counter != g_saved || *g_counter2 != g_saved2) {
        *g_counter = g_saved;
        *g_counter2 = g_saved2;
        if (InterlockedIncrement(&g_held) == 1) log_line("X2-CLK-003: first render-only frame counter step held");
    }
}

Row row_CLK003 = {
    "X2-CLK-003",
    "draw frame counter 0x1B417B4 / 0x1B417B0 (effect pulses, HD scrolls): held on render-only invocations",
    1, install, pre, post, 0
};

} // namespace x2
