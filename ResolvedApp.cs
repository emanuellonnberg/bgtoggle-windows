namespace BgToggle;

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
    string? Notes,
    string? RecipeId = null,
    // Recipe came from the bundled file, so the helper may act on it by id.
    bool HelperEligible = false
);
