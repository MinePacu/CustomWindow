#include "pch.h"
#include "BorderServiceHost.h"
#include <mutex>
#include <thread>
#include <vector>
#include <unordered_map>

static std::mutex g_ctxMutex;
static BS_Context* g_autoCtx = nullptr;
static std::thread g_worker;
static std::atomic<bool> g_running{ false };
static HANDLE g_startEvent = nullptr;
static HANDLE g_stopEvent = nullptr;

// overlay tracking
struct OverlayInfo {
    HWND target = nullptr; // original window
    HWND overlay = nullptr; // layered border window
    RECT lastRect{};
};

static std::unordered_map<HWND, OverlayInfo> g_overlays; // key: target hwnd

static void DestroyOverlay(OverlayInfo& oi)
{
    if (oi.overlay && IsWindow(oi.overlay))
        DestroyWindow(oi.overlay);
    oi.overlay = nullptr;
}

static void EnsureOverlayFor(HWND target, BS_Context* ctx)
{
    if (!IsWindow(target) || !IsWindowVisible(target)) return;
    if (g_overlays.find(target) != g_overlays.end()) return;

    RECT rc; if (!GetWindowRect(target, &rc)) return;

    HWND overlay = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
        L"STATIC", L"", WS_POPUP,
        rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top,
        nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!overlay) return;

    // make click-through
    SetWindowLongPtrW(overlay, GWL_EXSTYLE,
        GetWindowLongPtrW(overlay, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

    // initial paint
    HDC hdc = GetDC(overlay);
    if (hdc)
    {
        // simple hollow rect painting via layered API
        HDC memDC = CreateCompatibleDC(hdc);
        RECT r = { 0,0, rc.right - rc.left, rc.bottom - rc.top };
        BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bi.bmiHeader.biWidth = r.right;
        bi.bmiHeader.biHeight = -r.bottom; // top-down
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        bi.bmiHeader.biCompression = BI_RGB;
        void* bits = nullptr;
        HBITMAP bmp = CreateDIBSection(hdc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (bmp)
        {
            HBITMAP old = (HBITMAP)SelectObject(memDC, bmp);
            auto color = ctx ? ctx->argbColor : 0xFFFF0000; // ARGB
            BYTE a = (color >> 24) & 0xFF;
            BYTE rC = (color >> 16) & 0xFF;
            BYTE gC = (color >> 8) & 0xFF;
            BYTE bC = (color) & 0xFF;
            int t = ctx ? ctx->thickness : 2;
            // fill transparent
            memset(bits, 0, r.right * r.bottom * 4);
            // draw border pixels (no AA)
            for (int y = 0; y < r.bottom; ++y)
            {
                for (int x = 0; x < r.right; ++x)
                {
                    bool edge = (x < t) || (x >= r.right - t) || (y < t) || (y >= r.bottom - t);
                    if (edge)
                    {
                        BYTE* p = (BYTE*)bits + (y * r.right + x) * 4;
                        p[0] = bC; p[1] = gC; p[2] = rC; p[3] = a;
                    }
                }
            }
            POINT srcPos{ 0,0 };
            SIZE size{ r.right, r.bottom };
            POINT dstPos{ rc.left, rc.top };
            BLENDFUNCTION bf{ AC_SRC_OVER,0,255,AC_SRC_ALPHA };
            UpdateLayeredWindow(overlay, nullptr, &dstPos, &size, memDC, &srcPos, 0, &bf, ULW_ALPHA);
            SelectObject(memDC, old);
            DeleteObject(bmp);
        }
        DeleteDC(memDC);
        ReleaseDC(overlay, hdc);
    }
    ShowWindow(overlay, SW_SHOWNOACTIVATE);
    SetWindowPos(overlay, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_NOOWNERZORDER|SWP_SHOWWINDOW);

    OverlayInfo info; info.target = target; info.overlay = overlay; info.lastRect = rc;
    g_overlays[target] = info;
}

static void UpdateOverlayGeometry(OverlayInfo& oi, BS_Context* ctx)
{
    if (!IsWindow(oi.target) || !IsWindow(oi.overlay)) return;
    RECT rc; if (!GetWindowRect(oi.target, &rc)) return;
    if (EqualRect(&rc, &oi.lastRect)) return;
    oi.lastRect = rc;
    SetWindowPos(oi.overlay, HWND_TOPMOST, rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top,
        SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);
    // TODO: optionally repaint if size changed
}

static void RepaintOverlay(OverlayInfo& oi, BS_Context* ctx)
{
    if (!IsWindow(oi.overlay) || !IsWindow(oi.target)) return;
    RECT rc; if (!GetWindowRect(oi.target, &rc)) return;
    int width = rc.right - rc.left;
    int height = rc.bottom - rc.top;
    HDC hdc = GetDC(oi.overlay);
    if (!hdc) return;
    HDC memDC = CreateCompatibleDC(hdc);
    BITMAPINFO bi{}; bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = width; bi.bmiHeader.biHeight = -height; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32; bi.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HBITMAP bmp = CreateDIBSection(hdc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (bmp)
    {
        HBITMAP old = (HBITMAP)SelectObject(memDC, bmp);
        auto color = ctx ? ctx->argbColor : 0xFFFF0000;
        BYTE a = (color >> 24) & 0xFF;
        BYTE rC = (color >> 16) & 0xFF;
        BYTE gC = (color >> 8) & 0xFF;
        BYTE bC = (color) & 0xFF;
        int t = ctx ? ctx->thickness : 2;
        memset(bits, 0, width * height * 4);
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                bool edge = (x < t) || (x >= width - t) || (y < t) || (y >= height - t);
                if (edge)
                {
                    BYTE* p = (BYTE*)bits + (y * width + x) * 4;
                    p[0] = bC; p[1] = gC; p[2] = rC; p[3] = a;
                }
            }
        }
        POINT srcPos{ 0,0 }; SIZE size{ width,height }; POINT dstPos{ rc.left, rc.top }; BLENDFUNCTION bf{ AC_SRC_OVER,0,255,AC_SRC_ALPHA };
        UpdateLayeredWindow(oi.overlay, nullptr, &dstPos, &size, memDC, &srcPos, 0, &bf, ULW_ALPHA);
        SelectObject(memDC, old);
        DeleteObject(bmp);
    }
    DeleteDC(memDC);
    ReleaseDC(oi.overlay, hdc);
}

extern "C" {

BS_Context* BS_CreateContext(int argb, int thickness, int debug)
{
    auto ctx = new (std::nothrow) BS_Context(argb, thickness, debug);
    if (ctx)
        ctx->Log(0, L"BS_CreateContext OK");
    return ctx;
}

void BS_DestroyContext(BS_Context* ctx)
{
    if (!ctx) return;
    ctx->Log(0, L"BS_DestroyContext");
    // destroy overlays
    for (auto& kv : g_overlays) {
        DestroyOverlay(kv.second);
    }
    g_overlays.clear();
    delete ctx;
}

void BS_UpdateColor(BS_Context* ctx, int argb)
{
    if (!ctx) return;
    ctx->argbColor = argb;
    ctx->Log(0, L"BS_UpdateColor");
    for (auto& kv : g_overlays) RepaintOverlay(kv.second, ctx);
}

void BS_UpdateThickness(BS_Context* ctx, int t)
{
    if (!ctx) return;
    ctx->thickness = t;
    ctx->Log(0, L"BS_UpdateThickness");
    for (auto& kv : g_overlays) RepaintOverlay(kv.second, ctx);
}

void BS_UpdateRects(BS_Context* ctx, const BS_NativeRect* normal, int normalCount, const BS_NativeRect* top, int topCount)
{
    if (!ctx) return;
    ctx->Log(0, L"BS_UpdateRects (unused stub)");
}

void BS_ForceRedraw(BS_Context* ctx)
{
    if (!ctx) return;
    ctx->Log(0, L"BS_ForceRedraw");
    for (auto& kv : g_overlays) RepaintOverlay(kv.second, ctx);
}

void BS_SetLogger(BS_Context* ctx, BS_LogFn logger)
{
    if (!ctx) return;
    ctx->logger = logger;
    ctx->Log(0, L"BS_SetLogger");
}

void BS_SetPartialRatio(BS_Context* ctx, float ratio01)
{
    if (!ctx) return;
    ctx->partialRatio = ratio01;
    ctx->Log(0, L"BS_SetPartialRatio");
}

void BS_EnableMerge(BS_Context* ctx, int enable)
{
    if (!ctx) return;
    ctx->mergeEnabled = (enable != 0);
    ctx->Log(0, L"BS_EnableMerge");
}

void BS_UpdateWindows(BS_Context* ctx, const HWND* hwnds, int count)
{
    if (!ctx) return;
    ctx->Log(0, L"BS_UpdateWindows");
    // mark existing
    std::unordered_map<HWND, bool> stillPresent;
    for (auto& kv : g_overlays) stillPresent[kv.first] = false;

    for (int i = 0; i < count; ++i)
    {
        HWND h = hwnds[i];
        if (!IsWindow(h)) continue;
        stillPresent[h] = true; // exists or new
        if (g_overlays.find(h) == g_overlays.end())
        {
            EnsureOverlayFor(h, ctx);
        }
    }
    // destroy those not present
    for (auto it = g_overlays.begin(); it != g_overlays.end(); )
    {
        if (!stillPresent[it->first])
        {
            DestroyOverlay(it->second);
            it = g_overlays.erase(it);
        }
        else ++it;
    }
    // update geometry for remaining
    for (auto& kv : g_overlays)
        UpdateOverlayGeometry(kv.second, ctx);
}
}

static void WorkerProc()
{
    while (g_running.load())
    {
        HANDLE handles[2] = { g_stopEvent, g_startEvent };
        DWORD wait = WaitForMultipleObjects(2, handles, FALSE, 500);
        if (wait == WAIT_OBJECT_0) // stop
            break;
        if (wait == WAIT_OBJECT_0 + 1) // start event (unused now)
        {
            std::lock_guard<std::mutex> lock(g_ctxMutex);
            if (!g_autoCtx)
            {
                g_autoCtx = BS_CreateContext(0xFF0078FF, 2, 0);
            }
        }
        // Could poll here if needed
    }
    std::lock_guard<std::mutex> lock(g_ctxMutex);
    if (g_autoCtx)
    {
        BS_DestroyContext(g_autoCtx);
        g_autoCtx = nullptr;
    }
}

void BS_Internal_StartDefaultIfNeeded()
{
    if (g_running.load()) return;
    g_startEvent = CreateEventW(nullptr, FALSE, FALSE, L"Global\\BorderServiceCpp.Start");
    g_stopEvent = CreateEventW(nullptr, FALSE, FALSE, L"Global\\BorderServiceCpp.Stop");
    g_running = true;
    g_worker = std::thread(WorkerProc);
}

void BS_Internal_StopIfNeeded()
{
    if (!g_running.load()) return;
    SetEvent(g_stopEvent);
    g_running = false;
    if (g_worker.joinable()) g_worker.join();
    if (g_startEvent) { CloseHandle(g_startEvent); g_startEvent = nullptr; }
    if (g_stopEvent)  { CloseHandle(g_stopEvent);  g_stopEvent  = nullptr; }
}