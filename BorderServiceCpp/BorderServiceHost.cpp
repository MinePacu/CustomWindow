#include "pch.h"
 
#include "dpi_aware.h"
#include "BorderServiceHost.h"
#include "game_mode.h"
#include "BorderServiceCppExports.h"

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace NonLocalizable
{
    const static wchar_t* TOOL_WINDOW_CLASS_NAME = L"BorderServiceWindow";
    const static wchar_t* WINDOW_IS_LOCKED_PROP = L"BorderService_locked";
}

// Forward declaration for logging
extern void BS_Log(int level, const wchar_t* message);

BorderServiceHost::BorderServiceHost(DWORD mainThreadId) :
    m_hinstance(reinterpret_cast<HINSTANCE>(&__ImageBase)),
    m_mainThreadId(mainThreadId),
    m_running(true)
{
    s_instance = this;
    DPIAware::EnableDPIAwarenessForThisProcess();

    BS_Log(0, L"Initializing BorderServiceHost");

    if (InitMainWindow())
    {
        BS_Log(0, L"Main window initialized successfully");
        SubscribeToEvents();
        StartTrackingTargetWindows();
        BS_Log(0, L"BorderServiceHost initialization complete");
    }
    else
    {
        BS_Log(2, L"Failed to initialize BorderServiceHost main window");
    }
}

BorderServiceHost::~BorderServiceHost()
{
    BS_Log(0, L"BorderServiceHost destructor called");
    m_running = false;
    
    if (m_thread.joinable())
    {
        m_thread.join();
    }

    CleanUp();
    BS_Log(0, L"BorderServiceHost destroyed");
}

bool BorderServiceHost::InitMainWindow()
{
    WNDCLASSEXW wcex{};
    wcex.cbSize = sizeof(WNDCLASSEX);
    wcex.lpfnWndProc = WndProc_Helper;
    wcex.hInstance = m_hinstance;
    wcex.lpszClassName = NonLocalizable::TOOL_WINDOW_CLASS_NAME;
    
    if (!RegisterClassExW(&wcex))
    {
        DWORD error = GetLastError();
        if (error != ERROR_CLASS_ALREADY_EXISTS)
        {
            wchar_t msg[256];
            swprintf_s(msg, L"Failed to register window class, error: %lu", error);
            BS_Log(2, msg);
            return false;
        }
    }

    m_window = CreateWindowExW(WS_EX_TOOLWINDOW, NonLocalizable::TOOL_WINDOW_CLASS_NAME, L"", WS_POPUP, 0, 0, 0, 0, nullptr, nullptr, m_hinstance, this);
    if (!m_window)
    {
        DWORD error = GetLastError();
        wchar_t msg[256];
        swprintf_s(msg, L"Failed to create window, error: %lu", error);
        BS_Log(2, msg);
        return false;
    }

    BS_Log(0, L"BorderService window created successfully");
    return true;
}

LRESULT BorderServiceHost::WndProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept
{
    return DefWindowProc(window, message, wparam, lparam);
}

void BorderServiceHost::ProcessCommand(HWND window)
{
    //if targetwindow is in gamemode, disable border
    bool gameMode = detect_game_mode();
    if (gameMode)
    {
        BS_Log(1, L"Game mode detected, skipping border processing");
        return;
    }

    bool trackedwindow = IsLocked(window);
    if (trackedwindow)
    {
        BS_Log(0, L"Unlocking tracked window");
        if (UnlockTrackWindow(window))
        {
            auto iter = m_trackedWindow.find(window);
            if (iter != m_trackedWindow.end())
            {
                m_trackedWindow.erase(iter);
                BS_Log(0, L"Window removed from tracking");
            }
        }
    }
    else
    {
        BS_Log(0, L"Locking and tracking new window");
        if (LockTrackWindow(window))
        {
            AssignBorder(window);
        }
    }
}

void BorderServiceHost::StartTrackingTargetWindows()
{
    using result_t = std::vector<HWND>;
    result_t result;

    auto enumWindows = [](HWND hwnd, LPARAM param) -> BOOL {
        if (!IsWindowVisible(hwnd))
        {
            return TRUE;
        }

        auto windowName = GetWindowTextLength(hwnd);
        if (windowName > 0)
        {
            result_t& result = *reinterpret_cast<result_t*>(param);
            result.push_back(hwnd);
        }

        return TRUE;
        };

    EnumWindows(enumWindows, reinterpret_cast<LPARAM>(&result));

    wchar_t msg[256];
    swprintf_s(msg, L"Found %zu visible windows for potential tracking", result.size());
    BS_Log(0, msg);

    int trackedCount = 0;
    for (HWND window : result)
    {
        if (IsTracked(window))
        {
            AssignBorder(window);
            trackedCount++;
        }
    }
    
    swprintf_s(msg, L"Started tracking %d windows", trackedCount);
    BS_Log(0, msg);
}

