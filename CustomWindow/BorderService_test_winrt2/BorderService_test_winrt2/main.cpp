#include "pch.h"
#include <cwctype>
#include <algorithm>
#include <string>
#include <shellapi.h>
#include <cstdio>

using namespace winrt;

using Microsoft::WRL::ComPtr;

// Simple logging helpers
static void DebugLog(const std::wstring& s)
{
    OutputDebugStringW((s + L"\n").c_str());
    if (GetConsoleWindow()) {
        _putws(s.c_str());
    }
}

// Console control via args
static bool g_console = false;
static void EnsureConsole()
{
    if (!g_console) return;
    if (GetConsoleWindow()) return;
    if (AllocConsole())
    {
        FILE* fp;
        freopen_s(&fp, "CONOUT$", "w", stdout);
        freopen_s(&fp, "CONOUT$", "w", stderr);
        freopen_s(&fp, "CONIN$",  "r", stdin);
        DebugLog(L"[Overlay] Console allocated");
    }
}

// Config
static D2D1_COLOR_F g_borderColor = D2D1::ColorF(0.0f, 0.8f, 1.0f, 1.0f);
static float g_thickness = 3.0f;

static HWND g_overlay = nullptr;
static HWINEVENTHOOK g_hook1 = nullptr, g_hook2 = nullptr, g_hook3 = nullptr;
static HWINEVENTHOOK g_hook4 = nullptr, g_hook5 = nullptr, g_hook6 = nullptr; // new hooks

// Hash for HWND keys
struct HwndHash {
    size_t operator()(HWND h) const noexcept {
        return std::hash<std::uintptr_t>{}(reinterpret_cast<std::uintptr_t>(h));
    }
};
struct HwndEq { bool operator()(HWND a, HWND b) const noexcept { return a == b; } };

// Track target windows and their last known rect
static std::unordered_map<HWND, RECT, HwndHash, HwndEq> g_targets;

// DX/D2D/DComp objects
static ComPtr<ID2D1Factory1> g_d2dFactory;
static ComPtr<ID2D1Device> g_d2dDevice;
static ComPtr<ID2D1DeviceContext> g_d2dCtx;
static ComPtr<ID3D11Device> g_d3d;
static ComPtr<ID3D11DeviceContext> g_d3dCtx;
static ComPtr<IDXGIDevice> g_dxgiDevice;
static ComPtr<IDCompositionDevice> g_dcompDevice;
static ComPtr<IDCompositionTarget> g_dcompTarget;
static ComPtr<IDCompositionVisual> g_rootVisual;
static ComPtr<IDCompositionVisual> g_surfaceVisual;
static ComPtr<IDCompositionSurface> g_surface;

// Track current surface size to recreate on change
static UINT g_surfaceW = 0, g_surfaceH = 0;

// Virtual screen rect
static RECT g_virtualScreen{};

// Custom messages
constexpr UINT WM_APP_REFRESH = WM_APP + 1;

// Forward decls
std::vector<HWND> CollectUserVisibleWindows();
void RefreshOverlay();
static void SyncTargets();
static void UpdateOverlayRegion(const std::vector<RECT>& rects);

static bool IsWindowCloaked(HWND h)
{
    BOOL cloaked = FALSE;
    if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))))
        return cloaked != FALSE;
    return false;
}

static bool IsAltTabEligible(HWND h)
{
    if (!IsWindowVisible(h) || IsIconic(h)) return false;
    if (GetAncestor(h, GA_ROOT) != h) return false;

    LONG_PTR ex = GetWindowLongPtr(h, GWL_EXSTYLE);
    if (ex & WS_EX_TOOLWINDOW) return false;

    wchar_t cls[128] = {};
    GetClassNameW(h, cls, 128);
    if (wcscmp(cls, L"Shell_TrayWnd") == 0) return false;
    if (wcscmp(cls, L"Progman") == 0) return false;
    if (wcscmp(cls, L"WorkerW") == 0) return false;

    if (IsWindowCloaked(h)) return false;

    return true;
}

