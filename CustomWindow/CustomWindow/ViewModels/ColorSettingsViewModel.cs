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
                    UpdateBorderServiceColor();
                    break;
                case nameof(ObservableConfig.CaptionColor): 
                    OnPropertyChanged(nameof(CaptionColor)); 
                    OnPropertyChanged(nameof(CaptionBrush)); 
                    break;
                case nameof(ObservableConfig.CaptionTextColor): 
                    OnPropertyChanged(nameof(CaptionTextColor)); 
                    OnPropertyChanged(nameof(CaptionTextBrush)); 
                    break;
                case nameof(ObservableConfig.BorderThickness):
                    OnPropertyChanged(nameof(BorderThickness));
                    UpdateBorderServiceThickness();
                    break;
            }
        };
    }

    private void UpdateBorderServiceColor()
    {
        if (_config.AutoWindowChange && BorderServiceHost.IsRunning)
        {
            var borderHex = _config.BorderColor ?? "#0078FF";
            BorderServiceHost.UpdateColor(borderHex);
        }
    }

    private void UpdateBorderServiceThickness()
    {
        if (_config.AutoWindowChange && BorderServiceHost.IsRunning)
        {
            BorderServiceHost.UpdateThickness(_config.BorderThickness);
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
    public string? CaptionTextColorMode { get => _config.CaptionTextColorMode; set { _config.CaptionTextColorMode = value; OnPropertyChanged(); } }
    public string? CaptionColorMode { get => _config.CaptionColorMode; set { _config.CaptionColorMode = value; OnPropertyChanged(); } }

    public void ToggleBorderDemo() => BorderColor = BorderColor.R == 0x40 ? Color.FromArgb(0xFF, 0xFF, 0x80, 0x00) : Color.FromArgb(0xFF, 0x40, 0x80, 0xFF);
    public void ToggleCaptionDemo() => CaptionColor = CaptionColor.R == 0x20 ? Color.FromArgb(0xFF, 0x30, 0x30, 0x30) : Color.FromArgb(0xFF, 0x20, 0x20, 0x20);
    public void ToggleCaptionTextDemo() => CaptionTextColor = CaptionTextColor.R == 0xFF && CaptionTextColor.G == 0xFF && CaptionTextColor.B == 0xFF ? Color.FromArgb(0xFF, 0, 0, 0) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
}
