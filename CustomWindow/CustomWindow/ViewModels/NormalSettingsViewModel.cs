using CommunityToolkit.Mvvm.ComponentModel;
using CustomWindow.Utility;
using System.Linq;
using System.Windows.Input;
using System.Drawing;
using System.Collections.Generic;
using System;

namespace CustomWindow.ViewModels;

public partial class NormalSettingsViewModel : ObservableObject
{
    private readonly ObservableConfig _config;
    private static readonly string[] _defaultExcludes = new[] { "TextInputHost" }; // explorer 기본

    public NormalSettingsViewModel(ObservableConfig cfg)
    {
        _config = cfg;
        // 기본 제외 대상 추가 (중복 방지)
        var set = new HashSet<string>(_config.Snapshot.ExcludedPrograms, System.StringComparer.OrdinalIgnoreCase);
        bool added = false;
        foreach (var d in _defaultExcludes)
        {
            if (!set.Contains(d)) { _config.Snapshot.ExcludedPrograms.Add(d); added = true; }
        }
        if (added) OnPropertyChanged(nameof(ExcludedProgramList));
    }

    // New: toggle to show the BorderService console
    public bool ShowBorderServiceConsole
    {
        get => _config.ShowBorderServiceConsole;
        set
        {
            if (_config.ShowBorderServiceConsole == value) return;
            _config.ShowBorderServiceConsole = value;
            OnPropertyChanged();
            // Apply preference to running EXE (restart to switch console visibility)
            BorderService.SetConsoleVisibilityPreference(value);
        }
    }

    // Render mode selection
    public string BorderRenderMode
    {
        get => _config.BorderRenderMode;
        set
        {
            if (_config.BorderRenderMode == value) return;
            _config.BorderRenderMode = value;
            OnPropertyChanged();
            BorderService.SetRenderModePreference(value);

            if (AutoWindowChange)
            {
                // 재시작하여 모드 반영
                var borderHex = _config.BorderColor ?? "#0078FF";
                BorderService.StopIfRunning();
                BorderService.StartIfNeeded(borderHex, _config.BorderThickness, _config.Snapshot.ExcludedPrograms.ToArray());
                WindowTracker.AddExternalLog($"렌더 모드 변경 -> {_config.BorderRenderMode} (재시작)");
            }
        }
    }

    //  상태 텍스트: EXE 모드 표시
    public string BorderServiceStatusText
    {
        get
        {
            var exeSummary = BorderService.GetExeStatusSummary();
            return exeSummary;
        }
    }

    public void CheckBorderServiceStatus()
    {
        var exeSummary = BorderService.GetExeStatusSummary();
        WindowTracker.AddExternalLog($"BorderService 상태 확인: {exeSummary}");
        OnPropertyChanged(nameof(BorderServiceStatusText));
    }

    public bool AutoWindowChange
    {
        get => _config.AutoWindowChange;
        set
        {
            if (_config.AutoWindowChange == value) return;
            _config.AutoWindowChange = value;
            OnPropertyChanged();
            
            if (value)
            {
                WindowTracker.Start();
                
                // WindowStyleApplier 초기화 (캡션 색상 모드 적용)
                WindowStyleApplier.Initialize(_config);
                
                // BorderService 시작
                var borderHex = _config.BorderColor ?? "#FF0000"; // 기본 빨강
                int thickness = _config.BorderThickness;
                BorderService.SetConsoleVisibilityPreference(_config.ShowBorderServiceConsole);
                BorderService.SetRenderModePreference(_config.BorderRenderMode);
                BorderService.SetForegroundWindowOnly(_config.ForegroundWindowOnly);
                
                // 창 모서리 모드 설정
                BorderService.UpdateCornerMode(_config.WindowCornerMode);
                
                BorderService.StartIfNeeded(borderHex, thickness, _config.Snapshot.ExcludedPrograms.ToArray());
                
                WindowTracker.AddExternalLog($"AutoWindowChange ON: BorderService 시작 (Corner={_config.WindowCornerMode ?? "기본"})");
            }
            else
            {
                // AutoWindowChange OFF: 서비스 중지 및 원래 상태로 복원
                BorderService.StopIfRunning();
                WindowStyleApplier.Stop();
                
                // DWM 모드에서 창 모서리를 기본 상태로 복원
                RestoreWindowCornersToDefault();
                
                WindowTracker.Stop();
                WindowTracker.AddExternalLog("AutoWindowChange OFF: BorderService 중지 및 창 설정 복원");
            }

            // 상태 갱신
            CheckBorderServiceStatus();
        }
    }

