using CommunityToolkit.Mvvm.ComponentModel;
using CustomWindow.Utility;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace CustomWindow.ViewModels;

public partial class ColorSettingsViewModel : ObservableObject
{
    private readonly ObservableConfig _config;
    private int _demoLogCounter = 0;
    
    public ColorSettingsViewModel(ObservableConfig cfg)
    {
        _config = cfg;
        _config.Changed += (_, name) =>
        {
            switch (name)
            {
                case nameof(ObservableConfig.BorderColor): 
                    OnPropertyChanged(nameof(BorderColor)); 
                    OnPropertyChanged(nameof(BorderBrush)); 
                    OnPropertyChanged(nameof(BorderColorHex));
                    UpdateBorderServiceColor();
                    break;
                case nameof(ObservableConfig.CaptionColor): 
                    OnPropertyChanged(nameof(CaptionColor)); 
                    OnPropertyChanged(nameof(CaptionBrush));
                    // 캡션 색상이 변경되면 모든 창에 스타일 재적용
                    if (_config.CaptionColorMode == "커스텀" || _config.CaptionColorMode == "Custom")
                    {
                        WindowStyleApplier.RefreshAllWindows();
                    }
                    break;
                case nameof(ObservableConfig.CaptionTextColor): 
                    OnPropertyChanged(nameof(CaptionTextColor)); 
                    OnPropertyChanged(nameof(CaptionTextBrush));
                    // 캡션 텍스트 색상이 변경되면 모든 창에 스타일 재적용
                    if (_config.CaptionTextColorMode == "커스텀" || _config.CaptionTextColorMode == "Custom")
                    {
                        WindowStyleApplier.RefreshAllWindows();
                    }
                    break;
                case nameof(ObservableConfig.BorderThickness):
                    OnPropertyChanged(nameof(BorderThickness));
                    UpdateBorderServiceThickness();
                    break;
                case nameof(ObservableConfig.CaptionColorMode):
                    OnPropertyChanged(nameof(CaptionColorMode));
                    // 캡션 색상 모드가 변경되면 모든 창에 스타일 재적용
                    WindowStyleApplier.RefreshAllWindows();
                    break;
                case nameof(ObservableConfig.CaptionTextColorMode):
                    OnPropertyChanged(nameof(CaptionTextColorMode));
                    // 캡션 텍스트 색상 모드가 변경되면 모든 창에 스타일 재적용
                    WindowStyleApplier.RefreshAllWindows();
                    break;
            }
        };
    }

    private void UpdateBorderServiceColor()
    {
        if (_config.AutoWindowChange && BorderService.IsRunning)
        {
            var borderHex = _config.BorderColor ?? "#0078FF";
            BorderService.UpdateColor(borderHex);
        }
    }

    private void UpdateBorderServiceThickness()
    {
        if (_config.AutoWindowChange && BorderService.IsRunning)
        {
            BorderService.UpdateThickness(_config.BorderThickness);
        }
    }

    private static Color HexToColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
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

    private static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    // Only update when the value actually changes. Rely on _config.Changed event to raise property change notifications.
    public Color BorderColor
    {
        get => HexToColor(_config.BorderColor, Color.FromArgb(0xFF, 0x40, 0x80, 0xFF));
        set
        {
            var hex = ColorToHex(value);
            if (_config.BorderColor == hex) return; // no change => avoid notification loop
            _config.BorderColor = hex; // triggers _config.Changed which raises BorderColor & BorderBrush
        }
    }

    public string BorderColorHex => _config.BorderColor ?? "#FF4080FF";

