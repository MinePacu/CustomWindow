using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CustomWindow.ViewModels;
using CustomWindow.Utility;
using System.Text;
using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace CustomWindow.Pages
{
    public sealed partial class NormalSetting : Page
    {
        public NormalSettingsViewModel ViewModel { get; }
        
        private DispatcherTimer _statusUpdateTimer;
        private string _fullLogText = "";
        private int _totalLogCount = 0;
        private int _errorLogCount = 0;
        
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
            
            // 상태 업데이트 타이머 시작
            _statusUpdateTimer = new DispatcherTimer();
            _statusUpdateTimer.Interval = TimeSpan.FromSeconds(2);
            _statusUpdateTimer.Tick += StatusUpdateTimer_Tick;
            _statusUpdateTimer.Start();
        }

        private void NormalSetting_Unloaded(object sender, RoutedEventArgs e)
        {
            WindowTracker.LogAdded -= WindowTracker_LogAdded;
            BorderService.LogReceived -= BorderService_LogReceived;
            _statusUpdateTimer?.Stop();
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
            try
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        if (WindowTrackerLogBox != null && !string.IsNullOrEmpty(line))
                        {
                            _fullLogText = line + "\r\n" + _fullLogText;
                            
                            // 로그 개수 업데이트
                            _totalLogCount++;
                            if (line.Contains("[ERROR]") || line.Contains("failed") || line.Contains("error"))
                            {
                                _errorLogCount++;
                            }
                            
                            // 현재 필터에 따라 표시
                            ApplyLogFilter();
                            
                            // 통계 업데이트
                            UpdateLogStatistics();
                            
                            // 자동 스크롤
                            if (AutoScrollCheckBox?.IsChecked == true)
                            {
                                // 최신 로그가 맨 위로 오도록 스크롤
                                WindowTrackerLogBox.Select(0, 0);
                            }
                            
                            // 길이 제한 (메모리 관리)
                            if (_fullLogText.Length > 50000)
                            {
                                _fullLogText = _fullLogText[..50000];
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"AddLogToTextBox inner error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AddLogToTextBox error: {ex.Message}");
            }
        }

        private void NormalSetting_Loaded(object sender, RoutedEventArgs e)
        {
            bool isAdmin = ElevationHelper.IsRunAsAdmin();

            // 관리자 권한 상태 UI 업데이트
            UpdateAdminStatusUI(isAdmin);

            if (RestartWithAdminbutton is not null)
            {
                RestartWithAdminbutton.IsEnabled = !isAdmin; // 이미 관리자면 비활성화
            }
            if (AutoAdminToggle is not null)
            {
                AutoAdminToggle.IsEnabled = isAdmin; // 관리자 아니면 자동 관리자 설정 비활성화
            }

            RefreshLogs();
            UpdateServiceStatus();
        }

        /// <summary>
        /// 관리자 권한 상태 UI 업데이트
        /// </summary>
        private void UpdateAdminStatusUI(bool isAdmin)
        {
            try
            {
                if (isAdmin)
                {
                    // 관리자 권한
                    AdminStatusBadge.Background = new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)); // 초록색
                    AdminStatusIcon.Glyph = "\uE73E"; // 체크 아이콘
                    AdminStatusTextHeader.Text = "관리자";
                    AdminInfoBar.Severity = InfoBarSeverity.Success;
                    AdminInfoBar.Title = "관리자 권한으로 실행 중";
                    AdminInfoBar.Message = "모든 창에 접근할 수 있습니다.";
                }
                else
                {
                    // 일반 사용자
                    AdminStatusBadge.Background = new SolidColorBrush(Color.FromArgb(255, 196, 89, 17)); // 주황색
                    AdminStatusIcon.Glyph = "\uE7BA"; // 경고 아이콘
                    AdminStatusTextHeader.Text = "일반 사용자";
                    AdminInfoBar.Severity = InfoBarSeverity.Warning;
                    AdminInfoBar.Title = "일반 사용자 권한으로 실행 중";
                    AdminInfoBar.Message = "일부 창에 접근이 제한될 수 있습니다. 관리자 권한으로 재시작하는 것을 권장합니다.";
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"UpdateAdminStatusUI error: {ex.Message}");
            }
        }

        /// <summary>
        /// 서비스 상태 업데이트 (타이머 이벤트)
        /// </summary>
        private void StatusUpdateTimer_Tick(object sender, object e)
        {
            UpdateServiceStatus();
        }

        /// <summary>
        /// 서비스 상태 업데이트
        /// </summary>
        private void UpdateServiceStatus()
        {
            try
            {
                bool isRunning = BorderService.IsRunning;
                var windowHandles = WindowTracker.CurrentWindowHandles;
                int trackedCount = windowHandles?.Count ?? 0;
                
                // 서비스 상태 표시
                if (isRunning)
                {
                    ServiceStatusIndicator.Fill = new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)); // 초록색
                    ServiceStatusText.Text = "실행 중";
                    ServiceActivityProgress.IsIndeterminate = true;
                    ServiceActivityProgress.ShowError = false;
                }
                else
                {
                    ServiceStatusIndicator.Fill = new SolidColorBrush(Color.FromArgb(255, 138, 138, 138)); // 회색
                    ServiceStatusText.Text = "정지됨";
                    ServiceActivityProgress.IsIndeterminate = false;
                    ServiceActivityProgress.ShowError = false;
                }
                
                // 통계 업데이트
                TrackedWindowsText.Text = $"{trackedCount}개";
                BorderAppliedText.Text = isRunning ? $"{trackedCount}개" : "0개";
                
                // ViewModel 상태 업데이트
                ViewModel.CheckBorderServiceStatus();
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"UpdateServiceStatus error: {ex.Message}");
            }
        }

        /// <summary>
        /// 로그 통계 업데이트
        /// </summary>
        private void UpdateLogStatistics()
        {
            try
            {
                if (TotalLogCountText != null)
                {
                    TotalLogCountText.Text = _totalLogCount.ToString();
                }
                
                if (ErrorLogCountText != null)
                {
                    ErrorLogCountText.Text = _errorLogCount.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateLogStatistics error: {ex.Message}");
            }
        }

        /// <summary>
        /// 모서리 모드 변경 시 미리보기 업데이트
        /// </summary>
        private void CornerModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (CornerPreviewWindow == null) return;
                
                var selectedMode = ViewModel.WindowCornerMode;
                
                // 모서리 반경 설정
                var cornerRadius = selectedMode switch
                {
                    "둥글게 하지 않음" => new CornerRadius(0),
                    "둥글게" => new CornerRadius(12),
                    "덜 둥글게" => new CornerRadius(6),
                    _ => new CornerRadius(8) // 기본
                };
                
                CornerPreviewWindow.CornerRadius = cornerRadius;
                
                // 타이틀 바도 같이 업데이트
                if (CornerPreviewWindow.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Grid titleBar)
                {
                    var titleBarRadius = selectedMode switch
                    {
                        "둥글게 하지 않음" => new CornerRadius(0),
                        "둥글게" => new CornerRadius(12, 12, 0, 0),
                        "덜 둥글게" => new CornerRadius(6, 6, 0, 0),
                        _ => new CornerRadius(8, 8, 0, 0)
                    };
                    
                    titleBar.CornerRadius = titleBarRadius;
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"CornerModeComboBox_SelectionChanged error: {ex.Message}");
            }
        }

        /// <summary>
        /// 로그 검색
        /// </summary>
        private void LogSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyLogFilter();
        }

        /// <summary>
        /// 로그 필터 변경
        /// </summary>
        private void LogFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyLogFilter();
        }

        /// <summary>
        /// 로그 필터 적용
        /// </summary>
        private void ApplyLogFilter()
        {
            try
            {
                if (WindowTrackerLogBox == null || LogSearchBox == null || LogFilterComboBox == null)
                    return;
                
                var searchText = LogSearchBox.Text?.ToLower() ?? "";
                var filterIndex = LogFilterComboBox.SelectedIndex;
                
                var lines = _fullLogText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var filteredLines = lines.AsEnumerable();
                
                // 검색어 필터
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    filteredLines = filteredLines.Where(line => line.ToLower().Contains(searchText));
                }
                
                // 레벨 필터
                if (filterIndex > 0)
                {
                    filteredLines = filterIndex switch
                    {
                        1 => filteredLines.Where(line => line.Contains("[ERROR]") || line.Contains("failed") || line.Contains("error")),
                        2 => filteredLines.Where(line => line.Contains("[WARN]") || line.Contains("warning")),
                        3 => filteredLines.Where(line => line.Contains("[INFO]") || line.Contains("info")),
                        _ => filteredLines
                    };
                }
                
                WindowTrackerLogBox.Text = string.Join("\r\n", filteredLines);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyLogFilter error: {ex.Message}");
            }
        }

        /// <summary>
        /// 로그 내보내기
        /// </summary>
        private async void ExportWindowTrackerLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var savePicker = new FileSavePicker();
                
                // WinRT 초기화 - 현재 창의 HWND 가져오기
                var window = (Application.Current as App)?.Window;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
                }
                
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("텍스트 파일", new[] { ".txt" });
                savePicker.FileTypeChoices.Add("로그 파일", new[] { ".log" });
                savePicker.SuggestedFileName = $"BorderService_Log_{DateTime.Now:yyyyMMdd_HHmmss}";
                
                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    await FileIO.WriteTextAsync(file, _fullLogText);
                    WindowTracker.AddExternalLog($"로그 파일 내보내기 완료: {file.Path}");
                }
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"로그 내보내기 실패: {ex.Message}");
            }
        }

        private void RefreshLogs()
        {
            if (WindowTrackerLogBox == null) return;
            
            try
            {
                var lines = WindowTracker.GetRecentLogs();
                if (lines == null || lines.Count == 0)
                {
                    WindowTrackerLogBox.Text = "[로그 없음]";
                    _fullLogText = "";
                    _totalLogCount = 0;
                    _errorLogCount = 0;
                    return;
                }
                
                var sb = new StringBuilder();
                _totalLogCount = 0;
                _errorLogCount = 0;
                
                foreach (var l in lines)
                {
                    if (!string.IsNullOrEmpty(l))
                    {
                        sb.AppendLine(l);
                        _totalLogCount++;
                        
                        if (l.Contains("[ERROR]") || l.Contains("failed") || l.Contains("error"))
                        {
                            _errorLogCount++;
                        }
                    }
                }
                
                _fullLogText = sb.ToString();
                WindowTrackerLogBox.Text = _fullLogText;
                
                UpdateLogStatistics();
            }
            catch (Exception ex)
            {
                WindowTrackerLogBox.Text = $"[로그 로드 실패: {ex.Message}]";
                WindowTracker.AddExternalLog($"로그 새로고침 실패: {ex.Message}");
            }
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
                WindowTracker.AddExternalLog("로그를 클립보드에 복사했습니다.");
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
                _fullLogText = "";
                _totalLogCount = 0;
                _errorLogCount = 0;
                UpdateLogStatistics();
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
                    button.Content = "✓ 완료!";
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
                UpdateServiceStatus();
                
                // 사용자 피드백
                if (sender is Button button)
                {
                    var originalContent = button.Content;
                    bool running = BorderService.IsRunning;
                    button.Content = running ? "✓ 실행 중" : "✗ 정지됨";
                    
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
