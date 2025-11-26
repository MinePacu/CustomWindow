using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CustomWindow.Utility;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _path;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _lock = new(1,1);
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(500);
    private Timer? _timer;

    public ObservableConfig Config { get; }
    public event Action<AppConfig>? Saved;

    private ConfigStore(string path, AppConfig cfg)
    {
        _path = path;
        _backupPath = path + ".bak";
        Config = new ObservableConfig(cfg);
        Config.Changed += (_, _) => ScheduleSave();
    }

    public static async Task<ConfigStore> CreateAsync()
    {
        var path = GetPath();
        var cfg = await LoadAsync(path);
        Migrate(cfg);
        ApplyDefaults(cfg);
        return new ConfigStore(path, cfg);
    }

    public static string GetPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CustomWindow");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "Config.json");
    }

    private static async Task<AppConfig> LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                var bak = path + ".bak";
                if (File.Exists(bak)) File.Copy(bak, path, overwrite: true); else return new AppConfig();
            }
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AppConfig>(fs, Options) ?? new AppConfig();
        }
        catch { return new AppConfig(); }
    }

    private static void Migrate(AppConfig cfg)
    {
        switch (cfg.SchemaVersion)
        {
            case 1:
            default:
                break;
        }
    }

    private static void ApplyDefaults(AppConfig c)
    {
        c.BorderColor ??= "#FF4080FF";
        c.CaptionColor ??= "#FF202020";
        c.CaptionTextColor ??= "#FFFFFFFF";
        c.CaptionTextColorMode ??= "자동";
        c.CaptionColorMode ??= "밝게";
        c.WindowCornerMode ??= "기본";
        c.TaskbarCornerMode ??= "기본";

        // BorderThickness 값 검증 및 보정
        if (c.BorderThickness < 1)
        {
            WindowTracker.AddExternalLog($"[ConfigStore] Invalid BorderThickness {c.BorderThickness} detected, resetting to 3");
            c.BorderThickness = 3;
        }
        else if (c.BorderThickness > 20)
        {
            WindowTracker.AddExternalLog($"[ConfigStore] BorderThickness {c.BorderThickness} too large, clamping to 20");
            c.BorderThickness = 20;
        }
    }

    private void ScheduleSave()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer ??= new Timer(async _ => await SaveAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    public async Task SaveAsync(bool force = false)
    {
        await _lock.WaitAsync();
        try
        {
            var tmp = _path + ".tmp";
            var json = JsonSerializer.Serialize(Config.Snapshot, Options);
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(_path)) File.Copy(_path, _backupPath, overwrite: true);
            try { File.Replace(tmp, _path, _backupPath, ignoreMetadataErrors: true); }
            catch { File.Copy(tmp, _path, overwrite: true); File.Delete(tmp); }
            Saved?.Invoke(Config.Snapshot);
        }
        finally { _lock.Release(); }
    }

    public async Task FlushAsync()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        await SaveAsync(true);
        _timer?.Dispose();
        _lock.Dispose();
    }
}
