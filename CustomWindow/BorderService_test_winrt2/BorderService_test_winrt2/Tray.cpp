#include "pch.h"
#include "Globals.h"
#include "DwmUtil.h"
#include "OverlayDComp.h"
#include "Logging.h"
#include "Tray.h"

#ifndef ARRAYSIZE
#define ARRAYSIZE(a) (sizeof(a)/sizeof((a)[0]))
#endif

LRESULT CALLBACK OverlayProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_TIMER:
        if (wParam == 1) {
            if (g_mode == RenderMode::DComp) {
                RefreshOverlay();
            }
        }
        return 0;
    case WM_APP_REFRESH:
        if (g_mode == RenderMode::DComp) {
            RefreshOverlay();
        }
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

                if (msgStr.rfind(L"HWNDS ", 0) == 0) {
                    std::vector<HWND> targets;
                    size_t pos = 6;
                    while (pos < msgStr.size()) {
                        while (pos < msgStr.size() && msgStr[pos] == L' ') ++pos;
                        if (pos >= msgStr.size()) break;
                        size_t end = msgStr.find(L' ', pos);
                        std::wstring tok = msgStr.substr(pos, end == std::wstring::npos ? std::wstring::npos : end - pos);
                        HWND h = nullptr;
                        try {
                            if (tok.rfind(L"0x", 0) == 0 || tok.rfind(L"0X", 0) == 0) tok = tok.substr(2);
                            unsigned long long v = std::stoull(tok, nullptr, 16);
                            h = reinterpret_cast<HWND>(v);
                        } catch (...) { h = nullptr; }
                        if (h && IsWindow(h)) targets.push_back(h);
                        if (end == std::wstring::npos) break;
                        pos = end + 1;
                    }
                    ApplyDwmAttributesToTargets(targets);
                } else {
                    PostMessageW(hwnd, WM_APP_REFRESH, 0, 0);
                }
            }
            return 0;
        }
    case WM_NCHITTEST:
        return static_cast<LRESULT>(HTTRANSPARENT);
    case WM_MOUSEACTIVATE:
        return static_cast<LRESULT>(MA_NOACTIVATE);
    case WM_DISPLAYCHANGE:
    case WM_DPICHANGED:
        UpdateVirtualScreenAndResize();
        if (g_mode == RenderMode::DComp)
            PostMessageW(hwnd, WM_APP_REFRESH, 0, 0);
        return 0;
    case WM_APP_TRAY:
        if (lParam == WM_LBUTTONDBLCLK) {
            if (g_overlay) {
                ShowWindow(g_overlay, SW_SHOW);
                SetForegroundWindow(g_overlay);
            }
        } else if (lParam == WM_RBUTTONUP || lParam == WM_CONTEXTMENU) {
            POINT pt; GetCursorPos(&pt);
            HMENU menu = CreatePopupMenu();
            AppendMenuW(menu, MF_STRING, 1, L"Exit BorderService");
            SetForegroundWindow(hwnd);
            UINT cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, nullptr);
            DestroyMenu(menu);
            if (cmd == 1) {
                PostQuitMessage(0);
            }
        }
        return 0;
    case WM_DESTROY:
        if (g_nid.cbSize) Shell_NotifyIconW(NIM_DELETE, &g_nid);
        if (g_trayIcon) DestroyIcon(g_trayIcon);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

HWND CreateOverlayWindow(bool visible)
{
    UpdateVirtualScreenAndResize();

    WNDCLASSW wc{};
    wc.lpfnWndProc = OverlayProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"BorderOverlayDCompWindowClass";
    RegisterClassW(&wc);

    DWORD exStyle = WS_EX_TOOLWINDOW;
    DWORD style = WS_POPUP;
    if (visible) {
        exStyle |= WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
    }

    HWND h = CreateWindowExW(
        exStyle,
        wc.lpszClassName, L"", style,
        g_virtualScreen.left, g_virtualScreen.top,
        g_virtualScreen.right - g_virtualScreen.left,
        g_virtualScreen.bottom - g_virtualScreen.top,
        nullptr, nullptr, wc.hInstance, nullptr);

    CHANGEFILTERSTRUCT cfs{ sizeof(CHANGEFILTERSTRUCT) };
    ChangeWindowMessageFilterEx(h, WM_COPYDATA, MSGFLT_ALLOW, &cfs);

    if (visible) ShowWindow(h, SW_SHOW);
    if (visible) SetTimer(h, 1, 150, nullptr);

    DebugLog(L"[Overlay] Message window created and message filter applied");
    return h;
}

void InitTrayIcon(HWND hwnd)
{
    g_trayIcon = (HICON)LoadIconW(nullptr, IDI_APPLICATION);
    ZeroMemory(&g_nid, sizeof(g_nid));
    g_nid.cbSize = sizeof(NOTIFYICONDATAW);
    g_nid.hWnd = hwnd;
    g_nid.uID = 1;
    g_nid.uFlags = NIF_MESSAGE | NIF_TIP | NIF_ICON;
    g_nid.uCallbackMessage = WM_APP_TRAY;
    g_nid.hIcon = g_trayIcon;
    wcsncpy_s(g_nid.szTip, L"BorderService Overlay", _TRUNCATE);
    Shell_NotifyIconW(NIM_ADD, &g_nid);
}

void InstallWinEventHooks()
{
    DWORD flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
    g_hook1 = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE, nullptr, WinEventProc, 0, 0, flags);
    g_hook2 = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, nullptr, WinEventProc, 0, 0, flags);
    g_hook3 = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, nullptr, WinEventProc, 0, 0, flags);
    g_hook4 = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, nullptr, WinEventProc, 0, 0, flags);
    g_hook5 = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, nullptr, WinEventProc, 0, 0, flags);
    g_hook6 = SetWinEventHook(EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER, nullptr, WinEventProc, 0, 0, flags);
}

void UninstallWinEventHooks()
{
    if (g_hook1) { UnhookWinEvent(g_hook1); g_hook1 = nullptr; }
    if (g_hook2) { UnhookWinEvent(g_hook2); g_hook2 = nullptr; }
    if (g_hook3) { UnhookWinEvent(g_hook3); g_hook3 = nullptr; }
    if (g_hook4) { UnhookWinEvent(g_hook4); g_hook4 = nullptr; }
    if (g_hook5) { UnhookWinEvent(g_hook5); g_hook5 = nullptr; }
    if (g_hook6) { UnhookWinEvent(g_hook6); g_hook6 = nullptr; }
}

void CALLBACK WinEventProc(HWINEVENTHOOK, DWORD eventId, HWND hwnd, LONG idObject, LONG, DWORD, DWORD)
{
    if (eventId >= EVENT_OBJECT_CREATE && eventId <= EVENT_OBJECT_HIDE) {
        if (idObject != OBJID_WINDOW || hwnd == nullptr) return;
    }
    if (g_overlay && g_mode == RenderMode::DComp) {
        LRESULT res{};
        if (!SendMessageTimeoutW(g_overlay, WM_APP_REFRESH, 0, 0, SMTO_NORMAL, 50, reinterpret_cast<PDWORD_PTR>(&res))) {
            PostMessageW(g_overlay, WM_APP_REFRESH, 0, 0);
        }
    }
}
