namespace BgToggle;

/// <summary>
/// How an app is shut down. The right strategy depends on the app's bucket:
/// cooperator vs tray-trap vs service-watchdog vs service-only vs multi-process.
/// </summary>
public enum ShutdownStrategy
{
    /// <summary>WM_CLOSE → wait → kill tree. For apps that honor close.</summary>
    Graceful,
    /// <summary>Kill tree immediately. For tray-trap apps (Discord/Slack/Teams) and helpers.</summary>
    KillTree,
    /// <summary>Run a CLI for a clean shutdown (e.g. OneDrive /shutdown, steam -shutdown).</summary>
    Command,
    /// <summary>Stop services only. No UI process to kill.</summary>
    StopServiceOnly,
    /// <summary>Stop services first (kills the watchdog), then kill the UI tree.</summary>
    StopServiceThenKill
}

public record AutostartCandidate(AutostartMethod Method, string Key);

/// <summary>
/// Bundled knowledge for one app: process names, default install path, the
/// shutdown strategy that actually works for this app, plus any services to
/// stop or explicitly leave alone (e.g. NVIDIA driver services).
/// </summary>
public record Recipe(
    string Id,
    string DisplayName,
    string[] ProcessNames,
    string? DefaultLaunchPath = null,
    string? DefaultLaunchArgs = null,
    ShutdownStrategy Shutdown = ShutdownStrategy.Graceful,
    string? ShutdownCommand = null,
    string[]? ServiceNames = null,
    string[]? ServicesToLeaveAlone = null,
    AutostartCandidate[]? AutostartHints = null,
    string? Notes = null,
    bool RequiresAdmin = false
);

/// <summary>
/// Fully resolved per-app shutdown plan. Combines the user's AppDefinition
/// (overrides) on top of a Recipe (defaults). Runtime code consumes this.
/// </summary>
public record ResolvedApp(
    string Id,
    string DisplayName,
    string[] ProcessNames,
    string? LaunchPath,
    string? LaunchArgs,
    ShutdownStrategy Shutdown,
    string? ShutdownCommand,
    string[] ServiceNames,
    string[] ServicesToLeaveAlone,
    int GracefulTimeoutMs,
    AutostartMethod Autostart,
    string? AutostartKey,
    bool RequiresAdmin,
    string? Notes
);
