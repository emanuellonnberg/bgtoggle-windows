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
    public static DiffResult Diff(Profile target, Config config)
    {
        var toStart = new List<ResolvedApp>();
        var toStop = new List<ResolvedApp>();
        var ok = new List<ResolvedApp>();

        foreach (var app in config.Apps)
        {
            var resolved = AppResolver.Resolve(app);
            var shouldRun = target.AppIds.Contains(app.Id);
            var isRunning = ProcessManager.IsRunning(resolved.ProcessNames);

            if (shouldRun && !isRunning) toStart.Add(resolved);
            else if (!shouldRun && isRunning) toStop.Add(resolved);
            else ok.Add(resolved);
        }

        return new DiffResult(toStart, toStop, ok);
    }

    public static void Apply(Profile target, Config config, Action<string>? log = null)
    {
        var diff = Diff(target, config);

        // Stop first so apps freeing up VRAM/RAM/audio devices do so before we
        // start the new ones.
        foreach (var app in diff.ToStop)
        {
            log?.Invoke($"Stopping {app.DisplayName}");
            if (app.Autostart != AutostartMethod.None)
                AutostartManager.Disable(app);
            ProcessManager.Stop(app, log);
        }

        foreach (var app in diff.ToStart)
        {
            log?.Invoke($"Starting {app.DisplayName}");
            if (app.Autostart != AutostartMethod.None)
                AutostartManager.Enable(app);
            ProcessManager.Launch(app);
        }
    }
}