    /// <summary>
    /// 모든 창의 모서리 설정을 기본 상태로 복원합니다.
    /// AutoWindowChange OFF 시 호출됩니다.
    /// </summary>
    private void RestoreWindowCornersToDefault()
    {
        // Windows 11 이상에서만 작동
        if (!DwmWindowManager.SupportsCustomCaptionColors())
        {
            WindowTracker.AddExternalLog("Windows 10에서는 창 모서리 복원이 필요하지 않습니다");
            return;
        }

        try
        {
            // WindowTracker에서 현재 추적 중인 창 목록 가져오기
            var windows = WindowTracker.CurrentWindowHandles;
            if (windows == null || windows.Count == 0)
            {
                WindowTracker.AddExternalLog("복원할 창이 없습니다");
                return;
            }

            int successCount = 0;
            foreach (var handle in windows)
            {
                try
                {
                    // "기본" 모드로 설정하여 시스템 기본 동작으로 복원
                    if (DwmWindowManager.SetCornerPreference((IntPtr)handle, "기본"))
                    {
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    WindowTracker.AddExternalLog($"창 0x{handle:X} 모서리 복원 실패: {ex.Message}");
                }
            }

            WindowTracker.AddExternalLog($"창 모서리 기본 상태로 복원 완료: {successCount}/{windows.Count}개 창");
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"RestoreWindowCornersToDefault 오류: {ex.Message}");
        }
    }

