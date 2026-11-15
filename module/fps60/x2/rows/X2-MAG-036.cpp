// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-MAG-036: battle effect objects' particles (auras and glows on characters and enemies) advance once per game tick.
//
// Background. Besides spell DLLs, battle effects are driven by "OPU" effect objects in FFX-2.exe: small programs of
// opcodes (X2-MAG-025) that own a particle program each. The opcode handler for such an object (FUN_006ba5d0) runs on
// every walk of the effect units, the walk in the main loop's second loop that runs on every invocation, and steps the
// object's particle program through FUN_006a5f80 -> Ppp_ProgramStep. X2-MAG-006 leaves steps called from FFX-2.exe at
// the game's own pause state, so at 60 fps these particles advanced on render-only invocations too: double speed.
// X2-DIAG-PPPRATE showed it in a plain Thunder Plains battle (return 0x6A5FD7: clock advanced on 100% of render-only
// steps).
//
// The fix. That call (return address 0x6A5FD7) is registered with X2-MAG-006 as a paused caller: on render-only
// invocations the program steps with the particle pause flag set (drawn in place, not advanced), fresh-program rule
// included, as spell particles. Logic invocations and stock pacing are unchanged. The program still ends only on a
// logic step (a paused step never reports an end).
// Evidence: re/ledger/LENS-GLOW.md §3 (the call chain, S), re/ledger/REPORTS-1030.md (R1, R3, R7).
#include "../x2.h"

namespace x2 {

static bool install() {
    uint8_t* ppp = mag006_entry();
    if (!ppp) {
        log_line("  X2-MAG-036: needs X2-MAG-006 (its Ppp_ProgramStep hook) - off");
        return false;
    }
    // FUN_006a5f80: PUSH 0x30; PUSH EDI; MOV [EAX],imm32; CALL Ppp_ProgramStep; MOV ESI,EAX; ADD ESP,8; TEST ESI,ESI; JZ
    uint8_t* at = sig_find("X2-MAG-036 effect object step call (FUN_006a5f80)",
        "6A 30 57 C7 00 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B F0 83 C4 08 85 F6 74", 0x6A5FC9);
    if (!at) return false;
    if (call_target(at + 9) != ppp) {
        log_line("  X2-MAG-036: CALL at gva=0x%08X is not Ppp_ProgramStep - off", rt_to_gva(at + 9));
        return false;
    }
    if (!mag006_pause_exe_caller(at + 0xE)) {
        log_line("  X2-MAG-036: X2-MAG-006 caller list full - off");
        return false;
    }
    log_line("  X2-MAG-036: effect object step call gva=0x%08X (return 0x%08X, expected 0x006A5FD7) paused on render-only "
             "invocations via X2-MAG-006", rt_to_gva(at + 9), rt_to_gva(at + 0xE));
    return true;
}

Row row_MAG036 = {
    "X2-MAG-036",
    "battle effect objects' particles (OPU, FUN_006a5f80): paused on render-only invocations",
    1, install, nullptr, nullptr, 0
};

} // namespace x2