    public Color CaptionColor
    {
        get => HexToColor(_config.CaptionColor, Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
        set
        {
            var hex = ColorToHex(value);
            if (_config.CaptionColor == hex) return;
            _config.CaptionColor = hex;
        }
    }

    public Color CaptionTextColor
    {
        get => HexToColor(_config.CaptionTextColor, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        set
        {
            var hex = ColorToHex(value);
            if (_config.CaptionTextColor == hex) return;
            _config.CaptionTextColor = hex;
        }
    }

    public SolidColorBrush BorderBrush => new(BorderColor);
    public SolidColorBrush CaptionBrush => new(CaptionColor);
    public SolidColorBrush CaptionTextBrush => new(CaptionTextColor);

    public int BorderThickness 
    { 
        get => _config.BorderThickness; 
        set 
        { 
            if (_config.BorderThickness == value) return;
            _config.BorderThickness = value; 
            OnPropertyChanged(); 
        } 
    }

    public bool UseBorderSystemColor { get => _config.UseBorderSystemColor; set { _config.UseBorderSystemColor = value; OnPropertyChanged(); } }
    public bool UseBorderTransparency { get => _config.UseBorderTransparency; set { _config.UseBorderTransparency = value; OnPropertyChanged(); } }
    public bool UseCaptionSystemColor { get => _config.UseCaptionSystemColor; set { _config.UseCaptionSystemColor = value; OnPropertyChanged(); } }
    public bool UseCaptionTransparency { get => _config.UseCaptionTransparency; set { _config.UseCaptionTransparency = value; OnPropertyChanged(); } }
    public bool UseCaptionTextSystemColor { get => _config.UseCaptionTextSystemColor; set { _config.UseCaptionTextSystemColor = value; OnPropertyChanged(); } }
    public bool UseCaptionTextTransparency { get => _config.UseCaptionTextTransparency; set { _config.UseCaptionTextTransparency = value; OnPropertyChanged(); } }
    
    // Fix: Remove OnPropertyChanged() from setters - ObservableConfig already handles notifications via Changed event
    public string? CaptionTextColorMode { get => _config.CaptionTextColorMode; set => _config.CaptionTextColorMode = value; }
    public string? CaptionColorMode { get => _config.CaptionColorMode; set => _config.CaptionColorMode = value; }

    public void ToggleBorderDemo() => BorderColor = BorderColor.R == 0x40 ? Color.FromArgb(0xFF, 0xFF, 0x80, 0x00) : Color.FromArgb(0xFF, 0x40, 0x80, 0xFF);
    public void ToggleCaptionDemo() => CaptionColor = CaptionColor.R == 0x20 ? Color.FromArgb(0xFF, 0x30, 0x30, 0x30) : Color.FromArgb(0xFF, 0x20, 0x20, 0x20);
    public void ToggleCaptionTextDemo() => CaptionTextColor = CaptionTextColor.R == 0xFF && CaptionTextColor.G == 0xFF && CaptionTextColor.B == 0xFF ? Color.FromArgb(0xFF, 0, 0, 0) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// 데모 시연용 로그 생성 - 다양한 시나리오를 보여줌
    /// </summary>
    public async void GenerateDemoLogs()
    {
        try
        {
            _demoLogCounter++;
            
            WindowTracker.AddExternalLog("=".PadRight(60, '='));
            WindowTracker.AddExternalLog($"[DEMO #{_demoLogCounter}] 데모 시연 시작 - 시스템 동작 예시");
            WindowTracker.AddExternalLog("=".PadRight(60, '='));
            
            await Task.Delay(500);
            
            // 1. 서비스 시작 시뮬레이션
            WindowTracker.AddExternalLog("[BorderService] 테두리 서비스 초기화 중...");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[BorderService] Render mode: Auto (Windows 11 → DWM)");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[BorderService] Border color: #FF0078D4, Thickness: 3px");
            await Task.Delay(500);
            
            // 2. 창 감지 시뮬레이션
            WindowTracker.AddExternalLog("[WindowTracker] 새 창 감지: Microsoft Edge (HWND: 0x00120A4C)");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[WindowTracker] 새 창 감지: Visual Studio Code (HWND: 0x00230F18)");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[WindowTracker] 새 창 감지: Windows Terminal (HWND: 0x001A0D3E)");
            await Task.Delay(500);
            
            // 3. 테두리 적용 시뮬레이션
            WindowTracker.AddExternalLog("[DWM] Applied border to window 0x00120A4C (Microsoft Edge)");
            await Task.Delay(200);
            WindowTracker.AddExternalLog("[DWM] Applied border to window 0x00230F18 (Visual Studio Code)");
            await Task.Delay(200);
            WindowTracker.AddExternalLog("[DWM] Applied border to window 0x001A0D3E (Windows Terminal)");
            await Task.Delay(500);
            
            // 4. 포그라운드 모드 변경 시뮬레이션
            WindowTracker.AddExternalLog("[Config] ForegroundWindowOnly changed: False → True");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[DWM] Resetting all DWM attributes due to foreground mode change");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[DWM] Reset window 0x00120A4C");
            WindowTracker.AddExternalLog("[DWM] Reset window 0x00230F18");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[DWM] Foreground window detected: 0x001A0D3E (Windows Terminal)");
            await Task.Delay(200);
            WindowTracker.AddExternalLog("[DWM] Applied border to foreground window only");
            await Task.Delay(500);
            
            // 5. 색상 변경 시뮬레이션
            WindowTracker.AddExternalLog("[ColorSetting] Color changed -> #FFE81123 | Auto=True Running=True");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[ColorSetting] Sent UpdateColor(#FFE81123)");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[BorderService] Color update received: #FFE81123");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[DWM] Applied borders to 1 windows, total tracked: 1");
            await Task.Delay(500);
            
            // 6. 두께 변경 시뮬레이션
            WindowTracker.AddExternalLog("[ColorSetting] Thickness changed -> 5 | Auto=True Running=True");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[ColorSetting] Sent UpdateThickness(5)");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[BorderService] Thickness update received: 5px");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[DWM] Border thickness updated for all tracked windows");
            await Task.Delay(500);
            
            // 7. 창 닫힘 감지 시뮬레이션
            WindowTracker.AddExternalLog("[WindowTracker] Window closed: 0x00120A4C (Microsoft Edge)");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[DWM] Restored default border for window 0x00120A4C");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[WindowTracker] Removed from tracking list");
            await Task.Delay(500);
            
