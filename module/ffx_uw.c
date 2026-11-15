/* ===================================================================================
   FFX module: ultrawide through a SUWSF.ini ("Somewhat Universal Widescreen Fix", github.com/phantomgamers/suwsf, MIT License,
   Copyright (c) 2021 PhantomGamers). The module applies the patches of that file itself, the way SUWSF 1.x does (no ASI loader and
   no second d3d11.dll next to RevoFX's proxy):
   - sections whose name contains "Patch" are applied in the order of their names (SUWSF reads the file into a sorted map; sections
     of the same name merge, the later key wins), so "Match=n" finds the same place as with SUWSF
   - Pattern: hex bytes, ?? = any byte, searched in the whole image of Module ("auto" = the EXE); Offset >= 0;
     Match: n (1 = first), "last" / "end", "all" (default)
   - ValueType float (default), double, int: Value is an expression (+ - * / % ^, parentheses) of width, height and aspectratio;
     byte: hex bytes written as they are
   - [UserSettings] Resolution: "auto" (the primary screen, as SUWSF) or "WxH"; Enabled
   - text after ';' is a comment, quotes are removed from the values
   Built in (ffx_uw_data.h): the ultrawide patches ported to the current Steam FFX.exe and FFX-2.exe from a SUWSF.ini made for the
   version before the update (every place found again by its byte context in the old and the new EXE; relocated addresses and call
   targets mapped). A SUWSF.ini of the player in <game>\RevoFX\ or in the game folder is used instead.
   =================================================================================== */
#include <windows.h>
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include "revofx.h"
#include "ffx_uw_data.h"
#ifdef F60_EXE   /* FFX Enhanced (fps60exe): the patched EXE carries the patch code in its last sections - they are not searched */
size_t f60_uw_image_size(size_t n);
#define UW_IMAGE_SIZE(n) f60_uw_image_size(n)
#else
#define UW_IMAGE_SIZE(n) (n)
#endif

typedef struct { char k[32]; char *v; } UwKey;
typedef struct { char name[128]; UwKey key[16]; int nk; } UwSec;
static UwSec *uw_sec; static int uw_n;

static char *uw_trim(char *s) { char *e; while (*s == ' ' || *s == '\t') s++; e = s + strlen(s); while (e > s && (e[-1] == ' ' || e[-1] == '\t' || e[-1] == '\r' || e[-1] == '\n')) *--e = 0; return s; }
static UwSec *uw_find(const char *name) { int i; for (i = 0; i < uw_n; i++) if (!strcmp(uw_sec[i].name, name)) return &uw_sec[i]; return 0; }
static const char *uw_get(const UwSec *s, const char *k) { int i; if (s) for (i = 0; i < s->nk; i++) if (!strcmp(s->key[i].k, k)) return s->key[i].v; return 0; }
static void uw_set(UwSec *s, const char *k, const char *v) {   /* the later key wins; quotes removed */
    int i; char *c, *o; for (i = 0; i < s->nk && strcmp(s->key[i].k, k); i++) {}
    if (i == s->nk) { if (s->nk >= 16) return; s->nk++; strncpy(s->key[i].k, k, 31); s->key[i].k[31] = 0; s->key[i].v = 0; }
    free(s->key[i].v); c = (char*)malloc(strlen(v) + 1); if (!c) return;
    for (o = c; *v; v++) if (*v != '"') *o++ = *v; *o = 0; s->key[i].v = c;
}
static int uw_cap;
static void uw_line(char *l, UwSec **cur) {   /* one line of the ini: section or key */
    char *sc = strchr(l, ';'), *eq; if (sc) *sc = 0; l = uw_trim(l); if (!*l) return;
    if (*l == '[') { char *e = strchr(l, ']'), nm[128]; if (!e) return; *e = 0; strncpy(nm, uw_trim(l + 1), 127); nm[127] = 0;
        *cur = uw_find(nm); if (!*cur) { if (uw_n == uw_cap) { UwSec *n = (UwSec*)realloc(uw_sec, sizeof(UwSec) * (uw_cap = uw_cap ? uw_cap * 2 : 256)); if (!n) { *cur = 0; return; } uw_sec = n; }
            *cur = &uw_sec[uw_n++]; memset(*cur, 0, sizeof **cur); strcpy((*cur)->name, nm); }
        return; }
    if (!*cur || !(eq = strchr(l, '='))) return; *eq = 0; uw_set(*cur, uw_trim(l), uw_trim(eq + 1));
}
static int uw_load(const char *path) {
    FILE *f = fopen(path, "rb"); char line[2048]; UwSec *cur = 0;
    if (!f) return 0;
    while (fgets(line, sizeof line, f)) uw_line(line, &cur);
    fclose(f); return 1;
}
static void uw_text(const char *t) {   /* the built-in ini */
    char line[2048]; UwSec *cur = 0;
    while (*t) { size_t n = strcspn(t, "\n"); if (n > sizeof line - 1) n = sizeof line - 1; memcpy(line, t, n); line[n] = 0; uw_line(line, &cur); t += n; if (*t == '\n') t++; }
}
static int uw_cmp(const void *a, const void *b) { return strcmp(((const UwSec*)a)->name, ((const UwSec*)b)->name); }

