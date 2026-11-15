// f60_seh.cpp - __try / __except for the 60 fps patch (f60_compat.h): a vectored exception handler resumes a hardware fault of a
// thread at the newest F60Seh frame of that thread. Faults outside a __try block, and everything that is not a hardware fault
// (C++ exceptions, breakpoints, debugger events), are passed on untouched.
#include "f60_compat.h"

static DWORD g_tls = TLS_OUT_OF_INDEXES; static PVOID g_veh;
static F60Seh *top() { return g_tls == TLS_OUT_OF_INDEXES ? 0 : (F60Seh*)TlsGetValue(g_tls); }

extern "C" __attribute__((noreturn)) void __cdecl f60_seh_jump(F60Seh *f) { __builtin_longjmp(f->jb, 1); }
__declspec(naked) static void f60_seh_land() {   /* eax = the frame; the stack is the faulting one (deeper than the frame's) */
    __asm {
        push eax
        call f60_seh_jump
    }
}
static LONG CALLBACK veh(EXCEPTION_POINTERS *e) {
    DWORD c = e->ExceptionRecord->ExceptionCode;
    if (c != EXCEPTION_ACCESS_VIOLATION && c != EXCEPTION_IN_PAGE_ERROR && c != EXCEPTION_ILLEGAL_INSTRUCTION && c != EXCEPTION_PRIV_INSTRUCTION &&
        c != EXCEPTION_INT_DIVIDE_BY_ZERO && c != EXCEPTION_INT_OVERFLOW && c != EXCEPTION_ARRAY_BOUNDS_EXCEEDED && c != EXCEPTION_DATATYPE_MISALIGNMENT &&
        !(c >= EXCEPTION_FLT_DENORMAL_OPERAND && c <= EXCEPTION_FLT_UNDERFLOW)) return EXCEPTION_CONTINUE_SEARCH;
    F60Seh *f = top(); if (!f || !f->armed) return EXCEPTION_CONTINUE_SEARCH;
    f->code = c; f->armed = 0; TlsSetValue(g_tls, f->prev);
    e->ContextRecord->Eax = (DWORD)(uintptr_t)f; e->ContextRecord->Eip = (DWORD)(uintptr_t)&f60_seh_land;
    e->ContextRecord->EFlags &= ~0x400u;   /* direction flag clear, as the ABI wants it */
    return EXCEPTION_CONTINUE_EXECUTION;
}
F60Seh::F60Seh() {
    if (g_tls == TLS_OUT_OF_INDEXES) {   /* first __try of the process (the patch starts on one thread) */
        DWORD t = TlsAlloc(); if (InterlockedCompareExchange((volatile LONG*)&g_tls, (LONG)t, (LONG)TLS_OUT_OF_INDEXES) != (LONG)TLS_OUT_OF_INDEXES) TlsFree(t);
        else g_veh = AddVectoredExceptionHandler(1, veh); }
    code = 0; prev = top(); armed = 1; TlsSetValue(g_tls, this);
}
F60Seh::~F60Seh() { if (armed) { armed = 0; TlsSetValue(g_tls, prev); } }