bool BorderServiceHost::AssignBorder(HWND window)
{
    if (m_virtualDesktopUtils.IsWindowOnCurrentDesktop(window))
    {
        auto border = WindowBorder::Create(window, m_hinstance);
        if (border)
        {
            m_trackedWindow[window] = std::move(border);
            BS_Log(0, L"Border assigned to window on current desktop");
        }
        else
        {
            BS_Log(1, L"Failed to create border for window");
        }
    }
    else
    {
        m_trackedWindow[window] = nullptr;
        BS_Log(0, L"Window not on current desktop, border assignment deferred");
    }

    return true;
}

void BorderServiceHost::SubscribeToEvents()
{
    // subscribe to windows events
    DWORD events_to_subscribe[7] = {
        EVENT_OBJECT_LOCATIONCHANGE,
        EVENT_SYSTEM_MINIMIZESTART,
        EVENT_SYSTEM_MINIMIZEEND,
        EVENT_SYSTEM_MOVESIZEEND,
        EVENT_SYSTEM_FOREGROUND,
        EVENT_OBJECT_DESTROY,
        EVENT_OBJECT_FOCUS,
    };

    int successCount = 0;
    for (int i = 0; i < 7; ++i)
    {
        auto event = events_to_subscribe[i];
        auto hook = SetWinEventHook(event, event, nullptr, WinHookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook)
        {
            m_staticWinEventHooks.emplace_back(hook);
            successCount++;
        }
        else
        {
            wchar_t msg[256];
            swprintf_s(msg, L"Failed to set win event hook for event 0x%08X", event);
            BS_Log(1, msg);
        }
    }
    
    wchar_t msg[256];
    swprintf_s(msg, L"Successfully subscribed to %d/%d window events", successCount, 7);
    BS_Log(0, msg);
}

void BorderServiceHost::UnLockAll()
{
    BS_Log(0, L"Unlocking all tracked windows");
    int unlockCount = 0;
    
    for (const auto& [topWindow, border] : m_trackedWindow)
    {
        if (UnlockTrackWindow(topWindow))
        {
            unlockCount++;
        }
        else
        {
            BS_Log(1, L"Failed to unlock a tracked window");
        }
    }

    m_trackedWindow.clear();
    
    wchar_t msg[256];
    swprintf_s(msg, L"Unlocked %d windows", unlockCount);
    BS_Log(0, msg);
}

void BorderServiceHost::CleanUp()
{
    BS_Log(0, L"Starting cleanup");
    
    UnLockAll();
    
    // Unhook all events
    for (auto hook : m_staticWinEventHooks)
    {
        if (hook)
        {
            UnhookWinEvent(hook);
        }
    }
    m_staticWinEventHooks.clear();
    
    if (m_window)
    {
        DestroyWindow(m_window);
        m_window = nullptr;
        BS_Log(0, L"BorderService window destroyed");
    }

    UnregisterClass(NonLocalizable::TOOL_WINDOW_CLASS_NAME, reinterpret_cast<HINSTANCE>(&__ImageBase));
    BS_Log(0, L"Cleanup completed");
}

bool BorderServiceHost::IsLocked(HWND window) const noexcept
{
    auto handle = GetProp(window, NonLocalizable::WINDOW_IS_LOCKED_PROP);
    return (handle != NULL);
}

bool BorderServiceHost::LockTrackWindow(HWND window) const noexcept
{
    if (!SetProp(window, NonLocalizable::WINDOW_IS_LOCKED_PROP, reinterpret_cast<HANDLE>(1)))
    {
        DWORD error = GetLastError();
        wchar_t msg[256];
        swprintf_s(msg, L"SetProp failed, error: %lu", error);
        BS_Log(1, msg);
    }

    auto res = SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    if (!res)
    {
        DWORD error = GetLastError();
        wchar_t msg[256];
        swprintf_s(msg, L"Failed to set window topmost, error: %lu", error);
        BS_Log(1, msg);
    }
    else
    {
        BS_Log(0, L"Window locked as topmost");
    }

    return res;
}

