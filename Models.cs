namespace BgToggle;

/// <summary>How an app should be terminated when switching it off.
/// Kept for back-compat with recipe-less AppDefinitions; new entries should
/// use a RecipeId and let the recipe's ShutdownStrategy drive behavior.</summary>
public enum KillMethod
{
    /// <summary>Send WM_CLOSE first, force-kill if it doesn't exit within timeout.</summary>
    Graceful,
    /// <summary>Kill immediately, including child processes.</summary>
    Force
}

/// <summary>
/// Where an app's autostart entry lives.
/// </summary>
public enum AutostartMethod
{
    None,
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</summary>
    RegistryRunUser,
    /// <summary>HKLM\Software\Microsoft\Windows\CurrentVersion\Run (needs admin to write).</summary>
    RegistryRun,
    /// <summary>Shortcut/script in the user's Startup folder.</summary>
    StartupFolder
}

/// <summary>
/// One managed app. May reference a Recipe (which carries shutdown knowledge,
/// process names, default install path) and override individual fields.
///
/// Resolution rules:
///   - If RecipeId is set, missing fields fall back to the recipe.
///   - LaunchPath, when set on AppDefinition, overrides Recipe.DefaultLaunchPath
///     (useful when the install lives in a non-standard location).
///   - Legacy entries (no RecipeId) are still honored: ExeName, KillMethod,
///     GracefulTimeoutMs are used directly.
/// </summary>
public record AppDefinition(
    string Id,
    string DisplayName,
    string ExeName = "",
    string? LaunchPath = null,
    string? LaunchArgs = null,
    KillMethod KillMethod = KillMethod.Graceful,
    int GracefulTimeoutMs = 3000,
    AutostartMethod Autostart = AutostartMethod.None,
    string? AutostartKey = null,
    string? RecipeId = null
);

/// <summary>
/// A profile is a set of app IDs that SHOULD be running. Apps in the config
/// but not in this set will be stopped when the profile is applied.
/// Hotkey: optional global shortcut, e.g. "Ctrl+Alt+1", "Win+Shift+G".
/// AllApps: every app in the config should run, including ones added later;
/// AppIds is ignored.
/// </summary>
public record Profile(
    string Name,
    HashSet<string> AppIds,
    string? Hotkey = null,
    bool AllApps = false
)
{
    public bool Includes(string appId) => AllApps || AppIds.Contains(appId);
}

/// <summary>
/// Auto-switch rule. When the named exe starts running, BgToggle applies
/// the WhileRunningProfile. When it exits, BgToggle either applies
/// OnExitProfile (if set) or restores the profile that was active before
/// the trigger fired. Per-trigger restore state is held in TrayApp so
/// nested triggers (game launcher → game) restore in LIFO order.
/// </summary>
public record Trigger(
    string ExeName,
    string WhileRunningProfile,
    string? OnExitProfile = null,
    bool Enabled = true
);

/// <summary>Top-level config persisted to JSON.</summary>
public record Config(
    List<AppDefinition> Apps,
    List<Profile> Profiles,
    string? ActiveProfile = null,
    int LargeDiffConfirmThreshold = 5,
    bool ConfirmLargeDiffs = true,
    List<Trigger>? Triggers = null
);

public static class AppResolver
{
    /// <summary>
    /// Combine an AppDefinition with its Recipe (if any) into a runtime plan.
    /// AppDefinition values override recipe values when set.
    /// </summary>
    public static ResolvedApp Resolve(AppDefinition app) =>
        Resolve(app, app.RecipeId is null ? null : RecipeStore.Find(app.RecipeId));

    public static ResolvedApp Resolve(AppDefinition app, Recipe? recipe)
    {
        var processNames = recipe?.ProcessNames is { Length: > 0 }
            ? recipe.ProcessNames
            : new[] { ExeNames.Strip(app.ExeName) };

        // If user supplied LaunchPath, prefer it. Otherwise recipe default.
        var launchPath = !string.IsNullOrWhiteSpace(app.LaunchPath)
            ? app.LaunchPath
            : recipe?.DefaultLaunchPath;
        var launchArgs = app.LaunchArgs ?? recipe?.DefaultLaunchArgs;

        var strategy = recipe?.Shutdown
            ?? (app.KillMethod == KillMethod.Force
                ? ShutdownStrategy.KillTree
                : ShutdownStrategy.Graceful);

        var leaveAlone = recipe?.ServicesToLeaveAlone ?? Array.Empty<string>();
        // Leave-alone services are driver-tied; never stop or start them even
        // if a recipe also lists them by mistake.
        var services = (recipe?.ServiceNames ?? Array.Empty<string>())
            .Except(leaveAlone, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Autostart override: if AppDefinition set it, win. Otherwise pick the
        // first recipe hint.
        var autostart = app.Autostart;
        var autostartKey = app.AutostartKey;
        if (autostart == AutostartMethod.None && recipe?.AutostartHints is { Length: > 0 } hints)
        {
            autostart = hints[0].Method;
            autostartKey = hints[0].Key;
        }

        return new ResolvedApp(
            Id: app.Id,
            DisplayName: app.DisplayName,
            ProcessNames: processNames,
            LaunchPath: launchPath,
            LaunchArgs: launchArgs,
            Shutdown: strategy,
            ShutdownCommand: recipe?.ShutdownCommand,
            ServiceNames: services,
            ServicesToLeaveAlone: leaveAlone,
            GracefulTimeoutMs: app.GracefulTimeoutMs,
            Autostart: autostart,
            AutostartKey: autostartKey,
            RequiresAdmin: recipe?.RequiresAdmin ?? false,
            Notes: recipe?.Notes
        );
    }
}
