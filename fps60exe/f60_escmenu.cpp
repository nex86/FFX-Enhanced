// f60_escmenu.cpp - a "Frame Rate" row in the games' own option menu (Esc menu > Video), between VSync and Texture Quality,
// in FFX_enh.exe and FFX-2_enh.exe. Written for FFX_enh.exe (not part of FFX-60fps); docs\FPS60_EXE.md section 8.
//
// The Esc menu is escmenu.swf (Iggy), the same file in both games. It is data driven: the game fills the Video page with
// addVideoPageItem(index, type, label, current text, values, default) through its own CallSWF helper, the page sends
// every item's value back with EscMenuVideoSettingRefresh (one array, an S32 per item), and asks for help texts with
// EscMenuHelpTextRefresh(page, item), which the game answers from a (help text id, page, item) table. So:
//   - CallSWF (cdecl(menu, name, 6 x IggyDataValue)): when the game adds item ROW (Texture Quality) our row is added
//     first at ROW, and every item from ROW on is passed one index further down.
//   - the value parser (thiscall(menu; int out[N], args), N = 12): reads N + 1 values, ours out of the middle.
//   - the help table (.data): the Video page's items from ROW on move one index down; the help callback gets a table
//     entry of ours for item ROW (the handler finds none and passes NULL), and the lockit text function answers its id.
//   - escmenu.swf: its MSAA / HDAO exclusion names those two rows by index (EscPage.VideoSettingOptionMSAA = 7,
//     ...HDAO = 11, static const ints). When the game hands the file to IggyPlayerCreateFromMemory it is inflated,
//     the two constants are pointed at the int pool's 8 and 12, and the result goes to Iggy as a CWS file with stored
//     (uncompressed) zlib blocks. Nothing is written to disk; the archive stays as it is.
// Every address is found at run time from the menu's own strings (no fixed addresses): see esc_find. When anything is
// missing or does not look as expected, nothing is installed and the menu stays as shipped.
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include "../module/fps60/minhook/MinHook.h"
#include "../module/fps60/f60_compat.h"     // __try / __except for clang (a vectored handler; f60_seh.cpp)
#include "f60_texts.h"

extern "C" void f60_menu_log(const char *text);   // f60_host.cpp: one line into the log
extern "C" int  f60_menu_fps(void);               // the frame rate in force (30 = the game's own timing)
extern "C" void f60_menu_set(int fps);            // the player chose another rate in the menu

