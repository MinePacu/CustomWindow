using System;
using System.Runtime.InteropServices;
using System.Drawing; // Color 구조체 (System.Drawing.Primitives)

namespace CustomWindow.Utility;

/// <summary>
/// 특정 HWND 주변에 색상 테두리(overlay) 창을 생성하여 해당 창 이동/크기변경/표시 상태 변화에 따라 자동으로 동기화.
/// 기존 방법에서 owner 를 쓸 경우 HWND 의 최상위 창인 + TopMost 동기화가 복잡하므로
/// 대신 창 자체의 Z-Order 위치를 계산 변경으로 정확한 겹침을 방지. (상위에 테두리를 윗면에/아래로)
/// 특징: owner 없이 동작(완전 popup), 필요한 때만 Z-Order 조정, 빠른 바로 업데이트 위치.
/// </summary>
public sealed class WindowHighlighter : IDisposable
{
    private readonly IntPtr _targetHwnd;
    private IntPtr _overlayHwnd;
    private IntPtr _winEventHook;
    private Color _color;
    private int _thickness;
    private bool _disposed;

    private WinEventDelegate? _winEventCallbackRef; // GC 보호

    private static readonly string OverlayClassName = "WindowHighlighterOverlayClass";
    private static ushort _classAtom;
    private static WndProcDelegate? _wndProcStatic; // 정적 참조 (GC 방지)

    // Region 캐시
    private IntPtr _currentRegion = IntPtr.Zero;
    private int _lastWidth;
    private int _lastHeight;

    // Z-Order 상태 캐시
    private bool _lastTargetTopMost;

    static WindowHighlighter()
    {
        RegisterOverlayWindowClass();
    }

    public WindowHighlighter(IntPtr targetHwnd, Color color, int thickness = 2)
    {
        if (targetHwnd == IntPtr.Zero) throw new ArgumentException("Target HWND is zero", nameof(targetHwnd));
        _targetHwnd = targetHwnd;
        _color = color;
        _thickness = Math.Max(1, thickness);
        CreateOverlay();
        Hook();
        UpdateBounds();
        Show(true);
    }

    public void UpdateColor(Color c)
    {
        _color = c;
        if (_overlayHwnd != IntPtr.Zero)
            Native.InvalidateRect(_overlayHwnd, IntPtr.Zero, false);
    }

    // 외부(주기)에서 요청 시 위치/크기/Region + 필요 Z-Order 정확화
    public void Refresh() => UpdateBounds();

    public void ReorderOnly()
    {
        if (_overlayHwnd == IntPtr.Zero) return;
        EnsureZOrder();
    }

    public void UpdateThickness(int thickness)
    {
        int newT = Math.Max(1, thickness);
        if (newT == _thickness) return;
        _thickness = newT;
        RebuildRegion(_lastWidth, _lastHeight, force:true);
        if (_overlayHwnd != IntPtr.Zero)
            Native.InvalidateRect(_overlayHwnd, IntPtr.Zero, false);
    }

