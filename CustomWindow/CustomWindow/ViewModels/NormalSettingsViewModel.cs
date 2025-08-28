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
    private static readonly string[] _defaultExcludes = new[] { "TextInputHost" }; // explorer 제거

    public NormalSettingsViewModel(ObservableConfig cfg)
    {
        _config = cfg;
        // 기본 제외 항목 추가 (중복 방지)
        var set = new HashSet<string>(_config.Snapshot.ExcludedPrograms, System.StringComparer.OrdinalIgnoreCase);
        bool added = false;
        foreach (var d in _defaultExcludes)
        {
            if (!set.Contains(d)) { _config.Snapshot.ExcludedPrograms.Add(d); added = true; }
        }
        if (added) OnPropertyChanged(nameof(ExcludedProgramList));
        
        // BorderService DLL 상태 확인
        CheckBorderServiceStatus();
    }

    private bool _borderServiceAvailable;
    public bool BorderServiceAvailable
    {
        get => _borderServiceAvailable;
        set
        {
            if (_borderServiceAvailable == value) return;
            _borderServiceAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderServiceStatusText));
        }
    }

    public string BorderServiceStatusText => BorderServiceAvailable 
        ? "BorderService DLL 사용 가능" 
        : "BorderService DLL 없음 (제한된 기능)";

    public void CheckBorderServiceStatus()
    {
        BorderServiceAvailable = BorderService.IsDllAvailable();
        WindowTracker.AddExternalLog($"BorderService DLL 상태: {(BorderServiceAvailable ? "사용 가능" : "사용 불가")}");
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
                if (!BorderServiceAvailable)
                {
                    WindowTracker.AddExternalLog("경고: BorderService DLL이 없어 제한된 기능으로 동작합니다.");
                }
                
                WindowTracker.Start();
                // BorderService 시작
                var borderHex = _config.BorderColor ?? "#FF0000"; // 기본 빨강
                int thickness = _config.BorderThickness;
                BorderService.StartIfNeeded(borderHex, thickness, _config.Snapshot.ExcludedPrograms.ToArray());
                
                // 고급 설정 적용
                if (BorderServiceAvailable && BorderService.IsRunning)
                {
                    BorderService.SetPartialRatio(0.3f); // 30% 부분 업데이트
                    BorderService.EnableMerge(true);     // 겹침 병합 활성화
                }
                
                WindowTracker.AddExternalLog("AutoWindowChange ON: BorderService 기동 요청");
            }
            else
            {
                BorderService.StopIfRunning();
                WindowTracker.Stop();
                WindowTracker.AddExternalLog("AutoWindowChange OFF: BorderService 중지");
            }
        }
    }

    public string ExcludedProgramList
    {
        get => string.Join("\n", _config.Snapshot.ExcludedPrograms);
        set
        {
            // 중복 제거 처리작업 (줄 하나씩 하나)
            _config.Snapshot.ExcludedPrograms.Clear();
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (value != null)
            {
                foreach (var raw in value.Split('\n','\r'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    // 전체경로 입력시 파일명만 추출
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

            // AutoWindowChange 활성 상태라면 BorderService 재시작하여 변경 반영
            if (AutoWindowChange && BorderServiceAvailable)
            {
                BorderService.StopIfRunning();
                var borderHex = _config.BorderColor ?? "#0078FF";
                BorderService.StartIfNeeded(borderHex, _config.BorderThickness, _config.Snapshot.ExcludedPrograms.ToArray());
                WindowTracker.AddExternalLog("Excluded 목록 변경 -> BorderService 재시작");
            }
        }
    }

    // AutoWindowChange on 시
    public void OnBorderColorChanged()
    {
        if (AutoWindowChange)
        {
            var borderHex = _config.BorderColor ?? "#0078FF";
            BorderService.UpdateColor(borderHex); // EXE 모드에서는 재시작
        }
    }
    public void OnBorderThicknessChanged()
    {
        if (AutoWindowChange)
        {
            BorderService.UpdateThickness(_config.BorderThickness); // EXE 모드에서는 재시작
        }
    }

    // 강제 다시 그리기 명령
    public void ForceRedrawBorders()
    {
        if (BorderService.IsRunning)
        {
            try
            {
                BorderService.ForceRedraw();
                WindowTracker.AddExternalLog("테두리 강제 다시 그리기 실행");
            }
            catch (Exception ex)
            {
                WindowTracker.AddExternalLog($"테두리 다시 그리기 중 오류 발생: {ex.Message}");
            }
        }
        else
        {
            WindowTracker.AddExternalLog("BorderService가 실행 중이 아니어서 다시 그리기를 실행할 수 없습니다.");
        }
    }

    public string? WindowCornerMode { get => _config.WindowCornerMode; set { _config.WindowCornerMode = value; OnPropertyChanged(); } }

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
}
