#include "pch.h"
#include "Globals.h"
#include "DwmUtil.h"
#include "Args.h"

HRESULT CreateD3DDevice()
{
    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if defined(_DEBUG)
    // flags |= D3D11_CREATE_DEVICE_DEBUG; // enable if available
#endif
    D3D_FEATURE_LEVEL fls[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0 };
    D3D_FEATURE_LEVEL flOut{};
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, fls, _countof(fls), D3D11_SDK_VERSION, &g_d3d, &flOut, &g_d3dCtx);
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags, fls, _countof(fls), D3D11_SDK_VERSION, &g_d3d, &flOut, &g_d3dCtx);
    }
    if (SUCCEEDED(hr)) {
        hr = g_d3d.As(&g_dxgiDevice);
    }
    return hr;
}

HRESULT CreateD2D()
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

HRESULT CreateDComp(HWND hwnd)
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

HRESULT EnsureSurface(UINT width, UINT height)
{
    if (width == 0 || height == 0) return E_INVALIDARG;

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

void BeginDrawOnSurface(UINT width, UINT height, ID2D1DeviceContext** outCtx, POINT* offset)
{
    *outCtx = nullptr;
    Microsoft::WRL::ComPtr<IDXGISurface> dxgiSurface;
    RECT upd = { 0,0,(LONG)width,(LONG)height };
    if (SUCCEEDED(g_surface->BeginDraw(&upd, IID_PPV_ARGS(&dxgiSurface), offset))) {
        Microsoft::WRL::ComPtr<ID2D1Bitmap1> targetBmp;
        D2D1_BITMAP_PROPERTIES1 props = D2D1::BitmapProperties1(
            D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        g_d2dCtx->CreateBitmapFromDxgiSurface(dxgiSurface.Get(), &props, &targetBmp);
        g_d2dCtx->SetTarget(targetBmp.Get());
        *outCtx = g_d2dCtx.Get();
        (*outCtx)->AddRef();
    }
}

void EndDrawOnSurface()
{
    g_d2dCtx->SetTarget(nullptr);
    g_surface->EndDraw();
    g_dcompDevice->Commit();
}

void UpdateVirtualScreenAndResize()
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

    g_surfaceW = g_surfaceH = 0;
    g_surface.Reset();
}

void DrawBorders(ID2D1DeviceContext* ctx, const std::vector<RECT>& rects)
{
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> brush;
    ctx->CreateSolidColorBrush(g_borderColor, &brush);
    ctx->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);

    const float radius = CornerRadiusFromToken(g_cornerToken);
    const bool rounded = radius > 0.5f;

    for (const auto& r : rects)
    {
        D2D1_RECT_F rf = D2D1::RectF((FLOAT)r.left - (FLOAT)g_virtualScreen.left, (FLOAT)r.top - (FLOAT)g_virtualScreen.top,
                                     (FLOAT)r.right - (FLOAT)g_virtualScreen.left, (FLOAT)r.bottom - (FLOAT)g_virtualScreen.top);
        if (rounded) {
            D2D1_ROUNDED_RECT rr{ rf, radius, radius };
            ctx->DrawRoundedRectangle(rr, brush.Get(), g_thickness);
        } else {
            ctx->DrawRectangle(rf, brush.Get(), g_thickness);
        }
    }
}

void UpdateOverlayRegion(const std::vector<RECT>& zorderedRects)
{
    if (!g_overlay) return;

    HRGN finalRgn = CreateRectRgn(0, 0, 0, 0);
    HRGN coveredRgn = CreateRectRgn(0, 0, 0, 0);

    int t = (int)(g_thickness + 0.999f);
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
    if (!g_overlay || g_mode != RenderMode::DComp) return;

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
    Microsoft::WRL::ComPtr<ID2D1DeviceContext> ctx;
    BeginDrawOnSurface(width, height, &ctx, &offset);
    if (!ctx) return;

    ctx->BeginDraw();
    ctx->Clear(D2D1::ColorF(0, 0));

    // Debug log current settings before drawing
    DebugLog(L"[Overlay] Drawing with color: R=" + std::to_wstring(g_borderColor.r) + 
             L" G=" + std::to_wstring(g_borderColor.g) + 
             L" B=" + std::to_wstring(g_borderColor.b) + 
             L" A=" + std::to_wstring(g_borderColor.a) + 
             L" thickness=" + std::to_wstring(g_thickness));

    DrawBorders(ctx.Get(), rectsZ);

    if (SUCCEEDED(ctx->EndDraw())) {
        EndDrawOnSurface();
    }
}
