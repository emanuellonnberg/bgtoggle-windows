namespace BgToggle;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var startup = StartupArgs.Parse(args);
        if (startup.UninstallCleanup)
        {
            UninstallCleanup();
            return;
        }
        if (startup.FirstRun && startup.StartAtLogon is bool atLogon)
        {
            try { TrayAutostart.Set(atLogon, Application.ExecutablePath); }
            catch (Exception ex) { Logger.Error("Setting start-at-logon failed", ex); }
        }
        if (startup.ExitAfterSetup) return;

        // Single-instance lock. Global\ prefix so it works across user
        // sessions (e.g. RDP); user-name suffix scopes it per-user so two
        // users on the same box can each run their own BgToggle.
        var mutexName = $"Global\\BgToggle.SingleInstance.{Environment.UserName}";
        Mutex? mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance created it and ours may not open it.
            // Either way, one is already running.
            mutex = null;
            createdNew = false;
        }
        using var _ = mutex;
        if (!createdNew)
        {
            ApplicationConfiguration.Initialize();
            MessageBox.Show("BgToggle is already running (check the system tray).",
                "BgToggle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Logger.Prune();
        Logger.Info($"BgToggle starting (PID {Environment.ProcessId})");

        ApplicationConfiguration.Initialize();

        // Exceptions in menu/hotkey/timer handlers land here instead of the
        // try/catch below. Log them and keep the tray running.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Logger.Error("Unhandled UI exception", e.Exception);
            MessageBox.Show($"{e.Exception.Message}\n\nDetails were written to the log:\n{Logger.CurrentLogPath}",
                "BgToggle — unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Logger.Error("Unhandled background exception", ex);
        };

        try
        {
            Application.Run(new TrayApp());
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal exception in message loop", ex);
            throw;
        }
        finally
        {
            Logger.Info("BgToggle exiting");
        }
    }

    /// <summary>
    /// Run by the uninstaller (elevated, so HKCU is whoever ran it). Restore
    /// what BgToggle disabled and remove its own logon entry; best effort.
    /// </summary>
    private static void UninstallCleanup()
    {
        try { Logger.Info($"Uninstall cleanup: restored {AutostartManager.RestoreAllForCurrentUser()} per-user autostart entries"); }
        catch (Exception ex) { Logger.Error("Uninstall cleanup (per-user) failed", ex); }
        try { Logger.Info($"Uninstall cleanup: restored {AutostartManager.RestoreAllMachineWide()} machine-wide autostart entries"); }
        catch (Exception ex) { Logger.Error("Uninstall cleanup (machine-wide) failed", ex); }
        try { TrayAutostart.Set(false, Application.ExecutablePath); }
        catch (Exception ex) { Logger.Error("Uninstall cleanup (tray autostart) failed", ex); }
    }
}
