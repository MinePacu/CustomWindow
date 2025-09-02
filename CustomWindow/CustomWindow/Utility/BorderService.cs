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
    private static bool _lastShowConsole = false; // new: remember console preference
    private static string _lastCorner = "default"; // normalized tokens: default|donot|round|roundsmall

    // New: render mode preference (Auto/Dwm/DComp)
    private static string _renderModePreference = "Auto";

    // New: foreground window only mode
    private static bool _foregroundWindowOnly = false;

    // Excluded processes cache (names without extension)
    private static string[] _excluded = Array.Empty<string>();

    // IPC constants
    private const uint WM_COPYDATA = 0x004A;
    private const uint SMTO_NORMAL = 0x0000;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const string OverlayWindowClass = "BorderOverlayDCompWindowClass";

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
        => (IsExeModeRunning ? "EXE" : (_host != null ? "DLL" : "IDLE"));

    /// <summary>EXE 모드 실행 여부</summary>
    public static bool IsExeModeRunning => _winrtProc != null && !_winrtProc.HasExited;

    /// <summary>외부에서 실행된 오버레이 윈도우 존재 여부</summary>
    private static bool IsOverlayPresent()
    {
        try { return FindOverlayWindow() != IntPtr.Zero; } catch { return false; }
    }
  
    /// <summary>오버레이(내부 EXE 또는 외부) 사용 가능 여부</summary>
    private static bool OverlayAvailable => IsExeModeRunning || IsOverlayPresent();

    /// <summary>현재 실행 중인 EXE PID</summary>
    public static int? CurrentExePid => IsExeModeRunning ? _winrtProc!.Id : null;

    /// <summary>실행 중인 EXE의 전체 경로(가능한 경우)</summary>
    public static string? GetRunningExePath()
    {
        try { return _winrtProc?.MainModule?.FileName; } catch { return null; }
    }

    /// <summary>EXE 파일 존재 여부와 경로(실행 중이면 해당 경로, 아니면 검색)</summary>
    public static (bool Found, string? Path) GetExePathInfo()
    {
        var runningPath = GetRunningExePath();
        if (!string.IsNullOrEmpty(runningPath)) return (true, runningPath);
        var path = FindWinRTExePath();
        return (!string.IsNullOrEmpty(path), path);
    }

    /// <summary>요약 상태 문자열 (EXE 전용)</summary>
    public static string GetExeStatusSummary()
    {
        if (IsExeModeRunning)
        {
            var pid = CurrentExePid;
            var path = GetRunningExePath();
            return $"EXE 실행 중 (PID={pid}, {(string.IsNullOrEmpty(path) ? "경로 확인불가" : path)}{(_lastShowConsole ? ", Console=Shown" : ", Console=Hidden")}, Mode={_renderModePreference}, Corner={_lastCorner})";
        }
        if (IsOverlayPresent()) return "오버레이가 표시중입니다.";
        var info = GetExePathInfo();
        return info.Found ? $"EXE 후보: {info.Path}" : "EXE 파일을 찾을 수 없음";
    }

    /// <summary>렌더 모드 선호 설정(Auto/Dwm/DComp)</summary>
    public static void SetRenderModePreference(string mode)
    {
        lock (_sync)
        {
            var m = (mode ?? "Auto").Trim();
            if (!string.Equals(_renderModePreference, m, StringComparison.OrdinalIgnoreCase))
            {
                _renderModePreference = m;
                LogMessage($"Render mode preference set: {_renderModePreference}");
                if (IsExeModeRunning)
                {
                    LogMessage("Restarting EXE to apply render mode change");
                    RestartWinRTConsoleForSettingsUpdate(GetCurrentColorOrDefault(), GetCurrentThicknessOrDefault(), exePath: null, showConsole: _lastShowConsole);
                }
            }
        }
    }

    /// <summary>사용자 콘솔 표시 선호 설정</summary>
    public static void SetConsoleVisibilityPreference(bool show)
    {
        lock (_sync)
        {
            _lastShowConsole = show;
            LogMessage($"Console visibility preference set: {(_lastShowConsole ? "Show" : "Hide")}");
            if (IsExeModeRunning)
            {
                LogMessage("Restarting EXE to apply console visibility change");
                RestartWinRTConsoleForSettingsUpdate(GetCurrentColorOrDefault(), GetCurrentThicknessOrDefault(), exePath: null, showConsole: _lastShowConsole);
            }
        }
    }

    // New: update window corner mode
    public static void UpdateCornerMode(string? mode)
    {
        lock (_sync)
        {
            var normalized = NormalizeCorner(mode);
            if (string.Equals(_lastCorner, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _lastCorner = normalized;
            LogMessage($"Corner preference set: {_lastCorner}");

            if (OverlayAvailable)
            {
                if (!TrySendSettingsToOverlay(GetCurrentColorOrDefault(), GetCurrentThicknessOrDefault()))
                {
                    LogMessage("IPC failed for corner change");
                    if (IsExeModeRunning)
                    {
                        RestartWinRTConsoleForSettingsUpdate(GetCurrentColorOrDefault(), GetCurrentThicknessOrDefault(), exePath: null, showConsole: _lastShowConsole);
                    }
                }
                else
                {
                    TrySendRefreshToOverlay();
                }
            }
            else if (_host != null && _running)
            {
                try { _host.ForceRedraw(); } catch { }
            }
        }
    }

    private static string NormalizeCorner(string? mode)
    {
        var m = (mode ?? "기본").Trim();
        // Map localized strings to canonical tokens
        return m switch
        {
            "기본" => "default",
            "둥글게 하지 않음" => "donot",
            "둥글게" => "round",
            "덜 둥글게" => "roundsmall",
            _ => m.ToLowerInvariant()
        };
    }

    /// <summary>BorderService 시작</summary>
    public static void StartIfNeeded(string borderColorHex, int thickness, string[] excludedProcesses)
    {
        lock (_sync)
        {
            _excluded = excludedProcesses ?? Array.Empty<string>();

            if (PreferExeMode)
            {
                LogMessage($"Starting in EXE mode (Color={borderColorHex}, Thickness={thickness}, Console={(_lastShowConsole ? "Show" : "Hide")}, Mode={_renderModePreference}, Corner={_lastCorner}, ForegroundOnly={_foregroundWindowOnly})");
                // 콘솔 창 옵션을 사용자 설정에 맞게 시작
                StartWinRTConsole(borderColorHex, thickness, exePath: null, showConsole: _lastShowConsole);

                // Subscribe to WindowTracker updates to push HWND list to overlay
                try
                {
                    WindowTracker.WindowSetChanged -= OnWindowSetChanged;
                    WindowTracker.WindowSetChanged += OnWindowSetChanged;
                }
                catch { }
                return;
            }

            // DLL 모드 (옵션)
            LogMessage($"Starting in DLL mode (Color={borderColorHex}, Thickness={thickness})");
            StartWithDll(borderColorHex, thickness);
        }
    }

    private static void OnWindowSetChanged(IReadOnlyCollection<nint> handles)
    {
        try
        {
            if (!OverlayAvailable) return;

            // Apply exclusion: map to process names then filter
            var detailed = WindowTracker.GetCurrentWindowsDetailed();
            var excludedSet = new HashSet<string>(_excluded.Select(x => Path.GetFileNameWithoutExtension(x) ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            var filtered = detailed
                .Where(t => t.Handle != IntPtr.Zero)
                .Where(t => string.IsNullOrEmpty(t.ProcessName) || !excludedSet.Contains(t.ProcessName!))
                .Select(t => t.Handle)
                .ToArray();

            TrySendHwndListToOverlay(filtered);
        }
        catch (Exception ex)
        {
            LogMessage($"OnWindowSetChanged error: {ex.Message}");
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

            try { WindowTracker.WindowSetChanged -= OnWindowSetChanged; } catch { }

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
            _lastColor = string.IsNullOrWhiteSpace(borderColorHex) ? _lastColor : borderColorHex.Trim();

            // EXE 또는 외부 오버레이가 있으면 IPC 우선
            if (OverlayAvailable)
            {
                if (TrySendSettingsToOverlay(_lastColor, GetCurrentThicknessOrDefault()))
                {
                    LogMessage($"Applied color via IPC -> {_lastColor}");
                    return;
                }
                LogMessage($"IPC failed - attempting EXE restart for color change -> {_lastColor}");
                if (IsExeModeRunning)
                {
                    RestartWinRTConsoleForSettingsUpdate(_lastColor, GetCurrentThicknessOrDefault(), exePath: null, showConsole: _lastShowConsole);
                    return;
                }
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
            _lastThickness = thickness;

            // EXE 또는 외부 오버레이가 있으면 IPC 우선
            if (OverlayAvailable)
            {
                if (TrySendSettingsToOverlay(GetCurrentColorOrDefault(), thickness))
                {
                    LogMessage($"Applied thickness via IPC -> {thickness}");
                    return;
                }
                LogMessage($"IPC failed - attempting EXE restart for thickness change -> {thickness}");
                if (IsExeModeRunning)
                {
                    RestartWinRTConsoleForSettingsUpdate(GetCurrentColorOrDefault(), thickness, exePath: null, showConsole: _lastShowConsole);
                    return;
                }
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
            if (OverlayAvailable)
            {
                // IPC로 새로고침 요청
                if (!TrySendRefreshToOverlay())
                {
                    LogMessage("Force redraw IPC failed.");
                }
                return;
            }

            if (_host != null && _running)
            {
                try { _host.ForceRedraw(); LogMessage("Force redraw executed (DLL)"); }
                catch (Exception ex) { LogMessage($"Failed to force redraw (DLL): {ex.Message}"); }
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
            if (OverlayAvailable)
            {
                LogMessage("SetPartialRatio is not supported in EXE mode.");
                return;
            }

            if (_host != null && _running)
            {
                try { _host.SetPartialRatio(ratio); LogMessage($"Partial ratio set to {ratio:F2} (DLL)"); }
                catch (Exception ex) { LogMessage($"Failed to set partial ratio (DLL): {ex.Message}"); }
            }
        }
    }

    /// <summary>오버랩 병합 활성화/비활성화</summary>
    public static void EnableMerge(bool enable)
    {
        lock (_sync)
        {
            if (OverlayAvailable)
            {
                LogMessage("EnableMerge is not supported in EXE mode.");
                return;
            }

            if (_host != null && _running)
            {
                try { _host.EnableMerge(enable); LogMessage($"Merge {(enable ? "enabled" : "disabled")} (DLL)"); }
                catch (Exception ex) { LogMessage($"Failed to set merge mode (DLL): {ex.Message}"); }
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
                var exeOrOverlay = OverlayAvailable;
                var dllRunning = _running && _host != null;
                return exeOrOverlay || dllRunning;
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
        var searchPaths = new []
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

    // 안전한 로그 이벤트 발행: 구독자 예외가 앱을 종료시키지 않도록 보호
    private static void SafeRaiseLogReceived(string message)
    {
        try
        {
            var handlers = LogReceived;
            if (handlers == null) return;
            foreach (var del in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<string>)del).Invoke(message);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BorderService] LogReceived handler error: {ex}");
                }
            }
        }
        catch { }
    }

    private static void LogMessage(string message)
    {
        var logLine = $"[{DateTime.Now:HH:mm:ss}] [BorderService|{CurrentModeTag}] {message}";
        SafeRaiseLogReceived(logLine);
        
        // WindowTracker 로그에도 추가
        WindowTracker.AddExternalLog(logLine);
    }

    private static void LogExeOutput(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var logLine = $"[{DateTime.Now:HH:mm:ss}] [BorderService|EXE] {message}";
        SafeRaiseLogReceived(logLine);
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
        var modeArg = _renderModePreference.Equals("dwm", StringComparison.OrdinalIgnoreCase) ? "dwm" :
                      _renderModePreference.Equals("dcomp", StringComparison.OrdinalIgnoreCase) ? "dcomp" : "auto";
        var cornerArg = _lastCorner; // normalized
        var foregroundArg = _foregroundWindowOnly ? "1" : "0";
        return $"{(withConsole ? "--console " : string.Empty)}--mode {modeArg} --color \"{color}\" --thickness {thickness} --corner {cornerArg} --foregroundonly {foregroundArg}";
    }

    private static string? FindWinRTExePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var exeNames = new[] { "BorderService_test_winrt2.exe", "BorderServiceWinRT.exe" };
        var candidates = new List<string>();
        foreach (var name in exeNames)
        {
            candidates.Add(Path.Combine(baseDir, name));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "BorderService_test_winrt2", "x64", "Debug", name)));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "BorderService_test_winrt2", "x64", "Release", name)));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "BorderService_test_winrt2", "x64", "Debug", name)));
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "BorderService_test_winrt2", "x64", "Release", name)));
        }
        var existing = candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => new FileInfo(p)).OrderByDescending(fi => fi.LastWriteTimeUtc).ToList();
        if (existing.Count == 0) return null;
        var chosen = existing[0].FullName;
        LogMessage($"WinRT EXE candidates (newest first):{Environment.NewLine}{string.Join(Environment.NewLine, existing.Select(fi => $"- {fi.FullName} (LastWrite={fi.LastWriteTimeUtc:u})"))}");
        LogMessage($"Chosen WinRT EXE: {chosen}");
        return chosen;
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
            _lastShowConsole = showConsole;

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
                    WorkingDirectory = Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory,
                    RedirectStandardOutput = !showConsole,
                    RedirectStandardError = !showConsole
                };

                _winrtProc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start BorderServiceWinRT process.");

                if (!showConsole)
                {
                    _winrtProc.OutputDataReceived += (_, e) => { if (e.Data != null) LogExeOutput(e.Data); };
                    _winrtProc.ErrorDataReceived += (_, e) => { if (e.Data != null) LogExeOutput(e.Data); };
                    try { _winrtProc.BeginOutputReadLine(); _winrtProc.BeginErrorReadLine(); } catch { }
                }

                _winrtProc.EnableRaisingEvents = true;
                _winrtProc.Exited += (_, __) =>
                {
                    LogMessage("BorderServiceWinRT exited");
                    lock (_sync)
                    {
                        _winrtProc = null;
                        _running = false;
                        try { WindowTracker.WindowSetChanged -= OnWindowSetChanged; } catch { }
                    }
                };

                _running = true;
                LogMessage($"Started BorderServiceWinRT (EXE): {path} {args}");

                System.Threading.Tasks.Task.Run(() =>
                {
                    var hwnd = WaitForOverlayWindowReady();
                    if (hwnd != IntPtr.Zero)
                    {
                        TrySendCopyData(hwnd, BuildSettingsMessage());
                        TrySendCopyData(hwnd, BuildRefreshMessage());
                        LogMessage("Overlay ready -> settings re-applied via IPC");

                        var detailed = WindowTracker.GetCurrentWindowsDetailed();
                        var excludedSet = new HashSet<string>(_excluded.Select(x => Path.GetFileNameWithoutExtension(x) ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                        var filtered = detailed.Where(t => t.Handle != IntPtr.Zero)
                                               .Where(t => string.IsNullOrEmpty(t.ProcessName) || !excludedSet.Contains(t.ProcessName!))
                                               .Select(t => t.Handle)
                                               .ToArray();
                        TrySendHwndListToOverlay(filtered);
                    }
                    else { LogMessage("Overlay not ready within timeout after start"); }
                });

                var fvi = FileVersionInfo.GetVersionInfo(path);
                LogMessage($"EXE file: {Path.GetFileName(path)}, Desc={fvi.FileDescription}, Ver={fvi.FileVersion}, LastWrite={File.GetLastWriteTimeUtc(path):u}");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start BorderServiceWinRT (EXE): {ex.Message}");
                SafeStop();
            }
        }
    }

    private static bool TrySendHwndListToOverlay(IReadOnlyCollection<nint> handles)
    {
        if (handles == null || handles.Count == 0) return false;
        var hwnd = FindOverlayWindow();
        if (hwnd == IntPtr.Zero) return false;
        var parts = handles.Select(h => $"0x{((IntPtr)h).ToInt64():X}");
        var msg = "HWNDS " + string.Join(' ', parts);
        return TrySendCopyData(hwnd, msg);
    }

    private static string BuildSettingsMessage() => $"SET foregroundonly={(_foregroundWindowOnly ? "1" : "0")} color={NormalizeColor(GetCurrentColorOrDefault())} thickness={GetCurrentThicknessOrDefault()} corner={_lastCorner}";
    private static string BuildRefreshMessage() => $"REFRESH foregroundonly={(_foregroundWindowOnly ? "1" : "0")} color={NormalizeColor(GetCurrentColorOrDefault())} thickness={GetCurrentThicknessOrDefault()} corner={_lastCorner}";

    private static bool TrySendRefreshToOverlay()
    {
        var hwnd = FindOverlayWindow();
        if (hwnd == IntPtr.Zero && IsExeModeRunning)
        {
            LogMessage("Overlay not ready, waiting briefly for REFRESH...");
            hwnd = WaitForOverlayWindowReady();
        }
        if (hwnd == IntPtr.Zero) return false;
        return TrySendCopyData(hwnd, BuildRefreshMessage());
    }

    private static bool TrySendSettingsToOverlay(string colorHex, int thickness)
    {
        var hwnd = FindOverlayWindow();
        if (hwnd == IntPtr.Zero && IsExeModeRunning)
        {
            LogMessage("Overlay not ready, waiting briefly for settings...");
            hwnd = WaitForOverlayWindowReady();
        }
        if (hwnd == IntPtr.Zero) return false;
        var prevColor = _lastColor; var prevThick = _lastThickness;
        _lastColor = string.IsNullOrWhiteSpace(colorHex) ? _lastColor : colorHex;
        _lastThickness = thickness;
        var ok = TrySendCopyData(hwnd, BuildSettingsMessage());
        if (!ok) { _lastColor = prevColor; _lastThickness = prevThick; }
        return ok;
    }

    private static IntPtr WaitForOverlayWindowReady(int timeoutMs = 1500, int intervalMs = 100)
    {
        var sw = Stopwatch.StartNew();
        IntPtr h = IntPtr.Zero;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            h = FindOverlayWindow();
            if (h != IntPtr.Zero) return h;
            Thread.Sleep(intervalMs);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindOverlayWindow()
    {
        try
        {
            // Try by class name first
            var h = FindWindow(OverlayWindowClass, null);
            if (h != IntPtr.Zero) return h;

            // Fallback: enumerate all top-level windows and find matching class
            IntPtr found = IntPtr.Zero;
            EnumWindows((hwnd, lParam) =>
            {
                var sb = new System.Text.StringBuilder(128);
                int len = GetClassName(hwnd, sb, sb.Capacity);
                if (len > 0)
                {
                    var name = sb.ToString(0, len);
                    if (string.Equals(name, OverlayWindowClass, StringComparison.Ordinal))
                    {
                        found = hwnd;
                        return false; // stop
                    }
                }
                return true; // continue
            }, IntPtr.Zero);

            if (found == IntPtr.Zero)
                LogMessage("Overlay window not found for IPC (class BorderOverlayDCompWindowClass)");
            else
                LogMessage($"Overlay window found: 0x{found.ToInt64():X}");

            return found;
        }
        catch (Exception ex)
        {
            LogMessage($"FindOverlayWindow error: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    private static bool TrySendCopyData(IntPtr hwnd, string message)
    {
        IntPtr dataPtr = IntPtr.Zero;
        IntPtr cdsPtr = IntPtr.Zero;
        try
        {
            LogMessage($"IPC -> hwnd=0x{hwnd.ToInt64():X}, msg='{message}'");
            dataPtr = Marshal.StringToHGlobalUni(message);
            var cds = new COPYDATASTRUCT
            {
                dwData = IntPtr.Zero,
                cbData = (message.Length + 1) * 2,
                lpData = dataPtr
            };
            cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<COPYDATASTRUCT>());
            Marshal.StructureToPtr(cds, cdsPtr, false);
            IntPtr result;
            var sendRes = SendMessageTimeout(hwnd, WM_COPYDATA, IntPtr.Zero, cdsPtr, SMTO_ABORTIFHUNG | SMTO_NORMAL, 300, out result);
            if (sendRes == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                LogMessage($"IPC send failed (error={err})");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"IPC send failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (cdsPtr != IntPtr.Zero) Marshal.FreeHGlobal(cdsPtr);
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
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
        catch { }
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
        catch { }
        finally { _winrtProc = null; }
    }

    private static string NormalizeColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return "#0078FF";
        var c = color.Trim(); if (!c.StartsWith("#")) c = "#" + c; return c;
    }

    #region Win32
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadLibrary(string lpFileName);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)] private struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    #endregion

    /// <summary>포그라운드 창 전용 모드 설정</summary>
    public static void SetForegroundWindowOnly(bool foregroundOnly)
    {
        lock (_sync)
        {
            if (_foregroundWindowOnly == foregroundOnly) return;
            
            var previousMode = _foregroundWindowOnly;
            _foregroundWindowOnly = foregroundOnly;
            LogMessage($"Foreground window only mode: {(previousMode ? "Enabled" : "Disabled")} -> {(_foregroundWindowOnly ? "Enabled" : "Disabled")}");
            
            // EXE 모드에서는 메시지로 전달
            if (OverlayAvailable)
            {
                var msg = $"SET foregroundonly={(foregroundOnly ? "1" : "0")} color={GetCurrentColorOrDefault()} thickness={GetCurrentThicknessOrDefault()} corner={_lastCorner}";
                var hwnd = FindOverlayWindow();
                if (hwnd != IntPtr.Zero)
                {
                    TrySendCopyData(hwnd, msg);
                    
                    // 포그라운드 옵션 변경 후 충분한 시간을 두고 창 목록을 다시 전송
                    System.Threading.Tasks.Task.Delay(200).ContinueWith(_ =>
                    {
                        try
                        {
                            // 전체 창 목록을 다시 수집하여 전송 (필터링은 C++ 쪽에서 처리)
                            var allDetails = WindowTracker.GetCurrentWindowsDetailed();
                            var excludedSet = new HashSet<string>(_excluded.Select(x => Path.GetFileNameWithoutExtension(x) ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                            var allWindows = allDetails.Where(t => t.Handle != IntPtr.Zero)
                                                      .Where(t => string.IsNullOrEmpty(t.ProcessName) || !excludedSet.Contains(t.ProcessName!))
                                                      .Select(t => t.Handle)
                                                      .ToArray();
                        
                            TrySendHwndListToOverlay(allWindows);
                            LogMessage($"Sent complete window list ({allWindows.Length} windows) after foreground mode change");
                            
                            // 추가 새로고침 요청 (두 번째 새로고침으로 확실히 반영)
                            System.Threading.Tasks.Task.Delay(100).ContinueWith(__ =>
                            {
                                TrySendRefreshToOverlay();
                                LogMessage("Sent final refresh after foreground mode change");
                            }, System.Threading.Tasks.TaskScheduler.Default);
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Failed to send window list after foreground mode change: {ex.Message}");
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
                }
                else if (IsExeModeRunning)
                {
                    LogMessage("Restarting EXE to apply foreground window only change");
                    RestartWinRTConsoleForSettingsUpdate(GetCurrentColorOrDefault(), GetCurrentThicknessOrDefault(), exePath: null, showConsole: _lastShowConsole);
                }
            }
            else if (_host != null && _running)
            {
                // DLL 모드에서는 강제 다시 그리기
                try 
                { 
                    _host.ForceRedraw(); 
                    LogMessage("Force redraw executed after foreground mode change (DLL)");
                }
                catch (Exception ex) 
                { 
                    LogMessage($"Failed to force redraw after foreground mode change (DLL): {ex.Message}"); 
                }
            }
        }
    }
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