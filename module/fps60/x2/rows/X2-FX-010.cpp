// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-FX-010: map particle effects (flames, smoke, rain, lights) advance once per game tick, not once per drawn frame.
//
// Background. Particle effects that belong to the map, not to a spell, are "field particle programs". The field
// particle loop steps each of them through Ppp_FieldProgramStep (0x7FEF20) on every invocation of the main loop, the
// render-only ones included, so at 60 fps they ran at double speed (a torch flame flickering twice as fast). The step
// honours the engine's particle pause flag the same way Ppp_ProgramStep does (X2-MAG-006): while the flag is set, the
// particles are drawn but their clock, age and ops do not advance.
//
// The fix. On render-only invocations a field program steps with the pause flag set. X2-MAG-006 owns the hook on the
// field step and asks fx010_pause_field_step() which steps to pause. A program that has never advanced (clock and
// previous clock both 0) steps unpaused, or its first draw would show unset state (FFX lesson).
// Two parts that the pause alone does not cover (each with its own sub-switch, on):
//   X2-FX-010-SPAWN: spawning ignores the pause flag and starts every particle whose start time the clock has reached.
//     The logic step advances the clock after its spawn, so a paused step in between started next tick's particles
//     half a tick early, in their birth state. The step's spawn CALL skips while we pause it.
//   X2-FX-010-KEYADD: one draw op (0x847F30) adds two rates into the particle when its key matches and has no pause
//     test of its own (its sibling op does), so a paused call added them a second time: an effect that alternates
//     brightness every frame (Besaid light shafts in FFX). While the pause flag is set the key is made not to match for
//     that call. This also covers spell particle steps X2-MAG-006 pauses.
// Port of FFX FX-010 (+SPAWN, +KEYADD). Evidence: re/ledger/FX010-X2.md.
#include "../x2.h"

