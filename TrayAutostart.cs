using Microsoft.Win32;

namespace BgToggle;

/// <summary>BgToggle's own "start at logon": a plain HKCU Run value, no elevation.</summary>
public static class TrayAutostart
{
    public const string ValueName = "BgToggle";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled() => IsEnabled(RunKeyPath);

    public static void Set(bool enabled, string exePath) => Set(enabled, exePath, RunKeyPath);

    internal static bool IsEnabled(string runKeyPath)
    {
        using var run = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return run?.GetValue(ValueName) is string;
    }

    internal static void Set(bool enabled, string exePath, string runKeyPath)
    {
        using var run = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (enabled) run.SetValue(ValueName, $"\"{exePath}\"");
        else run.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
