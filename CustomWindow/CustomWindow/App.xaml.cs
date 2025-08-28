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
        public static ConfigStore? ConfigStore { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            
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
            _window = new MainWindow();
            _window.Activate();
            
            // 애플리케이션 종료 시 리소스 정리
            AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
            {
                try
                {
                    // BorderService 안전하게 중지
                    BorderService.StopIfRunning();
                    WindowTracker.Stop();
                    
                    // Native DLL 언로드
                    NativeDllLoader.UnloadBorderServiceDll();
                    
                    if (ConfigStore != null)
                        await ConfigStore.FlushAsync();
                }
                catch (Exception ex)
                {
                    // 종료 시 에러는 로그만 남기고 무시
                    System.Diagnostics.Debug.WriteLine($"Shutdown cleanup error: {ex.Message}");
                }
            };
            
            // 윈도우 종료 시에도 정리
            _window.Closed += (_, _) =>
            {
                try
                {
                    BorderService.StopIfRunning();
                    WindowTracker.Stop();
                    NativeDllLoader.UnloadBorderServiceDll();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Window close cleanup error: {ex.Message}");
                }
            };
        }
    }
}
