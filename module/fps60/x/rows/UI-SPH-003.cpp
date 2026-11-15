// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// UI-SPH-003: the Sphere Grid's menu hand is drawn by FUN_00a4f290 0xA4F290 (from the window draw FUN_00a58900 when
// the window's cursor flag +0x25 is set), which also steps the hand's left/right bob in the same call: x offset
// grid+0x116B4 (float, +-2*sin per call) and phase grid+0x116B8 (1..10, then -10..-1). The draw runs once per
// invocation, so the bob ran N/2 times as fast as at stock (U 01/11/2026: hand "blinks" too fast on the Sphere Grid at
// high fps). On render-only invocations both words are put back after the call: the draw stands, the step does not.
// Details: docs/fps60/rows/UI-SPH-003.md
#include "../fps60.h"
namespace fx {

typedef void(__cdecl* hand_t)(int win, int top, int xoff);

static uint8_t*  g_entry = nullptr;
static hand_t    g_tramp = nullptr;
static uint8_t** g_grid = nullptr;               // DAT_02305834: Sphere Grid state block
static bool      g_patched = false;
static volatile LONG g_held = 0;

// void(int win, int top, int xoff), plain RET (0xA4F628).
static void __cdecl h_hand(int win, int top, int xoff) {
    uint8_t* grid = *g_grid;
    if (g_disabled || !row_UISPH003.on || pacing_kind() != INV_BUILD || !grid) {
        g_tramp(win, top, xoff);
        return;
    }
    uint32_t* bob = (uint32_t*)(grid + 0x116B4);   // +0x116B4 offset, +0x116B8 phase
    uint32_t b0 = bob[0], b1 = bob[1];
    g_tramp(win, top, xoff);
    bob[0] = b0;
    bob[1] = b1;
    if (InterlockedIncrement(&g_held) == 1) log_line("UI-SPH-003: first Sphere Grid hand bob held on a render-only invocation");
}

static bool install() {
    // FUN_00a4f290: SUB ESP,0x1AC; cookie; MOV EAX,[EBP+0x10]; PUSH EBX; MOV EBX,[DAT_02305834] (+0x17); PUSH ESI;
    // PUSH EDI; MOV EDI,[EBP+8]
    g_entry = sig_find_unique("UI-SPH-003 Sphere Grid hand draw FUN_00a4f290",
        "55 8B EC 81 EC AC 01 00 00 A1 ?? ?? ?? ?? 33 C5 89 45 FC 8B 45 10 53 8B 1D ?? ?? ?? ?? 56 57 8B 7D 08",
        0xA4F290);
    if (!g_entry) return false;
    g_grid = (uint8_t**)read_abs(g_entry + 0x19);
    // the first bob read: FLD dword ptr [EBX+0x116B4] at +0x49
    if (!sig_match(g_entry + 0x49, "D9 83 B4 16 01 00")) {
        log_line("  UI-SPH-003: bob offset read at +0x49 not found - refused");
        return false;
    }
    log_line("  UI-SPH-003: hand draw at gva=0x%08X, grid pointer gva=0x%08X (expected 0x02305834; entry hooked on the game thread)",
             rt_to_gva_exe(g_entry), rt_to_gva_exe(g_grid));
    return true;
}

static void pre() {
    if (g_patched) return;
    g_patched = true;
    if (!hook_entry("UI-SPH-003", g_entry, (void*)&h_hand, (void**)&g_tramp)) {
        log_line("UI-SPH-003: entry hook failed - row off");
        row_UISPH003.on = 0;
        return;
    }
    log_line("UI-SPH-003: hand draw entry hooked on the game thread (tramp %p)", (void*)g_tramp);
}

Row row_UISPH003 = {
    "UI-SPH-003",
    "Sphere Grid menu hand bob (FUN_00a4f290 +0x116B4/+0x116B8) steps on logic ticks only",
    1, install, pre, nullptr, 0
};
} // namespace fx
