using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

namespace CustomWindow.Utility;

public static class BorderServiceHost
{
    private static IntPtr _ctx = IntPtr.Zero;
    private static readonly object _sync = new();
    private static Timer? _pushTimer;
    private static Timer? _livenessTimer;
    private static int _thickness;
    private static int _argbColor;
    private static bool _debug = true;
    private static BsLogDelegate? _logDelegate;
    private static HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsRunning => _ctx != IntPtr.Zero;

    #region Native interop
    private const string Dll = "BorderServiceCpp";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BsLogDelegate(int level, [MarshalAs(UnmanagedType.LPWStr)] string message);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_CreateContext")] private static extern IntPtr BS_CreateContext(int argb, int thickness, int debug);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_DestroyContext")] private static extern void BS_DestroyContext(IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_UpdateColor")] private static extern void BS_UpdateColor(IntPtr ctx, int argb);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_UpdateThickness")] private static extern void BS_UpdateThickness(IntPtr ctx, int t);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_ForceRedraw")] private static extern void BS_ForceRedraw(IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_SetLogger")] private static extern void BS_SetLogger(IntPtr ctx, BsLogDelegate? logger);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_SetPartialRatio")] private static extern void BS_SetPartialRatio(IntPtr ctx, float ratio01);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_EnableMerge")] private static extern void BS_EnableMerge(IntPtr ctx, int enable);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_UpdateWindows")] private static extern void BS_UpdateWindows(IntPtr ctx, IntPtr[] hwnds, int count);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_ClearAll")] private static extern void BS_ClearAll(IntPtr ctx);
    #endregion

    public static void StartIfNeeded(string borderColorHex, int thickness, string[] excludedProcesses)
    {
        lock (_sync)
        {
            _argbColor = ParseColor(borderColorHex);
            _thickness = thickness;
            _excluded = new HashSet<string>(excludedProcesses.Where(p=>!string.IsNullOrWhiteSpace(p)).Select(NormalizeExeBase), StringComparer.OrdinalIgnoreCase);

            if (_ctx != IntPtr.Zero)
            {
                BS_UpdateColor(_ctx, _argbColor);
                BS_UpdateThickness(_ctx, _thickness);
                WindowTracker.AddExternalLog($"BorderService 업데이트 (Color=0x{_argbColor:X8}, Thickness={_thickness}, Excl={string.Join(',', _excluded)})");
                return;
            }
            try
            {
                _ctx = BS_CreateContext(_argbColor, _thickness, _debug ? 1 : 0);
                if (_ctx == IntPtr.Zero) throw new InvalidOperationException("BS_CreateContext 실패");

                _logDelegate = (level, msg) =>
                {
                    var tag = level switch { 0 => "INFO", 1 => "WARN", 2 => "ERR", _ => level.ToString() };
                    WindowTracker.AddExternalLog($"[BS {tag}] {msg}");
                };
                BS_SetLogger(_ctx, _logDelegate);
                BS_EnableMerge(_ctx, 1);
                BS_SetPartialRatio(_ctx, 0.25f);
                WindowTracker.AddExternalLog($"BorderService 시작 (Color=0x{_argbColor:X8}, Thickness={_thickness}, Excl={string.Join(',', _excluded)})");
                _livenessTimer = new Timer(_ => { }, null, 5000, 5000);
                _pushTimer = new Timer(_ => PushWindows(), null, 0, 1000);
            }
            catch (DllNotFoundException ex) { WindowTracker.AddExternalLog("DLL 찾기 실패: " + ex.Message); SafeDispose(); }
            catch (BadImageFormatException ex) { WindowTracker.AddExternalLog("아키텍처 불일치(x86/x64) 또는 손상된 DLL: " + ex.Message); SafeDispose(); }
            catch (EntryPointNotFoundException ex) { WindowTracker.AddExternalLog("Export 함수 누락 또는 DLL 버전 불일치: " + ex.Message); SafeDispose(); }
            catch (Exception ex) { WindowTracker.AddExternalLog("BorderService 시작 실패: " + ex.Message); SafeDispose(); }
        }
    }

    public static void StopIfRunning()
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return;
            try
            {
                BS_ClearAll(_ctx); // explicit immediate clear
            }
            catch { }
            WindowTracker.AddExternalLog("BorderService 중지 시도");
            SafeDispose();
        }
    }

    public static void UpdateColor(string borderColorHex)
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return;
            var c = ParseColor(borderColorHex);
            if (c == _argbColor) return;
            _argbColor = c;
            BS_UpdateColor(_ctx, _argbColor);
            WindowTracker.AddExternalLog($"BorderService 색상 변경 0x{_argbColor:X8}");
        }
    }

    public static void UpdateThickness(int thickness)
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return;
            if (thickness == _thickness) return;
            _thickness = thickness;
            BS_UpdateThickness(_ctx, _thickness);
            WindowTracker.AddExternalLog($"BorderService 두께 변경 {_thickness}");
        }
    }

    public static void ForceRedraw()
    {
        lock (_sync)
        {
            if (_ctx != IntPtr.Zero) BS_ForceRedraw(_ctx);
        }
    }

    private static void PushWindows()
    {
        try
        {
            if (_ctx == IntPtr.Zero) return;
            var detailed = WindowTracker.GetCurrentWindowsDetailed();
            var filtered = detailed.Where(w => !IsExcluded(w.ProcessName)).Select(w => new IntPtr(w.Handle)).ToArray();
            BS_UpdateWindows(_ctx, filtered, filtered.Length); // if empty, native clears overlays
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog("PushWindows 오류: " + ex.Message);
        }
    }

    private static bool IsExcluded(string? procName)
    {
        if (string.IsNullOrWhiteSpace(procName)) return false;
        return _excluded.Contains(NormalizeExeBase(procName));
    }

    private static string NormalizeExeBase(string name)
    {
        var n = name.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n;
    }

    private static void SafeDispose()
    {
        try { if (_ctx != IntPtr.Zero) BS_DestroyContext(_ctx); } catch { }
        _ctx = IntPtr.Zero;
        _logDelegate = null;
        _livenessTimer?.Dispose(); _livenessTimer = null;
        _pushTimer?.Dispose(); _pushTimer = null;
        WindowTracker.AddExternalLog("BorderService 해제 완료");
    }

    private static int ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return unchecked((int)0xFF000000);
        var h = hex.Trim();
        if (h.StartsWith("#")) h = h[1..];
        if (h.Length == 6)
        {
            if (int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return unchecked((int)(0xFF000000 | (uint)rgb));
        }
        else if (h.Length == 8)
        {
            if (int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                return argb;
        }
        return unchecked((int)0xFF000000);
    }
}
