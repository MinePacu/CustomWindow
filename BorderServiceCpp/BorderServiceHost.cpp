#include "pch.h"
 
#include "dpi_aware.h"
#include "BorderServiceHost.h"
#include "game_mode.h"

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace NonLocalizable
{
    const static wchar_t* TOOL_WINDOW_CLASS_NAME = L"BorderServiceWindow";
    const static wchar_t* WINDOW_IS_LOCKED_PROP = L"BorderService_locked";
}

BorderServiceHost::BorderServiceHost(DWORD mainThreadId) :
    m_hinstance(reinterpret_cast<HINSTANCE>(&__ImageBase)),
    m_mainThreadId(mainThreadId)
{
    s_instance = this;
    DPIAware::EnableDPIAwarenessForThisProcess();

    if (InitMainWindow())
    {
        //InitializeWinhookEventIds();

        SubscribeToEvents();
        StartTrackingTargetWindows();
    }
    else
    {
        //Logger::error("Failed to init AlwaysOnTop module");
        // TODO: show localized message
    }
}

BorderServiceHost::~BorderServiceHost()
{
    m_running = false;
    m_thread.join();

    CleanUp();
}

bool BorderServiceHost::InitMainWindow()
{
    WNDCLASSEXW wcex{};
    wcex.cbSize = sizeof(WNDCLASSEX);
    wcex.lpfnWndProc = WndProc_Helper;
    wcex.hInstance = m_hinstance;
    wcex.lpszClassName = NonLocalizable::TOOL_WINDOW_CLASS_NAME;
    RegisterClassExW(&wcex);

    m_window = CreateWindowExW(WS_EX_TOOLWINDOW, NonLocalizable::TOOL_WINDOW_CLASS_NAME, L"", WS_POPUP, 0, 0, 0, 0, nullptr, nullptr, m_hinstance, this);
    if (!m_window)
    {
        // error log
        return false;
    }

    return true;
}

LRESULT BorderServiceHost::WndProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept
{
    return 0;
}

void BorderServiceHost::ProcessCommand(HWND window)
{
    //if targetwindow is in gamemode, disable border
    bool gameMode = detect_game_mode();
    if (gameMode)
    {
        return;
    }

    bool trackedwindow = IsLocked(window);
    if (trackedwindow)
    {
        if (UnlockTrackWindow(window))
        {
            auto iter = m_trackedWindow.find(window);
            if (iter != m_trackedWindow.end())
            {
                m_trackedWindow.erase(iter);
            }

        }
    }
    else
    {
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

    for (HWND window : result)
    {
        if (IsPinned(window))
        {
            AssignBorder(window);
        }
    }
}

bool BorderServiceHost::AssignBorder(HWND window)
{
    if (m_virtualDesktopUtils.IsWindowOnCurrentDesktop(window))
    {
        auto border = WindowBorder::Create(window, m_hinstance);
        if (border)
        {
            m_trackedWindow[window] = std::move(border);
        }
    }
    else
    {
        m_trackedWindow[window] = nullptr;
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

    for (int i = 0; i < 7; ++i)
    {
        auto event = events_to_subscribe[i];
        auto hook = SetWinEventHook(event, event, nullptr, WinHookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook)
        {
            m_staticWinEventHooks.emplace_back(hook);
        }
        else
        {
            //Logger::error(L"Failed to set win event hook");
        }
    }
}

void BorderServiceHost::UnLockAll()
{
    for (const auto& [topWindow, border] : m_trackedWindow)
    {
        if (!UnlockTrackWindow(topWindow))
        {
            //Logger::error(L"Unpinning topmost window failed");
        }
    }

    m_trackedWindow.clear();
}

void BorderServiceHost::CleanUp()
{
    UnLockAll();
    if (m_window)
    {
        DestroyWindow(m_window);
        m_window = nullptr;
    }

    UnregisterClass(NonLocalizable::TOOL_WINDOW_CLASS_NAME, reinterpret_cast<HINSTANCE>(&__ImageBase));
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
        //Logger::error(L"SetProp failed, {}", get_last_error_or_default(GetLastError()));
    }

    auto res = SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    if (!res)
    {
        //Logger::error(L"Failed to pin window, {}", get_last_error_or_default(GetLastError()));
    }

    return res;
}

bool BorderServiceHost::UnlockTrackWindow(HWND window) const noexcept
{
    RemoveProp(window, NonLocalizable::WINDOW_IS_LOCKED_PROP);
    auto res = SetWindowPos(window, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    if (!res)
    {
        //Logger::error(L"Failed to unpin window, {}", get_last_error_or_default(GetLastError()));
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
            // check if topmost was reset
            // fixes https://github.com/microsoft/PowerToys/issues/19168
            if (!LockTrackWindow(window))
            {
                //Logger::trace(L"A window no longer has Lock set and it should. Setting Lock again.");
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
    for (const auto& [window, border] : m_trackedWindow)
    {
        if (m_virtualDesktopUtils.IsWindowOnCurrentDesktop(window))
        {
            if (!border)
            {
                AssignBorder(window);
            }
        }
        else
        {
            if (border)
            {
                m_trackedWindow[window] = nullptr;
            }
        }
    }
}