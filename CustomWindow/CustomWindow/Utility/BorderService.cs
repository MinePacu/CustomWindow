using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace CustomWindow.Utility;

public static class BorderService
{
    private static Process? _winrtProc;
    private static BorderServiceCppHost? _host;
    private static readonly object _sync = new();
    private static bool _running = false;
    private static CancellationTokenSource? _postStartCts;

    // Prefer EXE mode by default
    private const bool PreferExeMode = true;

    // Keep last used values for EXE restart scenarios
    private static string _lastColor = "#0078FF";
    private static int _lastThickness = 3;

    // 로그 이벤트
    public static event Action<string>? LogReceived;    

    // 정적 생성자: EXE 모드 선호 시 DLL 선로딩 생략
    static BorderService()
    {
        if (!PreferExeMode)
        {
            try
            {
                NativeDllLoader.LoadBorderServiceDll();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Static constructor DLL load failed: {ex.Message}");
            }
        }
    }

    /// <summary>현재 모드 태그</summary>
    private static string CurrentModeTag
        => (_winrtProc != null && !_winrtProc.HasExited) ? "EXE" : (_host != null ? "DLL" : "IDLE");

    /// <summary>BorderService 시작</summary>
    public static void StartIfNeeded(string borderColorHex, int thickness, string[] excludedProcesses)
    {
        lock (_sync)
        {
            if (PreferExeMode)
            {
                LogMessage($"Starting in EXE mode (Color={borderColorHex}, Thickness={thickness})");
                StartWinRTConsole(borderColorHex, thickness, exePath: null, showConsole: true);
                return;
            }

            // DLL 모드 (옵션)
            LogMessage($"Starting in DLL mode (Color={borderColorHex}, Thickness={thickness})");
            StartWithDll(borderColorHex, thickness);
        }
    }

