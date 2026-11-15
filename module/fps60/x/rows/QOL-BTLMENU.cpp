// SPDX-License-Identifier: Apache-2.0
// Modified for the RevoFX FFX module (2026-11): see fps60/NOTICE.txt
// QOL-BTLMENU (gameplay option, default off): Fast Battle Menu. Every battle command window keeps the time since it
// opened at window+0xE0 (start time +0xDC, real-time clock FUN_00641410) and waits on it:
//   - input: the window input callbacks FUN_008a87a0..FUN_008a8b90 call their handler only once +0xE0 >= 0.3 / 0.45 s
//     (FLD [0.3f]; FCOMP [ECX+0xE0] or FLD [ECX+0xE0]; FCOMP [0.45]) - 8 sites
//   - contents: the list draws FUN_0089f370 / FUN_008a75f0 / FUN_008a7dc0 draw the rows only after 0.3 s - 3 sites
//   - open animation: the Switch / Overdrive / Main / Sub window draws slide in for 0.2 s (`if (e < 0.2)`), and the
//     phases after it start at e - 0.2 - 4 + 10 sites
// Each site's constant operand is pointed at a mod-owned 0.0 of the same type (the shared .rdata constants are used
// elsewhere and stay as they are): input is taken from the first tick the window runs, and the window and its rows
// show at once. stop() puts the operands back. Another mod did the input part by removing the FSUB in the window
// state loop (0x89AF3C), which makes +0xE0 the absolute clock for every battle window; this row only moves the
// waits. Details: docs/fps60/rows/QOL-BTLMENU.md
#include "../fps60.h"
namespace fx {

struct Shape { const char* name; const char* pat; int op; bool dbl; double want; int expect; };
static const Shape k_shapes[] = {
    { "input 0.45",       "D9 81 E0 00 00 00 DC 1D ?? ?? ?? ?? DF E0 F6 C4 05", 8, true,  0.45, 5 },
    { "input 0.3",        "D9 05 ?? ?? ?? ?? D8 99 E0 00 00 00 DF E0",          2, false, 0.3,  3 },
    { "rows 0.3 (EBX)",   "D9 05 ?? ?? ?? ?? D8 9B E0 00 00 00 DF E0",          2, false, 0.3,  1 },
    { "rows 0.3 (ESI)",   "D9 05 ?? ?? ?? ?? D8 9E E0 00 00 00 DF E0",          2, false, 0.3,  2 },
    { "open 0.2",         "DD 05 ?? ?? ?? ?? D8 D1 DF E0 F6 C4",                2, true,  0.2,  3 },
    { "open 0.2 (main)",  "DD 05 ?? ?? ?? ?? 99 D8 D1",                         2, true,  0.2,  1 },
    { "phase -0.2",       "E0 00 00 00 DC 25 ?? ?? ?? ??",                      6, true,  0.2,  9 },
    { "phase -0.2 (FLD)", "E0 00 00 00 DD 05 ?? ?? ?? ?? 83 C4 30 DC E9",       6, true,  0.2,  1 },
};
enum { N_SHAPES = sizeof(k_shapes) / sizeof(k_shapes[0]), MAX_SITES = 32 };

static const float  g_zero_f = 0.0f;
static const double g_zero_d = 0.0;
static uint8_t*  g_op[MAX_SITES];                // address of each site's absolute operand
static uint32_t  g_orig[MAX_SITES];
static bool      g_dbl[MAX_SITES];
static int       g_n = 0;
static bool      g_patched = false;

// scan state for accept(): the shape being scanned and the sites it found
static const Shape* g_cur = nullptr;
static uint8_t*  g_found[MAX_SITES];
static int       g_nfound = 0;

static bool accept(const uint8_t* at) {
    __try {
        const uint8_t* c = read_abs(at + g_cur->op);
        double v = g_cur->dbl ? *(const double*)c : (double)*(const float*)c;
        if (v < g_cur->want - 1e-6 || v > g_cur->want + 1e-6) return false;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    if (g_nfound < MAX_SITES) g_found[g_nfound++] = (uint8_t*)at;
    return true;
}

static bool install() {
    g_n = 0;
    for (int s = 0; s < N_SHAPES; s++) {
        const Shape& sh = k_shapes[s];
        g_cur = &sh;
        g_nfound = 0;
        int hits = 0;
        sig_scan_module(exe_base(), exe_size(), sh.pat, &hits, accept);
        if (hits != sh.expect || g_nfound != sh.expect || g_n + g_nfound > MAX_SITES) {
            log_line("  QOL-BTLMENU: %s: %d sites (expected %d) - refused", sh.name, hits, sh.expect);
            return false;
        }
        for (int i = 0; i < g_nfound; i++) {
            g_op[g_n] = g_found[i] + sh.op;
            g_orig[g_n] = *(uint32_t*)g_op[g_n];
            g_dbl[g_n] = sh.dbl;
            log_line("  QOL-BTLMENU: %-16s gva=0x%08X (constant %.2f at 0x%08X)", sh.name, rt_to_gva_exe(g_op[g_n] - 2),
                     sh.want, rt_to_gva_exe((void*)(uintptr_t)g_orig[g_n]));
            g_n++;
        }
    }
    log_line("  QOL-BTLMENU: %d operand sites", g_n);
    return true;
}

static void unpatch() {
    for (int i = 0; i < g_n; i++) patch_write(g_op[i], &g_orig[i], 4);
    g_patched = false;
}

static void pre() {
    if (g_patched) return;
    g_patched = true;
    bool ok = true;
    for (int i = 0; i < g_n && ok; i++) {
        uint32_t v = (uint32_t)(uintptr_t)(g_dbl[i] ? (const void*)&g_zero_d : (const void*)&g_zero_f);
        ok = patch_write(g_op[i], &v, 4);
    }
    if (!ok) {
        unpatch();
        log_line("QOL-BTLMENU: operand patch failed - row off");
        row_QOLBTLMENU.on = 0;
        return;
    }
    log_line("QOL-BTLMENU: %d battle menu waits set to 0", g_n);
}

static void stop() {
    if (g_patched) { unpatch(); log_line("QOL-BTLMENU: battle menu waits back to the game's"); }
}

Row row_QOLBTLMENU = {
    "QOL-BTLMENU",
    "gameplay option: Fast Battle Menu - battle command windows open and take input at once (no 0.2-0.45 s waits)",
    0, install, pre, nullptr, 0, stop
};
} // namespace fx
