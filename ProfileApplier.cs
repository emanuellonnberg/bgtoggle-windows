namespace BgToggle;

public record DiffResult(
    IReadOnlyList<ResolvedApp> ToStart,
    IReadOnlyList<ResolvedApp> ToStop,
    IReadOnlyList<ResolvedApp> AlreadyOk
);

public static class ProfileApplier
{
    /// <summary>
    /// Compute the diff between current state (what's actually running) and the
    /// target profile. Apps not in the config are ignored.
    /// </summary>
    public static DiffResult Diff(Profile target, Config config, ProcessSnapshot? snapshot = null)
    {
        var toStart = new List<ResolvedApp>();
        var toStop = new List<ResolvedApp>();
        var ok = new List<ResolvedApp>();
        snapshot ??= ProcessSnapshot.Take();

        foreach (var app in config.Apps)
        {
            var resolved = AppResolver.Resolve(app);
            var shouldRun = target.Includes(app.Id);
            var isRunning = ProcessManager.IsRunning(resolved, snapshot);

            if (shouldRun && !isRunning) toStart.Add(resolved);
            else if (!shouldRun && isRunning) toStop.Add(resolved);
            else ok.Add(resolved);
        }

        return new DiffResult(toStart, toStop, ok);
    }

    public static void Apply(Profile target, Config config, Action<string>? log = null) =>
        Apply(Diff(target, config), log);

    /// <summary>
    /// Apply a precomputed diff, e.g. the one the user just confirmed.
    /// Touches no UI, so it is safe to run off the UI thread.
    /// </summary>
    public static void Apply(DiffResult diff, Action<string>? log = null)
    {
        // Stop first so apps freeing up VRAM/RAM/audio devices do so before we
        // start the new ones.
        foreach (var app in diff.ToStop)
        {
            log?.Invoke($"Stopping {app.DisplayName}");
            if (app.Autostart != AutostartMethod.None)
                Guard(app, "disable autostart", () => AutostartManager.Disable(app), log);
            Guard(app, "stop", () => ProcessManager.Stop(app, log), log);
        }

        foreach (var app in diff.ToStart)
        {
            log?.Invoke($"Starting {app.DisplayName}");
            if (app.Autostart != AutostartMethod.None)
                Guard(app, "enable autostart", () => AutostartManager.Enable(app), log);
            Guard(app, "start", () => ProcessManager.Start(app, log), log);
        }
    }

    /// <summary>
    /// One app failing (e.g. an HKLM autostart write while not elevated)
    /// must not abort the rest of the profile.
    /// </summary>
    private static void Guard(ResolvedApp app, string what, Action action, Action<string>? log)
    {
        try { action(); }
        catch (Exception ex)
        {
            var msg = $"{app.DisplayName}: {what} failed: {ex.GetType().Name}: {ex.Message}";
            if (log is not null) log(msg);
            else Logger.Warn(msg);
        }
    }
}