    public bool ForegroundWindowOnly
    {
        get => _config.ForegroundWindowOnly;
        set
        {
            if (_config.ForegroundWindowOnly == value) return;
            
            var previousValue = _config.ForegroundWindowOnly;
            _config.ForegroundWindowOnly = value;
            OnPropertyChanged();
            
            // AutoWindowChange가 활성화된 경우 즉시 적용
            if (AutoWindowChange)
            {
                BorderService.SetForegroundWindowOnly(value);
                var fromState = previousValue ? "ON" : "OFF";
                var toState = value ? "ON" : "OFF";
                WindowTracker.AddExternalLog($"Foreground window mode changed: {fromState} -> {toState}");
                
                // 설정 변경 후 추가적인 창 새로고침 (1초 후)
                System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
                {
                    try
                    {
                        BorderService.ForceRedraw();
                        WindowTracker.AddExternalLog("Window state refreshed after foreground option change");
                    }
                    catch (Exception ex)
                    {
                        WindowTracker.AddExternalLog($"Failed to refresh window state: {ex.Message}");
                    }
                }, System.Threading.Tasks.TaskScheduler.Default);
            }
        }
    }

    public string ExcludedProgramList
    {
        get => string.Join("\n", _config.Snapshot.ExcludedPrograms);
        set
        {
            // 중복 제거 처리 (각 라인)
            _config.Snapshot.ExcludedPrograms.Clear();
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (value != null)
            {
                foreach (var raw in value.Split('\n','\r'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    // 전체경로 입력해도 파일명만 추출
                    try
                    {
                        line = System.IO.Path.GetFileName(line);
                    }
                    catch { }
                    // 확장자 .exe 제거
                    if (line.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
                        line = line[..^4];
                    if (line.Length == 0) continue;
                    set.Add(line);
                }
            }
            foreach (var item in set)
                _config.Snapshot.ExcludedPrograms.Add(item);
            OnPropertyChanged();

            // AutoWindowChange 활성 상태면 BorderService를 재시작하여 즉시 반영
            if (AutoWindowChange)
            {
                BorderService.StopIfRunning();
                var borderHex = _config.BorderColor ?? "#0078FF";
                BorderService.StartIfNeeded(borderHex, _config.BorderThickness, _config.Snapshot.ExcludedPrograms.ToArray());
                WindowTracker.AddExternalLog("Excluded 목록 변경 -> BorderService 재시작");
                CheckBorderServiceStatus();
            }
        }
    }

    // AutoWindowChange on 시
    public void OnBorderColorChanged()
    {
        if (AutoWindowChange)
        {
            var borderHex = _config.BorderColor ?? "#0078FF";
            BorderService.UpdateColor(borderHex); // EXE 선택적으로 반영
            CheckBorderServiceStatus();
        }
    }
    public void OnBorderThicknessChanged()
    {
        if (AutoWindowChange)
        {
            BorderService.UpdateThickness(_config.BorderThickness); // EXE 선택적으로 반영
            CheckBorderServiceStatus();
        }
    }

    // 강제 다시 그리기 요청
    public void ForceRedrawBorders()
    {
        if (BorderService.IsRunning)
        {
            try
            {
                BorderService.ForceRedraw();
                WindowTracker.AddExternalLog("테두리 강제 다시 그리기 요청");
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"테두리 강제 다시 그리기 중 오류 발생: {ex.Message}");
            }
        }
        else
        {
            WindowTracker.AddExternalLog("BorderService가 실행 중이 아니어서 다시 그리기를 수행할 수 없습니다.");
        }
    }

    public string? WindowCornerMode 
    { 
        get => _config.WindowCornerMode; 
        set 
        { 
            if (_config.WindowCornerMode == value) return;
            
            _config.WindowCornerMode = value; 
            OnPropertyChanged();
            
            // BorderService에 전달 (C++ EXE로 전달)
            BorderService.UpdateCornerMode(value);
            
            // Windows 11에서 현재 열린 창에 적용
            if (DwmWindowManager.SupportsCustomCaptionColors()) // Windows 11+
            {
                try
                {
                    ApplyCornerModeToAllWindows(value);
                    WindowTracker.AddExternalLog($"창 모서리 스타일 변경: {value ?? "기본"} (실시간 적용)");
                }
                catch (Exception ex)
                {
                    WindowTracker.AddExternalLog($"창 모서리 스타일 적용 실패: {ex.Message}");
                }
            }
            else
            {
                WindowTracker.AddExternalLog($"창 모서리 스타일 변경: {value ?? "기본"} (Windows 10에서는 적용 안됨)");
            }
            
            // 렌더링된 테두리 강제 새로고침
            if (AutoWindowChange && BorderService.IsRunning)
            {
                try
                {
                    // 약간의 지연 후 새로고침 (DWM 적용 완료 대기)
                    System.Threading.Tasks.Task.Delay(100).ContinueWith(_ =>
                    {
                        try
                        {
                            BorderService.ForceRedraw();
                            WindowTracker.AddExternalLog("창 모서리 변경 후 테두리 렌더링 새로고침");
                        }
                        catch (Exception ex)
                        {
                            WindowTracker.AddExternalLog($"테두리 새로고침 실패: {ex.Message}");
                        }
                    }, System.Threading.Tasks.TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    WindowTracker.AddExternalLog($"테두리 새로고침 스케줄링 실패: {ex.Message}");
                }
            }
        } 
    }
    
    /// <summary>
    /// 모든 창에 모서리 스타일을 즉시 적용합니다.
    /// </summary>
    private void ApplyCornerModeToAllWindows(string? cornerMode)
    {
        if (!AutoWindowChange)
        {
            WindowTracker.AddExternalLog("AutoWindowChange가 비활성화되어 모서리 스타일을 적용하지 않습니다");
            return;
        }

        try
        {
            // WindowTracker에서 현재 추적 중인 창 목록 가져오기
            var windows = WindowTracker.CurrentWindowHandles;
            if (windows == null || windows.Count == 0)
            {
                WindowTracker.AddExternalLog("적용할 창이 없습니다");
                return;
            }

            int successCount = 0;
            foreach (var handle in windows)
            {
                try
                {
                    if (DwmWindowManager.SetCornerPreference((IntPtr)handle, cornerMode))
                    {
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    WindowTracker.AddExternalLog($"창 0x{handle:X} 모서리 스타일 적용 실패: {ex.Message}");
                }
            }

            WindowTracker.AddExternalLog($"창 모서리 스타일 적용 완료: {successCount}/{windows.Count}개 창");
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"ApplyCornerModeToAllWindows 오류: {ex.Message}");
        }
    }

    private bool _autoAdminApplying; // 순환 방지
    public bool AutoAdmin
    {
        get => _config.AutoAdmin;
        set
        {
            if (_config.AutoAdmin == value) return;
            _config.AutoAdmin = value;
            OnPropertyChanged();
            if (_autoAdminApplying) return;
            if (value)
            {
                if (!ElevationHelper.IsRunAsAdmin())
                {
                    bool cancelled;
                    if (!ElevationHelper.TryRestartAsAdmin(out cancelled))
                    {
                        if (cancelled)
                        {
                            _autoAdminApplying = true;
                            _config.AutoAdmin = false;
                            OnPropertyChanged(nameof(AutoAdmin));
                            _autoAdminApplying = false;
                        }
                    }
                }
            }
        }
    }

    public bool MinimizeToTray { get => _config.MinimizeToTray; set { _config.MinimizeToTray = value; OnPropertyChanged(); } }
    public bool RestoreDefaultsOnExit { get => _config.RestoreDefaultsOnExit; set { _config.RestoreDefaultsOnExit = value; OnPropertyChanged(); } }
    public bool RunOnBoot { get => _config.RunOnBoot; set { _config.RunOnBoot = value; OnPropertyChanged(); } }
    public bool UseCustomTitleBar { get => _config.UseCustomTitleBar; set { _config.UseCustomTitleBar = value; OnPropertyChanged(); } }
    
    public bool EnableWindowTrackerLog 
    { 
        get => _config.EnableWindowTrackerLog; 
        set 
        { 
            if (_config.EnableWindowTrackerLog == value) return;
            _config.EnableWindowTrackerLog = value;
            OnPropertyChanged();
            WindowTracker.SetLoggingEnabled(value);
            if (value)
            {
                WindowTracker.AddExternalLog("자동 창 설정 로그 활성화");
            }
            else
            {
                WindowTracker.AddExternalLog("자동 창 설정 로그 비활성화");
            }
        } 
    }
}
