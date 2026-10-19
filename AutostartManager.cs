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
                WarnMachineWide(app);
                break;
            case AutostartMethod.StartupFolder:
                var path = StartupPath(app.AutostartKey);
                if (File.Exists(path))
                    File.Move(path, path + DisabledSuffix, overwrite: true);
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
                WarnMachineWide(app);
                break;
            case AutostartMethod.StartupFolder:
                var path = StartupPath(app.AutostartKey);
                var stash = path + DisabledSuffix;
                if (File.Exists(stash))
                    File.Move(stash, path, overwrite: true);
                break;
        }
    }

    /// <summary>
    /// Put back every autostart entry BgToggle disabled for the current
    /// user: stashed HKCU Run values and renamed Startup-folder files. Used
    /// before uninstall and from the tray's System menu.
    /// </summary>
    public static int RestoreAllForCurrentUser() =>
        RestoreAll(Registry.CurrentUser, RunKeyPath, StashKeyPath,
            Environment.GetFolderPath(Environment.SpecialFolder.Startup));

    /// <summary>Machine-wide leftovers from the old elevated tray; needs admin.</summary>
    public static int RestoreAllMachineWide() =>
        RestoreAll(Registry.LocalMachine, RunKeyPath, StashKeyPath, null);

    /// <returns>How many entries were restored.</returns>
    internal static int RestoreAll(RegistryKey root, string runKeyPath, string stashKeyPath, string? startupDir)
    {
        var restored = 0;

        using (var stash = root.OpenSubKey(stashKeyPath, writable: true))
        {
            if (stash is not null)
            {
                using var run = root.CreateSubKey(runKeyPath);
                foreach (var name in stash.GetValueNames())
                {
                    // Something re-registered itself meanwhile: keep the newer value.
                    if (run.GetValue(name) is null && stash.GetValue(name) is string value)
                    {
                        run.SetValue(name, value);
                        restored++;
                    }
                    stash.DeleteValue(name, throwOnMissingValue: false);
                }
            }
        }

        if (startupDir is not null && Directory.Exists(startupDir))
        {
            foreach (var disabled in Directory.EnumerateFiles(startupDir, "*" + DisabledSuffix))
            {
                var original = disabled[..^DisabledSuffix.Length];
                if (File.Exists(original)) continue;
                File.Move(disabled, original);
                restored++;
            }
        }

        return restored;
    }

    private const string DisabledSuffix = ".bgtoggle.disabled";

    // --- helpers ---

    /// <summary>
    /// HKLM Run only worked while the whole tray ran elevated. The tray now
    /// never does, and no bundled recipe needs it, so it is left alone.
    /// </summary>
    private static void WarnMachineWide(ResolvedApp app) =>
        Logger.Warn($"{app.DisplayName}: machine-wide (HKLM) autostart is no longer managed; see README");

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