namespace x2 {

extern Row row_FX010;

typedef void(__cdecl* spawn_fn)(void* prog);                     // Ppp_Spawn 0x7FFD00: cdecl(prog), plain RET
typedef int(__cdecl* keyadd_fn)(int part, int data, int c, int d); // op 0x847F30: cdecl, plain RET 0x848044

static uint8_t*          g_spawn_site = nullptr;   // CALL Ppp_Spawn inside the field step (0x7FEFAA)
static spawn_fn          g_spawn = nullptr;
static uint8_t*          g_keyadd_op = nullptr;
static keyadd_fn         g_keyadd_orig = nullptr;
static volatile int32_t* g_pause_flag = nullptr;   // the engine's particle pause flag (0x01CF579C)
static int               g_opt_spawn = 1, g_opt_keyadd = 1;
static bool              g_patches_done = false;
static int               g_pausing_field_step = 0; // game thread: inside a field step this row paused
static volatile LONG     g_paused_steps = 0, g_fresh_steps = 0, g_spawns_skipped = 0, g_keyadds_held = 0;

// The decision, without game state: pause a field step on a render-only invocation unless the program is fresh.
static bool field_step_should_pause(int invocation_kind, int32_t clock, int32_t previous_clock) {
    if (invocation_kind != INV_RENDER_ONLY) return false;
    return !(clock == 0 && previous_clock == 0);
}

bool fx010_pause_field_step(void* prog) {
    if (!row_FX010.on || g_disabled || !g_patches_done) return false;
    if (pacing_kind() != INV_RENDER_ONLY) return false;
    const int32_t* p = (const int32_t*)prog;
    if (!field_step_should_pause(INV_RENDER_ONLY, p[2], p[3])) {   // +8 clock, +0xC previous clock
        InterlockedIncrement(&g_fresh_steps);
        return false;
    }
    InterlockedIncrement(&g_paused_steps);
    return true;
}
void fx010_pausing_field_step(bool on) { g_pausing_field_step = on ? 1 : 0; }

static void __cdecl hook_spawn(void* prog) {
    if (g_pausing_field_step) { InterlockedIncrement(&g_spawns_skipped); return; }
    g_spawn(prog);
}

// The keyed add runs when the op's key (first word of its data) equals the particle's +0xC.
static int __cdecl hook_keyadd(int part, int data, int c, int d) {
    int32_t* key = nullptr;
    int32_t saved = 0;
    if (row_FX010.on && *g_pause_flag != 0) {
        __try {
            int32_t* k = (int32_t*)(intptr_t)data;
            if (*k == *(int32_t*)(intptr_t)(part + 0xC)) { key = k; saved = *k; *k = saved + 1; }
        } __except (EXCEPTION_EXECUTE_HANDLER) { key = nullptr; }
    }
    int r = g_keyadd_orig(part, data, c, d);
    if (key) {
        *key = saved;
        if (InterlockedIncrement(&g_keyadds_held) == 1) log_line("X2-FX-010-KEYADD: first keyed add held on a paused call");
    }
    return r;
}

static bool install() {
    if (!mag006_entry()) {
        log_line("  X2-FX-010: needs X2-MAG-006 (it owns the field step hook) - off");
        return false;
    }
    // field step: ... POP EDI; PUSH ESI; MOV [0x01CF57A4],0; CALL Ppp_Spawn; ADD ESP,4; CMP [0x01CF57A4],0; JE
    uint8_t* m = sig_find("X2-FX-010 field step spawn CALL",
        "5F 56 C7 05 ?? ?? ?? ?? 00 00 00 00 E8 ?? ?? ?? ?? 83 C4 04 83 3D ?? ?? ?? ?? 00 74 17 F6 C3 20", 0x7FEF9E);
    if (!m) return false;
    g_spawn_site = m + 0xC;
    uint8_t* spawn = call_target(g_spawn_site);
    // Ppp_Spawn prologue: PUSH EBP; MOV EBP,ESP; PUSH EBX; MOV EBX,[EBP+8]; PUSH EDI; XOR EDI,EDI; CMP [EBX+0x18],EDI
    if (!spawn || !sig_match(spawn, "55 8B EC 53 8B 5D 08 57 33 FF 39 7B 18")) {
        log_line("  X2-FX-010: spawn CALL target is not Ppp_Spawn - off");
        return false;
    }
    g_spawn = (spawn_fn)spawn;
    // the step's clock tail pause test: CMP dword [flag],0 at m+0x74 (0x7FF012), the flag X2-MAG-006 uses
    uint8_t* cmp = m + 0x74;
    if (!sig_match(cmp, "83 3D ?? ?? ?? ?? 00 75")) {
        log_line("  X2-FX-010: field step pause test not at 0x7FF012 - off");
        return false;
    }
    g_pause_flag = (volatile int32_t*)abs_operand("particle pause flag", cmp + 2);
    if (!g_pause_flag) return false;
    g_opt_spawn = config_row("X2-FX-010-SPAWN", 1);
    g_opt_keyadd = config_row("X2-FX-010-KEYADD", 1);
    if (g_opt_keyadd) {
        // aligned-stack prologue, then MOV EDI,[EBX+8]; LEA ESI,[EDI+0xA0]; ADD ESI,EAX; MOV EAX,[ECX]; CMP EAX,[EDI+0xC];
        // JNE +0x32; FLD [ECX+8] (the keyed add, no pause test)
        g_keyadd_op = sig_find("X2-FX-010-KEYADD keyed draw op",
            "53 8B DC 83 EC 08 83 E4 F0 83 C4 04 55 8B 6B 04 89 6C 24 04 8B EC 83 EC 38 A1 ?? ?? ?? ?? 33 C5 89 45 FC "
            "8B 43 10 8B 4B 0C 8B 40 0C 56 8B 00 57 8B 7B 08 8D B7 A0 00 00 00 03 F0 8B 01 3B 47 0C 75 32 D9 41 08",
            0x847F30);
        if (!g_keyadd_op) { log_line("  X2-FX-010-KEYADD: op not found - sub-switch off"); g_opt_keyadd = 0; }
    }
    log_line("  X2-FX-010: field step spawn CALL gva=0x%08X (0x007FEFAA) -> Ppp_Spawn gva=0x%08X, pause flag gva=0x%08X; "
             "SPAWN %s, KEYADD %s", rt_to_gva(g_spawn_site), rt_to_gva(spawn), rt_to_gva((const void*)g_pause_flag),
             g_opt_spawn ? "on" : "off", g_opt_keyadd ? "on" : "off");
    return true;
}

// First pre(), on the game thread (the step never runs while the patches are written).
static void pre() {
    if (g_patches_done) return;
    g_patches_done = true;
    if (g_opt_spawn && !patch_call_rel32(g_spawn_site, (void*)g_spawn, (void*)&hook_spawn)) {
        log_line("X2-FX-010-SPAWN: CALL patch failed - sub-switch off");
        g_opt_spawn = 0;
    }
    if (g_opt_keyadd) {
        if (hook_entry("X2-FX-010-KEYADD", g_keyadd_op, (void*)&hook_keyadd, (void**)&g_keyadd_orig))
            log_line("X2-FX-010-KEYADD: draw op gva=0x%08X hooked: keyed add skipped on paused calls", rt_to_gva(g_keyadd_op));
        else g_opt_keyadd = 0;
    }
}

static void post() {
    static DWORD last = 0;
    static LONG last_paused = 0;
    DWORD now = GetTickCount();
    if (now - last >= 10000 && g_paused_steps != last_paused) {
        last = now;
        last_paused = g_paused_steps;
        log_line("X2-FX-010: field particle steps paused on render-only %ld, fresh (unpaused) %ld, spawns skipped %ld",
                 g_paused_steps, g_fresh_steps, g_spawns_skipped);
    }
}

Row row_FX010 = {
    "X2-FX-010",
    "field particle programs: pause flag on render-only invocations (draw still runs), + SPAWN, KEYADD",
    1, install, pre, post, 0
};

} // namespace x2
