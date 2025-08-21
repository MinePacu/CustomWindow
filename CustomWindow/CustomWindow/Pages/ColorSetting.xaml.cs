using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CustomWindow.Utility;
using CustomWindow.ViewModels;

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
        private void borderColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleBorderDemo();
        private void captIonColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleCaptionDemo();
        private void captIonTextColorbutton_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleCaptionTextDemo();
    }
}
