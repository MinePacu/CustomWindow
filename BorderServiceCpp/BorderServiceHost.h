#pragma once

#include <map>

#include "VirtualDesktopUtils.h"
#include "WindowBorder.h"

struct WinHookEvent
{
    DWORD event;
    HWND hwnd;
    LONG idObject;
    LONG idChild;
    DWORD idEventThread;
    DWORD dwmsEventTime;
};

class BorderServiceHost {
public:
    BorderServiceHost(DWORD mainThreadId);
    ~BorderServiceHost();

protected:
    static LRESULT CALLBACK WndProc_Helper(HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept
    {
        auto thisRef = reinterpret_cast<BorderServiceHost*>(GetWindowLongPtr(window, GWLP_USERDATA));

        if (!thisRef && (message == WM_CREATE))
        {
            const auto createStruct = reinterpret_cast<LPCREATESTRUCT>(lparam);
            thisRef = static_cast<BorderServiceHost*>(createStruct->lpCreateParams);
            SetWindowLongPtr(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(thisRef));
        }

        return thisRef ? thisRef->WndProc(window, message, wparam, lparam) :
            DefWindowProc(window, message, wparam, lparam);
    }

private:
    static inline BorderServiceHost* s_instance = nullptr;
    std::vector<HWINEVENTHOOK> m_staticWinEventHooks{};
    VirtualDesktopUtils m_virtualDesktopUtils;

    HWND m_window{ nullptr };
    HINSTANCE m_hinstance;
    std::map<HWND, std::unique_ptr<WindowBorder>> m_trackedWindow{};
    HANDLE m_hPinEvent;
    HANDLE m_hTerminateEvent;
    DWORD m_mainThreadId;
    std::thread m_thread;
    bool m_running = true;

    LRESULT WndProc(HWND, UINT, WPARAM, LPARAM) noexcept;
    void HandleWinHookEvent(WinHookEvent* data) noexcept;

    bool InitMainWindow();
    void SubscribeToEvents();

    void ProcessCommand(HWND window);
    void StartTrackingTargetWindows();
    void UnLockAll();
    void CleanUp();

    bool IsLocked(HWND window) const noexcept;

    bool LockTrackWindow(HWND window) const noexcept;
    bool UnlockTrackWindow(HWND window) const noexcept;
    bool IsTracked(HWND window) const noexcept;
    bool AssignBorder(HWND window);
    void RefreshBorders();

    static void CALLBACK WinHookProc(HWINEVENTHOOK winEventHook,
        DWORD event,
        HWND window,
        LONG object,
        LONG child,
        DWORD eventThread,
        DWORD eventTime)
    {
        WinHookEvent data{ event, window, object, child, eventThread, eventTime };
        if (s_instance)
        {
            s_instance->HandleWinHookEvent(&data);
        }
    }
};