            // 8. 렌더 모드 변경 시뮬레이션
            WindowTracker.AddExternalLog("[NormalSetting] Render mode changed to: DComp");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[BorderService] Switching render mode: DWM → DComp");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[BorderService] Creating DirectComposition overlay...");
            await Task.Delay(400);
            WindowTracker.AddExternalLog("[Overlay] D3D11 device created successfully");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[Overlay] Direct2D device context initialized");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[Overlay] DirectComposition visual tree created");
            await Task.Delay(500);
            
            // 9. 통계 정보
            WindowTracker.AddExternalLog("[Stats] Current status:");
            WindowTracker.AddExternalLog("[Stats]   - Tracked windows: 2");
            WindowTracker.AddExternalLog("[Stats]   - Borders applied: 2");
            WindowTracker.AddExternalLog("[Stats]   - Render mode: DComp");
            WindowTracker.AddExternalLog("[Stats]   - Foreground only: True");
            WindowTracker.AddExternalLog("[Stats]   - CPU usage: 0.3%");
            WindowTracker.AddExternalLog("[Stats]   - Memory usage: 38.5 MB");
            await Task.Delay(500);
            
            // 10. 오류 시뮬레이션 (가끔씩만)
            if (_demoLogCounter % 3 == 0)
            {
                WindowTracker.AddExternalLog("[WARNING] Window 0x002B0C1A is cloaked, skipping border application");
                await Task.Delay(300);
                WindowTracker.AddExternalLog("[INFO] Cloaked windows are typically minimized or hidden windows");
                await Task.Delay(500);
            }
            
            // 11. 성공 완료
            WindowTracker.AddExternalLog("=".PadRight(60, '='));
            WindowTracker.AddExternalLog($"[DEMO #{_demoLogCounter}] 데모 시연 완료 ?");
            WindowTracker.AddExternalLog($"[DEMO] 총 {_demoLogCounter}번의 데모가 실행되었습니다.");
            WindowTracker.AddExternalLog("=".PadRight(60, '='));
            
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"[ERROR] Demo log generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 빠른 데모 로그 - 간단한 버전
    /// </summary>
    public void GenerateQuickDemoLogs()
    {
        try
        {
            WindowTracker.AddExternalLog("--- Quick Demo Start ---");
            WindowTracker.AddExternalLog("[BorderService] Service started successfully");
            WindowTracker.AddExternalLog("[WindowTracker] Detected 5 windows");
            WindowTracker.AddExternalLog("[DWM] Applied borders to 5 windows");
            WindowTracker.AddExternalLog("[ColorSetting] Color: #FF0078D4, Thickness: 3px");
            WindowTracker.AddExternalLog("[Stats] All systems operational ?");
            WindowTracker.AddExternalLog("--- Quick Demo End ---");
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"[ERROR] Quick demo failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 오류 시뮬레이션 데모 로그
    /// </summary>
    public async void GenerateErrorDemoLogs()
    {
        try
        {
            WindowTracker.AddExternalLog("=== Error Scenario Demo ===");
            await Task.Delay(300);
            
            WindowTracker.AddExternalLog("[BorderService] Starting service...");
            await Task.Delay(500);
            
            WindowTracker.AddExternalLog("[ERROR] Failed to initialize DWM: Access Denied");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[INFO] This usually happens when running without admin privileges");
            await Task.Delay(300);
            WindowTracker.AddExternalLog("[BorderService] Falling back to DComp mode...");
            await Task.Delay(500);
            
            WindowTracker.AddExternalLog("[Overlay] Creating DirectComposition overlay...");
            await Task.Delay(400);
            WindowTracker.AddExternalLog("[Overlay] Overlay created successfully ?");
            await Task.Delay(300);
            
            WindowTracker.AddExternalLog("[INFO] Service recovered and running in compatibility mode");
            WindowTracker.AddExternalLog("=== Error Recovery Complete ===");
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"[ERROR] Error demo failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 실시간 모니터링 시뮬레이션 데모 로그
    /// </summary>
    public async void GenerateMonitoringDemoLogs()
    {
        try
        {
            WindowTracker.AddExternalLog("=== Real-time Monitoring Demo ===");
            
            for (int i = 0; i < 10; i++)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var windowCount = 3 + (i % 3);
                var cpuUsage = 0.2 + (i * 0.1);
                var memUsage = 35.0 + (i * 2.5);
                
                WindowTracker.AddExternalLog($"[{timestamp}] Windows: {windowCount} | CPU: {cpuUsage:F1}% | Memory: {memUsage:F1}MB");
                
                if (i == 5)
                {
                    WindowTracker.AddExternalLog($"[{timestamp}] [INFO] New window detected: Notepad++");
                }
                
                if (i == 7)
                {
                    WindowTracker.AddExternalLog($"[{timestamp}] [INFO] Window closed: Calculator");
                }
                
                await Task.Delay(800);
            }
            
            WindowTracker.AddExternalLog("=== Monitoring Demo Complete ===");
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"[ERROR] Monitoring demo failed: {ex.Message}");
        }
    }
}
