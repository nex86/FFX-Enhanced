// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-MENU-003: menu particle effects (the Garment Grid's moving line, cursor sparkles) advance once per game tick.
//
// Background. Menus draw their small particle effects through their own particle system ("ky:ParSystem",
// Fx_MenuParticleDrawGroup 0x5E9440). It walks its 32 effect slots and steps each live one with Ppp_ProgramStep, and
// that step both advances the effect and draws it. It runs on every pass of the menu loop and of the battle menu draws,
// so at 60 fps (field main menu under X2-MENU-002, battle menus) these effects ran at double speed. X2-MAG-006 leaves
// steps called from FFX-2.exe at the game's own pause state, so it did not cover them.
//
// The fix. The menu system's step call (return address 0x5E95C0) is registered with X2-MAG-006 as a paused caller: on
// render-only invocations its programs step with the particle pause flag set (drawn in place, not advanced), the same
// handling as spell particles, fresh-program rule included. Under stock pacing (menus X2-MENU-001 holds) nothing
// changes.
// Evidence: re/ledger/ITEM-CURSOR.md §6 (the advance per draw, S), re/ledger/REPORTS-1030.md (R2, R4).
#include "../x2.h"

namespace x2 {

static bool install() {
    uint8_t* ppp = mag006_entry();
    if (!ppp) {
        log_line("  X2-MENU-003: needs X2-MAG-006 (its Ppp_ProgramStep hook) - off");
        return false;
    }
    // Fx_MenuParticleDrawGroup: PUSH 0x30; PUSH EBX; MOV [EAX],imm32; CALL Ppp_ProgramStep; ADD ESP,0x18; TEST EAX,EAX; JZ
    uint8_t* at = sig_find("X2-MENU-003 menu particle step call (Fx_MenuParticleDrawGroup)",
        "6A 30 53 C7 00 ?? ?? ?? ?? E8 ?? ?? ?? ?? 83 C4 18 85 C0 74 31", 0x5E95B2);
    if (!at) return false;
    if (call_target(at + 9) != ppp) {
        log_line("  X2-MENU-003: CALL at gva=0x%08X is not Ppp_ProgramStep - off", rt_to_gva(at + 9));
        return false;
    }
    if (!mag006_pause_exe_caller(at + 0xE)) {
        log_line("  X2-MENU-003: X2-MAG-006 caller list full - off");
        return false;
    }
    log_line("  X2-MENU-003: menu particle step call gva=0x%08X (return 0x%08X, expected 0x005E95C0) paused on render-only "
             "invocations via X2-MAG-006", rt_to_gva(at + 9), rt_to_gva(at + 0xE));
    return true;
}

Row row_MENU003 = {
    "X2-MENU-003",
    "menu particle effects (ky:ParSystem: Garment Grid line, cursor sparkles): paused on render-only invocations",
    1, install, nullptr, nullptr, 0
};

} // namespace x2