static bool GetWindowBounds(HWND h, RECT& out)
{
    if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, &out, sizeof(out))))
        return true;
    return GetWindowRect(h, &out) != 0;
}

std::vector<HWND> CollectUserVisibleWindows()
{
    std::vector<HWND> result;
    EnumWindows([](HWND h, LPARAM lParam) -> BOOL {
        auto& vec = *reinterpret_cast<std::vector<HWND>*>(lParam);
        if (!IsAltTabEligible(h)) return TRUE;
        RECT rc{};
        if (!GetWindowBounds(h, rc)) return TRUE;

        RECT inter{};
        if (IntersectRect(&inter, &rc, &g_virtualScreen) && (inter.right > inter.left) && (inter.bottom > inter.top))
            vec.push_back(h);
        return TRUE;
    }, reinterpret_cast<LPARAM>(&result));

    return result;
}

// Device setup
static HRESULT CreateD3DDevice()
{
    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if defined(_DEBUG)
    // flags |= D3D11_CREATE_DEVICE_DEBUG; // enable if available
#endif
    D3D_FEATURE_LEVEL fls[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0 };
    D3D_FEATURE_LEVEL flOut{};
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, fls, _countof(fls), D3D11_SDK_VERSION, &g_d3d, &flOut, &g_d3dCtx);
    if (FAILED(hr)) {
        // fallback WARP
        hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags, fls, _countof(fls), D3D11_SDK_VERSION, &g_d3d, &flOut, &g_d3dCtx);
    }
    if (SUCCEEDED(hr)) {
        hr = g_d3d.As(&g_dxgiDevice);
    }
    return hr;
}

static HRESULT CreateD2D()
{
    D2D1_FACTORY_OPTIONS fo{};
#if defined(_DEBUG)
    // fo.debugLevel = D2D1_DEBUG_LEVEL_INFORMATION;
#endif
    HRESULT hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, IID_PPV_ARGS(&g_d2dFactory));
    if (FAILED(hr)) return hr;
    hr = g_d2dFactory->CreateDevice(g_dxgiDevice.Get(), &g_d2dDevice);
    if (FAILED(hr)) return hr;
    hr = g_d2dDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &g_d2dCtx);
    return hr;
}

static HRESULT CreateDComp(HWND hwnd)
{
    HRESULT hr = DCompositionCreateDevice(g_dxgiDevice.Get(), IID_PPV_ARGS(&g_dcompDevice));
    if (FAILED(hr)) return hr;
    hr = g_dcompDevice->CreateTargetForHwnd(hwnd, TRUE, &g_dcompTarget);
    if (FAILED(hr)) return hr;

    hr = g_dcompDevice->CreateVisual(&g_rootVisual);
    if (FAILED(hr)) return hr;
    hr = g_dcompDevice->CreateVisual(&g_surfaceVisual);
    if (FAILED(hr)) return hr;

    hr = g_dcompTarget->SetRoot(g_rootVisual.Get());
    if (FAILED(hr)) return hr;

    hr = g_rootVisual->AddVisual(g_surfaceVisual.Get(), FALSE, nullptr);
    return hr;
}

static HRESULT EnsureSurface(UINT width, UINT height)
{
    if (width == 0 || height == 0) return E_INVALIDARG;

    // Recreate if size changed
    if (g_surface && (g_surfaceW != width || g_surfaceH != height)) {
        g_surface.Reset();
    }

    if (!g_surface) {
        HRESULT hr = g_dcompDevice->CreateSurface(width, height, DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE_PREMULTIPLIED, &g_surface);
        if (FAILED(hr)) return hr;
        g_surfaceVisual->SetContent(g_surface.Get());
        g_surfaceW = width;
        g_surfaceH = height;
        return S_OK;
    }
    return S_OK;
}