    private static void RegisterOverlayWindowClass()
    {
        if (_classAtom != 0) return; // 이미 등록
        _wndProcStatic = StaticWndProc;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _wndProcStatic,
            hInstance = Native.GetModuleHandle(null),
            lpszClassName = OverlayClassName
        };
        _classAtom = Native.RegisterClassEx(ref wc);
        if (_classAtom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 1410) // ERROR_CLASS_ALREADY_EXISTS
                throw new InvalidOperationException($"Failed to register overlay window class. Win32Error={err}");
        }
    }

    private void CreateOverlay()
    {
        // owner(부모) 를 target 로 지정하지 않는다. (지정하면 owner chain 배열 및 깜빡임 현상)
        _overlayHwnd = Native.CreateWindowEx(
            WindowStylesEx.WS_EX_LAYERED |
            WindowStylesEx.WS_EX_TRANSPARENT |
            WindowStylesEx.WS_EX_TOOLWINDOW |
            WindowStylesEx.WS_EX_NOACTIVATE,
            OverlayClassName,
            string.Empty,
            WindowStyles.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, // <-- parent 없음
            IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);

        if (_overlayHwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to create overlay window. Win32Error={err}");
        }

        AttachInstanceToHwnd();
        Native.SetLayeredWindowAttributes(_overlayHwnd, 0, 255, 0x02); // LWA_ALPHA
    }

    private void Hook()
    {
        _winEventCallbackRef = WinEventCallback;
        _winEventHook = Native.SetWinEventHook(
            EventConstants.EVENT_MIN,
            EventConstants.EVENT_MAX,
            IntPtr.Zero,
            _winEventCallbackRef,
            0, 0,
            WinEventFlags.WINEVENT_OUTOFCONTEXT | WinEventFlags.WINEVENT_SKIPOWNPROCESS);
    }

    private void WinEventCallback(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd != _targetHwnd) return;
        if (idObject != 0) return; // window 객체
        switch (eventType)
        {
            case EventConstants.EVENT_OBJECT_LOCATIONCHANGE:
            case EventConstants.EVENT_SYSTEM_FOREGROUND:
            case EventConstants.EVENT_OBJECT_SHOW:
            case EventConstants.EVENT_OBJECT_HIDE:
                UpdateBounds();
                break;
        }
    }

    private void UpdateBounds()
    {
        if (_overlayHwnd == IntPtr.Zero) return;
        if (!Native.IsWindow(_targetHwnd) || !Native.IsWindowVisible(_targetHwnd))
        {
            Show(false);
            return;
        }
        if (!Native.GetWindowRect(_targetHwnd, out RECT rc))
        {
            Show(false);
            return;
        }
        int width = rc.Right - rc.Left;
        int height = rc.Bottom - rc.Top;
        if (width <= 0 || height <= 0)
        {
            Show(false);
            return;
        }
        EnsureZOrder();
        // 위치/크기만 조정 (z-order 유지되지 않음)
        Native.SetWindowPos(_overlayHwnd, IntPtr.Zero,
            rc.Left, rc.Top, width, height,
            SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_SHOWWINDOW | SetWindowPosFlags.SWP_NOZORDER);
        Show(true);
        RebuildRegion(width, height);
        Native.InvalidateRect(_overlayHwnd, IntPtr.Zero, false);
    }

    private void EnsureZOrder()
    {
        if (_overlayHwnd == IntPtr.Zero) return;
        bool targetTopMost = IsTargetTopMost();
        bool overlayTopMost = ((long)Native.GetWindowLongPtr(_overlayHwnd, -20 /*GWL_EXSTYLE*/ ) & 0x8) != 0; // WS_EX_TOPMOST

        // 현재 overlay 바로 아래 창이 타겟인지 확인 (overlay 가 항상 바로 위에 있어야 함)
        IntPtr below = Native.GetWindow(_overlayHwnd, 2 /*GW_HWNDNEXT*/);
        bool alreadyCorrect = below == _targetHwnd && targetTopMost == overlayTopMost;
        if (alreadyCorrect) return; // 조정 불필요

        // TopMost 상태를 동기화 (필요한 경우)
        if (targetTopMost != overlayTopMost)
        {
            IntPtr styleAfter = targetTopMost ? (IntPtr)(-1) /*HWND_TOPMOST*/ : (IntPtr)(-2) /*HWND_NOTOPMOST*/;
            Native.SetWindowPos(_overlayHwnd, styleAfter, 0, 0, 0, 0,
                SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE);
            overlayTopMost = targetTopMost; // 갱신
        }

        // 타겟 창 바로 위로 이동: hWndInsertAfter = 타겟 HWND
        // (SetWindowPos 규칙: 이 창을 hWndInsertAfter 위(=앞)에 위치)
        Native.SetWindowPos(_overlayHwnd, _targetHwnd, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE);

        _lastTargetTopMost = targetTopMost;
    }

    private bool IsTargetTopMost()
    {
        int exStyle = (int)Native.GetWindowLongPtr(_targetHwnd, -20 /*GWL_EXSTYLE*/);
        const int WS_EX_TOPMOST = 0x00000008;
        return (exStyle & WS_EX_TOPMOST) != 0;
    }

    private void RebuildRegion(int width, int height, bool force = false)
    {
        if (width <= 0 || height <= 0) return;
        if (!force && width == _lastWidth && height == _lastHeight) return;
        _lastWidth = width; _lastHeight = height;

        int t = _thickness;
        if (t * 2 >= width || t * 2 >= height) t = 1;
        IntPtr outer = Native.CreateRectRgn(0, 0, width, height);
        IntPtr inner = Native.CreateRectRgn(t, t, width - t, height - t);
        IntPtr frame = Native.CreateRectRgn(0, 0, 0, 0);
        Native.CombineRgn(frame, outer, inner, 3 /*RGN_DIFF*/);

        if (_currentRegion != IntPtr.Zero)
        {
            Native.SetWindowRgn(_overlayHwnd, IntPtr.Zero, false);
            Native.DeleteObject(_currentRegion);
            _currentRegion = IntPtr.Zero;
        }
        Native.SetWindowRgn(_overlayHwnd, frame, true);
        Native.DeleteObject(outer);
        Native.DeleteObject(inner);
        _currentRegion = frame;
    }

    private void Show(bool show)
    {
        if (_overlayHwnd == IntPtr.Zero) return;
        Native.ShowWindow(_overlayHwnd, show ? 8 /*SW_SHOWNA*/ : 0 /*SW_HIDE*/);
    }

    // 인스턴스 WndProc -> 기본 처리 외에 및 커스텀
    private IntPtr InstanceWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case 0x000F: // WM_PAINT
                Paint(hWnd);
                break;
            case 0x0084: // WM_NCHITTEST -> 입력 차단
                return (IntPtr)(-1); // HTTRANSPARENT
        }
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var inst = GetInstanceFromHwnd(hWnd);
        if (inst != null)
            return inst.InstanceWndProc(hWnd, msg, wParam, lParam);
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static WindowHighlighter? GetInstanceFromHwnd(IntPtr hWnd)
    {
        IntPtr data = Native.GetWindowLongPtr(hWnd, -21); // GWLP_USERDATA
        if (data == IntPtr.Zero) return null;
        try
        {
            var handle = GCHandle.FromIntPtr(data);
            return handle.Target as WindowHighlighter;
        }
        catch { return null; }
    }

    private void AttachInstanceToHwnd()
    {
        var gch = GCHandle.Alloc(this, GCHandleType.Weak);
        Native.SetWindowLongPtr(_overlayHwnd, -21, GCHandle.ToIntPtr(gch));
    }

    private void Paint(IntPtr hWnd)
    {
        var hdc = Native.BeginPaint(hWnd, out PAINTSTRUCT ps);
        if (hdc != IntPtr.Zero)
        {
            Native.GetClientRect(hWnd, out RECT crc);
            int w = crc.Right - crc.Left;
            int h = crc.Bottom - crc.Top;
            if (w > 1 && h > 1)
            {
                int colorRef = _color.R | (_color.G << 8) | (_color.B << 16);
                IntPtr pen = Native.CreatePen(0 /*PS_SOLID*/, 1, colorRef);
                IntPtr oldPen = Native.SelectObject(hdc, pen);
                IntPtr nullBrush = Native.GetStockObject(5 /*NULL_BRUSH*/);
                IntPtr oldBrush = Native.SelectObject(hdc, nullBrush);
                Native.Rectangle(hdc, 0, 0, w - 1, h - 1);
                Native.SelectObject(hdc, oldPen);
                Native.SelectObject(hdc, oldBrush);
                Native.DeleteObject(pen);
            }
        }
        Native.EndPaint(hWnd, ref ps);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_winEventHook != IntPtr.Zero) Native.UnhookWinEvent(_winEventHook);
        if (_overlayHwnd != IntPtr.Zero) Native.DestroyWindow(_overlayHwnd);
        _winEventHook = IntPtr.Zero;
        _overlayHwnd = IntPtr.Zero;
        _currentRegion = IntPtr.Zero; // SetWindowRgn 처리후 삭제
    }

    #region Native Interop

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    private static class EventConstants
    {
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_HIDE = 0x8003;
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint EVENT_MIN = EVENT_SYSTEM_FOREGROUND;
        public const uint EVENT_MAX = EVENT_OBJECT_LOCATIONCHANGE;
    }

    [Flags]
    private enum WinEventFlags : uint
    {
        WINEVENT_OUTOFCONTEXT = 0x0000,
        WINEVENT_SKIPOWNTHREAD = 0x0001,
        WINEVENT_SKIPOWNPROCESS = 0x0002
    }

    [Flags]
    private enum WindowStyles : uint
    {
        WS_POPUP = 0x80000000
    }

    [Flags]
    private enum WindowStylesEx : uint
    {
        WS_EX_LAYERED = 0x00080000,
        WS_EX_TRANSPARENT = 0x00000020,
        WS_EX_TOOLWINDOW = 0x00000080,
        WS_EX_NOACTIVATE = 0x08000000,
        WS_EX_TOPMOST = 0x00000008
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        SWP_NOSIZE = 0x0001,
        SWP_NOMOVE = 0x0002,
        SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010,
        SWP_SHOWWINDOW = 0x0040,
        SWP_NOOWNERZORDER = 0x0200
    }

    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(WindowStylesEx exStyle, string lpClassName, string lpWindowName,
            WindowStyles dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, SetWindowPosFlags uFlags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, WinEventFlags dwFlags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        [DllImport("gdi32.dll")] public static extern IntPtr CreatePen(int fnPenStyle, int nWidth, int crColor);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int fnObject);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);
    }

    #endregion
}

/*
사용 예 (WinUI 3 앱):
private WindowHighlighter? _highlighter;
void StartHighlight(IntPtr hwnd)
{
    _highlighter?.Dispose();
    _highlighter = new WindowHighlighter(hwnd, Color.FromArgb(255, 0, 120, 255), thickness: 2);
}
void ChangeHighlightColor(Color c) => _highlighter?.UpdateColor(c);
void StopHighlight() { _highlighter?.Dispose(); _highlighter = null; }
*/
