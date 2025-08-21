using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CustomWindow.Utility;

/// <summary>
/// AutoWindowChange 가 켜져 있는 동안 현재 사용자에게 보이는 top-level window 핸들을 주기적으로 수집하고,
/// 사라진 창은 목록에서 제거하여 메모리 누수를 방지하는 경량 추적기.
/// 로그 기능을 제공하여 상태 변화를 확인할 수 있음.
/// </summary>
public static class WindowTracker
{
    private static readonly ConcurrentDictionary<nint, TrackedWindow> _windows = new();
    private static Timer? _timer;
    private static readonly object _sync = new();
    private static bool _running;
    private static TimeSpan _interval = TimeSpan.FromMilliseconds(1000); // 기본 1초 주기

    // 로그 저장 (고정 길이)
    private static readonly int _logCapacity = 500;
    private static readonly ConcurrentQueue<string> _logs = new();
    public static event Action<string>? LogAdded;

    private class TrackedWindow
    {
        public nint Handle { get; init; }
        public int ProcessId { get; init; }
        public string? ProcessName { get; init; }
        public DateTime FirstSeen { get; init; } = DateTime.UtcNow;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    /// <summary>현재 추적 중인 윈도우 핸들 스냅샷.</summary>
    public static IReadOnlyCollection<nint> CurrentWindowHandles => _windows.Keys.ToList();

    /// <summary>최근 로그 전체 반환 (최신순)</summary>
    public static IReadOnlyList<string> GetRecentLogs() => _logs.Reverse().ToList();

    /// <summary>추적 시작 (이미 실행 중이면 무시)</summary>
    public static void Start(TimeSpan? interval = null)
    {
        lock (_sync)
        {
            if (_running)
            {
                AddLog("이미 실행 중이어서 Start 무시");
                return;
            }
            if (interval != null) _interval = interval.Value;
            _running = true;
            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, _interval);
            AddLog($"추적 시작 (주기={_interval.TotalMilliseconds}ms)");
        }
    }

    /// <summary>추적 종료 및 모든 캐시 정리</summary>
    public static void Stop()
    {
        lock (_sync)
        {
            if (!_running)
            {
                AddLog("실행 중이 아니어서 Stop 무시");
                return;
            }
            _running = false;
            _timer?.Dispose();
            _timer = null;
            _windows.Clear();
            AddLog("추적 종료 및 목록 초기화");
        }
    }

    private static void Tick()
    {
        try
        {
            var now = DateTime.UtcNow;
            var seen = new HashSet<nint>();
            EnumWindows((hwnd, lparam) =>
            {
                if (!IsWindowVisible(hwnd)) return true; // continue
                if (IsIconic(hwnd)) return true; // 최소화 창 제외
                if (!HasNonEmptyTitle(hwnd)) return true; // 제목 없는 창 제외
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
                if (added)
                {
                    AddLog($"추가: 0x{handle.ToInt64():X} PID={pid} {(SafeGetProcessName(pid) ?? "?")}");
                }
                seen.Add(handle);
                return true;
            }, 0);

            int removed = 0;
            foreach (var kv in _windows.ToArray())
            {
                if (!seen.Contains(kv.Key) || !IsWindow(kv.Key))
                {
                    if (_windows.TryRemove(kv.Key, out _))
                        removed++;
                }
            }
            AddLog($"Tick: Active={seen.Count} Removed={removed}");
        }
        catch (Exception ex)
        {
            AddLog($"오류: {ex.Message}");
        }
    }

    private static bool HasNonEmptyTitle(nint hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0 || len > 512) return false; // 방어적 한도
        var sb = new StringBuilder(len + 1);
        if (GetWindowText(hwnd, sb, sb.Capacity) <= 0) return false;
        return sb.ToString().Trim().Length > 0;
    }

    private static string? SafeGetProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; } catch { return null; }
    }

    /// <summary>외부 모듈(BorderService 등)에서 공용 로그 스트림에 추가하기 위한 헬퍼.</summary>
    public static void AddExternalLog(string message) => AddLog(message);

    private static void AddLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.Enqueue(line);
        while (_logs.Count > _logCapacity && _logs.TryDequeue(out _)) { }
        LogAdded?.Invoke(line);
    }

    #region Win32 P/Invoke

    private delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, int lParam);

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

    #endregion
}