using System.ServiceProcess;

namespace BgToggle;

internal static class HelperHost
{
    public static string AppVersion { get; } =
        typeof(HelperHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>LocalSystem's %APPDATA% is not somewhere anyone looks.</summary>
    public static void ConfigureLogging() =>
        Logger.LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BgToggle", "logs");

    public static HelperServer CreateServer() => new(
        HelperProtocol.PipeName,
        Path.Combine(AppContext.BaseDirectory, "recipes.json"),
        new SystemHelperActions(),
        AppVersion,
        HelperPipeSecurity.Create());
}

internal sealed class HelperService : ServiceBase
{
    private CancellationTokenSource? _cts;
    private Task? _run;

    public HelperService() => ServiceName = HelperProtocol.ServiceName;

    protected override void OnStart(string[] args)
    {
        Logger.Info($"BgToggle helper {HelperHost.AppVersion} starting");
        _cts = new CancellationTokenSource();
        _run = HelperHost.CreateServer().RunAsync(_cts.Token);
    }

    protected override void OnStop()
    {
        _cts?.Cancel();
        try { _run?.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { /* cancellation */ }
        Logger.Info("BgToggle helper stopped");
    }
}
