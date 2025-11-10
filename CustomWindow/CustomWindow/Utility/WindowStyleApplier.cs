using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Windows.UI;

namespace CustomWindow.Utility;

/// <summary>
/// 창에 스타일(색상, 모서리 등)을 적용하는 클래스
/// </summary>
public static class WindowStyleApplier
{
    private static ObservableConfig? _config;
    private static bool _isEnabled;
    
    // 캡션 색상 적용 제외 대상 프로세스 목록
    private static readonly HashSet<string> _captionColorExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer" // 파일 탐색기 (탭 배경이 캡션으로 인식되는 문제 방지)
    };

    /// <summary>
    /// WindowStyleApplier를 초기화하고 시작합니다.
    /// </summary>
    public static void Initialize(ObservableConfig config)
    {
        _config = config;
        
        // WindowTracker의 WindowSetChanged 이벤트 구독
        WindowTracker.WindowSetChanged += OnWindowSetChanged;
        
        _isEnabled = true;
        WindowTracker.AddExternalLog("WindowStyleApplier initialized");
    }

    /// <summary>
    /// WindowStyleApplier를 중지합니다.
    /// </summary>
    public static void Stop()
    {
        WindowTracker.WindowSetChanged -= OnWindowSetChanged;
        _isEnabled = false;
        WindowTracker.AddExternalLog("WindowStyleApplier stopped");
    }

    /// <summary>
    /// 창 목록이 변경되었을 때 호출되는 핸들러
    /// </summary>
    private static void OnWindowSetChanged(IReadOnlyCollection<nint> handles)
    {
        if (!_isEnabled || _config == null)
            return;

        try
        {
            foreach (var handle in handles)
            {
                ApplyStylesToWindow(handle);
            }
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"OnWindowSetChanged error: {ex.Message}");
        }
    }

    /// <summary>
    /// 특정 창에 스타일을 적용합니다.
    /// </summary>
    public static void ApplyStylesToWindow(nint handle)
    {
        if (_config == null)
            return;

        try
        {
            var hwnd = (IntPtr)handle;

            // 1. 캡션 색상 모드 적용
            ApplyCaptionMode(hwnd);

            // 2. 캡션 텍스트 색상 모드 적용
            ApplyCaptionTextMode(hwnd);
            
            // 3. 창 모서리 스타일 적용 (Windows 11+)
            ApplyCornerMode(hwnd);

            // 참고: 테두리 색상은 BorderService에서 처리하므로 여기서는 제외
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"ApplyStylesToWindow failed for hwnd=0x{handle.ToInt64():X}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 창의 프로세스 이름을 가져옵니다.
    /// </summary>
    private static string? GetProcessNameForWindow(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0) return null;
            
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process?.ProcessName;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// 창이 캡션 색상 적용 제외 대상인지 확인합니다.
    /// </summary>
    private static bool IsCaptionColorExcluded(IntPtr hwnd)
    {
        var processName = GetProcessNameForWindow(hwnd);
        if (string.IsNullOrEmpty(processName))
            return false;
            
        return _captionColorExcludedProcesses.Contains(processName);
    }
    
    /// <summary>
    /// 창 모서리 스타일을 적용합니다. (Windows 11+ 전용)
    /// </summary>
    private static void ApplyCornerMode(IntPtr hwnd)
    {
        if (_config == null)
            return;
            
        // Windows 11 이상에서만 작동
        if (!DwmWindowManager.SupportsCustomCaptionColors())
            return;
            
        try
        {
            var cornerMode = _config.WindowCornerMode;
            if (DwmWindowManager.SetCornerPreference(hwnd, cornerMode))
            {
                WindowTracker.AddExternalLog($"Applied corner style '{cornerMode ?? "기본"}' to window 0x{hwnd.ToInt64():X}");
            }
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"ApplyCornerMode failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 캡션 색상 모드를 적용합니다.
    /// </summary>
    private static void ApplyCaptionMode(IntPtr hwnd)
    {
        if (_config == null)
            return;
        
        // 파일 탐색기 등 제외 대상인 경우 캡션 색상 적용 건너뛰기
        if (IsCaptionColorExcluded(hwnd))
        {
            WindowTracker.AddExternalLog($"Skipped caption color for excluded process (hwnd=0x{hwnd.ToInt64():X})");
            return;
        }

        var mode = DwmWindowManager.ParseCaptionMode(_config.CaptionColorMode);
        Color? customCaptionColor = null;
        Color? customTextColor = null;

        if (mode == DwmWindowManager.CaptionMode.Custom)
        {
            // 커스텀 모드: ColorSettingsViewModel에서 캡션 색상 가져오기
            customCaptionColor = HexToColor(_config.CaptionColor, Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
            
            // 캡션 텍스트 색상 모드가 커스텀이라면 해당 색상 사용, 아니면 자동 대비 색상
            var textMode = DwmWindowManager.ParseCaptionMode(_config.CaptionTextColorMode);
            if (textMode == DwmWindowManager.CaptionMode.Custom)
            {
                customTextColor = HexToColor(_config.CaptionTextColor, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            }
            // 텍스트 모드가 커스텀이 아니면 null로 두어 자동 대비 색상이 적용되도록 함
        }

        // 캡션 모드 설정 시 텍스트 색상도 함께 설정됨 (대비 보장)
        DwmWindowManager.SetCaptionMode(hwnd, mode, customCaptionColor, customTextColor);
    }

    /// <summary>
    /// 캡션 텍스트 색상 모드를 적용합니다.
    /// </summary>
    private static void ApplyCaptionTextMode(IntPtr hwnd)
    {
        if (_config == null)
            return;
        
        // 파일 탐색기 등 제외 대상인 경우 건너뛰기
        if (IsCaptionColorExcluded(hwnd))
            return;

        // 캡션 색상 모드가 커스텀이라면 텍스트 색상은 ApplyCaptionMode에서 이미 처리됨
        var captionMode = DwmWindowManager.ParseCaptionMode(_config.CaptionColorMode);
        if (captionMode == DwmWindowManager.CaptionMode.Custom)
        {
            // 커스텀 모드에서는 ApplyCaptionMode에서 이미 처리됨
            return;
        }

        var textMode = DwmWindowManager.ParseCaptionMode(_config.CaptionTextColorMode);

        if (textMode == DwmWindowManager.CaptionMode.Custom)
        {
            // 커스텀 텍스트 색상 적용
            var customColor = HexToColor(_config.CaptionTextColor, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            DwmWindowManager.SetTextColor(hwnd, customColor);
            WindowTracker.AddExternalLog($"Applied custom text color: R={customColor.R}, G={customColor.G}, B={customColor.B}");
        }
        // Light/Dark 모드는 SetCaptionMode에서 이미 처리되므로 여기서는 제외
    }

    /// <summary>
    /// Hex 색상 문자열을 Color로 변환합니다.
    /// </summary>
    private static Color HexToColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        hex = hex.TrimStart('#');

        try
        {
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(255, r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch { }

        return fallback;
    }

    /// <summary>
    /// 설정이 변경되었을 때 모든 창에 스타일을 다시 적용합니다.
    /// </summary>
    public static void RefreshAllWindows()
    {
        if (!_isEnabled)
            return;

        try
        {
            var handles = WindowTracker.CurrentWindowHandles;
            foreach (var handle in handles)
            {
                ApplyStylesToWindow(handle);
            }
            
            WindowTracker.AddExternalLog($"Refreshed styles for {handles.Count} windows");
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"RefreshAllWindows error: {ex.Message}");
        }
    }
    
    #region Win32 API
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    
    #endregion
}
