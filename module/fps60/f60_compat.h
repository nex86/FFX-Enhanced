// f60_compat.h - what the FFX-60fps source (written for MSVC) needs to build with RevoFX's compiler (clang, x86-windows-gnu) and
// to run inside the module: structured exception handling, the log and the settings of RevoFX. Included by fps60.h and x2.h.
#pragma once
#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <intrin.h>

// ---- __try / __except (EXCEPTION_EXECUTE_HANDLER) -------------------------------------------------------------------
// clang has no 32 bit SEH code generation for the gnu target. A frame per __try on a per-thread list; a vectored handler sends a
// hardware fault of that thread to the newest frame (f60_seh.cpp). Only the form the source uses: __try { } __except (EXCEPTION_EXECUTE_HANDLER) { }
// with GetExceptionCode() inside the handler. Leaving the block with return / break is fine (the frame's destructor unlinks it).
struct F60Seh {
    void *jb[5]; F60Seh *prev; DWORD code; int armed;
    F60Seh(); ~F60Seh();
};
#undef __try
#undef __except
#undef GetExceptionCode
#define __try if (F60Seh f60_seh_; !__builtin_setjmp(f60_seh_.jb))
#define __except(filter) else
#define GetExceptionCode() (f60_seh_.code)

// ---- module side (ffx_fps60.cpp) --------------------------------------------------------------------------------------
extern "C" {
void f60_log(const char *game, const char *text);            // one line into RevoFX's log: [INFO] [FPS60] [<game>] text
int  f60_row(const char *id, int def);                       // a row switch / row value: [Debug] FFXFps60Rows override, else def
int  f60_opt(const char *key, int def);                      // a setting of the Game tab ([Game] <key>)
void f60_game_dir(wchar_t *out, size_t cap);                 // folder of the EXE with trailing '\'
}
// MSVC intrinsics the source uses (not built into clang for the gnu target)
#undef _ReturnAddress
#undef _AddressOfReturnAddress
#define _ReturnAddress() __builtin_return_address(0)
#define _AddressOfReturnAddress() ((void*)((char*)__builtin_frame_address(0) + sizeof(void*)))
// The mods poll developer hotkeys (F6 - F11, numpad keys: toggles of rows, traces). Off in the module unless [Debug] FFXFps60Hotkeys = 1.
extern "C" short f60_key(int vk);
#define GetAsyncKeyState(vk) f60_key(vk)
// MSVC runtime names the source uses
#ifndef _TRUNCATE
#define _TRUNCATE ((size_t)-1)
#endif
