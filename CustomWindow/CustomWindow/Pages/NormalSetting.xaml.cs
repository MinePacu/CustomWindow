using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CustomWindow.ViewModels;
using CustomWindow.Utility;
using System.Text;
using System;

namespace CustomWindow.Pages
{
    public sealed partial class NormalSetting : Page
    {
        public NormalSettingsViewModel ViewModel { get; }
        public NormalSetting()
        {
            // ViewModel 을 먼저 생성하여 x:Bind 시 초기 로드 때 null 참조가 아니도록 함
            ViewModel = new NormalSettingsViewModel(App.ConfigStore!.Config);
            InitializeComponent();
            DataContext = ViewModel;
            Loaded += NormalSetting_Loaded;
            Unloaded += NormalSetting_Unloaded;
            
            // 로그 이벤트 구독
            WindowTracker.LogAdded += WindowTracker_LogAdded;
            BorderService.LogReceived += BorderService_LogReceived;
        }

        private void NormalSetting_Unloaded(object sender, RoutedEventArgs e)
        {
            WindowTracker.LogAdded -= WindowTracker_LogAdded;
            BorderService.LogReceived -= BorderService_LogReceived;
        }

        private void WindowTracker_LogAdded(string line)
        {
            AddLogToTextBox(line);
        }

        private void BorderService_LogReceived(string line)
        {
            AddLogToTextBox(line);
        }

        private void AddLogToTextBox(string line)
        {
            // UI 스레드에 안전하게 반영
            DispatcherQueue.TryEnqueue(() =>
            {
                if (WindowTrackerLogBox != null)
                {
                    WindowTrackerLogBox.Text = line + "\r\n" + WindowTrackerLogBox.Text;
                    // 길이 제한
                    if (WindowTrackerLogBox.Text.Length > 20000)
                    {
                        WindowTrackerLogBox.Text = WindowTrackerLogBox.Text[..20000];
                    }
                }
            });
        }

        private void NormalSetting_Loaded(object sender, RoutedEventArgs e)
        {
            bool isAdmin = ElevationHelper.IsRunAsAdmin();

            if (RestartWithAdminbutton is not null)
            {
                RestartWithAdminbutton.IsEnabled = !isAdmin; // 이미 관리자면 비활성화
            }
            if (AutoAdminToggle is not null)
            {
                AutoAdminToggle.IsEnabled = isAdmin; // 관리자 아니면 자동 관리자 설정 비활성화
            }

            RefreshLogs();
        }

        private void RefreshLogs()
        {
            if (WindowTrackerLogBox == null) return;
            var lines = WindowTracker.GetRecentLogs();
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                sb.AppendLine(l);
            }
            WindowTrackerLogBox.Text = sb.ToString();
        }

        private void RestartWithAdminbutton_Click(object sender, RoutedEventArgs e)
        {
            if (ElevationHelper.IsRunAsAdmin()) return; // 이미 관리자
            bool cancelled;
            ElevationHelper.TryRestartAsAdmin(out cancelled);
        }

        private void RefreshWindowTrackerLogs_Click(object sender, RoutedEventArgs e)
        {
            RefreshLogs();
        }

        private void CopyWindowTrackerLogs_Click(object sender, RoutedEventArgs e)
        {
            if (WindowTrackerLogBox == null) return;
            try
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(WindowTrackerLogBox.Text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"클립보드 복사 실패: {ex.Message}");
            }
        }

        private void ClearWindowTrackerLogs_Click(object sender, RoutedEventArgs e)
        {
            if (WindowTrackerLogBox != null)
            {
                WindowTrackerLogBox.Text = string.Empty;
            }
        }

        // 새로운 이벤트 핸들러들
        private void ForceRedrawBorders_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ViewModel.ForceRedrawBorders();
                
                // 사용자 피드백
                if (sender is Button button)
                {
                    var originalContent = button.Content;
                    button.Content = "완료!";
                    button.IsEnabled = false;
                    
                    // 1초 후 원래 상태로 복원
                    var timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(1);
                    timer.Tick += (_, _) =>
                    {
                        button.Content = originalContent;
                        button.IsEnabled = true;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"테두리 강제 다시 그리기 실패: {ex.Message}");
            }
        }

        private void CheckBorderServiceStatus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // BorderService 상태 재확인
                bool available = BorderService.IsDllAvailable();
                bool running = BorderService.IsRunning;
                
                string statusMessage = available 
                    ? (running ? "BorderService DLL 사용 가능하며 현재 실행 중" : "BorderService DLL 사용 가능하지만 실행 중이 아님")
                    : "BorderService DLL을 찾을 수 없음";
                
                WindowTracker.AddExternalLog($"상태 확인: {statusMessage}");
                
                // ViewModel 상태 업데이트
                ViewModel.CheckBorderServiceStatus();
                
                // 사용자 피드백
                if (sender is Button button)
                {
                    var originalContent = button.Content;
                    button.Content = available ? "✓ 사용 가능" : "✗ 사용 불가";
                    
                    // 2초 후 원래 상태로 복원
                    var timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(2);
                    timer.Tick += (_, _) =>
                    {
                        button.Content = originalContent;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"상태 확인 실패: {ex.Message}");
            }
        }
    }
}