/* expression of a value: numbers, width, height, aspectratio, + - * / % ^ (left to right) and parentheses */
static const char *uw_p; static int uw_err; static double uw_w, uw_h, uw_ar;
static double uw_expr(void);
static void uw_ws(void) { while (*uw_p == ' ' || *uw_p == '\t') uw_p++; }
static double uw_prim(void) {
    double v; char *e; uw_ws();
    if (*uw_p == '-') { uw_p++; return -uw_prim(); } if (*uw_p == '+') { uw_p++; return uw_prim(); }
    if (*uw_p == '(') { uw_p++; v = uw_expr(); uw_ws(); if (*uw_p == ')') uw_p++; else uw_err = 1; return v; }
    if (!strncmp(uw_p, "aspectratio", 11)) { uw_p += 11; return uw_ar; }
    if (!strncmp(uw_p, "width", 5)) { uw_p += 5; return uw_w; }
    if (!strncmp(uw_p, "height", 6)) { uw_p += 6; return uw_h; }
    v = strtod(uw_p, &e); if (e == uw_p) { uw_err = 1; return 0; } uw_p = e; return v;
}
static double uw_pow(void) { double v = uw_prim(); for (;;) { uw_ws(); if (*uw_p != '^') return v; uw_p++; v = pow(v, uw_prim()); } }
static double uw_term(void) { double v = uw_pow(); for (;;) { char c; uw_ws(); c = *uw_p; if (c != '*' && c != '/' && c != '%') return v; uw_p++;
    { double r = uw_pow(); v = c == '*' ? v * r : c == '/' ? v / r : fmod(v, r); } } }
static double uw_expr(void) { double v = uw_term(); for (;;) { char c; uw_ws(); c = *uw_p; if (c != '+' && c != '-') return v; uw_p++; { double r = uw_term(); v = c == '+' ? v + r : v - r; } } }
static int uw_eval(const char *s, double *out) { uw_p = s; uw_err = 0; *out = uw_expr(); uw_ws(); return !uw_err && !*uw_p; }

/* hex bytes (byte values; "??" = any byte in patterns, mask 0) */
static int uw_bytes(const char *s, unsigned char *b, unsigned char *m, int max) {
    int n = 0;
    while (*s && n < max) { unsigned v; int k;
        if (*s == ' ' || *s == '\t') { s++; continue; }
        if (*s == '?') { while (*s == '?') s++; if (m) { b[n] = 0; m[n++] = 0; continue; } return -1; }
        if (sscanf(s, "%x%n", &v, &k) != 1) return -1; s += k; b[n] = (unsigned char)v; if (m) m[n] = 1; n++; }
    return n;
}

/* all matches of a pattern in the image of a module (at most max are kept, the count is returned): memchr on the rarest fixed byte of
   the pattern (byte counts of the image, once per module), then the whole pattern */
