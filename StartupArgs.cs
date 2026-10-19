namespace BgToggle;

/// <summary>
/// Flags the installer passes. Setup runs the tray as the original
/// (unelevated) user with --first-run so the logon autostart lands in that
/// user's HKCU, not the admin's whose password was typed at the UAC prompt.
/// </summary>
public record StartupArgs(bool FirstRun, bool? StartAtLogon, bool ExitAfterSetup, bool UninstallCleanup)
{
    public static StartupArgs Parse(string[] args)
    {
        bool? logon = null;
        foreach (var a in args)
        {
            if (a.StartsWith("--start-at-logon=", StringComparison.OrdinalIgnoreCase))
            {
                logon = a["--start-at-logon=".Length..].ToLowerInvariant() switch
                {
                    "on" => true,
                    "off" => false,
                    _ => null
                };
            }
        }

        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);
        return new StartupArgs(Has("--first-run"), logon, Has("--exit-after-setup"), Has("--uninstall-cleanup"));
    }
}
