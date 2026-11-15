// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-UI-018: the Garment Grid screens (battle Spherechange and the main menu's Garment Grids) animate at normal speed.
//
// Background. The Garment Grid panels are drawn by a separate UI layer ("ky" windows). Its draw functions also advance
// its animation, PS2 style, once per call:
//   - FUN_005f10d0 (the window walk): two animation clocks (0xDF5F00, 0xDF5F04; the grid draw FUN_00542f80 uses
//     0xDF5F00 as its time, which moves the grid's arrow and lines) and a counter at +0 of each live window;
//   - FUN_005f4630 (the whole layer, from the window draw Win_DrawAll): the walk above plus FUN_005f5940 / 5af0 /
//     5c30 / 5dd0 / 6050, which step pulses, counters and small state machines kept in two blocks of globals
//     (0xDF5D40..0xDF603F and 0xD47CF0..0xD47D33);
//   - FUN_005e9d80 (drawn for windows of type 2, inside the walk): a model phase 0xDF2D58 (1..256).
// Both are reached on every invocation (Sg_MainLoop's tail 0x605EB3 -> 0x5E3170 -> 0x58F350 -> 0x5A5290, and the
// window draw), so at 60 fps the arrow, lines and pulses ran at double speed.
//
// The fix. On render-only invocations, the outermost call of either function is bracketed: the two global blocks and
// each live window's counter are copied before the call and written back after it. The panel is drawn, but nothing it
// steps survives the render-only frame, so it advances once per game tick as at stock. The window pointer table
// (0xDF5F10, 32 slots) is never written back, so a window the draw closes stays closed; a window's counter is restored
// only if the same window is still in its slot. Logic invocations and stock pacing run untouched.
// Evidence: re/ledger/UI018-X2.md (60/s counters read from the running game, call chain, decompiles).
#include "../x2.h"
#include <string.h>