static const unsigned char *uw_fbase; static size_t uw_freq[256];
static int uw_search(const unsigned char *base, size_t size, const unsigned char *b, const unsigned char *m, int n, const unsigned char **hit, int max) {
    const unsigned char *p, *end; int c = 0, k, a = -1;
    if (n <= 0 || size < (size_t)n) return 0;
    if (uw_fbase != base) { size_t i; memset(uw_freq, 0, sizeof uw_freq); for (i = 0; i < size; i++) uw_freq[base[i]]++; uw_fbase = base; }
    for (k = 0; k < n; k++) if (m[k] && (a < 0 || uw_freq[b[k]] < uw_freq[b[a]])) a = k;
    if (a < 0) return 0;
    for (p = base + a, end = base + size - (n - a); p < end + 1; p++) {
        p = (const unsigned char*)memchr(p, b[a], (size_t)(end + 1 - p)); if (!p) break;
        { const unsigned char *s = p - a; for (k = 0; k < n && (!m[k] || s[k] == b[k]); k++) {}
          if (k == n) { if (c < max) hit[c] = s; c++; } } }
    return c;
}

/* 1 = a SUWSF.ini was found and applied */
/* on: the ultrawide patches; shadows: the patches named "Shadow" (8K shadow maps of the game, not needed for the aspect ratio) */
int ffx_uw_apply(int on, int shadows) {
    char path[MAX_PATH]; const UwSec *us; const char *r; int i, done = 0, miss = 0, bad = 0, other = 0, w, h, ok = 0;
    static const unsigned char *hit[4096];
    if (!on && !shadows) { rfx_log("FFX: ultrawide off (Game tab)"); return 0; }
    snprintf(path, sizeof path, "%sSUWSF.ini", rfx_work_dir());
    if (!uw_load(path)) { snprintf(path, sizeof path, "%sSUWSF.ini", rfx_dir());
        if (!uw_load(path)) { const char *exe = rfx_exe_path(), *f = strrchr(exe, '\\'); int x2; f = f ? f + 1 : exe; x2 = !_strnicmp(f, "FFX-2", 5);
            uw_text(x2 ? ffx_uw_ini_ffx2 : ffx_uw_ini_ffx);   /* the built-in patches for the current Steam EXE */
            snprintf(path, sizeof path, "built-in patches for the Steam %s", x2 ? "FFX-2.exe" : "FFX.exe"); } }
    us = uw_find("UserSettings"); r = uw_get(us, "Enabled");
    if (r && (!_stricmp(r, "false") || !strcmp(r, "0"))) { rfx_log("FFX: ultrawide: %s says Enabled=false", path); return 0; }
    r = uw_get(us, "Resolution"); w = GetSystemMetrics(SM_CXSCREEN); h = GetSystemMetrics(SM_CYSCREEN);
    { char os[32]; int a2, b2; rfx_ini_str("Debug", "OutputSize", "", os, sizeof os); if (sscanf(os, "%dx%d", &a2, &b2) == 2 && a2 > 0 && b2 > 0) { w = a2; h = b2; } }   /* RevoFX [Debug] OutputSize (tests without that monitor) */
    if (r && _stricmp(r, "auto")) { int a, b; if (sscanf(r, "%dx%d", &a, &b) == 2 && a > 0 && b > 0) { w = a; h = b; } }
    uw_w = w; uw_h = h; uw_ar = (double)w / h;
    qsort(uw_sec, uw_n, sizeof(UwSec), uw_cmp);
    /* all or nothing: the patches run on a copy of the EXE image first; only when every patch for this program finds its place are the
       changed bytes written into the game (a SUWSF.ini for another version of the EXE would otherwise change wrong places) */
    {   HMODULE exe = GetModuleHandleA(NULL); const IMAGE_NT_HEADERS *nt = (const IMAGE_NT_HEADERS*)((const BYTE*)exe + ((const IMAGE_DOS_HEADER*)exe)->e_lfanew);
        size_t size = UW_IMAGE_SIZE(nt->OptionalHeader.SizeOfImage), j; unsigned char *img = (unsigned char*)malloc(size);
        if (!img) { rfx_log("FFX: ultrawide: no memory for the copy of the image"); goto done_; }
        memcpy(img, exe, size);
        for (i = 0; i < uw_n; i++) {
            const UwSec *s = &uw_sec[i]; const char *pat = uw_get(s, "Pattern"), *val = uw_get(s, "Value"), *vt = uw_get(s, "ValueType"), *mt = uw_get(s, "Match"),
                *md = uw_get(s, "Module"), *en = uw_get(s, "Enabled"), *of = uw_get(s, "Offset");
            unsigned char pb[256], pm[256], vb[256]; int pn, vn = 0, off = 0, nh, k;
            if (!strstr(s->name, "Patch")) continue;
            if (strstr(s->name, "Shadow") ? !shadows : !on) { other++; continue; }   /* switched off in the Game tab */
            if (en && strcmp(en, "true")) continue;
            if (md && _stricmp(md, "auto") && GetModuleHandleA(md) != exe) { other++; continue; }   /* for another program of the collection */
            if (of) { char *e; long v = strtol(of, &e, 10); if (e == of || v < 0) { bad++; continue; } off = (int)v; }
            if (!vt) vt = "float";
            if (!pat || (pn = uw_bytes(pat, pb, pm, 256)) <= 0) { bad++; continue; }
            if (!strcmp(vt, "byte")) { if (!val || (vn = uw_bytes(val, vb, 0, 256)) <= 0) { bad++; continue; } }
            else if (!strcmp(vt, "float") || !strcmp(vt, "double") || !strcmp(vt, "int")) { double d; if (!val || !uw_eval(val, &d)) { bad++; continue; }
                if (*vt == 'f') { float f = (float)d; memcpy(vb, &f, vn = 4); } else if (*vt == 'd') memcpy(vb, &d, vn = 8); else { int x = (int)d; memcpy(vb, &x, vn = 4); } }
            else { bad++; continue; }
            nh = uw_search(img, size, pb, pm, pn, hit, 4096);
            k = !mt || !strcmp(mt, "all") ? 0 : !strcmp(mt, "last") || !strcmp(mt, "end") ? nh : atoi(mt);
            if (!nh || k < 0 || k > nh || k > 4096 || (mt && strcmp(mt, "all") && k < 1)) { if (miss < 8) rfx_log("FFX: ultrawide: [%s] not found (%d matches)", s->name, nh); miss++; continue; }
            if (!k) { for (k = 0; k < nh && k < 4096; k++) if ((size_t)(hit[k] - img) + off + vn <= size) memcpy((void*)(hit[k] + off), vb, vn); }
            else if ((size_t)(hit[k - 1] - img) + off + vn <= size) memcpy((void*)(hit[k - 1] + off), vb, vn);
            done++;
        }
        if (!miss && done) {   /* the changed bytes into the game */
            unsigned char *g = (unsigned char*)exe;
            for (j = 0; j < size;) { size_t e; if (img[j] == g[j]) { j++; continue; } for (e = j; e < size && e - j < 4096 && img[e] != g[e]; e++) {} rfx_write_code(g + j, img + j, (int)(e - j)); j = e; }
            ok = 1; }
        free(img); uw_fbase = 0;
    }
    if (ok) rfx_log("FFX: ultrawide (%s): %dx%d, aspect ratio %.3f, %d patches applied (%d for another program, %d invalid)", path, w, h, uw_ar, done, other, bad);
    else rfx_log("FFX: ultrawide (%s): %d of %d patches for this program not found - made for another version of the EXE, nothing changed", path, miss, miss + done);
done_:
    for (i = 0; i < uw_n; i++) { int k; for (k = 0; k < uw_sec[i].nk; k++) free(uw_sec[i].key[k].v); } free(uw_sec); uw_sec = 0; uw_n = 0;
    return ok;
}
