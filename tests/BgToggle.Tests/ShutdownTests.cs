using System.Diagnostics;
using Xunit;

namespace BgToggle.Tests;

public class ShutdownTests
{
    private static ResolvedApp App(
        string[] processNames,
        ShutdownStrategy shutdown = ShutdownStrategy.Graceful,
        string[]? services = null,
        int gracefulTimeoutMs = 3000) => new(
            Id: "test", DisplayName: "Test", ProcessNames: processNames,
            LaunchPath: null, LaunchArgs: null, Shutdown: shutdown, ShutdownCommand: null,
            ServiceNames: services ?? Array.Empty<string>(), ServicesToLeaveAlone: Array.Empty<string>(),
            GracefulTimeoutMs: gracefulTimeoutMs, Autostart: AutostartMethod.None, AutostartKey: null,
            RequiresAdmin: false, Notes: null);

    [Fact]
    public void FindTopLevel_IncludesHiddenWindows()
    {
        // Tray apps usually have only hidden top-level windows; graceful
        // shutdown has to reach those, not just MainWindowHandle.
        var w = new NativeWindow();
        w.CreateHandle(new CreateParams { Caption = "bgtoggle-test-hidden" });
        try
        {
            var found = WindowCloser.FindTopLevel(Environment.ProcessId);
            Assert.Contains(w.Handle, found);
        }
        finally { w.DestroyHandle(); }
    }

    [Fact]
    public void Graceful_ProcessWithoutWindows_IsKilledWithoutWaitingForTimeout()
    {
        // Copy cmd.exe under a unique name so the test only ever matches
        // its own process. With stdin redirected and never written, cmd /k
        // blocks forever and owns no windows.
        var name = "bgtoggle-test-" + Guid.NewGuid().ToString("N")[..8];
        var exe = Path.Combine(Path.GetTempPath(), name + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exe);
        using var proc = Process.Start(new ProcessStartInfo(exe, "/k")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        })!;
        try
        {
            Assert.True(ProcessManager.IsRunning(name));

            var sw = Stopwatch.StartNew();
            ProcessManager.Stop(App(new[] { name }, gracefulTimeoutMs: 15000));
            sw.Stop();

            Assert.True(proc.WaitForExit(2000), "process should have been killed");
            Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms; should not wait the full graceful timeout");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(); proc.WaitForExit(2000); } catch { }
            try { File.Delete(exe); } catch { }
        }
    }

    [Fact]
    public void IsRunning_ServiceOnlyApp_UsesServiceState()
    {
        // RpcSs is always running on Windows; the process name never exists.
        var app = App(new[] { "bgtoggle-test-no-such-process-zzz" },
            ShutdownStrategy.StopServiceOnly, services: new[] { "RpcSs" });

        Assert.True(ProcessManager.IsRunning(app));
    }

    [Fact]
    public void IsRunning_ServiceOnlyApp_MissingService_IsFalse()
    {
        var app = App(new[] { "bgtoggle-test-no-such-process-zzz" },
            ShutdownStrategy.StopServiceOnly, services: new[] { "bgtoggle-test-no-such-service" });

        Assert.False(ProcessManager.IsRunning(app));
    }
}

public class ServicesToLeaveAloneTests
{
    private static Recipe WatchdogRecipe(string[] services, string[] leaveAlone) => new(
        Id: "vendor", DisplayName: "Vendor", ProcessNames: new[] { "vendor" },
        Shutdown: ShutdownStrategy.StopServiceThenKill,
        ServiceNames: services, ServicesToLeaveAlone: leaveAlone);

    [Fact]
    public void Resolve_FiltersLeaveAloneServicesOutOfServiceNames()
    {
        var recipe = WatchdogRecipe(new[] { "VendorSvc", "VendorDriverSvc" }, new[] { "vendordriversvc" });
        var resolved = AppResolver.Resolve(new AppDefinition("vendor", "Vendor", RecipeId: "vendor"), recipe);

        Assert.Equal(new[] { "VendorSvc" }, resolved.ServiceNames);
    }

    [Fact]
    public void Validator_FlagsServiceInBothLists()
    {
        var recipe = WatchdogRecipe(new[] { "VendorSvc", "VendorDriverSvc" }, new[] { "VendorDriverSvc" });

        var issues = RecipeValidator.Validate(new[] { recipe });

        Assert.Contains(issues, i => i.Severity == "ERROR" && i.Message.Contains("VendorDriverSvc"));
    }
}