static void BeginDrawOnSurface(UINT width, UINT height, ID2D1DeviceContext** outCtx, POINT* offset)
{
    *outCtx = nullptr;
    ComPtr<IDXGISurface> dxgiSurface;
    RECT upd = { 0,0,(LONG)width,(LONG)height };
    if (SUCCEEDED(g_surface->BeginDraw(&upd, IID_PPV_ARGS(&dxgiSurface), offset))) {
        ComPtr<ID2D1Bitmap1> targetBmp;
        D2D1_BITMAP_PROPERTIES1 props = D2D1::BitmapProperties1(
            D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        g_d2dCtx->CreateBitmapFromDxgiSurface(dxgiSurface.Get(), &props, &targetBmp);
        g_d2dCtx->SetTarget(targetBmp.Get());
        *outCtx = g_d2dCtx.Get();
        (*outCtx)->AddRef();
    }
}

static void EndDrawOnSurface()
{
    g_d2dCtx->SetTarget(nullptr);
    g_surface->EndDraw();
    g_dcompDevice->Commit();
}

static void UpdateVirtualScreenAndResize()
{
    RECT newVS{ GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_XVIRTUALSCREEN) + GetSystemMetrics(SM_CXVIRTUALSCREEN),
                GetSystemMetrics(SM_YVIRTUALSCREEN) + GetSystemMetrics(SM_CYVIRTUALSCREEN) };
    g_virtualScreen = newVS;

    if (g_overlay) {
        MoveWindow(g_overlay,
                   g_virtualScreen.left, g_virtualScreen.top,
                   g_virtualScreen.right - g_virtualScreen.left,
                   g_virtualScreen.bottom - g_virtualScreen.top,
                   FALSE);
    }

    // Force surface recreate on next draw
    g_surfaceW = g_surfaceH = 0;
    g_surface.Reset();
}

// Helpers: parse hex color
static bool ParseColorHex(const std::wstring& hex, D2D1_COLOR_F& out)
{
    if (hex.empty()) return false;
    std::wstring h = hex;
    if (h[0] == L'#') h.erase(h.begin());
    if (h.size() != 6 && h.size() != 8) return false;
    unsigned int val = 0;
    try {
        val = std::stoul(h, nullptr, 16);
    } catch (...) { return false; }

    float a = 1.0f, r = 0, g = 0, b = 0;
    if (h.size() == 8) {
        unsigned int A = (val >> 24) & 0xFF;
        unsigned int R = (val >> 16) & 0xFF;
        unsigned int G = (val >> 8) & 0xFF;
        unsigned int B = (val) & 0xFF;
        a = A / 255.0f; r = R / 255.0f; g = G / 255.0f; b = B / 255.0f;
    } else {
        unsigned int R = (val >> 16) & 0xFF;
        unsigned int G = (val >> 8) & 0xFF;
        unsigned int B = (val) & 0xFF;
        a = 1.0f; r = R / 255.0f; g = G / 255.0f; b = B / 255.0f;
    }
    out = D2D1::ColorF(r, g, b, a);
    return true;
}

static void ApplySettingsFromCommand(const std::wstring& cmd)
{
    std::wstring lower = cmd;
    std::transform(lower.begin(), lower.end(), lower.begin(), [](wchar_t c){ return (wchar_t)::towlower(c); });

    // find color
    size_t cpos = lower.find(L"color=");
    if (cpos != std::wstring::npos) {
        size_t start = cpos + 6;
        size_t end = lower.find_first_of(L" ;\r\n\t", start);
        std::wstring col = cmd.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start);
        D2D1_COLOR_F cf{};
        if (ParseColorHex(col, cf)) {
            g_borderColor = cf;
            DebugLog(L"[Overlay] Applied color: " + std::wstring(col));
        }
    }

    // find thickness
    size_t tpos = lower.find(L"thickness=");
    if (tpos != std::wstring::npos) {
        size_t start = tpos + 10;
        size_t end = lower.find_first_of(L" ;\r\n\t", start);
        std::wstring th = lower.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start);
        try {
            float tv = std::stof(th);
            if (tv > 0 && tv < 1000) {
                g_thickness = tv;
                DebugLog(L"[Overlay] Applied thickness: " + th);
            }
        } catch (...) {}
    }
}

