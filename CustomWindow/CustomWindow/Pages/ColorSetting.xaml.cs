using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using CustomWindow.Utility;
using CustomWindow.ViewModels;
using Windows.UI;

namespace CustomWindow.Pages
{
    public sealed partial class ColorSetting : Page
    {
        public ColorSettingsViewModel ViewModel { get; }
        
        public ColorSetting()
        {
            InitializeComponent();
            ViewModel = new ColorSettingsViewModel(App.ConfigStore!.Config);
            DataContext = ViewModel;
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

        // 데모 토글 메서드들 (기존 ColorSetting.xaml.cs에서 유지)
        private void borderColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleBorderDemo();
        private void captIonColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleCaptionDemo();
        private void captIonTextColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleCaptionTextDemo();
    }
}
