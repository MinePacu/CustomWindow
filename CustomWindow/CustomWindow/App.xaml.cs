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
using IOPath = System.IO.Path; // 별칭으로 모호성 해결
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
            
            // DLL 로딩 준비
            SetupNativeDllLoading();
        }

        private void SetupNativeDllLoading()
        {
            try
            {
                // 현재 디렉토리를 실행 파일 위치로 설정
                var exeDir = IOPath.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    Directory.SetCurrentDirectory(exeDir);
                }

                // DLL 로드 시도
                var dllLoaded = NativeDllLoader.LoadBorderServiceDll();
                
                // 로그를 위한 메시지
                var message = dllLoaded 
                    ? "Native DLL loading setup completed successfully"
                    : "Native DLL loading setup completed (DLL not found - will use fallback)";
                    
                System.Diagnostics.Debug.WriteLine($"[App] {message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] DLL loading setup failed: {ex.Message}");
            }
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
                    WindowTracker.Stop();
                    NativeDllLoader.UnloadBorderServiceDll();
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
                        WindowTracker.Stop();
                        NativeDllLoader.UnloadBorderServiceDll();
                        SystemTray.Dispose();
                    }
                }
                catch { }
            };
        }
    }
}
