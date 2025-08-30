using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace CustomWindow.Utility;

// Minimal Win32 tray icon helper for WinUI 3
internal static class SystemTray
{
    private const int WM_APP_TRAY = 0x8000 + 100;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private static IntPtr _msgWnd = IntPtr.Zero;
    private static WndProcDelegate? _wndProc;
    private static bool _added;
    private static uint _iconId = 1001;
    private static IntPtr _hIcon = IntPtr.Zero;
    private static Window? _winuiWindow;

    public static void Init(Window window, string tooltip = "CustomWindow")
    {
        if (_msgWnd != IntPtr.Zero) return;
        _winuiWindow = window;
        _wndProc = WndProc;
        var cls = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            lpszClassName = "CustomWindowTrayHost"
        };
        RegisterClass(ref cls);
        _msgWnd = CreateWindowEx(0, cls.lpszClassName!, "", 0, 0, 0, 0, 0, (IntPtr)(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_msgWnd == IntPtr.Zero) return;

        _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)0x7F00 /* IDI_APPLICATION */);

        var nid = new NOTIFYICONDATA();
        nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
        nid.hWnd = _msgWnd;
        nid.uID = _iconId;
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.uCallbackMessage = WM_APP_TRAY;
        nid.hIcon = _hIcon;
        nid.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _added = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    public static void Dispose()
    {
        try
        {
            if (_msgWnd != IntPtr.Zero && _added)
            {
                var nid = new NOTIFYICONDATA();
                nid.cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>();
                nid.hWnd = _msgWnd;
                nid.uID = _iconId;
                Shell_NotifyIcon(NIM_DELETE, ref nid);
            }
        }
        catch { }
        finally
        {
            _added = false;
            _msgWnd = IntPtr.Zero;
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            int lp = lParam.ToInt32();
            if (lp == WM_LBUTTONDBLCLK)
            {
                try
                {
                    ShowMainWindow();
                }
                catch { }
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void ShowMainWindow()
    {
        if (_winuiWindow == null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_winuiWindow);
        ShowWindow(hwnd, 9 /* SW_RESTORE */);
        SetForegroundWindow(hwnd);
        _winuiWindow.Activate();
    }

    #region P/Invoke
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct WNDCLASS
    {
        public uint style;
        public WndProcDelegate? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        // struct truncated: we only need basic fields for add/delete
    }

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern ushort RegisterClass([In] ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);
    #endregion
}
