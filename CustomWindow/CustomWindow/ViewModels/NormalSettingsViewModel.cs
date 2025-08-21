using CommunityToolkit.Mvvm.ComponentModel;
using CustomWindow.Utility;
using System.Linq;
using System.Windows.Input;
using System.Drawing;
using System.Collections.Generic;

namespace CustomWindow.ViewModels;

public partial class NormalSettingsViewModel : ObservableObject
{
    private readonly ObservableConfig _config;
    private static readonly string[] _defaultExcludes = new[] { "TextInputHost" }; // explorer 제거

    public NormalSettingsViewModel(ObservableConfig cfg)
    {
        _config = cfg;
        // 기본 제외 항목 주입 (없을 때만)
        var set = new HashSet<string>(_config.Snapshot.ExcludedPrograms, System.StringComparer.OrdinalIgnoreCase);
        bool added = false;
        foreach (var d in _defaultExcludes)
        {
            if (!set.Contains(d)) { _config.Snapshot.ExcludedPrograms.Add(d); added = true; }
        }
        if (added) OnPropertyChanged(nameof(ExcludedProgramList));
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
                // BorderService 실행
                var borderHex = _config.BorderColor ?? "#FF0000"; // 기본 색상
                int thickness = _config.BorderThickness;
                BorderServiceHost.StartIfNeeded(borderHex, thickness, _config.Snapshot.ExcludedPrograms.ToArray());
                WindowTracker.AddExternalLog("AutoWindowChange ON: BorderService 기동 요청");
            }
            else
            {
                BorderServiceHost.StopIfRunning();
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
            // 기존 목록 재구성 (줄 하나당 하나)
            _config.Snapshot.ExcludedPrograms.Clear();
            var set = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (value != null)
            {
                foreach (var raw in value.Split('\n','\r'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    // 경로가 들어오면 파일명만 추출
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

            // AutoWindowChange 활성 상태라면 BorderService 재시작하여 즉시 반영
            if (AutoWindowChange)
            {
                BorderServiceHost.StopIfRunning();
                var borderHex = _config.BorderColor ?? "#0078FF";
                BorderServiceHost.StartIfNeeded(borderHex, _config.BorderThickness, _config.Snapshot.ExcludedPrograms.ToArray());
                WindowTracker.AddExternalLog("Excluded 목록 변경 -> BorderService 재시작");
            }
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