static LRESULT CALLBACK OverlayProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_TIMER:
        if (wParam == 1) {
            SyncTargets();
            RefreshOverlay();
        }
        return 0;
    case WM_APP_REFRESH:
        SyncTargets();
        RefreshOverlay();
        return 0;
    case WM_COPYDATA:
        {
            PCOPYDATASTRUCT cds = reinterpret_cast<PCOPYDATASTRUCT>(lParam);
            if (cds && cds->lpData && cds->cbData > 0) {
                const wchar_t* data = static_cast<const wchar_t*>(cds->lpData);
                size_t wlen = cds->cbData / sizeof(wchar_t);
                while (wlen > 0 && data[wlen - 1] == L'\0') --wlen;
                std::wstring msgStr(data, data + wlen);
                DebugLog(L"[Overlay] WM_COPYDATA received: " + msgStr);
                ApplySettingsFromCommand(msgStr);
                PostMessageW(hwnd, WM_APP_REFRESH, 0, 0);
            } else {
                DebugLog(L"[Overlay] WM_COPYDATA received with empty data");
            }
            return 0;
        }
    case WM_NCHITTEST:
        return static_cast<LRESULT>(HTTRANSPARENT);
    case WM_MOUSEACTIVATE:
        return static_cast<LRESULT>(MA_NOACTIVATE); // 포커스/활성화 방지
    case WM_DISPLAYCHANGE:
    case WM_DPICHANGED:
        UpdateVirtualScreenAndResize();
        PostMessageW(hwnd, WM_APP_REFRESH, 0, 0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

static HWND CreateOverlayWindow()
{
    // Compute virtual screen
    UpdateVirtualScreenAndResize();

    WNDCLASSW wc{};
    wc.lpfnWndProc = OverlayProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"BorderOverlayDCompWindowClass";
    RegisterClassW(&wc);

    HWND h = CreateWindowExW(
        /* WS_EX_LAYERED | */ WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        wc.lpszClassName, L"", WS_POPUP,
        g_virtualScreen.left, g_virtualScreen.top,
        g_virtualScreen.right - g_virtualScreen.left,
        g_virtualScreen.bottom - g_virtualScreen.top,
        nullptr, nullptr, wc.hInstance, nullptr);

    // allow WM_COPYDATA from lower integrity senders
    CHANGEFILTERSTRUCT cfs{ sizeof(CHANGEFILTERSTRUCT) };
    ChangeWindowMessageFilterEx(h, WM_COPYDATA, MSGFLT_ALLOW, &cfs);

    ShowWindow(h, SW_SHOW);

    // periodic safety sync (faster: 150ms)
    SetTimer(h, 1, 150, nullptr);

    DebugLog(L"[Overlay] Overlay window created and message filter applied");

    return h;
}

void CALLBACK WinEventProc(HWINEVENTHOOK, DWORD eventId, HWND hwnd, LONG idObject, LONG, DWORD, DWORD)
{
    if (eventId >= EVENT_OBJECT_CREATE && eventId <= EVENT_OBJECT_HIDE) {
        if (idObject != OBJID_WINDOW || hwnd == nullptr) return;
    }
    if (g_overlay) {
        LRESULT res{};
        if (!SendMessageTimeoutW(g_overlay, WM_APP_REFRESH, 0, 0, SMTO_NORMAL, 50, reinterpret_cast<PDWORD_PTR>(&res))) {
            PostMessageW(g_overlay, WM_APP_REFRESH, 0, 0);
        }
    }
}

static void InstallWinEventHooks()
{
    DWORD flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
    g_hook1 = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE, nullptr, WinEventProc, 0, 0, flags);
    g_hook2 = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, nullptr, WinEventProc, 0, 0, flags);
    g_hook3 = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, nullptr, WinEventProc, 0, 0, flags);
    g_hook4 = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, nullptr, WinEventProc, 0, 0, flags);
    g_hook5 = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, nullptr, WinEventProc, 0, 0, flags);
    g_hook6 = SetWinEventHook(EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER, nullptr, WinEventProc, 0, 0, flags);
}

