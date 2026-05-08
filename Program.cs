namespace BgToggle;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single-instance lock. Global\ prefix so it works across user
        // sessions (e.g. RDP); user-name suffix scopes it per-user so two
        // users on the same box can each run their own BgToggle.
        var mutexName = $"Global\\BgToggle.SingleInstance.{Environment.UserName}";
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
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
}
