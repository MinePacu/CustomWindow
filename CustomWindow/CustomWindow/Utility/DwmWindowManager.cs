using System;
using System.Runtime.InteropServices;
using Windows.UI;

namespace CustomWindow.Utility;

/// <summary>
/// DWM(Desktop Window Manager) API를 사용하여 창의 속성을 관리하는 클래스
/// </summary>
public static class DwmWindowManager
{
    // DWMWA (Desktop Window Manager Window Attributes)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    // 캡션 색상 모드 정의
    public enum CaptionMode
    {
        Light,      // 밝은 모드
        Dark,       // 어두운 모드
        Custom      // 커스텀 색상
    }

    /// <summary>
    /// 창의 캡션 모드를 설정합니다.
    /// </summary>
    public static bool SetCaptionMode(IntPtr hwnd, CaptionMode mode, Color? customCaptionColor = null, Color? customTextColor = null)
    {
        try
        {
            switch (mode)
            {
                case CaptionMode.Light:
                    // 밝은 모드: DWMWA_USE_IMMERSIVE_DARK_MODE를 FALSE로 설정
                    // 밝은 배경 + 어두운 텍스트
                    bool lightResult = SetDarkMode(hwnd, false);
                    // 밝은 모드에서는 검은색 텍스트로 대비
                    lightResult &= SetTextColor(hwnd, Color.FromArgb(255, 0, 0, 0));
                    return lightResult;

                case CaptionMode.Dark:
                    // 어두운 모드: DWMWA_USE_IMMERSIVE_DARK_MODE를 TRUE로 설정
                    // 어두운 배경 + 밝은 텍스트
                    bool darkResult = SetDarkMode(hwnd, true);
                    // 어두운 모드에서는 흰색 텍스트로 대비
                    darkResult &= SetTextColor(hwnd, Color.FromArgb(255, 255, 255, 255));
                    return darkResult;

                case CaptionMode.Custom:
                    // 커스텀 모드: 사용자 지정 색상 적용
                    bool result = true;
                    
                    if (customCaptionColor.HasValue)
                    {
                        result &= SetCaptionColor(hwnd, customCaptionColor.Value);
                        
                        // 텍스트 색상이 명시적으로 지정되지 않은 경우, 자동으로 대비되는 색상 선택
                        if (!customTextColor.HasValue)
                        {
                            var contrastColor = GetContrastColor(customCaptionColor.Value);
                            result &= SetTextColor(hwnd, contrastColor);
                            WindowTracker.AddExternalLog($"Auto-selected text color for contrast: R={contrastColor.R}, G={contrastColor.G}, B={contrastColor.B}");
                        }
                        else
                        {
                            result &= SetTextColor(hwnd, customTextColor.Value);
                        }
                    }
                    else if (customTextColor.HasValue)
                    {
                        result &= SetTextColor(hwnd, customTextColor.Value);
                    }
                    
                    return result;

                default:
                    WindowTracker.AddExternalLog($"Unknown caption mode: {mode}");
                    return false;
            }
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"SetCaptionMode failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 주어진 배경 색상에 대해 대비되는 텍스트 색상을 반환합니다.
    /// </summary>
    private static Color GetContrastColor(Color backgroundColor)
    {
        // 상대 휘도(Relative Luminance) 계산 (WCAG 2.0 기준)
        // L = 0.2126 * R + 0.7152 * G + 0.0722 * B
        double r = backgroundColor.R / 255.0;
        double g = backgroundColor.G / 255.0;
        double b = backgroundColor.B / 255.0;

        // sRGB to linear RGB 변환
        r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        // 휘도 계산
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        // 휘도가 0.5보다 크면 어두운 배경이므로 밝은 텍스트, 작으면 밝은 배경이므로 어두운 텍스트
        // 임계값을 0.5로 설정 (조정 가능)
        if (luminance > 0.5)
        {
            // 밝은 배경 -> 검은색 텍스트
            return Color.FromArgb(255, 0, 0, 0);
        }
        else
        {
            // 어두운 배경 -> 흰색 텍스트
            return Color.FromArgb(255, 255, 255, 255);
        }
    }

    /// <summary>
    /// 창의 다크 모드를 설정합니다.
    /// </summary>
    private static bool SetDarkMode(IntPtr hwnd, bool useDarkMode)
    {
        try
        {
            int value = useDarkMode ? 1 : 0;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            
            if (result != 0)
            {
                WindowTracker.AddExternalLog($"SetDarkMode failed for hwnd=0x{hwnd.ToInt64():X}, HRESULT=0x{result:X}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"SetDarkMode exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 창의 테두리 색상을 설정합니다.
    /// </summary>
    public static bool SetBorderColor(IntPtr hwnd, Color color)
    {
        try
        {
            // COLORREF 형식: 0x00BBGGRR
            int colorref = (color.B << 16) | (color.G << 8) | color.R;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorref, sizeof(int));
            
            if (result != 0)
            {
                WindowTracker.AddExternalLog($"SetBorderColor failed, HRESULT=0x{result:X}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"SetBorderColor exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 창의 캡션(제목 표시줄) 색상을 설정합니다.
    /// </summary>
    public static bool SetCaptionColor(IntPtr hwnd, Color color)
    {
        try
        {
            // COLORREF 형식: 0x00BBGGRR
            int colorref = (color.B << 16) | (color.G << 8) | color.R;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorref, sizeof(int));
            
            if (result != 0)
            {
                WindowTracker.AddExternalLog($"SetCaptionColor failed, HRESULT=0x{result:X}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"SetCaptionColor exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 창의 캡션 텍스트 색상을 설정합니다.
    /// </summary>
    public static bool SetTextColor(IntPtr hwnd, Color color)
    {
        try
        {
            // COLORREF 형식: 0x00BBGGRR
            int colorref = (color.B << 16) | (color.G << 8) | color.R;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref colorref, sizeof(int));
            
            if (result != 0)
            {
                WindowTracker.AddExternalLog($"SetTextColor failed, HRESULT=0x{result:X}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"SetTextColor exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 문자열로 지정된 캡션 모드를 열거형으로 변환합니다.
    /// </summary>
    public static CaptionMode ParseCaptionMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return CaptionMode.Light;

        return mode.Trim().ToLowerInvariant() switch
        {
            "light" or "밝게" or "라이트" => CaptionMode.Light,
            "dark" or "어둡게" or "다크" => CaptionMode.Dark,
            "custom" or "커스텀" or "사용자 지정" => CaptionMode.Custom,
            _ => CaptionMode.Light
        };
    }

    /// <summary>
    /// 현재 Windows 버전이 커스텀 캡션 색상을 지원하는지 확인합니다.
    /// </summary>
    public static bool SupportsCustomCaptionColors()
    {
        return IsWindows11OrGreater();
    }
    
    /// <summary>
    /// 창의 모서리 스타일을 설정합니다. (Windows 11+ 전용)
    /// </summary>
    /// <param name="hwnd">대상 창 핸들</param>
    /// <param name="cornerMode">모서리 모드: "기본", "둥글게 하지 않음", "둥글게", "덜 둥글게"</param>
    /// <returns>성공 여부</returns>
    public static bool SetCornerPreference(IntPtr hwnd, string? cornerMode)
    {
        // Windows 11 전용 기능
        if (!IsWindows11OrGreater())
        {
            WindowTracker.AddExternalLog($"[Win10] SetCornerPreference is only supported on Windows 11+");
            return false;
        }

        try
        {
            // DWMWCP (DWM Window Corner Preference) 값
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            const int DWMWCP_DEFAULT = 0;           // 기본 (시스템 설정 따름)
            const int DWMWCP_DONOTROUND = 1;        // 둥글게 하지 않음
            const int DWMWCP_ROUND = 2;             // 둥글게
            const int DWMWCP_ROUNDSMALL = 3;        // 덜 둥글게

            int preference = cornerMode?.Trim() switch
            {
                "둥글게 하지 않음" => DWMWCP_DONOTROUND,
                "둥글게" => DWMWCP_ROUND,
                "덜 둥글게" => DWMWCP_ROUNDSMALL,
                "기본" or null or "" => DWMWCP_DEFAULT,
                _ => DWMWCP_DEFAULT
            };

            int result = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            
            if (result != 0)
            {
                WindowTracker.AddExternalLog($"SetCornerPreference failed for hwnd=0x{hwnd.ToInt64():X}, HRESULT=0x{result:X}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"SetCornerPreference exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Windows 11 이상인지 확인 (빌드 22000 이상)
    /// </summary>
    private static bool IsWindows11OrGreater()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            // Windows 11은 빌드 22000 이상
            return version.Major >= 10 && version.Build >= 22000;
        }
        catch
        {
            return false;
        }
    }

    #region P/Invoke

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);

    #endregion
}
