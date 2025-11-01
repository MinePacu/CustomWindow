#include "pch.h"
#include "Globals.h"
#include "DwmUtil.h"
#include "OverlayDComp.h"
#include "Logging.h"
#include "Tray.h"
#include "Args.h"
#include "ConsoleUtil.h"

#ifndef ARRAYSIZE
#define ARRAYSIZE(a) (sizeof(a)/sizeof((a)[0]))
#endif

static bool ParseColorString(const std::wstring& hex, D2D1_COLOR_F& out)
{
    if (hex.empty()) return false;
    std::wstring h = hex;
    if (h[0] == L'#') h.erase(h.begin());
    if (h.size() != 6 && h.size() != 8) return false;
    
    unsigned int val = 0;
    try { 
        val = std::stoul(h, nullptr, 16); 
    } catch (...) { 
        return false; 
    }

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
        r = R / 255.0f; g = G / 255.0f; b = B / 255.0f; a = 1.0f;
    }
    out = D2D1::ColorF(r, g, b, a);
    return true;
}

static void HandleSettingsMessage(const std::wstring& msg)
{
    // Expect: SET foregroundonly=0/1 color=#.. thickness=N corner=token or REFRESH ...
    auto lower = msg; 
    std::transform(lower.begin(), lower.end(), lower.begin(), ::towlower);
    
    bool wasForgroundOnly = g_foregroundWindowOnly;
    std::wstring previousCorner = g_cornerToken; // 이전 corner 값 저장
    
    // Parse foregroundonly
    size_t fgPos = lower.find(L"foregroundonly=");
    if (fgPos != std::wstring::npos) {
        size_t start = fgPos + 15;
        size_t end = lower.find_first_of(L" \r\n\t", start);
        std::wstring fgStr = lower.substr(start, end == std::wstring::npos ? 1 : end - start);
        g_foregroundWindowOnly = (fgStr == L"1" || fgStr == L"true");
        DebugLog(L"[Overlay] ForegroundWindowOnly updated: " + std::to_wstring(g_foregroundWindowOnly));
    }
    
    // Parse color
    size_t colorPos = lower.find(L"color=");
    if (colorPos != std::wstring::npos) {
        size_t start = colorPos + 6;
        size_t end = lower.find_first_of(L" \r\n\t", start);
        std::wstring colorStr = msg.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start);
        
        D2D1_COLOR_F newColor;
        if (ParseColorString(colorStr, newColor)) {
            g_borderColor = newColor;
            DebugLog(L"[Overlay] Color updated: " + colorStr);
        }
    }
    
    // Parse thickness
    size_t thickPos = lower.find(L"thickness=");
    if (thickPos != std::wstring::npos) {
        size_t start = thickPos + 10;
        size_t end = lower.find_first_of(L" \r\n\t", start);
        std::wstring thickStr = lower.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start);
        
        try {
            float newThickness = std::stof(thickStr);
            if (newThickness > 0 && newThickness < 1000) {
                g_thickness = newThickness;
                DebugLog(L"[Overlay] Thickness updated: " + std::to_wstring(newThickness));
            }
        } catch (...) {
            // Invalid thickness value
        }
    }
    
    // Parse corner
    size_t cpos = lower.find(L"corner=");
    if (cpos != std::wstring::npos) {
        size_t start = cpos + 7; 
        size_t end = lower.find_first_of(L" \r\n\t", start);
        g_cornerToken = lower.substr(start, end == std::wstring::npos ? std::wstring::npos : end - start);
        DebugLog(L"[Overlay] Corner updated: " + g_cornerToken);
    }

    // corner 설정이 변경되었을 때 모든 창에 실시간 적용
    if (previousCorner != g_cornerToken) {
        DebugLog(L"[Overlay] Corner preference changed from '" + previousCorner + L"' to '" + g_cornerToken + L"'");
        
        if (g_mode == RenderMode::Dwm) {
            // DWM 모드: 모든 창에 모서리 설정 재적용
            auto hwnds = CollectUserVisibleWindows();
            for (HWND h : hwnds) {
                ApplyCornerPreference(h, g_cornerToken);
            }
            DebugLog(L"[Overlay] Applied corner preference to " + std::to_wstring(hwnds.size()) + L" windows");
        } else if (g_mode == RenderMode::DComp) {
            // DComp 모드: 오버레이 다시 그리기 (DrawBorders 내부에서 radius 사용)
            if (g_overlay) {
                PostMessageW(g_overlay, WM_APP_REFRESH, 0, 0);
                DebugLog(L"[Overlay] Triggered DComp refresh for corner change");
            }
            
            // Windows 11에서는 DComp 모드에서도 실제 창 모서리 적용
            if (IsWindows11OrGreater()) {
                auto hwnds = CollectUserVisibleWindows();
                for (HWND h : hwnds) {
                    ApplyCornerPreference(h, g_cornerToken);
                }
                DebugLog(L"[Overlay] Applied corner preference to " + std::to_wstring(hwnds.size()) + L" windows (DComp+Win11)");
            }
        }
    }

    // 포그라운드 옵션이 변경된 경우 전체 처리
    if (wasForgroundOnly != g_foregroundWindowOnly) {
        DebugLog(L"[Overlay] Foreground mode changed from " + std::to_wstring(wasForgroundOnly) + 
                 L" to " + std::to_wstring(g_foregroundWindowOnly));
        
        if (g_mode == RenderMode::Dwm) {
            // DWM 모드에서는 전체 상태를 재설정
            ResetAndApplyDwmAttributes();
            
            // 모서리 설정도 다시 적용
            auto hwnds = CollectUserVisibleWindows();
            for (HWND h : hwnds) {
                ApplyCornerPreference(h, g_cornerToken);
            }
            DebugLog(L"[Overlay] Reset and reapplied all DWM attributes due to foreground mode change");
        } else if (g_mode == RenderMode::DComp) {
            // DComp 모드에서는 단순 새로고침 트리거
            if (g_overlay) {
                PostMessageW(g_overlay, WM_APP_REFRESH, 0, 0);
                DebugLog(L"[Overlay] Triggered DComp refresh due to foreground mode change");
            }
        }
    }
}

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
                    
                    // 포그라운드 전용 모드에서는 받은 창 목록을 다시 필터링
                    if (g_foregroundWindowOnly) {
                        HWND foregroundWnd = GetForegroundWindow();
                        std::vector<HWND> filteredTargets;
                        for (HWND h : targets) {
                            if (h == foregroundWnd || GetAncestor(h, GA_ROOT) == foregroundWnd) {
                                filteredTargets.push_back(h);
                            }
                        }
                        targets = filteredTargets;
                        DebugLog(L"[Overlay] Applied foreground filtering to HWNDS message: " + 
                                std::to_wstring(filteredTargets.size()) + L" windows remaining");
                    }
                    
                    ApplyDwmAttributesToTargets(targets);
                    if (g_mode == RenderMode::Dwm) {
                        for (HWND h : targets) ApplyCornerPreference(h, g_cornerToken);
                    }
                } else {
                    HandleSettingsMessage(msgStr);
                    if (g_mode == RenderMode::DComp)
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
            AppendMenuW(menu, MF_STRING, 1, L"Show");
            AppendMenuW(menu, MF_STRING, 2, L"Exit");
            if (g_console && GetConsoleWindow()) {
                AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
                AppendMenuW(menu, MF_STRING, 3, L"Hide Console");
                AppendMenuW(menu, MF_STRING, 4, L"Show Console");
            }
            SetForegroundWindow(hwnd);
            UINT cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, nullptr);
            DestroyMenu(menu);
            if (cmd == 1) {
                if (g_overlay) {
                    ShowWindow(g_overlay, SW_SHOW);
                    SetForegroundWindow(g_overlay);
                }
            } else if (cmd == 2) {
                PostQuitMessage(0);
            } else if (cmd == 3) {
                ShowConsole(false);
            } else if (cmd == 4) {
                ShowConsole(true);
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
    
    // 포그라운드 창 전용 모드에서는 포그라운드 창 변경 시 즉시 업데이트
    if (g_foregroundWindowOnly && eventId == EVENT_SYSTEM_FOREGROUND) {
        if (g_overlay && g_mode == RenderMode::DComp) {
            LRESULT res{};
            if (!SendMessageTimeoutW(g_overlay, WM_APP_REFRESH, 0, 0, SMTO_NORMAL, 50, reinterpret_cast<PDWORD_PTR>(&res))) {
                PostMessageW(g_overlay, WM_APP_REFRESH, 0, 0);
            }
        }
        return;
    }
    
    if (g_overlay && g_mode == RenderMode::DComp) {
        LRESULT res{};
        if (!SendMessageTimeoutW(g_overlay, WM_APP_REFRESH, 0, 0, SMTO_NORMAL, 50, reinterpret_cast<PDWORD_PTR>(&res))) {
            PostMessageW(g_overlay, WM_APP_REFRESH, 0, 0);
        }
    }
}