    private static void StartWithDll(string borderColorHex, int thickness)
    {
        if (_running && _host != null)
        {
            LogMessage("BorderService already running - updating settings");
            UpdateColor(borderColorHex);
            UpdateThickness(thickness);
            return;
        }

        try
        {
            var argbColor = ParseColor(borderColorHex);
            _host = new BorderServiceCppHost(argbColor, thickness, debug: true);
            _host.LogReceived += OnNativeLogReceived;

            _postStartCts?.Cancel();
            _postStartCts = new CancellationTokenSource();
            var token = _postStartCts.Token;

            System.Threading.Tasks.Task.Delay(500, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                try
                {
                    lock (_sync)
                    {
                        if (_running && _host != null)
                            _host.ForceRedraw();
                    }
                    LogMessage("Triggered border assignment for visible windows");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to trigger border assignment: {ex.Message}");
                }
            }, System.Threading.Tasks.TaskScheduler.Default);

            _running = true;
            LogMessage("BorderService started successfully (DLL)");
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to start BorderService (DLL): {ex.Message}");
            SafeStop();
        }
    }

    /// <summary>BorderService 중지</summary>
    public static void StopIfRunning()
    {
        lock (_sync)
        {
            _postStartCts?.Cancel();
            _postStartCts = null;

            if (!_running && _winrtProc == null) return;

            LogMessage("Stopping BorderService");
            // EXE 모드 우선 종료
            StopWinRTConsoleIfRunning();
            // DLL 모드 종료
            SafeStop();
            LogMessage("BorderService stopped");
        }
    }

    /// <summary>색상 업데이트</summary>
    public static void UpdateColor(string borderColorHex)
    {
        lock (_sync)
        {
            // EXE 모드: 재시작으로 반영
            if (_winrtProc != null && !_winrtProc.HasExited)
            {
                _lastColor = string.IsNullOrWhiteSpace(borderColorHex) ? _lastColor : borderColorHex.Trim();
                LogMessage($"Restarting EXE for color change -> {_lastColor}");
                RestartWinRTConsoleForSettingsUpdate(borderColorHex, GetCurrentThicknessOrDefault(), exePath: null, showConsole: true);
                return;
            }

            // DLL 모드가 살아있다면(테스트/백업 용)
            if (_host != null && _running)
            {
                try
                {
                    var argbColor = ParseColor(borderColorHex);
                    _host.UpdateColor(argbColor);
                    LogMessage($"Color updated to {borderColorHex} (DLL)");
                }
                catch (Exception ex) { LogMessage($"Failed to update color (DLL): {ex.Message}"); }
            }
        }
    }

    /// <summary>두께 업데이트</summary>
    public static void UpdateThickness(int thickness)
    {
        lock (_sync)
        {
            // EXE 모드: 재시작으로 반영
            if (_winrtProc != null && !_winrtProc.HasExited)
            {
                _lastThickness = thickness;
                LogMessage($"Restarting EXE for thickness change -> {thickness}");
                RestartWinRTConsoleForSettingsUpdate(GetCurrentColorOrDefault(), thickness, exePath: null, showConsole: true);
                return;
            }

            // DLL 모드(백업)
            if (_host != null && _running)
            {
                try { _host.UpdateThickness(thickness); LogMessage($"Thickness updated to {thickness} (DLL)"); }
                catch (Exception ex) { LogMessage($"Failed to update thickness (DLL): {ex.Message}"); }
            }
        }
    }

    /// <summary>강제 다시 그리기</summary>
    public static void ForceRedraw()
    {
        lock (_sync)
        {
            if (_winrtProc != null && !_winrtProc.HasExited)
            {
                LogMessage("Force redraw is not supported in EXE mode (no IPC). Use restart via UpdateColor/UpdateThickness.");
                return;
            }

            if (_host != null && _running)
            {
                try
                {
                    _host.ForceRedraw();
                    LogMessage("Force redraw executed (DLL)");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to force redraw (DLL): {ex.Message}");
                }
            }
            else
            {
                LogMessage("Force redraw requested (not running)");
            }
        }
    }

    /// <summary>부분 렌더링 비율 설정</summary>
    public static void SetPartialRatio(float ratio)
    {
        lock (_sync)
        {
            if (_winrtProc != null && !_winrtProc.HasExited)
            {
                LogMessage("SetPartialRatio is not supported in EXE mode.");
                return;
            }

            if (_host != null && _running)
            {
                try
                {
                    _host.SetPartialRatio(ratio);
                    LogMessage($"Partial ratio set to {ratio:F2} (DLL)");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to set partial ratio (DLL): {ex.Message}");
                }
            }
        }
    }

    /// <summary>오버랩 병합 활성화/비활성화</summary>
    public static void EnableMerge(bool enable)
    {
        lock (_sync)
        {
            if (_winrtProc != null && !_winrtProc.HasExited)
            {
                LogMessage("EnableMerge is not supported in EXE mode.");
                return;
            }

            if (_host != null && _running)
            {
                try
                {
                    _host.EnableMerge(enable);
                    LogMessage($"Merge {(enable ? "enabled" : "disabled")} (DLL)");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to set merge mode (DLL): {ex.Message}");
                }
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
                var exeRunning = _winrtProc != null && !_winrtProc.HasExited;
                var dllRunning = _running && _host != null;
                return exeRunning || dllRunning;
            }
        }
    }

    /// <summary>DLL 파일 경로 정보</summary>
    public static string GetDllSearchInfo()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        
        var paths = new[]
        {
            Path.Combine(currentDir, "BorderServiceCpp.dll"),
            Path.Combine(exeDir, "BorderServiceCpp.dll"),
            Path.Combine(appDir, "BorderServiceCpp.dll")
        };
        
        var info = $"DLL Search Paths:\n";
        info += $"Current Directory: {currentDir}\n";
        info += $"Executable Directory: {exeDir}\n";
        info += $"App Domain Directory: {appDir}\n\n";
        
        foreach (var path in paths)
        {
            info += $"{path}: {(File.Exists(path) ? "EXISTS" : "NOT FOUND")}\n";
        }
        
        return info;
    }

    /// <summary>DLL 로드 가능 여부 확인</summary>
    public static bool IsDllAvailable()
    {
        try
        {
            // 먼저 DLL 파일 위치 로깅
            LogMessage(GetDllSearchInfo());
            
            // LoadLibrary를 사용하여 DLL 수동 로드 시도
            var dllPath = FindDllPath();
            if (!string.IsNullOrEmpty(dllPath))
            {
                LogMessage($"Found DLL at: {dllPath}");
                
                // 수동으로 DLL 로드
                var handle = LoadLibrary(dllPath);
                if (handle != IntPtr.Zero)
                {
                    LogMessage("DLL loaded successfully with LoadLibrary");
                    FreeLibrary(handle);
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    LogMessage($"LoadLibrary failed with error: {error}");
                }
            }
            
            // DLL 로드 테스트
            var testPtr = BorderServiceCppHost.BS_CreateContext(unchecked((int)0xFF000000), 2, 0);
            if (testPtr != IntPtr.Zero)
            {
                BorderServiceCppHost.BS_DestroyContext(testPtr);
                LogMessage("DLL function call test successful");
                return true;
            }
        }
        catch (DllNotFoundException ex)
        {
            LogMessage($"BorderServiceCpp.dll not found: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            LogMessage($"DLL entry point not found: {ex.Message}");
        }
        catch (BadImageFormatException ex)
        {
            LogMessage($"DLL format error (architecture mismatch?): {ex.Message}");
        }
        catch (Exception ex)
        {
            LogMessage($"DLL test failed: {ex.Message}");
        }
        return false;
    }

    private static string? FindDllPath()
    {
        var searchPaths = new[]
        {
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.CurrentDirectory
        };
        
        foreach (var searchPath in searchPaths)
        {
            var dllPath = Path.Combine(searchPath, "BorderServiceCpp.dll");
            if (File.Exists(dllPath))
            {
                return dllPath;
            }
        }
        
        return null;
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
        var logLine = $"[{DateTime.Now:HH:mm:ss}] [BorderService|{CurrentModeTag}] {message}";
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

    private static string BuildArgs(string borderColorHex, int thickness, bool withConsole)
    {
        var color = string.IsNullOrWhiteSpace(borderColorHex) ? "#0078FF" : borderColorHex.Trim();
        return $"{(withConsole ? "--console " : "")}--color \"{color}\" --thickness {thickness}";
    }

    private static string? FindWinRTExePath()
    {
        // 우선순위: 앱 폴더 옆 => 솔루션 형제 폴더 => Debug/Release 빌드 출력
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var exeNames = new[] { "BorderServiceWinRT.exe", "BorderService_test_winrt2.exe" };
        var candidates = new List<string>();

        foreach (var name in exeNames)
        {
            candidates.Add(Path.Combine(baseDir, name));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "BorderService_test_winrt2", "x64", "Debug", name)));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "BorderService_test_winrt2", "x64", "Release", name)));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "BorderService_test_winrt2", "x64", "Debug", name)));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "BorderService_test_winrt2", "x64", "Release", name)));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string GetCurrentColorOrDefault() => _lastColor;
    private static int GetCurrentThicknessOrDefault() => _lastThickness;

    /// <summary>BorderServiceWinRT 콘솔 시작</summary>
    public static void StartWinRTConsole(string borderColorHex, int thickness, string? exePath = null, bool showConsole = true)
    {
        lock (_sync)
        {
            _lastColor = string.IsNullOrWhiteSpace(borderColorHex) ? "#0078FF" : borderColorHex;
            _lastThickness = thickness;

            if (_running && _winrtProc != null && !_winrtProc.HasExited)
            {
                LogMessage("Already running (EXE) - applying settings by restart");
                RestartWinRTConsoleForSettingsUpdate(borderColorHex, thickness, exePath, showConsole);
                return;
            }

            try
            {
                var path = exePath ?? FindWinRTExePath();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new FileNotFoundException("BorderServiceWinRT executable not found.", path);

                var args = BuildArgs(borderColorHex, thickness, withConsole: showConsole);
                var psi = new ProcessStartInfo(path, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = !showConsole,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory
                };

                _winrtProc = Process.Start(psi);
                if (_winrtProc == null)
                    throw new InvalidOperationException("Failed to start BorderServiceWinRT process.");

                _winrtProc.EnableRaisingEvents = true;
                _winrtProc.Exited += (_, __) =>
                {
                    LogMessage("BorderServiceWinRT exited");
                    lock (_sync)
                    {
                        _winrtProc = null;
                        _running = false;
                    }
                };

                _running = true;
                LogMessage($"Started BorderServiceWinRT (EXE): {path} {args}");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start BorderServiceWinRT (EXE): {ex.Message}");
                SafeStop();
            }
        }
    }

    private static void RestartWinRTConsoleForSettingsUpdate(string borderColorHex, int thickness, string? exePath, bool showConsole)
    {
        try
        {
            if (_winrtProc != null && !_winrtProc.HasExited)
            {
                _winrtProc.Kill(entireProcessTree: true);
                _winrtProc.WaitForExit(1000);
            }
        }
        catch { /* ignore */ }
        _winrtProc = null;
        _running = false;
        StartWinRTConsole(borderColorHex, thickness, exePath, showConsole);
    }

    private static void StopWinRTConsoleIfRunning()
    {
        try
        {
            if (_winrtProc != null && !_winrtProc.HasExited)
            {
                _winrtProc.Kill(entireProcessTree: true);
                _winrtProc.WaitForExit(2000);
            }
        }
        catch { /* ignore */ }
        finally
        {
            _winrtProc = null;
        }
    }

    #region Windows API for DLL Loading
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    #endregion
}

