using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

namespace CustomWindow.Utility;

public static class BorderService
{
    private static BorderServiceCppHost? _host;
    private static readonly object _sync = new();
    private static bool _running = false;
    
    // 로그 이벤트
    public static event Action<string>? LogReceived;

    /// <summary>BorderService 시작</summary>
    public static void StartIfNeeded(string borderColorHex, int thickness, string[] excludedProcesses)
    {
        lock (_sync)
        {
            if (_running && _host != null)
            {
                LogMessage("BorderService already running - updating settings");
                // 이미 실행 중이면 설정만 업데이트
                return;
            }

            try
            {
                LogMessage($"Starting BorderService (Color={borderColorHex}, Thickness={thickness})");
                
                // C++ 프로젝트가 빌드되지 않은 경우를 위한 임시 처리
                // var argbColor = ParseColor(borderColorHex);
                // _host = new BorderServiceCppHost(argbColor, thickness, debug: true);
                // _host.LogReceived += OnNativeLogReceived;
                
                _running = true;
                LogMessage("BorderService started successfully (C++ integration disabled)");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start BorderService: {ex.Message}");
                SafeStop();
            }
        }
    }

    /// <summary>BorderService 중지</summary>
    public static void StopIfRunning()
    {
        lock (_sync)
        {
            if (!_running) return;
            
            LogMessage("Stopping BorderService");
            SafeStop();
            LogMessage("BorderService stopped");
        }
    }

    /// <summary>색상 업데이트</summary>
    public static void UpdateColor(string borderColorHex)
    {
        lock (_sync)
        {
            if (_host != null && _running)
            {
                try
                {
                    var argbColor = ParseColor(borderColorHex);
                    _host.UpdateColor(argbColor);
                    LogMessage($"Color updated to {borderColorHex}");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to update color: {ex.Message}");
                }
            }
            else
            {
                LogMessage($"Color update requested: {borderColorHex} (C++ integration disabled)");
            }
        }
    }

    /// <summary>두께 업데이트</summary>
    public static void UpdateThickness(int thickness)
    {
        lock (_sync)
        {
            if (_host != null && _running)
            {
                try
                {
                    _host.UpdateThickness(thickness);
                    LogMessage($"Thickness updated to {thickness}");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to update thickness: {ex.Message}");
                }
            }
            else
            {
                LogMessage($"Thickness update requested: {thickness} (C++ integration disabled)");
            }
        }
    }

    /// <summary>강제 다시 그리기</summary>
    public static void ForceRedraw()
    {
        lock (_sync)
        {
            if (_host != null && _running)
            {
                try
                {
                    _host.ForceRedraw();
                    LogMessage("Force redraw executed");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to force redraw: {ex.Message}");
                }
            }
            else
            {
                LogMessage("Force redraw requested (C++ integration disabled)");
            }
        }
    }

    /// <summary>실행 상태 확인</summary>
    public static bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _running; // && _host != null;
            }
        }
    }

    private static void SafeStop()
    {
        try
        {
            if (_host != null)
            {
                _host.LogReceived -= OnNativeLogReceived;
                _host.Dispose();
                _host = null;
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Error during BorderService cleanup: {ex.Message}");
        }
        finally
        {
            _running = false;
        }
    }

    private static void OnNativeLogReceived(int level, string message)
    {
        var levelStr = level switch
        {
            0 => "INFO",
            1 => "WARN", 
            2 => "ERROR",
            _ => $"L{level}"
        };
        
        LogMessage($"[Native {levelStr}] {message}");
    }

    private static void LogMessage(string message)
    {
        var logLine = $"[{DateTime.Now:HH:mm:ss}] [BorderService] {message}";
        LogReceived?.Invoke(logLine);
        
        // WindowTracker 로그에도 추가
        WindowTracker.AddExternalLog(logLine);
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

/// <summary>C++ BorderServiceHost와의 상호운용을 위한 래퍼 클래스 (현재 비활성화)</summary>
internal class BorderServiceCppHost : IDisposable
{
    private IntPtr _nativeHandle;
    private bool _disposed;
    private readonly GCHandle _logCallbackHandle;
    
    public event Action<int, string>? LogReceived;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void LogCallback(int level, [MarshalAs(UnmanagedType.LPWStr)] string message);

    public BorderServiceCppHost(int argbColor, int thickness, bool debug)
    {
        // C++ 프로젝트가 준비되면 활성화 예정
        throw new NotImplementedException("C++ integration not yet available");
    }

    public void UpdateColor(int argbColor)
    {
        ThrowIfDisposed();
        // BS_UpdateColor(_nativeHandle, argbColor);
    }

    public void UpdateThickness(int thickness)
    {
        ThrowIfDisposed();
        // BS_UpdateThickness(_nativeHandle, thickness);
    }

    public void ForceRedraw()
    {
        ThrowIfDisposed();
        // BS_ForceRedraw(_nativeHandle);
    }

    private void OnLogCallback(int level, string message)
    {
        LogReceived?.Invoke(level, message ?? string.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            if (_nativeHandle != IntPtr.Zero)
            {
                // BS_DestroyContext(_nativeHandle);
                _nativeHandle = IntPtr.Zero;
            }
        }
        catch { }
        
        try
        {
            if (_logCallbackHandle.IsAllocated)
                _logCallbackHandle.Free();
        }
        catch { }
        
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BorderServiceCppHost));
    }

    #region P/Invoke Declarations (비활성화됨)
    /*
    private const string DllName = "BorderServiceCpp";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr BS_CreateContext(int argb, int thickness, int debug);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BS_DestroyContext(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BS_UpdateColor(IntPtr ctx, int argb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BS_UpdateThickness(IntPtr ctx, int thickness);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BS_ForceRedraw(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void BS_SetLogger(IntPtr ctx, LogCallback logger);
    */
    #endregion
}
