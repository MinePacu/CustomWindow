#include "pch.h"
#include "Logging.h"

static BOOL WINAPI ConsoleCtrlHandler(DWORD ctrlType)
{
    switch (ctrlType)
    {
    case CTRL_C_EVENT:
    case CTRL_BREAK_EVENT:
    case CTRL_CLOSE_EVENT:
    case CTRL_LOGOFF_EVENT:
    case CTRL_SHUTDOWN_EVENT:
        // Instead of exiting, just hide the console window.
        {
            HWND h = GetConsoleWindow();
            if (h) ShowWindow(h, SW_HIDE);
        }
        return TRUE; // handled
    }
    return FALSE;
}

void ConfigureConsoleWindow()
{
    // Add handler so closing console (X) hides instead of terminating process.
    SetConsoleCtrlHandler(ConsoleCtrlHandler, TRUE);

    // Ensure system menu Close hides the console as well by subclassing.
    HWND h = GetConsoleWindow();
    if (!h) return;
    LONG_PTR style = GetWindowLongPtr(h, GWL_STYLE);
    style |= WS_MINIMIZEBOX; // allow minimize button
    SetWindowLongPtr(h, GWL_STYLE, style);
}

void ShowConsole(bool show)
{
    HWND h = GetConsoleWindow();
    if (!h) return;
    ShowWindow(h, show ? SW_SHOW : SW_HIDE);
}
