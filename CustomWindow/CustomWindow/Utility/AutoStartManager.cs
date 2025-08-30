using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace CustomWindow.Utility;

internal static class AutoStartManager
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "CustomWindow";

    private static string GetExePath()
    {
        try
        {
            var path = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrWhiteSpace(path)) path = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            // Quote if contains space
            if (!path.StartsWith("\"")) path = "\"" + path + "\"";
            return path;
        }
        catch { return string.Empty; }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key == null) return false;
            var val = key.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch { return false; }
    }

    public static bool SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key == null) return false;
            if (enable)
            {
                var exe = GetExePath();
                if (string.IsNullOrWhiteSpace(exe)) return false;
                key.SetValue(ValueName, exe);
                WindowTracker.AddExternalLog($"자동 시작 등록됨: {exe}");
            }
            else
            {
                if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    WindowTracker.AddExternalLog("자동 시작 해제됨");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            WindowTracker.AddExternalLog($"자동 시작 설정 실패: {ex.Message}");
            return false;
        }
    }

    public static void EnsureState(bool desired)
    {
        try
        {
            var current = IsEnabled();
            if (current != desired)
            {
                SetEnabled(desired);
            }
        }
        catch { }
    }
}
