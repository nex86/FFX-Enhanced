// SPDX-License-Identifier: Apache-2.0
// Modified for FFX-2_enh.exe (2026-11): the X2-VSYNC part of FFX-60fps src/x2/rows/X2-DIAG-PRESENT.cpp (the diagnostic
// row and its taps are left out); see module/fps60/NOTICE.txt.
// X2-VSYNC (option, default 0): while on, IDXGISwapChain::Present gets SyncInterval 1 (wait for the next screen refresh:
// no tearing). Present is found through a throwaway device and swap chain on a hidden window (every swap chain shares the
// vtable) and hooked with MinHook.
#include "../../module/fps60/x2/x2.h"
#include <d3d11.h>
#include <dxgi.h>

namespace x2 {

typedef HRESULT(STDMETHODCALLTYPE* present_t)(IDXGISwapChain* sc, UINT sync, UINT flags);

static present_t g_present_orig = nullptr;
static void*     g_present = nullptr;
extern Row row_VSYNC;
static bool g_hooked = false;
static volatile LONG g_forced = 0;

static HRESULT STDMETHODCALLTYPE h_present(IDXGISwapChain* sc, UINT sync, UINT flags) {
    UINT out = sync;
    if (row_VSYNC.on) {
        if (InterlockedIncrement(&g_forced) == 1) log_line("X2-VSYNC: first Present with SyncInterval 1 (the game passed %u)", sync);
        out = 1;
    }
    return g_present_orig(sc, out, flags);
}

// Present's address from a throwaway swap chain (DXGI's vtable slot 8, shared by all swap chains of the process).
static void* find_present() {
    WNDCLASSW wc = {};
    wc.lpfnWndProc = DefWindowProcW;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"x2_present_probe";
    RegisterClassW(&wc);
    HWND w = CreateWindowW(wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW, 0, 0, 8, 8, nullptr, nullptr, wc.hInstance, nullptr);
    if (!w) return nullptr;
    DXGI_SWAP_CHAIN_DESC d = {};
    d.BufferCount = 1;
    d.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    d.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    d.OutputWindow = w;
    d.SampleDesc.Count = 1;
    d.Windowed = TRUE;
    IDXGISwapChain* sc = nullptr;
    ID3D11Device* dev = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    void* p = nullptr;
    HRESULT hr = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                                               &d, &sc, &dev, nullptr, &ctx);
    if (SUCCEEDED(hr) && sc) p = (*(void***)sc)[8];
    if (ctx) ctx->Release();
    if (sc) sc->Release();
    if (dev) dev->Release();
    DestroyWindow(w);
    UnregisterClassW(wc.lpszClassName, wc.hInstance);
    if (!p) log_line("  X2-VSYNC: throwaway swap chain failed (0x%08lX)", (unsigned long)hr);
    return p;
}

static bool install_vsync() {
    if (!g_hooked) {
        g_present = find_present();
        if (!g_present) return false;
        if (!hook_entry("X2 Present", g_present, (void*)&h_present, (void**)&g_present_orig)) return false;
        g_hooked = true;
    }
    log_line("  X2-VSYNC: IDXGISwapChain::Present at %p hooked; SyncInterval 1 while on", g_present);
    return true;
}

Row row_VSYNC = {
    "X2-VSYNC",
    "V-Sync: frames wait for the screen refresh (no tearing; the game presents without it)",
    0, install_vsync, nullptr, nullptr, 0
};

} // namespace x2
