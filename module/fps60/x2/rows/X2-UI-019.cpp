// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// X2-UI-019: scrolling the Config page plays the cursor sound once, not twice on top of itself.
//
// Background. The Config page's input step (0x788C40) plays the cursor sound (FUN_00744710(0x80000001)) when the
// cursor moves up or down. When the move leaves the 9-row window, the same call goes on into the scroll state (4 or 6,
// via its state loop) and plays the sound again, in the same frame: two identical sounds on top of each other, twice
// as loud. The stock page rarely scrolls; with the mod's extra Config rows (X2-CFG-MENU) it scrolls all the time.
//
// The fix. The two scroll-state CALLs (states 4 and 6, entered only from the up/down move that already played the
// sound) go to a stub that plays nothing. Page jumps (states 8 / 10) play their one sound as before.
// Evidence: re/ledger/CFG-MENU-X2.md (CFGX2-011 and the R9 section), re/ledger/REPORTS-1030.md (R9).
#include "../x2.h"

namespace x2 {

extern Row row_UI019;

typedef uint32_t(__cdecl* menu_sound_fn)(uint32_t sound);   // FUN_00744710: cdecl, plain RET

static uint8_t*      g_sites[2] = {};                       // CALLs in states 4 and 6 (0x788EA8 / 0x788F12)
static menu_sound_fn g_menu_sound = nullptr;
static bool          g_patched = false;
static volatile LONG g_skipped = 0;

static uint32_t __cdecl hook_scroll_sound(uint32_t sound) {
    if (!row_UI019.on || g_disabled) return g_menu_sound(sound);
    if (InterlockedIncrement(&g_skipped) == 1) log_line("X2-UI-019: first repeated scroll sound skipped");
    return 0;
}

static bool install() {
    // Config input step: PUSH EBP; MOV EBP,ESP; SUB ESP,0x1C; CALL; CMP [..],EAX; JL; PUSH EDI
    uint8_t* step = sig_find("X2-UI-019 Config input step (0x788C40)",
        "55 8B EC 83 EC 1C E8 ?? ?? ?? ?? 39 05 ?? ?? ?? ?? 0F 8C ?? ?? ?? ?? 57", 0x788C40);
    if (!step) return false;
    // the up / down moves (state 3) and the two scroll states each PUSH 0x80000001; CALL FUN_00744710
    uint8_t* move_up = step + 0xEB, *move_down = step + 0x13F;
    g_sites[0] = step + 0x268;
    g_sites[1] = step + 0x2D2;
    uint8_t* sound = call_target(g_sites[0]);
    bool ok = sound && in_image(sound) && sig_match(sound, "55 8B EC 8B 4D 08 8B C1 C1 E8 1F F7 D0 83 E0 01 80 3D");
    uint8_t* const all_sites[4] = { move_up, move_down, g_sites[0], g_sites[1] };
    for (uint8_t* site : all_sites)
        ok = ok && sig_match(site - 5, "68 01 00 00 80 E8") && call_target(site) == sound;
    if (!ok) {
        log_line("  X2-UI-019: cursor sound CALLs not where proven - off");
        return false;
    }
    g_menu_sound = (menu_sound_fn)sound;
    log_line("  X2-UI-019: scroll sound CALLs gva=0x%08X / 0x%08X (0x00788EA8 / 0x00788F12) -> stub", rt_to_gva(g_sites[0]),
             rt_to_gva(g_sites[1]));
    return true;
}

static void pre() {
    if (g_patched) return;
    g_patched = true;
    if (!patch_call_rel32(g_sites[0], (void*)g_menu_sound, (void*)&hook_scroll_sound)) { row_UI019.on = 0; return; }
    if (!patch_call_rel32(g_sites[1], (void*)g_menu_sound, (void*)&hook_scroll_sound)) {
        patch_call_rel32(g_sites[0], (void*)&hook_scroll_sound, (void*)g_menu_sound);
        row_UI019.on = 0;
    }
}

Row row_UI019 = {
    "X2-UI-019",
    "Config page scroll: cursor sound once (states 4 / 6 repeated it in the same frame)",
    1, install, pre, nullptr, 0
};

} // namespace x2
