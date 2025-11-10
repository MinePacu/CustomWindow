using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using CustomWindow.Utility;
using CustomWindow.ViewModels;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using System;
using System.Linq;

namespace CustomWindow.Pages
{
    public sealed partial class ColorSetting : Page
    {
        public ColorSettingsViewModel ViewModel { get; }
        
        // Windows 11 지원 여부
        public bool IsWindows11OrGreater { get; }
        public bool IsWindows10 => !IsWindows11OrGreater;
        
        public ColorSetting()
        {
            InitializeComponent();
            
            // Windows 버전 확인
            IsWindows11OrGreater = CheckWindows11();
            
            ViewModel = new ColorSettingsViewModel(App.ConfigStore!.Config);
            DataContext = ViewModel;
            
            Loaded += ColorSetting_Loaded;
        }

        private void ColorSetting_Loaded(object sender, RoutedEventArgs e)
        {
            // Windows 10에서 커스텀 캡션 색상 기능 비활성화
            if (IsWindows10)
            {
                ShowWindows10Warning();
                DisableCustomCaptionFeaturesForWindows10();
            }
        }

        private bool CheckWindows11()
        {
            try
            {
                var version = Environment.OSVersion.Version;
                // Windows 11은 빌드 22000 이상
                bool isWin11 = version.Major >= 10 && version.Build >= 22000;
                
                WindowTracker.AddExternalLog($"[ColorSetting] OS Version: {version.Major}.{version.Build} - Windows {(isWin11 ? "11+" : "10")}");
                
                return isWin11;
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[ColorSetting] Version check failed: {ex.Message}");
                return false;
            }
        }

        private void ShowWindows10Warning()
        {
            WindowTracker.AddExternalLog("[ColorSetting] Windows 10 detected - Custom caption colors disabled");
            
            // InfoBar를 동적으로 추가하여 사용자에게 알림
            var infoBar = new InfoBar
            {
                Title = "Windows 10 제한사항",
                Message = "커스텀 제목 표시줄 색상 및 텍스트 색상은 Windows 11 이상에서만 지원됩니다. Windows 10에서는 '밝게'/'어둡게' 모드만 사용할 수 있습니다.",
                Severity = InfoBarSeverity.Warning,
                IsOpen = true,
                IsClosable = true,
                Margin = new Thickness(0, 0, 0, 12)
            };
            
            Panel.Children.Insert(0, infoBar);
        }

        private void DisableCustomCaptionFeaturesForWindows10()
        {
            try
            {
                // 1. 캡션 색상 Expander 비활성화
                if (CaptionColorExpander != null)
                {
                    CaptionColorExpander.IsEnabled = false;
                    CaptionColorExpander.Description = "Windows 11+ 전용 - Windows 10에서는 지원되지 않습니다";
                }

                // 2. 캡션 텍스트 색상 Expander 비활성화
                if (CaptionTextColorExpander != null)
                {
                    CaptionTextColorExpander.IsEnabled = false;
                    CaptionTextColorExpander.Description = "Windows 11+ 전용 - Windows 10에서는 지원되지 않습니다";
                }

                // 3. ComboBox에서 "커스텀" 항목 제거
                if (CaptionModeComboBox != null && CaptionModeComboBox.Items.Count > 2)
                {
                    var customItem = CaptionModeComboBox.Items.OfType<string>().FirstOrDefault(s => s == "커스텀");
                    if (customItem != null)
                    {
                        CaptionModeComboBox.Items.Remove(customItem);
                        WindowTracker.AddExternalLog("[ColorSetting] Removed 'Custom' option from ComboBox for Windows 10");
                    }
                }

                // 4. ViewModel에서 Custom 모드 사용 중이면 Dark로 전환
                if (ViewModel.CaptionColorMode == "커스텀")
                {
                    ViewModel.CaptionColorMode = "어둡게";
                    WindowTracker.AddExternalLog("[ColorSetting] Changed from Custom to Dark mode for Windows 10 compatibility");
                }

                WindowTracker.AddExternalLog("[ColorSetting] Custom caption color features disabled for Windows 10");
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[ColorSetting] Error disabling custom features: {ex.Message}");
            }
        }

