using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using CustomWindow.ViewModels;
using CustomWindow.Utility;
using Windows.UI;

namespace CustomWindow.Pages
{
    public sealed partial class ColorSettings : Page
    {
        public ColorSettingsViewModel ViewModel { get; }
        public ColorSettings()
        {
            this.InitializeComponent();
            ViewModel = new ColorSettingsViewModel(App.ConfigStore!.Config);
            DataContext = ViewModel;
        }

        private static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        private void BorderColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            var auto = App.ConfigStore!.Config.AutoWindowChange;
            var running = BorderService.IsRunning;
            var hex = ToHex(args.NewColor);

            WindowTracker.AddExternalLog($"[ColorSettings] Color changed -> {hex} | Auto={auto} Running={running}");

            if (auto && running)
            {
                BorderService.UpdateColor(hex);
                WindowTracker.AddExternalLog($"[ColorSettings] Sent UpdateColor({hex})");
            }
            else if (!auto)
            {
                WindowTracker.AddExternalLog("[ColorSettings] AutoWindowChange=Off, pending until enabled");
            }
            else if (!running)
            {
                WindowTracker.AddExternalLog("[ColorSettings] BorderService not running, start AutoWindowChange first");
            }
        }

        private void BorderThicknessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var auto = App.ConfigStore!.Config.AutoWindowChange;
            var running = BorderService.IsRunning;
            var th = (int)e.NewValue;

            WindowTracker.AddExternalLog($"[ColorSettings] Thickness changed -> {th} | Auto={auto} Running={running}");

            if (auto && running)
            {
                BorderService.UpdateThickness(th);
                WindowTracker.AddExternalLog($"[ColorSettings] Sent UpdateThickness({th})");
            }
            else if (!auto)
            {
                WindowTracker.AddExternalLog("[ColorSettings] AutoWindowChange=Off, pending until enabled");
            }
            else if (!running)
            {
                WindowTracker.AddExternalLog("[ColorSettings] BorderService not running, start AutoWindowChange first");
            }
        }
    }
}