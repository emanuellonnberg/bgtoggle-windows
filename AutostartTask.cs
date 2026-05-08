using System.Diagnostics;

namespace BgToggle;

/// <summary>
/// Registers BgToggle.exe as a Windows Scheduled Task that runs at logon
/// with "highest privileges" — silent auto-elevation, no UAC prompt every
/// launch. Required for service-watchdog recipes (Razer, Adobe, iCUE, etc.)
/// to work under a normal user account.
///
/// Implemented via schtasks.exe (built-in, no NuGet). The /Create call
/// itself needs admin, so we shell out with ShellExecute verb "runas" which
/// triggers UAC once at install time.
/// </summary>
public static class AutostartTask
{
    public const string TaskName = "BgToggle";

    public static bool IsInstalled() => Run("/Query", $"/TN \"{TaskName}\"").exitCode == 0;

    public static bool Install(string exePath)
    {
        // /RL HIGHEST = run with highest privileges (silent elevation)
        // /SC ONLOGON = trigger on user logon
        // /F = overwrite existing
        var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F";
        return RunElevated(args);
    }

    public static bool Uninstall() =>
        RunElevated($"/Delete /TN \"{TaskName}\" /F");

    private static bool RunElevated(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(15000);
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Error("schtasks elevated launch failed", ex);
            return false;
        }
    }

    private static (int exitCode, string stdout) Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return (p.ExitCode, stdout);
        }
        catch (Exception ex)
        {
            Logger.Error("schtasks query failed", ex);
            return (-1, "");
        }
    }
}
