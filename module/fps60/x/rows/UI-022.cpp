// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// UI-022: while Lulu's Fury is armed (DAT_0133c930), the stick comes from a queue an input thread fills
// (FUN_0068ccd0: one sample per poll, neutral when empty), polled by FUN_00630de0 at the top of every
// Sg_MainLoopBacklogRunner pass and once more inside the logic tick (0x89AE65). Under the mod's pacing the runner
// polls on render-only invocations too, but the pad ring is latched on logic ticks only (UI-017), so those samples
// were dropped: at 120 fps 3 of 4, and the spin history lost its corners (U 01/11/2026: Fury spins don't count at
// 120, fine at 60; D DIAG-FURY 16:52/16:53: count 1 at 120 vs 10 at 60). Fix: the runner's poll is held while
// pacing and run from pre() on logic invocations only, so each logic tick polls as at stock.
// Details: docs/fps60/rows/UI-022.md
#include "../fps60.h"
#include <intrin.h>
namespace fx {

typedef void(__cdecl* poll_t)(void);

static uint8_t*  g_poll = nullptr;               // FUN_00630de0
static poll_t    g_tramp = nullptr;
static uint8_t*  g_ret = nullptr;                // return address of the runner's CALL
static uint8_t*  g_armed = nullptr;              // DAT_0133c930
static bool      g_patched = false;
static volatile LONG g_held = 0, g_moved = 0;

// void(void), tail JMP into FUN_00631550 (fastcall, plain RET).
static void __cdecl h_poll() {
    if (!g_disabled && row_UI022.on && pacing_active() && (uint8_t*)_ReturnAddress() == g_ret) {
        if (InterlockedIncrement(&g_held) == 1) log_line("UI-022: first runner Fury poll held (moved to the logic invocation)");
        return;
    }
    g_tramp();
}

static bool install() {
    if (!g_mainloop_callsite) return false;
    // Sg_MainLoopBacklogRunner: PUSH EBP; MOV EBP,ESP; CMP [DAT_0133c930],0; JZ +5; CALL FUN_00630de0 (+0x0C)
    uint8_t* runner = g_mainloop_callsite - 0x27;
    if (!sig_match(runner + 3, "83 3D ?? ?? ?? ?? 00 74 05 E8")) {
        log_line("  UI-022: runner Fury poll call not found - refused");
        return false;
    }
    g_armed = (uint8_t*)read_abs(runner + 5);
    g_poll = call_target(runner + 0x0C);
    g_ret = runner + 0x11;
    // FUN_00630de0: CALL FUN_00631300; MOV ECX,EAX; JMP FUN_00631550
    if (!sig_match(g_poll, "E8 ?? ?? ?? ?? 8B C8 E9")) {
        log_line("  UI-022: poll FUN_00630de0 bytes unexpected - refused");
        return false;
    }
    log_line("  UI-022: poll gva=0x%08X (expected 0x00630DE0), armed flag gva=0x%08X (expected 0x0133C930)",
             rt_to_gva_exe(g_poll), rt_to_gva_exe(g_armed));
    return true;
}

static void pre() {
    if (!g_patched) {
        g_patched = true;
        if (!hook_entry("UI-022", g_poll, (void*)&h_poll, (void**)&g_tramp)) {
            log_line("UI-022: entry hook failed - row off");
            row_UI022.on = 0;
            return;
        }
        log_line("UI-022: poll FUN_00630de0 entry hooked on the game thread (tramp %p)", (void*)g_tramp);
        return;
    }
    // the runner's poll for this pass was held (it runs before pre); make it here when this pass is a logic tick
    if (g_tramp && pacing_active() && pacing_kind() != INV_BUILD && *(int*)g_armed != 0) {
        g_tramp();
        if (InterlockedIncrement(&g_moved) == 1) log_line("UI-022: first Fury poll made on a logic invocation");
    }
}

Row row_UI022 = {
    "UI-022",
    "Lulu's Fury: stick queue polled once per logic tick (render-only invocations no longer drop spin samples)",
    1, install, pre, nullptr, 0
};
} // namespace fx
