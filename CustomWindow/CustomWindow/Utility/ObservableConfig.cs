using System;
using System.ComponentModel;

namespace CustomWindow.Utility;

public sealed partial class ObservableConfig : INotifyPropertyChanged
{
    private readonly AppConfig _cfg;
    public AppConfig Snapshot => _cfg;

    public ObservableConfig(AppConfig cfg) => _cfg = cfg;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<ObservableConfig,string>? Changed;
    private void Raise(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke(this, name);

        if (name == nameof(RunOnBoot))
        {
            try { AutoStartManager.EnsureState(_cfg.RunOnBoot); } catch { }
        }
    }

    private bool Set<T>(T current, T value, Action<T> assign, string name)
    {
        if (Equals(current, value)) return false;
        assign(value);
        Raise(name);
        return true;
    }

    public string? BorderColor { get => _cfg.BorderColor; set => Set(_cfg.BorderColor, value, v => _cfg.BorderColor = v, nameof(BorderColor)); }
    public string? CaptionColor { get => _cfg.CaptionColor; set => Set(_cfg.CaptionColor, value, v => _cfg.CaptionColor = v, nameof(CaptionColor)); }
    public string? CaptionTextColor { get => _cfg.CaptionTextColor; set => Set(_cfg.CaptionTextColor, value, v => _cfg.CaptionTextColor = v, nameof(CaptionTextColor)); }
    public bool UseBorderSystemColor { get => _cfg.UseBorderSystemColor; set => Set(_cfg.UseBorderSystemColor, value, v => _cfg.UseBorderSystemColor = v, nameof(UseBorderSystemColor)); }
    public bool UseBorderTransparency { get => _cfg.UseBorderTransparency; set => Set(_cfg.UseBorderTransparency, value, v => _cfg.UseBorderTransparency = v, nameof(UseBorderTransparency)); }
    public bool UseCaptionSystemColor { get => _cfg.UseCaptionSystemColor; set => Set(_cfg.UseCaptionSystemColor, value, v => _cfg.UseCaptionSystemColor = v, nameof(UseCaptionSystemColor)); }
    public bool UseCaptionTransparency { get => _cfg.UseCaptionTransparency; set => Set(_cfg.UseCaptionTransparency, value, v => _cfg.UseCaptionTransparency = v, nameof(UseCaptionTransparency)); }
    public bool UseCaptionTextSystemColor { get => _cfg.UseCaptionTextSystemColor; set => Set(_cfg.UseCaptionTextSystemColor, value, v => _cfg.UseCaptionTextSystemColor = v, nameof(UseCaptionTextSystemColor)); }
    public bool UseCaptionTextTransparency { get => _cfg.UseCaptionTextTransparency; set => Set(_cfg.UseCaptionTextTransparency, value, v => _cfg.UseCaptionTextTransparency = v, nameof(UseCaptionTextTransparency)); }
    public string? CaptionTextColorMode { get => _cfg.CaptionTextColorMode; set => Set(_cfg.CaptionTextColorMode, value, v => _cfg.CaptionTextColorMode = v, nameof(CaptionTextColorMode)); }
    public string? CaptionColorMode { get => _cfg.CaptionColorMode; set => Set(_cfg.CaptionColorMode, value, v => _cfg.CaptionColorMode = v, nameof(CaptionColorMode)); }
    public int BorderThickness { get => _cfg.BorderThickness; set => Set(_cfg.BorderThickness, value, v => _cfg.BorderThickness = v, nameof(BorderThickness)); }
    public bool AutoWindowChange { get => _cfg.AutoWindowChange; set => Set(_cfg.AutoWindowChange, value, v => _cfg.AutoWindowChange = v, nameof(AutoWindowChange)); }
    public string? WindowCornerMode { get => _cfg.WindowCornerMode; set => Set(_cfg.WindowCornerMode, value, v => _cfg.WindowCornerMode = v, nameof(WindowCornerMode)); }
    public bool AutoAdmin { get => _cfg.AutoAdmin; set => Set(_cfg.AutoAdmin, value, v => _cfg.AutoAdmin = v, nameof(AutoAdmin)); }
    public bool MinimizeToTray { get => _cfg.MinimizeToTray; set => Set(_cfg.MinimizeToTray, value, v => _cfg.MinimizeToTray = v, nameof(MinimizeToTray)); }
    public bool RestoreDefaultsOnExit { get => _cfg.RestoreDefaultsOnExit; set => Set(_cfg.RestoreDefaultsOnExit, value, v => _cfg.RestoreDefaultsOnExit = v, nameof(RestoreDefaultsOnExit)); }
    public bool RunOnBoot { get => _cfg.RunOnBoot; set => Set(_cfg.RunOnBoot, value, v => _cfg.RunOnBoot = v, nameof(RunOnBoot)); }
    public bool UseCustomTitleBar { get => _cfg.UseCustomTitleBar; set => Set(_cfg.UseCustomTitleBar, value, v => _cfg.UseCustomTitleBar = v, nameof(UseCustomTitleBar)); }
    public bool EnableTaskbarBorder { get => _cfg.EnableTaskbarBorder; set => Set(_cfg.EnableTaskbarBorder, value, v => _cfg.EnableTaskbarBorder = v, nameof(EnableTaskbarBorder)); }
    public string? TaskbarCornerMode { get => _cfg.TaskbarCornerMode; set => Set(_cfg.TaskbarCornerMode, value, v => _cfg.TaskbarCornerMode = v, nameof(TaskbarCornerMode)); }
    public bool ForceEmptyWindowTitles { get => _cfg.ForceEmptyWindowTitles; set => Set(_cfg.ForceEmptyWindowTitles, value, v => _cfg.ForceEmptyWindowTitles = v, nameof(ForceEmptyWindowTitles)); }
    public bool ForceBorderColor { get => _cfg.ForceBorderColor; set => Set(_cfg.ForceBorderColor, value, v => _cfg.ForceBorderColor = v, nameof(ForceBorderColor)); }
    public int WindowApplyDelayMs { get => _cfg.WindowApplyDelayMs; set => Set(_cfg.WindowApplyDelayMs, value, v => _cfg.WindowApplyDelayMs = v, nameof(WindowApplyDelayMs)); }
    public bool ShowBorderServiceConsole { get => _cfg.ShowBorderServiceConsole; set => Set(_cfg.ShowBorderServiceConsole, value, v => _cfg.ShowBorderServiceConsole = v, nameof(ShowBorderServiceConsole)); }
    public string BorderRenderMode { get => _cfg.BorderRenderMode; set => Set(_cfg.BorderRenderMode, value, v => _cfg.BorderRenderMode = v, nameof(BorderRenderMode)); }
}
