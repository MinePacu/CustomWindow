using CommunityToolkit.Mvvm.ComponentModel;
using CustomWindow.Utility;
using Windows.UI;
using Microsoft.UI.Xaml.Media;

namespace CustomWindow.ViewModels;

public partial class ColorSettingsViewModel : ObservableObject
{
    private readonly ObservableConfig _config;
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
            // 두께 값 검증: 1-20 범위 제한
            if (value < 1)
            {
                WindowTracker.AddExternalLog($"[ColorSettingsViewModel] Invalid thickness value ignored: {value} (must be >= 1)");
                return;
            }
            if (value > 20)
            {
                WindowTracker.AddExternalLog($"[ColorSettingsViewModel] Thickness value clamped: {value} -> 20");
                value = 20;
            }
            
            if (_config.BorderThickness == value) return;
            
            WindowTracker.AddExternalLog($"[ColorSettingsViewModel] BorderThickness changing: {_config.BorderThickness} -> {value}");
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
}
