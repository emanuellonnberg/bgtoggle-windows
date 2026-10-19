using Xunit;

namespace BgToggle.Tests;

public class HelperRequestHandlerTests
{
    private readonly FakeActions _actions = new();
    private readonly HelperRequestHandler _handler;

    public HelperRequestHandlerTests()
    {
        var vendor = new Recipe("vendor", "Vendor", new[] { "razer" },
            Shutdown: ShutdownStrategy.StopServiceThenKill,
            ServiceNames: new[] { "RzSvc", "RzDriver" }, ServicesToLeaveAlone: new[] { "RzDriver" });
        _handler = new HelperRequestHandler(new HelperAllowList(new[] { vendor }), _actions, "1.2.3");
    }

    private static HelperRequest Req(string op, string? id = "vendor", int protocol = HelperProtocol.Version) =>
        new(protocol, op, id);

    [Fact]
    public void ProtocolMismatch_Refused()
    {
        var r = _handler.Handle(Req(HelperProtocol.Ping, protocol: 2), clientSessionId: 1);

        Assert.False(r.Ok);
        Assert.Contains("protocol mismatch", r.Error);
        Assert.Equal(HelperProtocol.Version, r.Protocol);
    }

    [Fact]
    public void Ping_ReturnsVersion()
    {
        var r = _handler.Handle(Req(HelperProtocol.Ping, id: null), 1);

        Assert.True(r.Ok);
        Assert.Equal("1.2.3", r.AppVersion);
    }

    [Fact]
    public void UnknownOp_Refused()
    {
        var r = _handler.Handle(Req("formatDisk"), 1);

        Assert.False(r.Ok);
        Assert.Contains("unknown op 'formatDisk'", r.Error);
    }

    [Fact]
    public void UnknownRecipe_Refused_NoActions()
    {
        var r = _handler.Handle(Req(HelperProtocol.StopServices, "not-installed"), 1);

        Assert.False(r.Ok);
        Assert.Contains("recipe 'not-installed' is not in the installed recipes", r.Error);
        Assert.Equal(0, _actions.CallCount);
    }

    [Fact]
    public void MissingRecipeId_Refused()
    {
        var r = _handler.Handle(Req(HelperProtocol.StartServices, id: null), 1);

        Assert.False(r.Ok);
        Assert.Equal(0, _actions.CallCount);
    }

    [Fact]
    public void StopServices_UsesAllowListNotRequest()
    {
        var r = _handler.Handle(Req(HelperProtocol.StopServices), 1);

        Assert.True(r.Ok);
        Assert.Equal(new[] { "RzSvc" }, _actions.Stopped);
        Assert.NotEmpty(r.Log!);
    }

    [Fact]
    public void StartServices_UsesAllowList()
    {
        _handler.Handle(Req(HelperProtocol.StartServices), 1);

        Assert.Equal(new[] { "RzSvc" }, _actions.Started);
    }

    [Fact]
    public void Kill_AppliesSessionRule()
    {
        _actions.ServicePids["RzSvc"] = 12;
        _actions.Processes.AddRange(new[]
        {
            new RunningProcess(10, 1, "razer"),   // client session, listed → kill
            new RunningProcess(11, 2, "razer"),   // other user → leave
            new RunningProcess(12, 0, "RzSvc"),   // session 0, recipe service PID → kill
            new RunningProcess(13, 0, "razer"),   // session 0, not a service PID → leave
        });

        var r = _handler.Handle(Req(HelperProtocol.KillProcesses), 1);

        Assert.True(r.Ok);
        Assert.Equal(new[] { 10, 12 }, _actions.Killed.Order());
    }

    [Fact]
    public void Kill_OneFails_OthersStillKilled()
    {
        _actions.ServicePids["RzSvc"] = 12;
        _actions.Processes.AddRange(new[] { new RunningProcess(10, 1, "razer"), new RunningProcess(12, 0, "RzSvc") });
        _actions.FailKill.Add(10);

        var r = _handler.Handle(Req(HelperProtocol.KillProcesses), 1);

        Assert.True(r.Ok);
        Assert.Equal(new[] { 12 }, _actions.Killed);
        Assert.Contains(r.Log!, l => l.Contains("PID 10"));
    }

    [Fact]
    public void Kill_AllFail_NotOk()
    {
        _actions.Processes.Add(new RunningProcess(10, 1, "razer"));
        _actions.FailKill.Add(10);

        Assert.False(_handler.Handle(Req(HelperProtocol.KillProcesses), 1).Ok);
    }

    [Fact]
    public void Kill_Session0ServiceHost_WithoutTree_ClientSessionWithTree()
    {
        // A service host's tree can reach into other users' sessions
        // (per-session helpers), so only client-session roots take their tree.
        _actions.ServicePids["RzSvc"] = 12;
        _actions.Processes.AddRange(new[] { new RunningProcess(10, 1, "razer"), new RunningProcess(12, 0, "RzSvc") });

        _handler.Handle(Req(HelperProtocol.KillProcesses), 1);

        Assert.True(_actions.KilledTree[10]);
        Assert.False(_actions.KilledTree[12]);
    }

    [Fact]
    public void RecipesLoadError_ReportedClearly()
    {
        var handler = new HelperRequestHandler(HelperAllowList.Empty, _actions, "1.2.3",
            recipesError: "installed recipes.json could not be loaded");

        var r = handler.Handle(Req(HelperProtocol.StopServices), 1);

        Assert.False(r.Ok);
        Assert.Contains("installed recipes.json could not be loaded", r.Error);
        Assert.True(handler.Handle(Req(HelperProtocol.Ping, id: null), 1).Ok);
    }
}

public class SystemHelperActionsTests
{
    [Fact]
    public void Kill_RechecksIdentity_RefusesWhenNameDiffers()
    {
        // Stands in for PID reuse: the snapshot said "someone-else", but the
        // PID now belongs to another process. It must survive.
        var name = "bgtoggle-test-" + Guid.NewGuid().ToString("N")[..8];
        var exe = Path.Combine(Path.GetTempPath(), name + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exe);
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "/k")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true
        })!;
        try
        {
            var stale = new RunningProcess(proc.Id, proc.SessionId, "someone-else");

            Assert.Throws<InvalidOperationException>(() => new SystemHelperActions().Kill(stale, entireTree: false));
            Assert.False(proc.WaitForExit(500), "process with a different name must not be killed");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(); proc.WaitForExit(2000); } catch { }
            try { File.Delete(exe); } catch { }
        }
    }

    [Fact]
    public void HelperLogDir_IsUnderInstallDir() =>
        Assert.Equal(Path.Combine(@"C:\Program Files\BgToggle", "logs"), HelperPaths.LogDir(@"C:\Program Files\BgToggle"));
}
