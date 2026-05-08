using Microsoft.Win32;

namespace BgToggle;

/// <summary>
/// Manages the most common autostart locations. When we disable an entry we
/// stash the original value under HKCU\Software\BgToggle\Stash so we can
/// restore it later.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StashKeyPath = @"Software\BgToggle\Stash";

    public static bool IsEnabled(ResolvedApp app)
    {
        if (app.AutostartKey is null) return false;

        return app.Autostart switch
        {
            AutostartMethod.RegistryRunUser =>
                ReadRunValue(Registry.CurrentUser, app.AutostartKey) != null,
            AutostartMethod.RegistryRun =>
                ReadRunValue(Registry.LocalMachine, app.AutostartKey) != null,
            AutostartMethod.StartupFolder =>
                File.Exists(StartupPath(app.AutostartKey)),
            _ => false
        };
    }

    public static void Disable(ResolvedApp app)
    {
        if (app.AutostartKey is null) return;

        switch (app.Autostart)
        {
            case AutostartMethod.RegistryRunUser:
                StashAndDeleteRun(Registry.CurrentUser, app.AutostartKey);
                break;
            case AutostartMethod.RegistryRun:
                StashAndDeleteRun(Registry.LocalMachine, app.AutostartKey);
                break;
            case AutostartMethod.StartupFolder:
                var path = StartupPath(app.AutostartKey);
                if (File.Exists(path))
                    File.Move(path, path + ".bgtoggle.disabled", overwrite: true);
                break;
        }
    }

    public static void Enable(ResolvedApp app)
    {
        if (app.AutostartKey is null) return;

        switch (app.Autostart)
        {
            case AutostartMethod.RegistryRunUser:
                RestoreRun(Registry.CurrentUser, app.AutostartKey);
                break;
            case AutostartMethod.RegistryRun:
                RestoreRun(Registry.LocalMachine, app.AutostartKey);
                break;
            case AutostartMethod.StartupFolder:
                var path = StartupPath(app.AutostartKey);
                var stash = path + ".bgtoggle.disabled";
                if (File.Exists(stash))
                    File.Move(stash, path, overwrite: true);
                break;
        }
    }

    // --- helpers ---

    private static string? ReadRunValue(RegistryKey root, string name)
    {
        using var key = root.OpenSubKey(RunKeyPath);
        return key?.GetValue(name) as string;
    }

    private static void StashAndDeleteRun(RegistryKey root, string name)
    {
        using var run = root.OpenSubKey(RunKeyPath, writable: true);
        var existing = run?.GetValue(name) as string;
        if (existing is null) return;

        using var stash = root.CreateSubKey(StashKeyPath);
        stash.SetValue(name, existing);

        run!.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void RestoreRun(RegistryKey root, string name)
    {
        using var stash = root.OpenSubKey(StashKeyPath, writable: true);
        var stashed = stash?.GetValue(name) as string;
        if (stashed is null) return;

        using var run = root.OpenSubKey(RunKeyPath, writable: true);
        run?.SetValue(name, stashed);
        stash!.DeleteValue(name, throwOnMissingValue: false);
    }

    private static string StartupPath(string filename) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            filename);
}
