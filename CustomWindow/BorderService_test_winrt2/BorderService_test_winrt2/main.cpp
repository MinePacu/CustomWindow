#include "pch.h"
#include <shellapi.h>
#include <string>
#include <string_view>

using namespace winrt;
using Microsoft::WRL::ComPtr;

// Config
static D2D1_COLOR_F g_borderColor = D2D1::ColorF(0.0f, 0.8f, 1.0f, 1.0f);
static float g_thickness = 3.0f;

static HWND g_overlay = nullptr;
static HWINEVENTHOOK g_hook1 = nullptr, g_hook2 = nullptr, g_hook3 = nullptr;

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

    ShowWindow(h, SW_SHOW);

    // periodic safety sync (1s)
    SetTimer(h, 1, 1000, nullptr);

    return h;
}

void CALLBACK WinEventProc(HWINEVENTHOOK, DWORD, HWND hwnd, LONG idObject, LONG, DWORD, DWORD)
{
    if (idObject != OBJID_WINDOW || hwnd == nullptr) return;
    // marshal to UI thread (overlay wnd)
    if (g_overlay) PostMessageW(g_overlay, WM_APP_REFRESH, 0, 0);
}

static void InstallWinEventHooks()
{
    DWORD flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
    g_hook1 = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE, nullptr, WinEventProc, 0, 0, flags);
    g_hook2 = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, nullptr, WinEventProc, 0, 0, flags);
    g_hook3 = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, nullptr, WinEventProc, 0, 0, flags);
}

