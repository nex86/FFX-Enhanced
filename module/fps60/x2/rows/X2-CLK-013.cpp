// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-CLK-013: voiced in-game scenes that follow a voice timeline ("SyncData") run at stock pacing, so lips stay in sync.
//
// Background. Some voiced scenes run on a wall-clock timeline instead of the time step: while the SyncData mode flag
// (0xDF6508) is 1, Sg_WaitFrameSync (0x606900) fetches one timeline entry per invocation (Sg_FetchSyncEntry 0x6067F0),
// waits until that entry's time, and adds catch-up invocations when it is late. At 60 Hz the render-only invocations
// call it too and consume entries meant for game ticks, so the scene's animation (mouths) drifts ahead of the voice.
//
// The fix (port of FFX CLK-013). While the flag is 1 the row holds stock pacing (30 fps, the original timing); the
// hold ends when the timeline ends (its last entry clears the flag). X2-CLK-013-HOLD=0 only logs the scene start / end.
// Evidence: re/ledger/TURBO-X2.md "Voice" (the timeline, S), re/ledger/REPORTS-1030.md (R5).
#include "../x2.h"

namespace x2 {

extern Row row_CLK013;

static volatile int32_t* g_sync_mode = nullptr;   // 0xDF6508: 1 = SyncData timeline running
static bool g_active = false, g_holding = false;
static int  g_opt_hold = 1;

static void release_hold() {
    g_active = false;
    if (!g_holding) return;
    g_holding = false;
    pacing_hold_stock("X2-CLK-013 (SyncData scene)", false);
}

static void pre() {
    bool on = *g_sync_mode == 1;
    if (on == g_active) return;
    g_active = on;
    log_line("X2-CLK-013: SyncData timeline %s", on ? "starts - stock pacing while it runs" : "ends");
    if (!g_opt_hold || on == g_holding) return;
    g_holding = on;
    pacing_hold_stock("X2-CLK-013 (SyncData scene)", on);
}

static bool install() {
    // Sg_WaitFrameSync: PUSH EBP; MOV EBP,ESP; SUB ESP,0x10; FLD [carry]; FLD ST0; FLD [EBP+8]; FLD ST0; FADDP ST2
    uint8_t* f = sig_find("X2-CLK-013 Sg_WaitFrameSync",
        "55 8B EC 83 EC 10 D9 05 ?? ?? ?? ?? D9 C0 D9 45 08 D9 C0 DE C2 DD 05", 0x606900);
    if (!f) return false;
    // +0x4B CMP [backlog],0; JNZ / +0x67 CMP [mode],1; JNZ (the SyncData branch)
    if (!sig_match(f + 0x4B, "83 3D ?? ?? ?? ?? 00 0F 85") || !sig_match(f + 0x67, "83 3D ?? ?? ?? ?? 01 0F 85") ||
        abs_operand("backlog", f + 0x4D) != (uint8_t*)g_backlog_count) {
        log_line("  X2-CLK-013: Sg_WaitFrameSync not as proven - off");
        return false;
    }
    g_sync_mode = (volatile int32_t*)abs_operand("SyncData mode", f + 0x69);
    if (!g_sync_mode) return false;
    g_opt_hold = config_row("X2-CLK-013-HOLD", 1);
    log_line("  X2-CLK-013: Sg_WaitFrameSync gva=0x%08X, SyncData mode gva=0x%08X (0x00DF6508); %s", rt_to_gva(f),
             rt_to_gva((const void*)g_sync_mode), g_opt_hold ? "stock pacing while a SyncData scene runs" : "log only (HOLD=0)");
    return true;
}

Row row_CLK013 = {
    "X2-CLK-013",
    "SyncData (voice timeline) scenes hold stock pacing: lip sync",
    1, install, pre, nullptr, 0, 0, release_hold
};

} // namespace x2