namespace {

enum { ROW = 4, HELP_ID = 0x7F00, MAXN = 24 };

void logf(const char *fmt, ...) { char b[600]; va_list a; va_start(a, fmt); vsnprintf(b, sizeof b, fmt, a); va_end(a); f60_menu_log(b); }

// ---- the game's code: what esc_find resolves ----
struct IggyVal { uint32_t type, pad, a, b; };          // IggyDataValue: 4 = number (double at +8), 5 = UTF-8 (+8 text, +12 length), 9 = value ref (+8)
static_assert(sizeof(IggyVal) == 16, "IggyDataValue");
typedef void (__cdecl *callswf_t)(void *, const char *, IggyVal, IggyVal, IggyVal, IggyVal, IggyVal, IggyVal);
typedef void (__fastcall *parse_t)(void *, void *, int *, void *);
typedef void *(__fastcall *getctx_t)(void *, void *);
typedef void (__cdecl *helpcb_t)(void *);
typedef int (__cdecl *lockit_t)(int, char *, int);
typedef void *(__stdcall *create_t)(const void *, uint32_t, void *);

callswf_t g_callswf, o_callswf; parse_t g_parse, o_parse; getctx_t g_getctx; helpcb_t g_cb, o_cb; lockit_t g_lockit, o_lockit; create_t g_create, o_create;
volatile uint16_t *g_page, *g_item; uint16_t *g_table; int g_tcount, g_n, g_vpage, g_probe_id;
bool g_on, g_added; int g_lang;
int g_vals[5], g_nvals;
const char *g_valtext[5];
const char *text_of(int fps) { return fps == 30 ? "30 FPS (Original)" : fps == 60 ? "60 FPS" : fps == 90 ? "90 FPS" : fps == 120 ? "120 FPS" : "240 FPS"; }
void *g_refresh_ret;   // return address of the parser call inside the EscMenuVideoSettingRefresh handler

// ---- Iggy (iggy_w32.dll exports, the calls the game makes itself) ----
typedef uintptr_t (__stdcall *ref_create_array_t)(void *, int);
typedef void (__stdcall *path_from_ref_t)(void *, void *, uintptr_t);
typedef void (__stdcall *path_make_array_t)(void *, void *, int);
typedef void (__stdcall *path_set_index_t)(void *, int);
typedef int (__stdcall *set_string_t)(void *, int, int, const char *, int);
typedef int (__stdcall *get_s32_t)(void *, int, int, int *);
typedef uintptr_t (__stdcall *ref_from_temp_t)(void *, uintptr_t, int);
typedef void (__stdcall *ref_free_t)(void *, uintptr_t);
ref_create_array_t IggyValueRefCreateArray; path_from_ref_t IggyValuePathFromRef; path_make_array_t IggyValuePathMakeArrayRef;
path_set_index_t IggyValuePathSetArrayIndex; set_string_t IggyValueSetStringUTF8RS; get_s32_t IggyValueGetS32RS;
ref_from_temp_t IggyValueRefFromTempRef; ref_free_t IggyValueRefFree;

IggyVal num(double d) { IggyVal v = { 4, 0, 0, 0 }; memcpy(&v.a, &d, 8); return v; }
IggyVal str(const char *s) { IggyVal v = { 5, 0, (uint32_t)(uintptr_t)s, (uint32_t)strlen(s) }; return v; }
double num_of(const IggyVal &v) { double d; memcpy(&d, &v.a, 8); return d; }

// The game's text language: its own "Texture Quality" label compared with the eight it can be.
void detect_lang() {
    char b[256] = { 0 };
    __try { o_lockit(g_probe_id, b, sizeof b); } __except (EXCEPTION_EXECUTE_HANDLER) { b[0] = 0; }
    int l = 0;
    int m = -1; for (int i = 0; i < 8; i++) if (!strcmp(b, k_probe[i])) { m = i; break; }
    l = m < 0 ? 0 : m;
    static int logged = -2; if (m != logged) { logged = m; if (m < 0) logf("Esc menu: text language not recognised (\"%.60s\") - English", b); else logf("Esc menu: text language %d (%.60s)", m, b); }
    g_lang = l;
}

int cur_index() {
    int f = f60_menu_fps(), best = 0;
    for (int i = 0; i < g_nvals; i++) if (g_vals[i] <= f) best = i;
    return best;
}

// our row, added right before the game's item ROW
void add_row(void *menu) {
    void *ctx = g_getctx(menu, 0);
    uintptr_t ref = IggyValueRefCreateArray(ctx, g_nvals);
    alignas(8) char path[64] = { 0 }, arr[64] = { 0 };
    IggyValuePathFromRef(path, ctx, ref);
    IggyValuePathMakeArrayRef(arr, path, 0);
    for (int i = 0; i < g_nvals; i++) { IggyValuePathSetArrayIndex(arr, i); IggyValueSetStringUTF8RS(arr, 0, 0, g_valtext[i], (int)strlen(g_valtext[i])); }
    IggyVal v5 = { 9, 0, (uint32_t)ref, 0 };
    o_callswf(menu, "addVideoPageItem", num(ROW), str("spinner"), str(k_label[g_lang]), str(g_valtext[cur_index()]), v5, num(-1));
    IggyValueRefFree(g_getctx(menu, 0), ref);
}

void __cdecl h_callswf(void *menu, const char *name, IggyVal a1, IggyVal a2, IggyVal a3, IggyVal a4, IggyVal a5, IggyVal a6) {
    if (g_on && name && a1.type == 4 && !strcmp(name, "addVideoPageItem")) {
        int idx = (int)num_of(a1);
        if (idx == 0) { g_added = false; detect_lang(); }
        if (idx == ROW && !g_added) {
            bool ok = true;
            __try { add_row(menu); } __except (EXCEPTION_EXECUTE_HANDLER) { ok = false; }
            static bool once; if (!once || !ok) { once = true; logf(ok ? "Esc menu: Frame Rate row added (Video page, row %d)" : "Esc menu: adding the Frame Rate row FAULTED", ROW); }
            g_added = ok;
        }
        if (g_added && idx >= ROW && idx < MAXN) a1 = num(idx + 1);
    }
    o_callswf(menu, name, a1, a2, a3, a4, a5, a6);
}

void __fastcall h_parse(void *menu, void *edx, int *out, void *args) {
    void *ret = __builtin_return_address(0);
    if (!g_on || !g_added) { o_parse(menu, edx, out, args); return; }
    int v[MAXN] = { 0 }, n = g_n + 1; bool ok = true;
    __try {
        void *ctx = g_getctx(menu, 0);
        uintptr_t ref = IggyValueRefFromTempRef(ctx, *(uintptr_t *)((char *)args + 4), 1);
        alignas(8) char path[64] = { 0 }, arr[64] = { 0 };
        IggyValuePathFromRef(path, g_getctx(menu, 0), ref);
        IggyValuePathMakeArrayRef(arr, path, 0);
        for (int i = 0; i < n; i++) { IggyValuePathSetArrayIndex(arr, i); IggyValueGetS32RS(arr, 0, 0, &v[i]); }
        IggyValueRefFree(g_getctx(menu, 0), ref);
    } __except (EXCEPTION_EXECUTE_HANDLER) { ok = false; }
    if (!ok) { logf("Esc menu: reading the Video values FAULTED - the game's own parser takes over"); o_parse(menu, edx, out, args); return; }
    for (int i = 0; i < g_n; i++) out[i] = v[i < ROW ? i : i + 1];
    int k = v[ROW]; bool changed = k >= 0 && k < g_nvals && g_vals[k] != f60_menu_fps();
    // The parser has two callers. EscMenuOnConfirmKey only compares the values with the game's settings: no difference =
    // the menu is told to cancel and every row goes back to what it showed when the page opened; a difference = the
    // "apply?" popup, and on yes the menu confirms its rows and sends EscMenuVideoSettingRefresh, whose handler applies.
    // So a change of our row alone has to look like a difference there (the array is only compared and freed), and the
    // rate is switched and saved in the refresh handler only.
    if (ret != g_refresh_ret) { if (changed) out[0] ^= 0x40000000; return; }
    if (changed) { logf("Esc menu: Frame Rate %d fps chosen", g_vals[k]); f60_menu_set(g_vals[k]); }
}

void __cdecl h_cb(void *entry) {
    static uint16_t fake[4];
    if (!entry && g_on && g_added && *g_page == g_vpage && *g_item == ROW) {
        fake[0] = HELP_ID; fake[1] = 0; fake[2] = (uint16_t)g_vpage; fake[3] = ROW; entry = fake;
    }
    o_cb(entry);
}

int __cdecl h_lockit(int id, char *dest, int cap) {
    if (id == HELP_ID && dest && cap > 0) {
        const char *t = k_help[g_lang]; int n = (int)strlen(t); if (n > cap - 1) n = cap - 1;
        memcpy(dest, t, n); dest[n] = 0; return n;
    }
    return o_lockit(id, dest, cap);
}

// ---- escmenu.swf: inflate, patch two ints, hand it on as zlib with stored blocks ----
struct Bits { const uint8_t *p, *e; uint32_t buf; int cnt; bool bad; };
int need(Bits &s, int n) {
    while (s.cnt < n) { if (s.p >= s.e) { s.bad = true; return 0; } s.buf |= (uint32_t)*s.p++ << s.cnt; s.cnt += 8; }
    int v = (int)(s.buf & ((1u << n) - 1)); s.buf >>= n; s.cnt -= n; return v;
}
struct Huff { uint16_t count[16], sym[320]; };
bool build(Huff &h, const uint8_t *len, int n) {
    memset(h.count, 0, sizeof h.count);
    for (int i = 0; i < n; i++) h.count[len[i]]++;
    h.count[0] = 0; uint16_t offs[16]; offs[1] = 0;
    for (int i = 1; i < 15; i++) offs[i + 1] = offs[i] + h.count[i];
    for (int i = 0; i < n; i++) if (len[i]) h.sym[offs[len[i]]++] = (uint16_t)i;
    return true;
}
int decode(Bits &s, const Huff &h) {
    int code = 0, first = 0, index = 0;
    for (int l = 1; l < 16; l++) {
        code |= need(s, 1); int c = h.count[l];
        if (code - c < first) return h.sym[index + (code - first)];
        index += c; first += c; first <<= 1; code <<= 1;
    }
    s.bad = true; return 0;
}
// zlib stream -> out (exactly cap bytes expected); false on any error
bool inflate_zlib(const uint8_t *in, size_t n, uint8_t *out, size_t cap) {
    static const uint16_t lbase[29] = { 3,4,5,6,7,8,9,10,11,13,15,17,19,23,27,31,35,43,51,59,67,83,99,115,131,163,195,227,258 };
    static const uint8_t lext[29] = { 0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,0 };
    static const uint16_t dbase[30] = { 1,2,3,4,5,7,9,13,17,25,33,49,65,97,129,193,257,385,513,769,1025,1537,2049,3073,4097,6145,8193,12289,16385,24577 };
    static const uint8_t dext[30] = { 0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10,11,11,12,12,13,13 };
    if (n < 6 || (in[0] & 0x0F) != 8 || ((in[0] << 8) | in[1]) % 31) return false;
    Bits s = { in + 2, in + n, 0, 0, false }; size_t o = 0; int last;
    static Huff lh, dh;                                  // the patch thread is not running yet; Iggy loads on the game thread
    do {
        last = need(s, 1); int type = need(s, 2);
        if (type == 0) {
            s.buf = 0; s.cnt = 0;
            if (s.e - s.p < 4) return false;
            unsigned len = s.p[0] | s.p[1] << 8; s.p += 4;
            if ((size_t)(s.e - s.p) < len || o + len > cap) return false;
            memcpy(out + o, s.p, len); s.p += len; o += len;
        } else if (type == 1 || type == 2) {
            uint8_t lens[320];
            int nl = 288, nd = 30;
            if (type == 1) {
                for (int i = 0; i < 144; i++) lens[i] = 8; for (int i = 144; i < 256; i++) lens[i] = 9;
                for (int i = 256; i < 280; i++) lens[i] = 7; for (int i = 280; i < 288; i++) lens[i] = 8;
                for (int i = 0; i < 30; i++) lens[288 + i] = 5;
            } else {
                nl = need(s, 5) + 257; nd = need(s, 5) + 1; int nc = need(s, 4) + 4;
                static const uint8_t ord[19] = { 16,17,18,0,8,7,9,6,10,5,11,4,12,3,13,2,14,1,15 };
                uint8_t cl[19] = { 0 }; for (int i = 0; i < nc; i++) cl[ord[i]] = (uint8_t)need(s, 3);
                Huff ch; build(ch, cl, 19);
                int i = 0;
                while (i < nl + nd && !s.bad) {
                    int sym = decode(s, ch);
                    if (sym < 16) lens[i++] = (uint8_t)sym;
                    else {
                        int rep = 0; uint8_t v = 0;
                        if (sym == 16) { if (!i) return false; v = lens[i - 1]; rep = 3 + need(s, 2); }
                        else if (sym == 17) rep = 3 + need(s, 3);
                        else rep = 11 + need(s, 7);
                        if (i + rep > nl + nd) return false;
                        while (rep--) lens[i++] = v;
                    }
                }
            }
            build(lh, lens, nl); build(dh, lens + nl, nd);
            for (;;) {
                int sym = decode(s, lh);
                if (s.bad) return false;
                if (sym < 256) { if (o >= cap) return false; out[o++] = (uint8_t)sym; }
                else if (sym == 256) break;
                else {
                    sym -= 257; if (sym >= 29) return false;
                    int len = lbase[sym] + need(s, lext[sym]);
                    int ds = decode(s, dh); if (ds >= 30) return false;
                    size_t dist = dbase[ds] + need(s, dext[ds]);
                    if (s.bad || dist > o || o + len > cap) return false;
                    for (int k = 0; k < len; k++, o++) out[o] = out[o - dist];
                }
            }
        } else return false;
        if (s.bad) return false;
    } while (!last);
    return o == cap;
}

// the two traits: name, kind const, slot id, type int, value index (u30, two bytes), value kind int
const uint8_t k_msaa[8] = { 0xB6, 0x12, 0x00, 0x0D, 0x17, 0x9A, 0x01, 0x03 };   // VideoSettingOptionMSAA = ints[154] = 7
const uint8_t k_hdao[8] = { 0xB7, 0x12, 0x00, 0x0E, 0x17, 0x9F, 0x01, 0x03 };   // VideoSettingOptionHDAO = ints[159] = 11
const uint8_t k_msaa_new[2] = { 0x93, 0x01 }, k_hdao_new[2] = { 0xA0, 0x01 };   // ints[147] = 8, ints[160] = 12

uint8_t *find1(uint8_t *p, size_t n, const uint8_t *pat, size_t m, int *count) {
    uint8_t *hit = 0; *count = 0;
    for (size_t i = 0; i + m <= n; i++) if (p[i] == pat[0] && !memcmp(p + i, pat, m)) { if (!hit) hit = p + i; ++*count; }
    return hit;
}
// ints[147] / ints[160] must read 8 / 12: checked in the int pool of the DoABC block (it follows the ABC version 16.46)
bool pool_check(uint8_t *swf, size_t n) {
    static const uint8_t abc_ver[4] = { 0x10, 0x00, 0x2E, 0x00 };
    int c; uint8_t *v = find1(swf, n, abc_ver, 4, &c);
    if (c != 1) return false;
    uint8_t *p = v + 4, *e = swf + n;
    auto u32v = [&](void) -> uint32_t {                   // AVM2 u30 / s32: 7 bits per byte, no sign extension
        uint32_t r = 0; for (int sh = 0; sh < 35 && p < e; sh += 7) { uint8_t b = *p++; r |= (uint32_t)(b & 0x7F) << sh; if (!(b & 0x80)) break; } return r; };
    uint32_t cnt = u32v(); if (cnt < 161) return false;
    uint32_t vals[161] = { 0 };
    for (uint32_t i = 1; i < 161; i++) vals[i] = u32v();
    return vals[154] == 7 && vals[159] == 11 && vals[147] == 8 && vals[160] == 12;
}

struct Cached { const void *in; uint32_t size; uint8_t *out; uint32_t out_size; } g_cache[4];

uint8_t *patch_swf(const uint8_t *in, uint32_t size, uint32_t *out_size) {
    for (auto &c : g_cache) if (c.in == in && c.size == size && c.out) { *out_size = c.out_size; return c.out; }
    uint32_t full = *(const uint32_t *)(in + 4);
    if (full < 8 || full > 64u << 20) return 0;
    uint8_t *u = (uint8_t *)malloc(full); if (!u) return 0;
    memcpy(u, in, 8);
    if (!inflate_zlib(in + 8, size - 8, u + 8, full - 8)) { free(u); return 0; }
    int c1, c2; uint8_t *m = find1(u, full, k_msaa, 8, &c1), *h = find1(u, full, k_hdao, 8, &c2);
    if (c1 != 1 || c2 != 1) { free(u); return 0; }                      // another SWF (or another escmenu.swf)
    if (!pool_check(u, full)) { free(u); logf("Esc menu: escmenu.swf has the two rows but not the expected int pool - not patched"); return 0; }
    memcpy(m + 5, k_msaa_new, 2); memcpy(h + 5, k_hdao_new, 2);
    // zlib, stored blocks of 65535 bytes, adler32
    uint32_t body = full - 8, nblk = (body + 65534) / 65535, total = 8 + 2 + nblk * 5 + body + 4;
    uint8_t *o = (uint8_t *)malloc(total); if (!o) { free(u); return 0; }
    memcpy(o, u, 8); uint8_t *q = o + 8; *q++ = 0x78; *q++ = 0x01;
    uint32_t a = 1, b = 0;
    for (uint32_t off = 0; off < body; ) {
        uint32_t len = body - off < 65535 ? body - off : 65535;
        *q++ = off + len >= body ? 1 : 0; *q++ = len & 0xFF; *q++ = len >> 8; *q++ = ~len & 0xFF; *q++ = (~len >> 8) & 0xFF;
        memcpy(q, u + 8 + off, len);
        for (uint32_t i = 0; i < len; i++) { a = (a + q[i]) % 65521; b = (b + a) % 65521; }
        q += len; off += len;
    }
    uint32_t ad = b << 16 | a; *q++ = ad >> 24; *q++ = (ad >> 16) & 0xFF; *q++ = (ad >> 8) & 0xFF; *q++ = ad & 0xFF;
    free(u);
    for (auto &c : g_cache) if (!c.out) { c.in = in; c.size = size; c.out = o; c.out_size = total; break; }
    *out_size = total; return o;
}

void *__stdcall h_create(const void *data, uint32_t size, void *cfg) {
    if (g_on && data && size > 16 && !memcmp(data, "CWS", 3)) {
        uint8_t *p = 0; uint32_t n = 0;
        __try { p = patch_swf((const uint8_t *)data, size, &n); } __except (EXCEPTION_EXECUTE_HANDLER) { p = 0; }
        if (p) {
            static bool once; if (!once) { once = true; logf("Esc menu: escmenu.swf loaded with the Video rows MSAA / HDAO at 8 / 12 (in memory, %u bytes)", n); }
            return o_create(p, n, cfg);
        }
    }
    return o_create(data, size, cfg);
}

// ---- finding the game's code ----
uint8_t *g_base; uint32_t g_tlo, g_thi, g_size;
bool in_text(uint32_t rva) { return rva >= g_tlo && rva < g_thi; }
uint32_t rd32(uint32_t rva) { return *(uint32_t *)(g_base + rva); }
uint32_t va2rva(uint32_t va) { return va - (uint32_t)(uintptr_t)g_base; }
// the RVA of a zero terminated string (exactly one)
uint32_t find_str(const char *s) {
    size_t n = strlen(s) + 1; uint32_t hit = 0; int c = 0;
    for (uint32_t i = 1; i + n <= g_size; i++)
        if (g_base[i] == (uint8_t)s[0] && g_base[i - 1] == 0 && !memcmp(g_base + i, s, n)) { hit = i; if (++c > 1) return 0; }
    return c == 1 ? hit : 0;
}
// RVAs of "68 <va>" in .text
int find_push(uint32_t rva, uint32_t *out, int max) {
    uint32_t va = (uint32_t)(uintptr_t)g_base + rva; int c = 0;
    for (uint32_t i = g_tlo; i + 5 <= g_thi; i++)
        if (g_base[i] == 0x68 && *(uint32_t *)(g_base + i + 1) == va) { if (c < max) out[c] = i; c++; }
    return c;
}
// first match of pat (0x100 = any byte) in [rva, rva + n)
uint32_t scan(uint32_t rva, uint32_t n, const int *pat, int m) {
    for (uint32_t i = rva; i + m <= rva + n && i + m <= g_thi; i++) {
        int k = 0; while (k < m && (pat[k] == 0x100 || g_base[i + k] == pat[k])) k++;
        if (k == m) return i;
    }
    return 0;
}
uint32_t call_at(uint32_t rva) { return g_base[rva] == 0xE8 ? rva + 5 + *(int32_t *)(g_base + rva + 1) : 0; }

#define FAIL(...) do { logf("Esc menu: " __VA_ARGS__); return false; } while (0)
bool esc_find() {
    g_base = (uint8_t *)GetModuleHandleW(0);
    IMAGE_NT_HEADERS32 *nt = (IMAGE_NT_HEADERS32 *)(g_base + ((IMAGE_DOS_HEADER *)g_base)->e_lfanew);
    IMAGE_SECTION_HEADER *sec = IMAGE_FIRST_SECTION(nt);
    g_size = nt->OptionalHeader.SizeOfImage;
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++) if (!memcmp(sec[i].Name, ".text", 5)) { g_tlo = sec[i].VirtualAddress; g_thi = g_tlo + sec[i].Misc.VirtualSize; }
    // only the game's own sections: the patch code (.f60, .f60s) carries some of the same strings
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++) if (!memcmp(sec[i].Name, ".f60", 4) && sec[i].VirtualAddress < g_size) g_size = sec[i].VirtualAddress;
    if (!g_thi) FAIL("no .text");
    uint32_t p[16], s; int c;
    // CallSWF: the call right after every push "addVideoPageItem"
    if (!(s = find_str("addVideoPageItem"))) FAIL("addVideoPageItem not found");
    c = find_push(s, p, 16); if (c < 4 || c > 16) FAIL("addVideoPageItem used %d times", c);
    uint32_t cs = 0;
    for (int i = 0; i < c; i++) {
        static const int e8[1] = { 0xE8 }; uint32_t k = scan(p[i], 0x40, e8, 1), t = k ? call_at(k) : 0;
        if (!t || (cs && t != cs)) FAIL("CallSWF not the same everywhere"); cs = t;
    }
    static const int callswf_pro[] = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x64 };
    if (!scan(cs, 6, callswf_pro, 6)) FAIL("CallSWF has an unexpected shape");
    // EscMenuVideoSettingRefresh: "push handler; push name"; the handler calls the parser as "push [ebp+0xC]; mov ecx,esi; push edi; call"
    if (!(s = find_str("EscMenuVideoSettingRefresh")) || find_push(s, p, 2) != 1 || g_base[p[0] - 5] != 0x68) FAIL("EscMenuVideoSettingRefresh registration not found");
    uint32_t h = va2rva(rd32(p[0] - 4)); if (!in_text(h)) FAIL("video refresh handler outside .text");
    static const int parse_call[] = { 0xFF, 0x75, 0x0C, 0x8B, 0xCE, 0x57, 0xE8 };
    uint32_t k = scan(h, 0x40, parse_call, 7); uint32_t parse = k ? call_at(k + 6) : 0;
    g_refresh_ret = g_base + k + 11;
    static const int parse_pro[] = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x34, 0x8B, 0x45, 0x0C, 0x53, 0x56, 0x57, 0x6A, 0x01, 0xFF, 0x70, 0x04, 0x8B, 0xD9, 0x89, 0x5D, 0xFC, 0xE8 };
    if (!parse || !scan(parse, 23, parse_pro, 23)) FAIL("video value parser not found");
    uint32_t gc = call_at(parse + 22);
    static const int getctx_pro[] = { 0x8B, 0x41, 0x100, 0x8B, 0x00, 0xC3 };
    if (!gc || !scan(gc, 6, getctx_pro, 6)) FAIL("Iggy context getter has an unexpected shape");
    static const int cmp_n[] = { 0x83, 0xFE, 0x100, 0x7C };
    if (!(k = scan(parse, 0x80, cmp_n, 4))) FAIL("number of Video items not found");
    g_n = g_base[k + 2]; if (g_n <= ROW || g_n + 1 >= MAXN) FAIL("%d Video items", g_n);
    // EscMenuHelpTextRefresh: page / item globals, the help table, its length, the callback, the lockit text function
    if (!(s = find_str("EscMenuHelpTextRefresh")) || find_push(s, p, 2) != 1 || g_base[p[0] - 5] != 0x68) FAIL("EscMenuHelpTextRefresh registration not found");
    h = va2rva(rd32(p[0] - 4)); if (!in_text(h)) FAIL("help handler outside .text");
    static const int st_page[] = { 0x66, 0x89, 0x35 }, st_item[] = { 0x66, 0x89, 0x0D }, cmp_tab[] = { 0x66, 0x39, 0x34, 0xC5 }, cnt_tab[] = { 0x40, 0x83, 0xF8 },
                     push_cb[] = { 0x68, 0x100, 0x100, 0x100, 0x100, 0x8B, 0xCF, 0xE8 };
    uint32_t a1 = scan(h, 0x200, st_page, 3), a2 = scan(h, 0x200, st_item, 3), a3 = scan(h, 0x200, cmp_tab, 4), a4 = scan(h, 0x200, cnt_tab, 3), a5 = scan(h, 0x200, push_cb, 8);
    if (!a1 || !a2 || !a3 || !a4 || !a5) FAIL("help handler has an unexpected shape");
    g_page = (volatile uint16_t *)(uintptr_t)rd32(a1 + 3); g_item = (volatile uint16_t *)(uintptr_t)rd32(a2 + 3);
    g_table = (uint16_t *)(uintptr_t)(rd32(a3 + 4) - 4); g_tcount = g_base[a4 + 3];
    uint32_t cb = va2rva(rd32(a5 + 1)); if (!in_text(cb)) FAIL("help callback outside .text");
    static const int lk_call[] = { 0xFF, 0x31, 0xE8 };
    if (!(k = scan(cb, 0x40, lk_call, 3))) FAIL("lockit text call not found");
    uint32_t lk = call_at(k + 2); if (!in_text(lk)) FAIL("lockit text function outside .text");
    // the Video page: the page with the most help entries; it must have items 0 .. n-1 once each, ROW = Texture Quality
    int cnt[8] = { 0 }; uint16_t pg[8] = { 0 }; int np = 0;
    for (int i = 0; i < g_tcount; i++) {
        uint16_t *e = g_table + 4 * i; if (e[3] >= 100) continue;
        int j = 0; while (j < np && pg[j] != e[2]) j++;
        if (j == np) { if (np == 8) FAIL("help table: too many pages"); pg[np++] = e[2]; }
        cnt[j]++;
    }
    int best = 0; for (int j = 1; j < np; j++) if (cnt[j] > cnt[best]) best = j;
    g_vpage = pg[best];
    if (cnt[best] != g_n) FAIL("help table: page %d has %d items, the menu %d", g_vpage, cnt[best], g_n);
    g_probe_id = -1;
    for (int i = 0; i < g_tcount; i++) { uint16_t *e = g_table + 4 * i; if (e[2] == g_vpage && e[3] == ROW) g_probe_id = e[0] - 1; }
    if (g_probe_id < 0) FAIL("help table: no Texture Quality entry");
    // Iggy
    HMODULE ig = GetModuleHandleA("iggy_w32.dll"); if (!ig) FAIL("iggy_w32.dll not loaded");
