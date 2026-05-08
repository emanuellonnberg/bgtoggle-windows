using System.Diagnostics;

namespace BgToggle;

public static class ProcessManager
{
    public static IReadOnlyList<Process> FindRunning(string exeName)
    {
        var name = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exeName[..^4]
            : exeName;
        try
        {
            return Process.GetProcessesByName(name);
        }
        catch
        {
            return Array.Empty<Process>();
        }
    }

    public static IReadOnlyList<Process> FindRunning(IEnumerable<string> exeNames)
    {
        var list = new List<Process>();
        foreach (var n in exeNames) list.AddRange(FindRunning(n));
        return list;
    }

    public static bool IsRunning(string exeName) => FindRunning(exeName).Count > 0;

    public static bool IsRunning(IEnumerable<string> exeNames) =>
        exeNames.Any(n => FindRunning(n).Count > 0);

    public static void Launch(ResolvedApp app)
    {
        if (string.IsNullOrWhiteSpace(app.LaunchPath))
        {
            Debug.WriteLine($"No LaunchPath for {app.DisplayName}; cannot start.");
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
        catch (Exception ex) { Debug.WriteLine($"Launch failed for {app.DisplayName}: {ex.Message}"); }
    }

    /// <summary>
    /// Stop an app using its resolved shutdown strategy. Logs progress via the
    /// optional callback and swallows per-process exceptions so a single bad
    /// process doesn't abort a profile apply.
    /// </summary>
    public static void Stop(ResolvedApp app, Action<string>? log = null)
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
                ServiceManager.StopMany(app.ServiceNames, log);
                break;

            case ShutdownStrategy.StopServiceThenKill:
                ServiceManager.StopMany(app.ServiceNames, log);
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

    private static void Graceful(ResolvedApp app, Action<string>? log)
    {
        var procs = FindRunning(app.ProcessNames);
        foreach (var p in procs)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                    p.CloseMainWindow();
                if (!p.WaitForExit(app.GracefulTimeoutMs))
                    p.Kill(entireProcessTree: true);
            }
            catch (Exception ex) { log?.Invoke($"{app.DisplayName} stop failed (PID {p.Id}): {ex.Message}"); }
            finally { p.Dispose(); }
        }
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
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            if (!IsRunning(app.ProcessNames)) return true;
            Thread.Sleep(200);
        }
        return !IsRunning(app.ProcessNames);
    }
}