static void UninstallWinEventHooks()
{
    if (g_hook1) { UnhookWinEvent(g_hook1); g_hook1 = nullptr; }
    if (g_hook2) { UnhookWinEvent(g_hook2); g_hook2 = nullptr; }
    if (g_hook3) { UnhookWinEvent(g_hook3); g_hook3 = nullptr; }
    if (g_hook4) { UnhookWinEvent(g_hook4); g_hook4 = nullptr; }
    if (g_hook5) { UnhookWinEvent(g_hook5); g_hook5 = nullptr; }
    if (g_hook6) { UnhookWinEvent(g_hook6); g_hook6 = nullptr; }
}

static void DrawBorders(ID2D1DeviceContext* ctx, const std::vector<RECT>& rects)
{
    ComPtr<ID2D1SolidColorBrush> brush;
    ctx->CreateSolidColorBrush(g_borderColor, &brush);
    ctx->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    for (const auto& r : rects)
    {
        D2D1_RECT_F rf = D2D1::RectF((FLOAT)r.left - (FLOAT)g_virtualScreen.left, (FLOAT)r.top - (FLOAT)g_virtualScreen.top,
                                     (FLOAT)r.right - (FLOAT)g_virtualScreen.left, (FLOAT)r.bottom - (FLOAT)g_virtualScreen.top);
        ctx->DrawRectangle(rf, brush.Get(), g_thickness);
    }
}

static void UpdateOverlayRegion(const std::vector<RECT>& zorderedRects)
{
    if (!g_overlay) return;

    HRGN finalRgn = CreateRectRgn(0, 0, 0, 0);
    HRGN coveredRgn = CreateRectRgn(0, 0, 0, 0);

    int t = (int)(g_thickness + 0.999f); // ceil
    if (t < 1) t = 1;
        
    for (const auto& r : zorderedRects)
    {
        RECT winR{ r.left - g_virtualScreen.left, r.top - g_virtualScreen.top,
                   r.right - g_virtualScreen.left, r.bottom - g_virtualScreen.top };

        RECT topBand    { winR.left - t, winR.top - t, winR.right + t, winR.top };
        RECT bottomBand { winR.left - t, winR.bottom,  winR.right + t, winR.bottom + t };
        RECT leftBand   { winR.left - t, winR.top - t, winR.left,      winR.bottom + t };
        RECT rightBand  { winR.right,    winR.top - t, winR.right + t, winR.bottom + t };

        HRGN bandRgn = CreateRectRgn(0, 0, 0, 0);
        auto addBand = [&](const RECT& band) {
            HRGN rgnBand = CreateRectRgn(band.left, band.top, band.right, band.bottom);
            CombineRgn(bandRgn, bandRgn, rgnBand, RGN_OR);
            DeleteObject(rgnBand);
        };
        addBand(topBand);
        addBand(bottomBand);
        addBand(leftBand);
        addBand(rightBand);

        HRGN visibleBands = CreateRectRgn(0, 0, 0, 0);
        CombineRgn(visibleBands, bandRgn, coveredRgn, RGN_DIFF);

        CombineRgn(finalRgn, finalRgn, visibleBands, RGN_OR);

        RECT occ{ winR.left - t, winR.top - t, winR.right + t, winR.bottom + t };
        HRGN occRgn = CreateRectRgn(occ.left, occ.top, occ.right, occ.bottom);
        CombineRgn(coveredRgn, coveredRgn, occRgn, RGN_OR);

        DeleteObject(occRgn);
        DeleteObject(visibleBands);
        DeleteObject(bandRgn);
    }

    SetWindowRgn(g_overlay, finalRgn, FALSE);
    DwmFlush();
    DeleteObject(coveredRgn);
}

