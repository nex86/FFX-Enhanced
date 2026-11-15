/* revofx.h for module\ffx_uw.c inside FFX Enhanced (no RevoFX): the few functions that file uses, implemented in f60_host.cpp. */
#ifndef F60_UWSHIM_REVOFX_H
#define F60_UWSHIM_REVOFX_H
void rfx_log(const char *fmt, ...);
const char *rfx_work_dir(void);   /* here: the game folder (a SUWSF.ini of the player there is used instead of the built-in patches) */
const char *rfx_dir(void);
const char *rfx_exe_path(void);
void rfx_ini_str(const char *sec, const char *key, const char *def, char *out, int n);
void rfx_write_code(void *dst, const void *src, int n);
#endif
