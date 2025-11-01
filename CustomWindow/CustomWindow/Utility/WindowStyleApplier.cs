using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace CustomWindow.Utility;

/// <summary>
/// 창에 스타일(색상, 모서리 등)을 적용하는 클래스
/// </summary>
public static class WindowStyleApplier
{
    private static ObservableConfig? _config;
    private static bool _isEnabled;

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

            // 참고: 테두리 색상은 BorderService에서 처리하므로 여기서는 제외
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"ApplyStylesToWindow failed for hwnd=0x{handle.ToInt64():X}: {ex.Message}");
        }
    }

    /// <summary>
    /// 캡션 색상 모드를 적용합니다.
    /// </summary>
    private static void ApplyCaptionMode(IntPtr hwnd)
    {
        if (_config == null)
            return;

        var mode = DwmWindowManager.ParseCaptionMode(_config.CaptionColorMode);
        Color? customCaptionColor = null;
        Color? customTextColor = null;

        if (mode == DwmWindowManager.CaptionMode.Custom)
        {
            // 커스텀 모드: ColorSettingsViewModel에서 캡션 색상 가져오기
            customCaptionColor = HexToColor(_config.CaptionColor, Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
            
            // 캡션 텍스트 색상 모드가 커스텀이면 해당 색상 사용, 아니면 자동 대비 색상
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

        // 캡션 색상 모드가 커스텀이 아닌 경우에만 텍스트 색상 독립 적용
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
}