bool BorderServiceHost::UnlockTrackWindow(HWND window) const noexcept
{
    RemoveProp(window, NonLocalizable::WINDOW_IS_LOCKED_PROP);
    auto res = SetWindowPos(window, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    if (!res)
    {
        DWORD error = GetLastError();
        wchar_t msg[256];
        swprintf_s(msg, L"Failed to remove window topmost, error: %lu", error);
        BS_Log(1, msg);
    }
    else
    {
        BS_Log(0, L"Window unlocked from topmost");
    }

    return res;
}

bool BorderServiceHost::IsTracked(HWND window) const noexcept
{
    auto iter = m_trackedWindow.find(window);
    return (iter != m_trackedWindow.end());
}

void BorderServiceHost::HandleWinHookEvent(WinHookEvent* data) noexcept
{
    if (!data->hwnd)
    {
        return;
    }

    // Debug logging for events (only log specific events to avoid spam)
    if (data->event == EVENT_SYSTEM_FOREGROUND || data->event == EVENT_OBJECT_DESTROY)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"Window event 0x%08X received for HWND 0x%p", data->event, data->hwnd);
        BS_Log(0, msg);
    }

    std::vector<HWND> toErase{};
    for (const auto& [window, border] : m_trackedWindow)
    {
        // check if the window was closed, since for some EVENT_OBJECT_DESTROY doesn't work
        // fixes https://github.com/microsoft/PowerToys/issues/15300
        bool visible = IsWindowVisible(window);
        if (!visible)
        {
            UnlockTrackWindow(window);
            toErase.push_back(window);
        }
    }

    if (!toErase.empty())
    {
        wchar_t msg[256];
        swprintf_s(msg, L"Removing %zu invisible windows from tracking", toErase.size());
        BS_Log(0, msg);
    }

    for (const auto window : toErase)
    {
        m_trackedWindow.erase(window);
    }

    switch (data->event)
    {
    case EVENT_OBJECT_LOCATIONCHANGE:
    {
        auto iter = m_trackedWindow.find(data->hwnd);
        if (iter != m_trackedWindow.end())
        {
            const auto& border = iter->second;
            if (border)
            {
                border->UpdateBorderPosition();
            }
        }
    }
    break;
    case EVENT_SYSTEM_MINIMIZESTART:
    {
        auto iter = m_trackedWindow.find(data->hwnd);
        if (iter != m_trackedWindow.end())
        {
            m_trackedWindow[data->hwnd] = nullptr;
            BS_Log(0, L"Window minimized, border temporarily removed");
        }
    }
    break;
    case EVENT_SYSTEM_MINIMIZEEND:
    {
        auto iter = m_trackedWindow.find(data->hwnd);
        if (iter != m_trackedWindow.end())
        {
            LockTrackWindow(data->hwnd);
            AssignBorder(data->hwnd);
            BS_Log(0, L"Window restored, border reassigned");
        }
    }
    break;
    case EVENT_SYSTEM_MOVESIZEEND:
    {
        auto iter = m_trackedWindow.find(data->hwnd);
        if (iter != m_trackedWindow.end())
        {
            const auto& border = iter->second;
            if (border)
            {
                border->UpdateBorderPosition();
            }
        }
    }
    break;
    case EVENT_SYSTEM_FOREGROUND:
    {
        RefreshBorders();
    }
    break;
    case EVENT_OBJECT_FOCUS:
    {
        for (const auto& [window, border] : m_trackedWindow)
        {
            // check if Locker was reset
            if (!IsLocked(window))
            {
                BS_Log(0, L"Window lock was reset, reapplying");
                LockTrackWindow(window);
            }
        }
    }
    break;
    default:
        break;
    }
}

void BorderServiceHost::RefreshBorders()
{
    BS_Log(0, L"Refreshing all borders for virtual desktop changes");
    
    int refreshedCount = 0;
    for (const auto& [window, border] : m_trackedWindow)
    {
        if (m_virtualDesktopUtils.IsWindowOnCurrentDesktop(window))
        {
            if (!border)
            {
                AssignBorder(window);
                refreshedCount++;
            }
        }
        else
        {
            if (border)
            {
                m_trackedWindow[window] = nullptr;
                refreshedCount++;
            }
        }
    }
    
    if (refreshedCount > 0)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"Refreshed borders for %d windows", refreshedCount);
        BS_Log(0, msg);
    }
}