void RefreshOverlay()
{
    if (!g_overlay) return;

    UINT width = g_virtualScreen.right - g_virtualScreen.left;
    UINT height = g_virtualScreen.bottom - g_virtualScreen.top;
    if (FAILED(EnsureSurface(width, height))) return;

    std::vector<RECT> rectsZ;
    {
        auto hwnds = CollectUserVisibleWindows();
        rectsZ.reserve(hwnds.size());
        for (HWND h : hwnds) {
            RECT rc{};
            if (GetWindowBounds(h, rc)) rectsZ.push_back(rc);
        }
    }

    UpdateOverlayRegion(rectsZ);

    POINT offset{ 0,0 };
    ComPtr<ID2D1DeviceContext> ctx;
    BeginDrawOnSurface(width, height, &ctx, &offset);
    if (!ctx) return;

    ctx->BeginDraw();
    ctx->Clear(D2D1::ColorF(0, 0));

    DrawBorders(ctx.Get(), rectsZ);

    if (SUCCEEDED(ctx->EndDraw())) {
        EndDrawOnSurface();
    }
}

static void SyncTargets()
{
    auto current = CollectUserVisibleWindows();

    std::unordered_set<HWND, HwndHash, HwndEq> curset;
    curset.reserve(current.size());

    for (HWND h : current)
    {
        curset.insert(h);
        RECT rc{};
        if (GetWindowBounds(h, rc)) {
            g_targets[h] = rc;
        }
    }

    for (auto it = g_targets.begin(); it != g_targets.end(); )
    {
        HWND h = it->first;
        if (!IsWindow(h) || curset.find(h) == curset.end())
            it = g_targets.erase(it);
        else
            ++it;
    }
}

// Parse command-line args and apply initial settings
static void ParseArgsAndApply()
{
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return;

    auto tolower = [](std::wstring s) {
        std::transform(s.begin(), s.end(), s.begin(), [](wchar_t c){ return (wchar_t)::towlower(c); });
        return s;
    };

    for (int i = 0; i < argc; ++i)
    {
        std::wstring arg = tolower(argv[i]);
        if (arg == L"--console") { g_console = true; continue; }

        if (arg == L"--color" && i + 1 < argc) {
            D2D1_COLOR_F cf{};
            if (ParseColorHex(argv[i + 1], cf)) { g_borderColor = cf; DebugLog(L"[Overlay] Arg color: " + std::wstring(argv[i + 1])); }
            ++i; continue;
        }
        if (arg.rfind(L"--color=", 0) == 0) {
            std::wstring val = std::wstring(argv[i] + 8);
            D2D1_COLOR_F cf{};
            if (ParseColorHex(val, cf)) { g_borderColor = cf; DebugLog(L"[Overlay] Arg color: " + val); }
            continue;
        }
        if (arg == L"--thickness" && i + 1 < argc) {
            try { float tv = std::stof(argv[i + 1]); if (tv > 0 && tv < 1000) { g_thickness = tv; DebugLog(L"[Overlay] Arg thickness: " + std::wstring(argv[i + 1])); } } catch (...) {}
            ++i; continue;
        }
        if (arg.rfind(L"--thickness=", 0) == 0) {
            std::wstring val = std::wstring(argv[i] + 12);
            try { float tv = std::stof(val); if (tv > 0 && tv < 1000) { g_thickness = tv; DebugLog(L"[Overlay] Arg thickness: " + val); } } catch (...) {}
            continue;
        }
    }

    LocalFree(argv);
}

int main()
{
    // Parse args and optionally allocate console first
    ParseArgsAndApply();
    EnsureConsole();

    // DPI awareness for accurate coordinates
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    winrt::init_apartment();

    // Create overlay first
    g_overlay = CreateOverlayWindow();

    // Create devices
    if (FAILED(CreateD3DDevice())) return -1;
    if (FAILED(CreateD2D())) return -2;
    if (FAILED(CreateDComp(g_overlay))) return -3;

    // Initial sync + draw
    SyncTargets();
    RefreshOverlay();

    // Hooks
    InstallWinEventHooks();

    DebugLog(L"[Overlay] Started overlay loop");

    // Message loop
    MSG msg{};
    while (GetMessageW(&msg, nullptr, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    UninstallWinEventHooks();
    return 0;
}