/// <summary>C++ BorderServiceHost와의 상호운용을 위한 래퍼 클래스</summary>
internal class BorderServiceCppHost : IDisposable
{
    private IntPtr _nativeHandle;
    private bool _disposed;
    private GCHandle _logCallbackHandle;
    
    public event Action<int, string>? LogReceived;

    // 델리게이트를 internal로 변경하여 P/Invoke 메서드와 접근성을 맞춤
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void LogCallback(int level, [MarshalAs(UnmanagedType.LPWStr)] string message);

    public BorderServiceCppHost(int argbColor, int thickness, bool debug)
    {
        try
        {
            // 로그 콜백 설정
            LogCallback logCallback = OnLogCallback;
            _logCallbackHandle = GCHandle.Alloc(logCallback);
            
            _nativeHandle = BS_CreateContext(argbColor, thickness, debug ? 1 : 0);
            if (_nativeHandle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create native BorderService context");
                
            BS_SetLogger(_nativeHandle, logCallback);
            
            // 기본 설정
            BS_SetPartialRatio(_nativeHandle, 0.25f);
            BS_EnableMerge(_nativeHandle, 1);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void UpdateColor(int argbColor)
    {
        ThrowIfDisposed();
        BS_UpdateColor(_nativeHandle, argbColor);
    }

    public void UpdateThickness(int thickness)
    {
        ThrowIfDisposed();
        BS_UpdateThickness(_nativeHandle, thickness);
    }

    public void ForceRedraw()
    {
        ThrowIfDisposed();
        BS_ForceRedraw(_nativeHandle);
    }

    public void SetPartialRatio(float ratio)
    {
        ThrowIfDisposed();
        BS_SetPartialRatio(_nativeHandle, ratio);
    }

    public void EnableMerge(bool enable)
    {
        ThrowIfDisposed();
        BS_EnableMerge(_nativeHandle, enable ? 1 : 0);
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
                try
                {
                    // 네이티브 측 콜백 끊기(더 이상 호출 못 하게)
                    BS_SetLogger(_nativeHandle, null);
                }
                catch { /* ignore */ }

                // 컨텍스트 파괴
                BS_DestroyContext(_nativeHandle);
                _nativeHandle = IntPtr.Zero;
            }
        }
        catch { }

        try
        {
            if (_logCallbackHandle.IsAllocated)
                _logCallbackHandle.Free(); // 콜백 핀 해제는 파괴 이후
        }
        catch { }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BorderServiceCppHost));
    }

    #region P/Invoke Declarations
    private const string DllName = "BorderServiceCpp.dll"; // .dll 확장자 명시

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern IntPtr BS_CreateContext(int argb, int thickness, int debug);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern void BS_DestroyContext(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern void BS_UpdateColor(IntPtr ctx, int argb);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern void BS_UpdateThickness(IntPtr ctx, int thickness);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern void BS_ForceRedraw(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern void BS_SetLogger(IntPtr ctx, LogCallback logger);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern void BS_SetPartialRatio(IntPtr ctx, float ratio);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern void BS_EnableMerge(IntPtr ctx, int enable);
    #endregion
}