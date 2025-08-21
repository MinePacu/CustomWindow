using CommunityToolkit.Mvvm.ComponentModel;
using CustomWindow.Utility;

namespace CustomWindow.ViewModels;

public partial class WindowSettingsViewModel : ObservableObject
{
    private readonly ObservableConfig _config;
    public WindowSettingsViewModel(ObservableConfig cfg) => _config = cfg;

    public bool EnableTaskbarBorder { get => _config.EnableTaskbarBorder; set { _config.EnableTaskbarBorder = value; OnPropertyChanged(); } }
    public string? TaskbarCornerMode { get => _config.TaskbarCornerMode; set { _config.TaskbarCornerMode = value; OnPropertyChanged(); } }
    public bool ForceEmptyWindowTitles { get => _config.ForceEmptyWindowTitles; set { _config.ForceEmptyWindowTitles = value; OnPropertyChanged(); } }
    public bool ForceBorderColor { get => _config.ForceBorderColor; set { _config.ForceBorderColor = value; OnPropertyChanged(); } }
    public int WindowApplyDelayMs { get => _config.WindowApplyDelayMs; set { _config.WindowApplyDelayMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowApplyDelayText)); } }
    public string WindowApplyDelayText { get => WindowApplyDelayMs.ToString(); set { if (int.TryParse(value, out var ms)) WindowApplyDelayMs = ms; else if (string.IsNullOrWhiteSpace(value)) WindowApplyDelayMs = 0; OnPropertyChanged(); } }
}
