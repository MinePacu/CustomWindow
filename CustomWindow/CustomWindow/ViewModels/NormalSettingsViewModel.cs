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
        // 기본 제외 값 추가 (중복 방지)
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
                // 재시작하여 즉시 반영
                var borderHex = _config.BorderColor ?? "#0078FF";
                BorderService.StopIfRunning();
                BorderService.StartIfNeeded(borderHex, _config.BorderThickness, _config.Snapshot.ExcludedPrograms.ToArray());
                WindowTracker.AddExternalLog($"렌더 방식 변경 -> {_config.BorderRenderMode} (재시작)");
            }
        }
    }

    //  상태 텍스트: EXE 모드 요약
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
                var borderHex = _config.BorderColor ?? "#FF0000"; // 기본 색상
                int thickness = _config.BorderThickness;
                BorderService.SetConsoleVisibilityPreference(_config.ShowBorderServiceConsole);
                BorderService.SetRenderModePreference(_config.BorderRenderMode);
                BorderService.SetForegroundWindowOnly(_config.ForegroundWindowOnly);
                
                // 창 모서리 설정 적용
                BorderService.UpdateCornerMode(_config.WindowCornerMode);
                
                BorderService.StartIfNeeded(borderHex, thickness, _config.Snapshot.ExcludedPrograms.ToArray());
                
                WindowTracker.AddExternalLog($"AutoWindowChange ON: BorderService 시작 (Corner={_config.WindowCornerMode ?? "기본"})");
            }
            else
            {
                BorderService.StopIfRunning();
                WindowStyleApplier.Stop();
                WindowTracker.Stop();
                WindowTracker.AddExternalLog("AutoWindowChange OFF: BorderService 중지");
            }

            // 상태 갱신
            CheckBorderServiceStatus();
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
                WindowTracker.AddExternalLog($"포그라운드 창 전용 모드: {(previousValue ? "활성화" : "비활성화")} -> {(value ? "활성화" : "비활성화")}");
                
                // 상태 변경 후 추가적인 강제 새로고침 (1초 후)
                System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
                {
                    try
                    {
                        BorderService.ForceRedraw();
                        WindowTracker.AddExternalLog("포그라운드 옵션 변경 후 최종 강제 새로고침 완료");
                    }
                    catch (Exception ex)
                    {
                        WindowTracker.AddExternalLog($"포그라운드 옵션 변경 후 최종 강제 새로고침 실패: {ex.Message}");
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
            // 중복 제거 처리 (줄 단위)
            _config.Snapshot.ExcludedPrograms.Clear();
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (value != null)
            {
                foreach (var raw in value.Split('\n','\r'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    // 전체경로 입력해도 파일명만 사용
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
            BorderService.UpdateColor(borderHex); // EXE 우선으로 반영
            CheckBorderServiceStatus();
        }
    }
    public void OnBorderThicknessChanged()
    {
        if (AutoWindowChange)
        {
            BorderService.UpdateThickness(_config.BorderThickness); // EXE 우선으로 반영
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

    public string? WindowCornerMode { get => _config.WindowCornerMode; set { _config.WindowCornerMode = value; BorderService.UpdateCornerMode(value); } }

    private bool _autoAdminApplying; // 전환 중
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
}
