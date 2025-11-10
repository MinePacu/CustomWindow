using System.Collections.Generic;

namespace CustomWindow.Utility;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    // Color settings
    public string? BorderColor { get; set; }
    public string? CaptionColor { get; set; }
    public string? CaptionTextColor { get; set; }
    public bool UseBorderSystemColor { get; set; }
    public bool UseBorderTransparency { get; set; }
    public bool UseCaptionSystemColor { get; set; }
    public bool UseCaptionTransparency { get; set; }
    public bool UseCaptionTextSystemColor { get; set; }
    public bool UseCaptionTextTransparency { get; set; }
    public string? CaptionTextColorMode { get; set; } = "밝게";
    public string? CaptionColorMode { get; set; } = "밝게";
    
    // Border thickness setting
    public int BorderThickness { get; set; } = 10;

    // Normal settings
    public bool AutoWindowChange { get; set; }
    public bool ForegroundWindowOnly { get; set; } // 포그라운드 창에서만 테두리 표시
    public List<string> ExcludedPrograms { get; set; } = new();
    public string? WindowCornerMode { get; set; }
    public bool AutoAdmin { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool RestoreDefaultsOnExit { get; set; }
    public bool RunOnBoot { get; set; }
    public bool UseCustomTitleBar { get; set; }

    // Show EXE console window for BorderService
    public bool ShowBorderServiceConsole { get; set; } // default false

    // Window settings
    public bool EnableTaskbarBorder { get; set; }
    public string? TaskbarCornerMode { get; set; }
    public bool ForceEmptyWindowTitles { get; set; }
    public bool ForceBorderColor { get; set; }
    public int WindowApplyDelayMs { get; set; } = 200;

    // Border render method: "Auto", "Dwm", or "DComp"
    public string BorderRenderMode { get; set; } = "Auto";
    
    // Enable/Disable window tracker logging
    public bool EnableWindowTrackerLog { get; set; } = true;
}
