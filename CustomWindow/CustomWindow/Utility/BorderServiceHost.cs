using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace CustomWindow.Utility;

/// <summary>
/// BorderServiceCpp 네이티브 DLL (BS_* exported functions) 에 대한 P/Invoke 래퍼.
/// 이전 C++/CLI managed 클래스 의존을 제거하고 DLL 함수 직접 호출.
/// </summary>
public static class BorderServiceHost
{
    // 네이티브 컨텍스트 핸들
    private static IntPtr _ctx = IntPtr.Zero;
    private static readonly object _sync = new();
    private static Timer? _pushTimer; // periodically push window list
    private static Timer? _livenessTimer;
    private static int _thickness;
    private static int _argbColor;
    private static bool _debug = true;
    private static BsLogDelegate? _logDelegate; // GC 보존

    public static bool IsRunning => _ctx != IntPtr.Zero;

    #region Native interop
    private const string Dll = "BorderServiceCpp"; // BorderServiceCpp.dll

    // C++: BS_NativeRect { int Left; int Top; int Right; int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BsLogDelegate(int level, [MarshalAs(UnmanagedType.LPWStr)] string message);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_CreateContext")]
    private static extern IntPtr BS_CreateContext(int argb, int thickness, int debug);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_DestroyContext")]
    private static extern void BS_DestroyContext(IntPtr ctx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_UpdateColor")]
    private static extern void BS_UpdateColor(IntPtr ctx, int argb);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_UpdateThickness")]
    private static extern void BS_UpdateThickness(IntPtr ctx, int t);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_ForceRedraw")]
    private static extern void BS_ForceRedraw(IntPtr ctx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_SetLogger")]
    private static extern void BS_SetLogger(IntPtr ctx, BsLogDelegate? logger);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_SetPartialRatio")]
    private static extern void BS_SetPartialRatio(IntPtr ctx, float ratio01);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_EnableMerge")]
    private static extern void BS_EnableMerge(IntPtr ctx, int enable);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_UpdateWindows")]
    private static extern void BS_UpdateWindows(IntPtr ctx, IntPtr[] hwnds, int count);

    // Re-added: still exported (stub) for potential future use
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "BS_UpdateRects")]
    private static extern void BS_UpdateRects(IntPtr ctx, IntPtr normal, int normalCount, IntPtr top, int topCount);
    #endregion

    public static void StartIfNeeded(string borderColorHex, int thickness, string[] excludedProcesses)
    {
        lock (_sync)
        {
            _argbColor = ParseColor(borderColorHex);
            _thickness = thickness;

            if (_ctx != IntPtr.Zero)
            {
                BS_UpdateColor(_ctx, _argbColor);
                BS_UpdateThickness(_ctx, _thickness);
                WindowTracker.AddExternalLog($"BorderService 업데이트 (Color=0x{_argbColor:X8}, Thickness={_thickness})");
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
                WindowTracker.AddExternalLog($"BorderService 시작 (Color=0x{_argbColor:X8}, Thickness={_thickness}, Excl={string.Join(',', excludedProcesses.Where(p=>!string.IsNullOrWhiteSpace(p)))})");
                _livenessTimer = new Timer(_ => { /* heartbeat */ }, null, 5000, 5000);
                _pushTimer = new Timer(_ => PushWindows(), null, 0, 1000);
            }
            catch (DllNotFoundException ex)
            {
                WindowTracker.AddExternalLog("DLL 찾기 실패: " + ex.Message);
                SafeDispose();
            }
            catch (BadImageFormatException ex)
            {
                WindowTracker.AddExternalLog("아키텍처 불일치(x86/x64) 또는 손상된 DLL: " + ex.Message);
                SafeDispose();
            }
            catch (EntryPointNotFoundException ex)
            {
                WindowTracker.AddExternalLog("Export 함수 누락 또는 DLL 버전 불일치: " + ex.Message);
                SafeDispose();
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog("BorderService 시작 실패: " + ex.Message);
                SafeDispose();
            }
        }
    }

    public static void StopIfRunning()
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return;
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
            var handles = WindowTracker.CurrentWindowHandles;
            if (handles.Count == 0) return;
            var arr = handles.Select(h => new IntPtr(h)).ToArray();
            BS_UpdateWindows(_ctx, arr, arr.Length);
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog("PushWindows 오류: " + ex.Message);
        }
    }

    /// <summary>사각형 집합 업데이트(필요 시 향후 사용). 현재 호출부 없으므로 공개만.</summary>
    public static void UpdateRects((int L,int T,int R,int B)[] normal, (int L,int T,int R,int B)[] top)
    {
        lock (_sync)
        {
            if (_ctx == IntPtr.Zero) return;
            int nCount = normal?.Length ?? 0;
            int tCount = top?.Length ?? 0;
            if (nCount==0 && tCount==0)
            {
                BS_UpdateRects(_ctx, IntPtr.Zero, 0, IntPtr.Zero, 0);
                return;
            }
            var sizeN = nCount * Marshal.SizeOf<NativeRect>();
            var sizeT = tCount * Marshal.SizeOf<NativeRect>();
            IntPtr bufN = IntPtr.Zero; IntPtr bufT = IntPtr.Zero;
            try
            {
                if (nCount>0)
                {
                    bufN = Marshal.AllocHGlobal(sizeN);
                    for (int i=0;i<nCount;i++)
                    {
                        var nr = new NativeRect { Left=normal[i].L, Top=normal[i].T, Right=normal[i].R, Bottom=normal[i].B };
                        Marshal.StructureToPtr(nr, bufN + i*Marshal.SizeOf<NativeRect>(), false);
                    }
                }
                if (tCount>0)
                {
                    bufT = Marshal.AllocHGlobal(sizeT);
                    for (int i=0;i<tCount;i++)
                    {
                        var tr = new NativeRect { Left=top[i].L, Top=top[i].T, Right=top[i].R, Bottom=top[i].B };
                        Marshal.StructureToPtr(tr, bufT + i*Marshal.SizeOf<NativeRect>(), false);
                    }
                }
                BS_UpdateRects(_ctx, bufN, nCount, bufT, tCount);
            }
            finally
            {
                if (bufN!=IntPtr.Zero) Marshal.FreeHGlobal(bufN);
                if (bufT!=IntPtr.Zero) Marshal.FreeHGlobal(bufT);
            }
        }
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