#define IG(n, d) if (!(n = (decltype(n))GetProcAddress(ig, d))) FAIL("iggy_w32.dll has no " d)
    IG(IggyValueRefCreateArray, "_IggyValueRefCreateArray@8"); IG(IggyValuePathFromRef, "_IggyValuePathFromRef@12");
    IG(IggyValuePathMakeArrayRef, "_IggyValuePathMakeArrayRef@12"); IG(IggyValuePathSetArrayIndex, "_IggyValuePathSetArrayIndex@8");
    IG(IggyValueSetStringUTF8RS, "_IggyValueSetStringUTF8RS@20"); IG(IggyValueGetS32RS, "_IggyValueGetS32RS@16");
    IG(IggyValueRefFromTempRef, "_IggyValueRefFromTempRef@12"); IG(IggyValueRefFree, "_IggyValueRefFree@8");
    IG(g_create, "_IggyPlayerCreateFromMemory@12");
#undef IG
    g_callswf = (callswf_t)(g_base + cs); g_parse = (parse_t)(g_base + parse); g_getctx = (getctx_t)(g_base + gc);
    g_cb = (helpcb_t)(g_base + cb); g_lockit = (lockit_t)(g_base + lk);
    logf("Esc menu: CallSWF 0x%08X, parser 0x%08X (%d items), help table 0x%08X (%d entries, Video page %d), callback 0x%08X, lockit 0x%08X",
         (uint32_t)(uintptr_t)g_callswf, (uint32_t)(uintptr_t)g_parse, g_n, (uint32_t)(uintptr_t)g_table, g_tcount, g_vpage,
         (uint32_t)(uintptr_t)g_cb, (uint32_t)(uintptr_t)g_lockit);
    return true;
}

} // namespace