namespace x2 {

extern Row row_UI018;

typedef void(__cdecl* void_fn)();   // both: no arguments, plain RET (0x5F126C; 0x5F46E3 / 0x5F46EC)

enum { WINDOW_SLOTS = 32, UI_BLOCK_LEN = 0x300, PULSE_BLOCK_LEN = 0x44 };
static uint8_t*   g_window_walk = nullptr;        // FUN_005f10d0
static uint8_t*   g_ui_draw = nullptr;            // FUN_005f4630
static void_fn    g_window_walk_orig = nullptr, g_ui_draw_orig = nullptr;
static uint8_t*   g_ui_block = nullptr;           // 0xDF5D40..0xDF603F (clocks, counters, state; holds the window table)
static uint8_t*   g_window_table = nullptr;       // 0xDF5F10: 32 window pointers, never restored
static uint8_t*   g_pulse_block = nullptr;        // 0xD47CF0..0xD47D33 (pulse value / direction, panel offsets)
static int32_t*   g_model_phase = nullptr;        // 0xDF2D58 (FUN_005e9d80)
static bool       g_hooked = false;
static int        g_depth = 0;                    // game thread: nesting of the two hooked functions
static volatile LONG g_rollbacks = 0, g_counters_restored = 0;

struct UiSnapshot {
    uint8_t  ui[UI_BLOCK_LEN];
    uint8_t  pulse[PULSE_BLOCK_LEN];
    uint8_t* window[WINDOW_SLOTS];
    int32_t  window_counter[WINDOW_SLOTS];
    int32_t  model_phase;
};
static UiSnapshot g_snap;

// Writes the snapshot back, except the window table; a window's counter only if the same window is in its slot.
// Pure on its arguments (tested without the game). Returns the number of window counters restored.
static int restore_ui(const UiSnapshot& s, uint8_t* ui, size_t table_offset, uint8_t* pulse) {
    memcpy(ui, s.ui, table_offset);
    size_t after = table_offset + WINDOW_SLOTS * sizeof(void*);
    memcpy(ui + after, s.ui + after, UI_BLOCK_LEN - after);
    memcpy(pulse, s.pulse, PULSE_BLOCK_LEN);
    uint8_t** table = (uint8_t**)(ui + table_offset);
    int n = 0;
    for (int i = 0; i < WINDOW_SLOTS; i++)
        if (s.window[i] && table[i] == s.window[i]) { *(int32_t*)s.window[i] = s.window_counter[i]; n++; }
    return n;
}

static void take_snapshot(UiSnapshot& s) {
    memcpy(s.ui, g_ui_block, UI_BLOCK_LEN);
    memcpy(s.pulse, g_pulse_block, PULSE_BLOCK_LEN);
    s.model_phase = *g_model_phase;
    uint8_t** table = (uint8_t**)g_window_table;
    for (int i = 0; i < WINDOW_SLOTS; i++) {
        s.window[i] = table[i];
        s.window_counter[i] = table[i] ? *(int32_t*)table[i] : 0;
    }
}

static void run_bracketed(void_fn orig) {
    if (!row_UI018.on || g_disabled || g_depth > 0 || !render_only()) {
        g_depth++;
        orig();
        g_depth--;
        return;
    }
    take_snapshot(g_snap);
    g_depth++;
    orig();
    g_depth--;
    LONG n = restore_ui(g_snap, g_ui_block, (size_t)(g_window_table - g_ui_block), g_pulse_block);
    *g_model_phase = g_snap.model_phase;
    InterlockedExchangeAdd(&g_counters_restored, n);
    if (InterlockedIncrement(&g_rollbacks) == 1)
        log_line("X2-UI-018: first render-only Garment Grid UI draw rolled back (%ld window counters)", n);
}
static void __cdecl hook_window_walk() { run_bracketed(g_window_walk_orig); }
static void __cdecl hook_ui_draw() { run_bracketed(g_ui_draw_orig); }

static bool install() {
    // FUN_005f10d0: PUSH EBP; MOV EBP,ESP; PUSH ECX; INC [clock]; PUSH EBX/ESI/EDI; XOR EDI,EDI; INC [clock2];
    // MOV [EBP-4],EDI; MOV EBX,window_table
    g_window_walk = sig_find("X2-UI-018 window walk FUN_005f10d0",
        "55 8B EC 51 FF 05 ?? ?? ?? ?? 53 56 57 33 FF FF 05 ?? ?? ?? ?? 89 7D FC BB ?? ?? ?? ??", 0x5F10D0);
    // FUN_005f4630: CMP [shown],0; JE end; PUSH 2; CALL; MOV [flag],1; CALL FUN_005f5940; PUSH 3
    g_ui_draw = sig_find("X2-UI-018 UI draw FUN_005f4630",
        "83 3D ?? ?? ?? ?? 00 0F 84 A7 00 00 00 6A 02 E8 ?? ?? ?? ?? C7 05 ?? ?? ?? ?? 01 00 00 00 E8 ?? ?? ?? ?? 6A 03", 0x5F4630);
    // FUN_005f5dd0: PUSH EBP; MOV EBP,ESP; PUSH ECX; PUSH ESI; CALL; MOV ECX,[pulse]; MOV ESI,EAX; MOV [..],imm; TEST ECX,ECX
    uint8_t* pulse_fn = sig_find("X2-UI-018 pulse FUN_005f5dd0",
        "55 8B EC 51 56 E8 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 8B F0 C7 05 ?? ?? ?? ?? ?? ?? ?? ?? 85 C9 79 0D", 0x5F5DD0);
    // FUN_005e9d80 tail: MOV EAX,[phase]; INC EAX; MOV [phase],EAX; CMP EAX,0x100; JLE; SUB EAX,0x100; MOV [phase],EAX
    uint8_t* phase_step = sig_find("X2-UI-018 model phase step (FUN_005e9d80)",
        "A1 ?? ?? ?? ?? 40 A3 ?? ?? ?? ?? 3D 00 01 00 00 7E 0A 2D 00 01 00 00 A3", 0x5EA165);
    if (!g_window_walk || !g_ui_draw || !pulse_fn || !phase_step) return false;
    g_model_phase = (int32_t*)abs_operand("ky model phase", phase_step + 1);      // 0xDF2D58
    if (!g_model_phase || abs_operand("ky model phase (store)", phase_step + 7) != (uint8_t*)g_model_phase) return false;
    uint8_t* clock = abs_operand("ky UI clock", g_window_walk + 6);          // 0xDF5F00
    uint8_t* clock2 = abs_operand("ky UI clock 2", g_window_walk + 0x11);    // 0xDF5F04
    g_window_table = abs_operand("ky window table", g_window_walk + 0x19);   // 0xDF5F10
    uint8_t* pulse = abs_operand("ky UI pulse", pulse_fn + 0xC);             // 0xD47D0C
    if (!clock || !clock2 || !g_window_table || !pulse) return false;
    if (clock2 != clock + 4 || g_window_table != clock + 0x10) {
        log_line("  X2-UI-018: clocks / window table not laid out as proven (0xDF5F00 / +4 / +0x10) - off");
        return false;
    }
    g_ui_block = clock - 0x1C0;                                              // 0xDF5D40
    g_pulse_block = pulse - 0x1C;                                            // 0xD47CF0
    log_line("  X2-UI-018: window walk gva=0x%08X, UI draw gva=0x%08X, clock gva=0x%08X (0x00DF5F00), window table gva=0x%08X, "
             "blocks gva=0x%08X+0x%X and 0x%08X+0x%X, model phase gva=0x%08X (0x00DF2D58)", rt_to_gva(g_window_walk), rt_to_gva(g_ui_draw), rt_to_gva(clock),
             rt_to_gva(g_window_table), rt_to_gva(g_ui_block), UI_BLOCK_LEN, rt_to_gva(g_pulse_block), PULSE_BLOCK_LEN, rt_to_gva(g_model_phase));
    return true;
}

// First pre(), on the game thread (neither function runs while the hooks go in).
static void pre() {
    if (g_hooked) return;
    g_hooked = true;
    if (!hook_entry("X2-UI-018 window walk", g_window_walk, (void*)&hook_window_walk, (void**)&g_window_walk_orig)) {
        row_UI018.on = 0;
        return;
    }
    if (!hook_entry("X2-UI-018 UI draw", g_ui_draw, (void*)&hook_ui_draw, (void**)&g_ui_draw_orig)) {
        hook_remove(g_window_walk);
        row_UI018.on = 0;
    }
}

static void post() {
    static DWORD last = 0;
    static LONG last_n = 0;
    DWORD now = GetTickCount();
    if (now - last >= 30000 && g_rollbacks != last_n) {
        last = now;
        last_n = g_rollbacks;
        log_line("X2-UI-018: render-only Garment Grid UI draws rolled back %ld (window counters %ld)", g_rollbacks, g_counters_restored);
    }
}

Row row_UI018 = {
    "X2-UI-018",
    "Garment Grid UI (ky windows): its per-draw animation state rolled back after render-only draws",
    1, install, pre, post, 0
};

} // namespace x2
