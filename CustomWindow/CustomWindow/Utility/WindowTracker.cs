using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CustomWindow.Utility;

public static class WindowTracker
{
    private static readonly ConcurrentDictionary<nint, TrackedWindow> _windows = new();
    private static Timer? _timer;
    private static readonly object _sync = new();
    private static bool _running;
    private static TimeSpan _interval = TimeSpan.FromMilliseconds(1000);

    private static readonly int _logCapacity = 500;
    private static readonly ConcurrentQueue<string> _logs = new();
    public static event Action<string>? LogAdded;

    // New: notify when window set changes (for EXE IPC)
    public static event Action<IReadOnlyCollection<nint>>? WindowSetChanged;

    private class TrackedWindow
    {
        public nint Handle { get; init; }
        public int ProcessId { get; init; }
        public string? ProcessName { get; init; }
        public DateTime FirstSeen { get; init; } = DateTime.UtcNow;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    /// <summary>현재 추적 중인 윈도우 핸들 목록.</summary>
    public static IReadOnlyCollection<nint> CurrentWindowHandles => _windows.Keys.ToList();

    /// <summary>최근 로그 전체 반환 (최신순)</summary>
    public static IReadOnlyList<string> GetRecentLogs() => _logs.Reverse().ToList();

    // New: provide process names along with handles for filtering (excluded list etc.)
    public static IReadOnlyList<(nint Handle, string? ProcessName)> GetCurrentWindowsDetailed() => _windows.Values.Select(w => (w.Handle, w.ProcessName)).ToList();

    /// <summary>추적 시작 (이미 실행 중이면 무시)</summary>
    public static void Start(TimeSpan? interval = null)
    {
        lock (_sync)
        {
            if (_running)
            {
                AddLog("이미 실행 중이라 Start 무시");
                return;
            }
            if (interval != null) _interval = interval.Value;
            _running = true;
            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, _interval);
            AddLog($"추적 시작 (주기={_interval.TotalMilliseconds}ms)");
        }
    }

    /// <summary>추적 중지 및 내부 캐시 클리어</summary>
    public static void Stop()
    {
        lock (_sync)
        {
            if (!_running)
            {
                AddLog("추적 중이 아니라 Stop 무시");
                return;
            }
            _running = false;
            _timer?.Dispose();
            _timer = null;
            _windows.Clear();
            AddLog("추적 중지 및 캐시 초기화");
            try { WindowSetChanged?.Invoke(Array.Empty<nint>()); } catch { }
        }
    }

    private static void Tick()
    {
        try
        {
            var now = DateTime.UtcNow;
            var seen = new HashSet<nint>();
            int skippedInvisible = 0;
            int addedCount = 0;
            EnumWindows((hwnd, lparam) =>
            {
                if (!IsWindowVisible(hwnd)) { skippedInvisible++; return true; }
                if (IsIconic(hwnd)) return true; // 최소화 창 제외
                if (!HasNonEmptyTitle(hwnd)) return true; // 캡션 없는 창 제외
                if (!HasUsableRect(hwnd)) return true; // zero / off-screen
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0) return true;
                var handle = (nint)hwnd;
                bool added = false;
                _windows.AddOrUpdate(handle,
                    _ =>
                    {
                        added = true;
                        return new TrackedWindow
                        {
                            Handle = handle,
                            ProcessId = (int)pid,
                            ProcessName = SafeGetProcessName(pid),
                            FirstSeen = now,
                            LastSeen = now
                        };
                    },
                    (_, existing) => { existing.LastSeen = now; return existing; });
                if (added) { addedCount++; AddLog($"추가: 0x{handle.ToInt64():X} PID={pid} {(SafeGetProcessName(pid) ?? "?")}"); }
                seen.Add(handle);
                return true;
            }, IntPtr.Zero);

            int removed = 0;
            foreach (var kv in _windows.ToArray())
            {
                if (!seen.Contains(kv.Key) || !IsWindow(kv.Key))
                {
                    if (_windows.TryRemove(kv.Key, out _))
                        removed++;
                }
            }
            AddLog($"Tick: Active={seen.Count} Added={addedCount} Removed={removed} SkipInvisible={skippedInvisible}");

            // Notify subscribers with the current set
            try { WindowSetChanged?.Invoke(seen.ToArray()); } catch { }
        }
        catch (Exception ex)
        {
            AddLog($"오류: {ex.Message}");
        }
    }

    private static bool HasNonEmptyTitle(nint hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0 || len > 512) return false; // 유한한 한도
        var sb = new StringBuilder(len + 1);
        if (GetWindowText(hwnd, sb, sb.Capacity) <= 0) return false;
        return sb.ToString().Trim().Length > 0;
    }

    private static bool HasUsableRect(nint hwnd)
    {
        RECT rc;
        if (!GetWindowRect(hwnd, out rc)) return false;
        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;
        if (w <= 0 || h <= 0) return false;
        // basic off-screen check against virtual screen
        int vx = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
        int vy = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
        int vw = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
        int vh = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
        int vRight = vx + vw;
        int vBottom = vy + vh;
        if (rc.Right <= vx || rc.Left >= vRight || rc.Bottom <= vy || rc.Top >= vBottom) return false;
        return true;
    }

    private static string? SafeGetProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; } catch { return null; }
    }

    /// <summary>외부 모듈(BorderService 등)에서 전달한 로그 라인을 추가하기 위한 래퍼.</summary>
    public static void AddExternalLog(string message) => AddLog(message);

    private static void AddLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.Enqueue(line);
        while (_logs.Count > _logCapacity && _logs.TryDequeue(out _)) { }
        // 안전 이벤트 발행: 개별 구독자 예외 격리
        try
        {
            var handlers = LogAdded;
            if (handlers != null)
            {
                foreach (var d in handlers.GetInvocationList())
                {
                    try { ((Action<string>)d).Invoke(line); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WindowTracker] LogAdded handler error: {ex}");
                    }
                }
            }
        }
        catch { }
    }

    #region Win32 P/Invoke

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    #endregion
}