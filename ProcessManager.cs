using System.Diagnostics;

namespace BgToggle;

public static class ProcessManager
{
    /// <summary>
    /// The elevated helper, set by the tray at startup. Null (or not running)
    /// means every operation is done locally, as unelevated BgToggle can.
    /// </summary>
    public static IHelperClient? Helper { get; set; }

    /// <summary>Processes matching the given names. Callers must dispose them.</summary>
    public static IReadOnlyList<Process> FindRunning(IEnumerable<string> exeNames)
    {
        var wanted = new HashSet<string>(exeNames.Select(ExeNames.Strip), StringComparer.OrdinalIgnoreCase);
        var list = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            bool match;
            try { match = wanted.Contains(p.ProcessName); }
            catch { match = false; /* exited mid-enumeration */ }

            if (match) list.Add(p);
            else p.Dispose();
        }
        return list;
    }

    public static bool IsRunning(string exeName) => ProcessSnapshot.Take().Contains(exeName);

    public static bool IsRunning(IEnumerable<string> exeNames) => ProcessSnapshot.Take().ContainsAny(exeNames);

    /// <summary>
    /// Whether the app counts as "on". Service-only apps have no UI process
    /// to look for, so their service state decides. Pass a snapshot when
    /// checking many apps at once.
    /// </summary>
    public static bool IsRunning(ResolvedApp app, ProcessSnapshot? snapshot = null) =>
        app.Shutdown == ShutdownStrategy.StopServiceOnly && app.ServiceNames.Length > 0
            ? app.ServiceNames.Any(ServiceManager.IsRunning)
            : (snapshot ?? ProcessSnapshot.Take()).ContainsAny(app.ProcessNames);

    /// <summary>
    /// Turn an app on: the reverse of <see cref="Stop"/>. Service-stop
    /// strategies get their services started first, since the UI usually
    /// needs its watchdog service to work.
    /// </summary>
    public static void Start(ResolvedApp app, Action<string>? log = null)
    {
        var usesServices = app.Shutdown is ShutdownStrategy.StopServiceOnly or ShutdownStrategy.StopServiceThenKill;
        if (usesServices && app.ServiceNames.Length > 0 && !TryHelper(app, HelperProtocol.StartServices, log))
            ServiceManager.StartMany(app.ServiceNames, log);

        // Service-only apps normally have nothing to launch.
        if (app.Shutdown == ShutdownStrategy.StopServiceOnly && string.IsNullOrWhiteSpace(app.LaunchPath))
            return;

        Launch(app);
    }

    public static void Launch(ResolvedApp app)
    {
        if (string.IsNullOrWhiteSpace(app.LaunchPath))
        {
            Logger.Warn($"No LaunchPath for {app.DisplayName}; cannot start.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = app.LaunchPath,
            Arguments = app.LaunchArgs ?? string.Empty,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(app.LaunchPath) ?? string.Empty
        };

        try { Process.Start(psi); }
        catch (Exception ex) { Logger.Error($"Launch failed for {app.DisplayName} ({app.LaunchPath})", ex); }
    }

    /// <summary>
    /// Stop an app using its resolved shutdown strategy. Logs progress via the
    /// optional callback and swallows per-process exceptions so a single bad
    /// process doesn't abort a profile apply.
    /// </summary>
    public static void Stop(ResolvedApp app, Action<string>? log = null)
    {
        StopLocally(app, log);

        // Whatever survived (elevated vendor UI, a service host whose stop
        // timed out) is the helper's job, if it may act for this app.
        if (CanUseHelper(app) && IsRunning(app))
            TryHelper(app, HelperProtocol.KillProcesses, log);
    }

    private static void StopLocally(ResolvedApp app, Action<string>? log)
    {
        switch (app.Shutdown)
        {
            case ShutdownStrategy.Command:
                StopViaCommand(app, log);
                // Some apps (Steam) take a few seconds to settle; if anything
                // remains, fall through to a kill tree as a safety net.
                if (WaitForExit(app, 5000)) return;
                log?.Invoke($"{app.DisplayName} did not exit after command; killing tree");
                KillTree(app, log);
                break;

            case ShutdownStrategy.StopServiceOnly:
                StopServices(app, log);
                break;

            case ShutdownStrategy.StopServiceThenKill:
                StopServices(app, log);
                KillTree(app, log);
                break;

            case ShutdownStrategy.KillTree:
                KillTree(app, log);
                break;

            case ShutdownStrategy.Graceful:
            default:
                Graceful(app, log);
                break;
        }
    }

    private static void StopServices(ResolvedApp app, Action<string>? log)
    {
        if (!TryHelper(app, HelperProtocol.StopServices, log))
            ServiceManager.StopMany(app.ServiceNames, log);
    }

    /// <summary>
    /// Only apps whose recipe came from the bundled file go to the helper:
    /// it acts on its own installed copy, keyed by id.
    /// </summary>
    private static bool CanUseHelper(ResolvedApp app) =>
        app.HelperEligible && app.RecipeId is not null && Helper?.Status == HelperStatus.Running;

    /// <summary>True if the helper did it; false means do it locally (or give up).</summary>
    private static bool TryHelper(ResolvedApp app, string op, Action<string>? log)
    {
        if (!CanUseHelper(app)) return false;

        var response = Helper!.Send(op, app.RecipeId);
        if (response is null)
        {
            log?.Invoke($"{app.DisplayName}: helper unavailable for {op}; trying locally");
            return false;
        }
        foreach (var line in response.Log ?? new())
            log?.Invoke($"{app.DisplayName} (helper): {line}");
        if (!response.Ok)
            log?.Invoke($"{app.DisplayName}: helper refused {op}: {response.Error}");
        return response.Ok;
    }

    private static void StopViaCommand(ResolvedApp app, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(app.LaunchPath) || string.IsNullOrWhiteSpace(app.ShutdownCommand))
        {
            log?.Invoke($"{app.DisplayName}: command shutdown but no LaunchPath/ShutdownCommand; falling back to kill tree");
            KillTree(app, log);
            return;
        }

        try
        {
            log?.Invoke($"{app.DisplayName}: invoking {Path.GetFileName(app.LaunchPath)} {app.ShutdownCommand}");
            var psi = new ProcessStartInfo
            {
                FileName = app.LaunchPath,
                Arguments = app.ShutdownCommand,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(app.LaunchPath) ?? string.Empty
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            log?.Invoke($"{app.DisplayName}: shutdown command failed: {ex.Message}");
        }
    }

    /// <summary>
    /// WM_CLOSE to every top-level window of every process, one shared wait,
    /// then kill whatever is left. If nothing could be asked to close there
    /// is nothing to wait for, so kill straight away.
    /// </summary>
    private static void Graceful(ResolvedApp app, Action<string>? log)
    {
        var posted = 0;
        var procs = FindRunning(app.ProcessNames);
        foreach (var p in procs)
        {
            try { posted += WindowCloser.PostClose(p.Id); }
            catch (Exception ex) { log?.Invoke($"{app.DisplayName} close failed (PID {p.Id}): {ex.Message}"); }
            finally { p.Dispose(); }
        }

        if (posted == 0)
        {
            log?.Invoke($"{app.DisplayName}: no windows to close; killing tree");
        }
        else
        {
            log?.Invoke($"{app.DisplayName}: sent WM_CLOSE to {posted} window(s); waiting up to {app.GracefulTimeoutMs} ms");
            if (WaitForExit(app, app.GracefulTimeoutMs)) return;
            log?.Invoke($"{app.DisplayName}: still running after {app.GracefulTimeoutMs} ms; killing tree");
        }
        KillTree(app, log);
    }

    private static void KillTree(ResolvedApp app, Action<string>? log)
    {
        var procs = FindRunning(app.ProcessNames);
        foreach (var p in procs)
        {
            try { p.Kill(entireProcessTree: true); }
            catch (Exception ex) { log?.Invoke($"{app.DisplayName} kill failed (PID {p.Id}): {ex.Message}"); }
            finally { p.Dispose(); }
        }
    }

    private static bool WaitForExit(ResolvedApp app, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!IsRunning(app.ProcessNames)) return true;
            Thread.Sleep(200);
        }
        return !IsRunning(app.ProcessNames);
    }
}
