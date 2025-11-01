using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using CustomWindow.Utility;
using System.Reflection;
using IOPath = System.IO.Path;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CustomWindow
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private AppWindow? _appWindow;
        public static ConfigStore? ConfigStore { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();

            // 전역 예외 핸들러 등록 (진단/안정성 향상)
            this.UnhandledException += (s, e) =>
            {
                try
                {
                    WindowTracker.AddExternalLog($"Unhandled UI exception: {e.Exception?.Message}");
                    System.Diagnostics.Debug.WriteLine($"[App] Unhandled UI exception: {e.Exception}");
                    e.Handled = true; // 가능하면 중단 방지
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    WindowTracker.AddExternalLog($"Unhandled domain exception: {ex?.Message ?? e.ExceptionObject?.ToString()}");
                    System.Diagnostics.Debug.WriteLine($"[App] Unhandled domain exception: {ex}");
                }
                catch { }
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    WindowTracker.AddExternalLog($"Unobserved task exception: {e.Exception?.Message}");
                    System.Diagnostics.Debug.WriteLine($"[App] Unobserved task exception: {e.Exception}");
                    e.SetObserved();
                }
                catch { }
            };
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            ConfigStore = await ConfigStore.CreateAsync();

            // Apply autostart state at app startup
            try { AutoStartManager.EnsureState(ConfigStore.Config.RunOnBoot); } catch { }

            // Apply BorderService settings if AutoWindowChange is enabled
            if (ConfigStore.Config.AutoWindowChange)
            {
                try
                {
                    // WindowStyleApplier 초기화 (캡션 색상 모드 적용)
                    WindowStyleApplier.Initialize(ConfigStore.Config);
                    
                    // Apply all BorderService preferences before starting
                    BorderService.SetConsoleVisibilityPreference(ConfigStore.Config.ShowBorderServiceConsole);
                    BorderService.SetRenderModePreference(ConfigStore.Config.BorderRenderMode);
                    BorderService.SetForegroundWindowOnly(ConfigStore.Config.ForegroundWindowOnly);
                    BorderService.UpdateCornerMode(ConfigStore.Config.WindowCornerMode);

                    // Start BorderService with configured settings
                    WindowTracker.Start();
                    var borderHex = ConfigStore.Config.BorderColor ?? "#0078FF";
                    BorderService.StartIfNeeded(borderHex, ConfigStore.Config.BorderThickness, ConfigStore.Config.Snapshot.ExcludedPrograms.ToArray());
                    
                    WindowTracker.AddExternalLog($"App startup: BorderService auto-started (Corner={ConfigStore.Config.WindowCornerMode ?? "기본"})");
                }
                catch (Exception ex)
                {
                    WindowTracker.AddExternalLog($"Failed to auto-start BorderService: {ex.Message}");
                }
            }

            _window = new MainWindow();
            _window.Activate();

            // AppWindow Closing handling for minimize-to-tray
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = AppWindow.GetFromWindowId(windowId);
                if (_appWindow != null)
                {
                    _appWindow.Closing += (sender, ev) =>
                    {
                        try
                        {
                            if (ConfigStore?.Config.MinimizeToTray == true)
                            {
                                ev.Cancel = true;
                                sender.Hide();
                                WindowTracker.AddExternalLog("창 닫기 → 트레이 최소화");
                                return;
                            }
                        }
                        catch { }
                    };
                }
            }
            catch { }

            try { SystemTray.Init(_window, "CustomWindow"); } catch { }

            AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
            {
                try
                {
                    BorderService.StopIfRunning();
                    WindowStyleApplier.Stop();
                    WindowTracker.Stop();
                    SystemTray.Dispose();
                    if (ConfigStore != null)
                        await ConfigStore.FlushAsync();
                }
                catch { }
            };

            _window.Closed += (_, _) =>
            {
                try
                {
                    if (ConfigStore?.Config.MinimizeToTray != true)
                    {
                        BorderService.StopIfRunning();
                        WindowStyleApplier.Stop();
                        WindowTracker.Stop();
                        SystemTray.Dispose();
                    }
                }
                catch { }
            };
        }
    }
}
