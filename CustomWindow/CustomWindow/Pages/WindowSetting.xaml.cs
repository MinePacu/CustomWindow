using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CustomWindow.ViewModels;
using CustomWindow.Utility;
using System;
using System.Diagnostics;
using System.Linq;

namespace CustomWindow.Pages
{
    public sealed partial class WindowSetting : Page
    {
        public WindowSettingsViewModel ViewModel { get; }
        
        public WindowSetting()
        {
            InitializeComponent();
            ViewModel = new WindowSettingsViewModel(App.ConfigStore!.Config);
            DataContext = ViewModel;
        }

        /// <summary>
        /// Windows 탐색기 재시작 버튼 클릭 이벤트
        /// </summary>
        private async void RestartExplorerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Windows 탐색기 재시작",
                    Content = "Windows 탐색기를 재시작하시겠습니까?\n\n작업 표시줄과 시작 메뉴가 일시적으로 사라집니다.\n진행 중인 작업을 저장하세요.",
                    PrimaryButtonText = "재시작",
                    CloseButtonText = "취소",
                    DefaultButton = ContentDialogButton.Close
                };
                
                dialog.XamlRoot = this.XamlRoot;
                
                var result = await dialog.ShowAsync();
                
                if (result == ContentDialogResult.Primary)
                {
                    RestartExplorer();
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[WindowSetting] 탐색기 재시작 대화상자 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// Windows 탐색기 프로세스 재시작
        /// </summary>
        private void RestartExplorer()
        {
            try
            {
                WindowTracker.AddExternalLog("[WindowSetting] Windows 탐색기 재시작 시작...");
                
                // explorer.exe 프로세스 찾기 및 종료
                var explorerProcesses = Process.GetProcessesByName("explorer");
                
                if (explorerProcesses.Length == 0)
                {
                    WindowTracker.AddExternalLog("[WindowSetting] 실행 중인 탐색기 프로세스를 찾을 수 없습니다.");
                    
                    // 탐색기가 없으면 바로 시작
                    StartExplorer();
                    return;
                }
                
                // 모든 explorer.exe 종료
                foreach (var process in explorerProcesses)
                {
                    try
                    {
                        WindowTracker.AddExternalLog($"[WindowSetting] 탐색기 프로세스 종료 중... (PID: {process.Id})");
                        process.Kill();
                        process.WaitForExit(5000); // 최대 5초 대기
                        process.Dispose();
                    }
                    catch (Exception ex)
                    {
                        WindowTracker.AddExternalLog($"[WindowSetting] 탐색기 프로세스 종료 실패: {ex.Message}");
                    }
                }
                
                // 잠시 대기 (시스템이 안정화되도록)
                System.Threading.Thread.Sleep(1000);
                
                // explorer.exe 재시작
                StartExplorer();
                
                WindowTracker.AddExternalLog("[WindowSetting] Windows 탐색기 재시작 완료");
                
                // 사용자 피드백
                ShowRestartCompletedNotification();
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[WindowSetting] 탐색기 재시작 실패: {ex.Message}");
                ShowRestartFailedNotification(ex.Message);
            }
        }

        /// <summary>
        /// Windows 탐색기 시작
        /// </summary>
        private void StartExplorer()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };
                
                Process.Start(startInfo);
                WindowTracker.AddExternalLog("[WindowSetting] Windows 탐색기 시작됨");
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[WindowSetting] 탐색기 시작 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 재시작 완료 알림 표시
        /// </summary>
        private async void ShowRestartCompletedNotification()
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "완료",
                    Content = "Windows 탐색기가 재시작되었습니다.",
                    CloseButtonText = "확인",
                    DefaultButton = ContentDialogButton.Close
                };
                
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
            catch { }
        }

        /// <summary>
        /// 재시작 실패 알림 표시
        /// </summary>
        private async void ShowRestartFailedNotification(string errorMessage)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "오류",
                    Content = $"Windows 탐색기 재시작에 실패했습니다.\n\n{errorMessage}",
                    CloseButtonText = "확인",
                    DefaultButton = ContentDialogButton.Close
                };
                
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
            catch { }
        }

        /// <summary>
        /// 도움말 버튼 클릭 이벤트
        /// </summary>
        private async void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var helpContent = new StackPanel { Spacing = 12 };
                
                helpContent.Children.Add(new TextBlock
                {
                    Text = "고급 창 설정 도움말",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 16
                });
                
                helpContent.Children.Add(new TextBlock
                {
                    Text = "창 제목을 공백으로 변경",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4)
                });
                
                helpContent.Children.Add(new TextBlock
                {
                    Text = "모든 창의 제목 표시줄 텍스트를 빈 문자열로 강제 변경합니다. 미니멀한 UI를 원할 때 유용합니다.",
                    TextWrapping = TextWrapping.Wrap
                });
                
                helpContent.Children.Add(new TextBlock
                {
                    Text = "테두리 색상 강제 적용",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4)
                });
                
                helpContent.Children.Add(new TextBlock
                {
                    Text = "창의 기본 테두리 색상을 무시하고 설정한 색상을 강제로 적용합니다. 일부 앱에서 테두리 색상이 적용되지 않을 때 사용하세요.",
                    TextWrapping = TextWrapping.Wrap
                });
                
                helpContent.Children.Add(new TextBlock
                {
                    Text = "창 스타일 적용 지연 시간",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4)
                });
                
                helpContent.Children.Add(new TextBlock
                {
                    Text = "새 창이 생성될 때 스타일을 적용하기 전 대기 시간입니다. 창 깜빡임이나 시각적 결함이 발생하면 100-300ms로 설정하세요.",
                    TextWrapping = TextWrapping.Wrap
                });
                
                var dialog = new ContentDialog
                {
                    Title = "도움말",
                    Content = new ScrollViewer
                    {
                        Content = helpContent,
                        MaxHeight = 400
                    },
                    CloseButtonText = "닫기",
                    DefaultButton = ContentDialogButton.Close
                };
                
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"[WindowSetting] 도움말 표시 오류: {ex.Message}");
            }
        }
    }
}
