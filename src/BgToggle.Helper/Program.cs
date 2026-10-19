using System.ServiceProcess;

namespace BgToggle;

internal static class Program
{
    private static void Main(string[] args)
    {
        HelperHost.ConfigureLogging();

        if (args.Contains("--console"))
        {
            // Debugging aid: same server in the foreground (run elevated to
            // exercise service control for real).
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            Console.WriteLine($@"BgToggle helper {HelperHost.AppVersion} on \.\pipe\{HelperProtocol.PipeName}; Ctrl+C to stop");
            HelperHost.CreateServer().RunAsync(cts.Token).GetAwaiter().GetResult();
            return;
        }

        ServiceBase.Run(new HelperService());
    }
}