        /// <summary>
        /// 프리셋 색상 버튼 클릭 이벤트 핸들러
        /// </summary>
        private void PresetColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string hexColor)
                {
                    // HEX 문자열을 Color로 변환
                    var color = ParseHexColor(hexColor);
                    if (color.HasValue)
                    {
                        ViewModel.BorderColor = color.Value;
                        WindowTracker.AddExternalLog($"[ColorSetting] Preset color applied: {hexColor}");
                    }
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[ColorSetting] Preset color error: {ex.Message}");
            }
        }

        /// <summary>
        /// HEX 색상 복사 버튼 클릭 이벤트 핸들러
        /// </summary>
        private void CopyBorderHexColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hexValue = ViewModel.BorderColorHex;
                if (!string.IsNullOrEmpty(hexValue))
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(hexValue);
                    Clipboard.SetContent(dataPackage);
                    
                    WindowTracker.AddExternalLog($"[ColorSetting] Copied to clipboard: {hexValue}");
                    
                    // 사용자에게 복사 완료 알림 (간단한 ToolTip 변경)
                    if (sender is Button btn)
                    {
                        var originalContent = btn.Content;
                        btn.Content = "? 복사됨!";
                        
                        // 2초 후 원래 텍스트로 복원
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                        timer.Tick += (s, args) =>
                        {
                            btn.Content = originalContent;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[ColorSetting] Copy hex color error: {ex.Message}");
            }
        }

        /// <summary>
        /// HEX 문자열을 Color로 변환
        /// </summary>
        private Color? ParseHexColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                
                if (hex.Length == 6)
                {
                    // RGB 형식
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return Color.FromArgb(255, r, g, b);
                }
                else if (hex.Length == 8)
                {
                    // ARGB 형식
                    byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[ColorSetting] Parse hex color error: {ex.Message}");
            }
            
            return null;
        }

        private static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        private void BorderColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            var auto = App.ConfigStore!.Config.AutoWindowChange;
            var running = BorderService.IsRunning;
            var hex = ToHex(args.NewColor);

            WindowTracker.AddExternalLog($"[ColorSetting] Color changed -> {hex} | Auto={auto} Running={running}");

            if (auto && running)
            {
                BorderService.UpdateColor(hex);
                WindowTracker.AddExternalLog($"[ColorSetting] Sent UpdateColor({hex})");
            }
            else if (!auto)
            {
                WindowTracker.AddExternalLog("[ColorSetting] AutoWindowChange=Off, pending until enabled");
            }
            else if (!running)
            {
                WindowTracker.AddExternalLog("[ColorSetting] BorderService not running, start AutoWindowChange first");
            }
        }

        private void BorderThicknessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var auto = App.ConfigStore!.Config.AutoWindowChange;
            var running = BorderService.IsRunning;
            var th = (int)e.NewValue;

            WindowTracker.AddExternalLog($"[ColorSetting] Thickness changed -> {th} | Auto={auto} Running={running}");

            if (auto && running)
            {
                BorderService.UpdateThickness(th);
                WindowTracker.AddExternalLog($"[ColorSetting] Sent UpdateThickness({th})");
            }
            else if (!auto)
            {
                WindowTracker.AddExternalLog("[ColorSetting] AutoWindowChange=Off, pending until enabled");
            }
            else if (!running)
            {
                WindowTracker.AddExternalLog("[ColorSetting] BorderService not running, start AutoWindowChange first");
            }
        }

        // 데모 토글 메서드 (기존 ColorSetting.xaml.cs에서 유지)
        private void borderColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleBorderDemo();
        private void captIonColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleCaptionDemo();
        private void captIonTextColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleCaptionTextDemo();
    }
}