static void UninstallWinEventHooks()
{
    if (g_hook1) { UnhookWinEvent(g_hook1); g_hook1 = nullptr; }
    if (g_hook2) { UnhookWinEvent(g_hook2); g_hook2 = nullptr; }
    if (g_hook3) { UnhookWinEvent(g_hook3); g_hook3 = nullptr; }
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

// Z-Order를 보존한 사각형 목록을 받아, 위 창들로 가려진 부분을 제외한 '바깥쪽 띠'만
// 오버레이 윈도우 영역으로 설정
static void UpdateOverlayRegion(const std::vector<RECT>& zorderedRects)
{
    if (!g_overlay) return;

    // 최종 오버레이 영역(윈도우가 보이는 부분)
    HRGN finalRgn = CreateRectRgn(0, 0, 0, 0);
    // 위에 위치한 창들의 합집합(가려지는 영역)
    HRGN coveredRgn = CreateRectRgn(0, 0, 0, 0);

    int t = (int)(g_thickness + 0.999f); // ceil
    if (t < 1) t = 1;
        
    // 상단(TopMost) -> 하단 순서로 열거된 rects 입력을 전제로 함
    for (const auto& r : zorderedRects)
    {
        // 오버레이 좌표계로 변환
        RECT winR{ r.left - g_virtualScreen.left, r.top - g_virtualScreen.top,
                   r.right - g_virtualScreen.left, r.bottom - g_virtualScreen.top };

        // 테두리 '바깥쪽 띠' 영역 생성
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

        // 이미 위 창들이 덮고 있는 영역(coveredRgn)을 빼서 실제로 보이는 부분만 남김
        HRGN visibleBands = CreateRectRgn(0, 0, 0, 0);
        CombineRgn(visibleBands, bandRgn, coveredRgn, RGN_DIFF);

        // 최종 오버레이 영역에 합치기
        CombineRgn(finalRgn, finalRgn, visibleBands, RGN_OR);

        // 다음 창들을 위해 '덮는 영역'에 현재 창 사각형(띠 두께만큼 확장)을 추가
        RECT occ{ winR.left - t, winR.top - t, winR.right + t, winR.bottom + t };
        HRGN occRgn = CreateRectRgn(occ.left, occ.top, occ.right, occ.bottom);
        CombineRgn(coveredRgn, coveredRgn, occRgn, RGN_OR);

        // 정리
        DeleteObject(occRgn);
        DeleteObject(visibleBands);
        DeleteObject(bandRgn);
    }

    // 윈도우 영역 적용(시스템이 finalRgn의 소유권을 가짐)
    SetWindowRgn(g_overlay, finalRgn, FALSE);
    DeleteObject(coveredRgn);
}

// RefreshOverlay 교체: Z-Order 유지 목록으로 클리핑/렌더링
void RefreshOverlay()
{
    if (!g_overlay) return;

    UINT width = g_virtualScreen.right - g_virtualScreen.left;
    UINT height = g_virtualScreen.bottom - g_virtualScreen.top;
    if (FAILED(EnsureSurface(width, height))) return;

    // 1) Z-Order를 보존한 창 목록 확보
    std::vector<RECT> rectsZ;
    {
        auto hwnds = CollectUserVisibleWindows(); // EnumWindows 순서: 상단 -> 하단
        rectsZ.reserve(hwnds.size());
        for (HWND h : hwnds) {
            RECT rc{};
            if (GetWindowBounds(h, rc)) rectsZ.push_back(rc);
        }
    }

    // 2) 오버레이 윈도우 영역을 '가시 부분의 바깥 띠'로만 설정(가려진 곳은 제외)
    UpdateOverlayRegion(rectsZ);

    // 3) 렌더링
    POINT offset{ 0,0 };
    ComPtr<ID2D1DeviceContext> ctx;
    BeginDrawOnSurface(width, height, &ctx, &offset);
    if (!ctx) return;

    ctx->BeginDraw();
    ctx->Clear(D2D1::ColorF(0, 0));

    // 라인은 전체를 그리되, 오버레이 윈도우 영역으로 최종 클립됨
    DrawBorders(ctx.Get(), rectsZ);

    if (SUCCEEDED(ctx->EndDraw())) {
        EndDrawOnSurface();
    }
}

// 보이는 창 목록과 g_targets를 동기화: 신규/이동/닫힘 반영
static void SyncTargets()
{
    // 현재 보이는 창들 수집
    auto current = CollectUserVisibleWindows();

    // 빠른 포함 검사용 집합
    std::unordered_set<HWND, HwndHash, HwndEq> curset;
    curset.reserve(current.size());

    for (HWND h : current)
    {
        curset.insert(h);
        RECT rc{};
        if (GetWindowBounds(h, rc)) {
            g_targets[h] = rc; // 추가 또는 업데이트
        }
    }

    // 사라진(닫힘/비가시) 창 제거
    for (auto it = g_targets.begin(); it != g_targets.end(); )
    {
        HWND h = it->first;
        if (!IsWindow(h) || curset.find(h) == curset.end())
            it = g_targets.erase(it);
        else
            ++it;
    }
}

static bool TryParseColorHex(std::wstring_view s, D2D1_COLOR_F& out)
{
    if (!s.empty() && s.front() == L'#') s.remove_prefix(1);
    if (s.size() != 6 && s.size() != 8) return false;

    unsigned int v = 0;
    for (wchar_t ch : s) {
        unsigned int d = 0;
        if (ch >= L'0' && ch <= L'9') d = ch - L'0';
        else if (ch >= L'a' && ch <= L'f') d = 10u + (ch - L'a');
        else if (ch >= L'A' && ch <= L'F') d = 10u + (ch - L'A');
        else return false;
        v = (v << 4) | d;
    }

    unsigned int a = 0xFF, r, g, b;
    if (s.size() == 8) { a = (v >> 24) & 0xFF; r = (v >> 16) & 0xFF; g = (v >> 8) & 0xFF; b = v & 0xFF; }
    else { r = (v >> 16) & 0xFF; g = (v >> 8) & 0xFF; b = v & 0xFF; }

    out = D2D1::ColorF(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
    return true;
}

struct CmdArgs {
    bool console = false;
    bool hasColor = false; D2D1_COLOR_F color{};
    bool hasThickness = false; float thickness = 0.0f;
};

static CmdArgs ParseCmd()
{
    CmdArgs out{};
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return out;

    for (int i = 1; i < argc; ++i) {
        std::wstring_view a = argv[i];
        if (a == L"--console") out.console = true;
        else if (a == L"--color" && i + 1 < argc) {
            std::wstring_view v = argv[++i];
            D2D1_COLOR_F c{};
            if (TryParseColorHex(v, c)) { out.hasColor = true; out.color = c; }
        }
        else if ((a == L"--thickness" || a == L"-t") && i + 1 < argc) {
            std::wstring_view v = argv[++i];
            wchar_t* endp = nullptr;
            float t = wcstof(std::wstring(v).c_str(), &endp);
            if (endp && t > 0.0f) { out.hasThickness = true; out.thickness = t; }
        }
    }
    LocalFree(argv);
    return out;
}

static void ApplyArgs(const CmdArgs& args)
{
    if (args.hasColor) g_borderColor = args.color;
    if (args.hasThickness) g_thickness = args.thickness;
}

int main()
{
    auto args = ParseCmd();
    if (args.console) {
        AllocConsole();
        FILE* f;
        freopen_s(&f, "CONOUT$", "w", stdout);
        freopen_s(&f, "CONOUT$", "w", stderr);
        wprintf(L"[BorderServiceWinRT] console attached\n");
    }
    ApplyArgs(args);

    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    winrt::init_apartment();

    g_overlay = CreateOverlayWindow();
    if (FAILED(CreateD3DDevice())) return -1;
    if (FAILED(CreateD2D())) return -2;
    if (FAILED(CreateDComp(g_overlay))) return -3;

    SyncTargets();
    RefreshOverlay();
    InstallWinEventHooks();

    if (args.console) {
        wprintf(L"[BorderServiceWinRT] thickness=%.2f color=%.3f,%.3f,%.3f,%.3f\n",
            g_thickness, g_borderColor.r, g_borderColor.g, g_borderColor.b, g_borderColor.a);
    }

    MSG msg{};
    while (GetMessageW(&msg, nullptr, 0, 0)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
    UninstallWinEventHooks();
    return 0;
}
