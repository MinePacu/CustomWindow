using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CustomWindow.ViewModels;
using CustomWindow.Utility;
using System.Text;

namespace CustomWindow.Pages
{
    public sealed partial class NormalSetting : Page
    {
        public NormalSettingsViewModel ViewModel { get; }
        public NormalSetting()
        {
            // ViewModel 를 먼저 생성하여 x:Bind 가 초기 로드 시 null 참조되지 않도록 함
            ViewModel = new NormalSettingsViewModel(App.ConfigStore!.Config);
            InitializeComponent();
            DataContext = ViewModel;
            Loaded += NormalSetting_Loaded;
            Unloaded += NormalSetting_Unloaded;
            WindowTracker.LogAdded += WindowTracker_LogAdded;
        }

        private void NormalSetting_Unloaded(object sender, RoutedEventArgs e)
        {
            WindowTracker.LogAdded -= WindowTracker_LogAdded;
        }

        private void WindowTracker_LogAdded(string line)
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
                AutoAdminToggle.IsEnabled = isAdmin; // 관리자 아니면 자동 관리자 토글 비활성화
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
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(WindowTrackerLogBox.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        }

        private void ClearWindowTrackerLogs_Click(object sender, RoutedEventArgs e)
        {
            if (WindowTrackerLogBox != null)
            {
                WindowTrackerLogBox.Text = string.Empty;
            }
        }
    }
}
