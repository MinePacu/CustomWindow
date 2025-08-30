#include "pch.h"
#include <cwctype>
#include <algorithm>
#include <string>
#include <shellapi.h>
#include <cstdio>
#include "Globals.h"
#include "Logging.h"
#include "DwmUtil.h"
#include "OverlayDComp.h"
#include "Tray.h"
#include "Args.h"

int main()
{
    // Parse args and optionally allocate console first
    ParseArgsAndApply();
    EnsureConsole(g_console);

    // DPI awareness for accurate coordinates
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    winrt::init_apartment();

    // Create message window (visible overlay only if DComp)
    g_overlay = CreateOverlayWindow(g_mode == RenderMode::DComp);

    // Tray icon
    InitTrayIcon(g_overlay);

    if (g_mode == RenderMode::DComp) {
        // Create devices
        if (FAILED(CreateD3DDevice())) return -1;
        if (FAILED(CreateD2D())) return -2;
        if (FAILED(CreateDComp(g_overlay))) return -3;

        // Initial draw
        RefreshOverlay();

        // Hooks
        InstallWinEventHooks();

        DebugLog(L"[Overlay] Started overlay loop (DComp)");
    } else {
        DebugLog(L"[Overlay] Started in DWM mode (no overlay)");
    }

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
