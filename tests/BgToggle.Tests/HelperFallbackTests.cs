using Xunit;

namespace BgToggle.Tests;

[CollectionDefinition("ProcessManagerHelper", DisableParallelization = true)]
public class ProcessManagerHelperCollection { }

[Collection("ProcessManagerHelper")]
public class HelperFallbackTests : IDisposable
{
    private readonly FakeHelperClient _helper = new();

    public HelperFallbackTests() => ProcessManager.Helper = _helper;
    public void Dispose() => ProcessManager.Helper = null;

    private const string MissingService = "bgtoggle-test-no-such-service";

    private static ResolvedApp App(string[] services, bool eligible = true,
        ShutdownStrategy shutdown = ShutdownStrategy.StopServiceOnly) => new(
            Id: "vendor", DisplayName: "Vendor", ProcessNames: new[] { "bgtoggle-test-no-such-process-zzz" },
            LaunchPath: null, LaunchArgs: null, Shutdown: shutdown, ShutdownCommand: null,
            ServiceNames: services, ServicesToLeaveAlone: Array.Empty<string>(), GracefulTimeoutMs: 1000,
            Autostart: AutostartMethod.None, AutostartKey: null, RequiresAdmin: true, Notes: null,
            RecipeId: "vendor", HelperEligible: eligible);

    [Fact]
    public void Eligible_HelperRunning_StopUsesHelper()
    {
        ProcessManager.Stop(App(new[] { MissingService }));

        Assert.Contains((HelperProtocol.StopServices, "vendor"), _helper.Calls);
    }

    [Fact]
    public void Eligible_HelperMissing_NoHelperCall()
    {
        _helper.Status = HelperStatus.NotInstalled;

        ProcessManager.Stop(App(new[] { MissingService }));

        Assert.Empty(_helper.Calls);
    }

    [Fact]
    public void NotEligible_NoHelperCall()
    {
        ProcessManager.Stop(App(new[] { MissingService }, eligible: false));

        Assert.Empty(_helper.Calls);
    }

    [Fact]
    public void Start_Eligible_UsesHelperStartServices()
    {
        ProcessManager.Start(App(new[] { MissingService }));

        Assert.Equal(new[] { (HelperProtocol.StartServices, (string?)"vendor") }, _helper.Calls);
    }

    [Fact]
    public void Stop_StillRunning_AsksHelperToKill()
    {
        // RpcSs never stops (and the fake doesn't try), so the app still
        // reads as running afterwards.
        ProcessManager.Stop(App(new[] { "RpcSs" }));

        Assert.Contains((HelperProtocol.KillProcesses, "vendor"), _helper.Calls);
    }

    [Fact]
    public void Stop_Stopped_NoKillRequest()
    {
        ProcessManager.Stop(App(new[] { MissingService }));

        Assert.DoesNotContain(_helper.Calls, c => c.Op == HelperProtocol.KillProcesses);
    }

    [Fact]
    public void HelperLogLines_ReachLogCallback()
    {
        var lines = new List<string>();

        ProcessManager.Stop(App(new[] { MissingService }), lines.Add);

        Assert.Contains(lines, l => l.Contains("fake stopServices"));
    }

    [Fact]
    public void HklmAutostart_IsNoOp_NoThrow()
    {
        var app = App(new[] { MissingService }) with
        {
            Autostart = AutostartMethod.RegistryRun, AutostartKey = "bgtoggle-test-x"
        };

        AutostartManager.Disable(app);
        AutostartManager.Enable(app);
    }

    [Fact]
    public void AppLabel_AdminSuffix_OnlyWithoutHelper()
    {
        Assert.Equal("Razer  (admin)", TrayApp.AppLabel("Razer", requiresAdmin: true, HelperStatus.NotInstalled));
        Assert.Equal("Razer", TrayApp.AppLabel("Razer", requiresAdmin: true, HelperStatus.Running));
        Assert.Equal("Razer", TrayApp.AppLabel("Razer", requiresAdmin: false, HelperStatus.NotInstalled));
    }
}