// Installs the row (game 1 = FFX, 2 = FFX-2). Called before the game's own entry point runs, so escmenu.swf (loaded at
// startup) is caught. Returns whether the row is on.
extern "C" int f60_escmenu_install(int game) {
    bool ok = false;
    __try { ok = esc_find(); } __except (EXCEPTION_EXECUTE_HANDLER) { ok = false; logf("Esc menu: search FAULTED"); }
    if (!ok) { logf("Esc menu: Frame Rate row not installed - the menu stays as shipped"); return 0; }
    // both games: the rates the launcher offers (FFX: its rate row; FFX-2: x2_pacing.cpp, N invocations per tick)
    g_vals[0] = 30; g_vals[1] = 60; g_vals[2] = 90; g_vals[3] = 120; g_vals[4] = 240; g_nvals = 5;
    for (int i = 0; i < g_nvals; i++) g_valtext[i] = text_of(g_vals[i]);
    MH_STATUS st = MH_Initialize();
    if (st != MH_OK && st != MH_ERROR_ALREADY_INITIALIZED) { logf("Esc menu: MH_Initialize failed (%s)", MH_StatusToString(st)); return 0; }
    struct H { void *t, *d, **o; const char *n; } hk[] = {
        { (void *)g_create, (void *)h_create, (void **)&o_create, "IggyPlayerCreateFromMemory" },
        { (void *)g_callswf, (void *)h_callswf, (void **)&o_callswf, "CallSWF" },
        { (void *)g_parse, (void *)h_parse, (void **)&o_parse, "video value parser" },
        { (void *)g_cb, (void *)h_cb, (void **)&o_cb, "help callback" },
        { (void *)g_lockit, (void *)h_lockit, (void **)&o_lockit, "lockit text" },
    };
    int made = 0;
    for (auto &x : hk) {
        if ((st = MH_CreateHook(x.t, x.d, x.o)) != MH_OK) { logf("Esc menu: hook %s failed (%s)", x.n, MH_StatusToString(st)); break; }
        made++;
    }
    bool ok2 = made == (int)(sizeof hk / sizeof hk[0]);
    for (int i = 0; ok2 && i < made; i++) if ((st = MH_EnableHook(hk[i].t)) != MH_OK) { logf("Esc menu: enabling %s failed (%s)", hk[i].n, MH_StatusToString(st)); ok2 = false; }
    if (ok2) {
        // the help table: the Video page's items from ROW on one index down (they move in the menu)
        DWORD old; size_t bytes = (size_t)g_tcount * 8;
        if (VirtualProtect(g_table, bytes, PAGE_READWRITE, &old)) {
            for (int i = 0; i < g_tcount; i++) { uint16_t *e = g_table + 4 * i; if (e[2] == g_vpage && e[3] >= ROW && e[3] < 100) e[3]++; }
            VirtualProtect(g_table, bytes, old, &old);
            g_on = true;
        } else logf("Esc menu: help table not writable");
    }
    if (!g_on) {
        for (int i = 0; i < made; i++) { MH_DisableHook(hk[i].t); MH_RemoveHook(hk[i].t); }
        logf("Esc menu: Frame Rate row not installed"); return 0;
    }
    logf("Esc menu: Frame Rate row installed (%s)", "30 / 60 / 90 / 120 / 240 fps");
    return 1;
}
