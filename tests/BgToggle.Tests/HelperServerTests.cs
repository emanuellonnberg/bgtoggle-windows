using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Xunit;

namespace BgToggle.Tests;

public class HelperServerTests : IDisposable
{
    private readonly string _pipe = "BgToggle.Test." + Guid.NewGuid().ToString("N");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bgtoggle-helper-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _cts = new();
    private readonly FakeActions _actions = new();
    private Task? _run;

    private string RecipesPath => Path.Combine(_dir, "recipes.json");

    public HelperServerTests()
    {
        Directory.CreateDirectory(_dir);
        RecipeFile.Save(RecipesPath, new[] { Recipe("vendor") });
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _run?.Wait(5000); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Recipe Recipe(string id) => new(id, id, new[] { id },
        Shutdown: ShutdownStrategy.StopServiceThenKill, ServiceNames: new[] { "VendorSvc" });

    private void StartServer(PipeSecurity? security = null) =>
        _run = new HelperServer(_pipe, RecipesPath, _actions, "9.9.9", security).RunAsync(_cts.Token);

    private HelperClient Client() => new(_pipe, () => Environment.ProcessId);

    [Fact]
    public void Ping_RoundTrip()
    {
        StartServer();

        var r = Client().Send(HelperProtocol.Ping, null);

        Assert.NotNull(r);
        Assert.True(r!.Ok);
        Assert.Equal("9.9.9", r.AppVersion);
    }

    [Fact]
    public void StopServices_KnownRecipe_CallsActions()
    {
        StartServer();

        var r = Client().Send(HelperProtocol.StopServices, "vendor");

        Assert.True(r!.Ok);
        Assert.Equal(new[] { "VendorSvc" }, _actions.Stopped);
    }

    [Fact]
    public void UnknownRecipe_Refused()
    {
        StartServer();

        var r = Client().Send(HelperProtocol.StopServices, "nope");

        Assert.False(r!.Ok);
        Assert.Empty(_actions.Stopped);
    }

    [Fact]
    public void Garbage_ThenValidRequest_StillServed()
    {
        StartServer();
        using (var raw = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut))
        {
            raw.Connect(2000);
            raw.Write(Encoding.UTF8.GetBytes("{{{\n"));
            var reply = HelperWire.Read<HelperResponse>(raw);
            Assert.False(reply!.Ok);
            Assert.Equal("bad request", reply.Error);
        }

        Assert.True(Client().Send(HelperProtocol.Ping, null)!.Ok);
    }

    [Fact]
    public void ClientDisconnect_ThenValidRequest_StillServed()
    {
        StartServer();
        using (var raw = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut))
            raw.Connect(2000);

        Assert.True(Client().Send(HelperProtocol.Ping, null)!.Ok);
    }

    [Fact]
    public void SilentClient_DoesNotBlockOthers()
    {
        StartServer();
        using var idle = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut);
        idle.Connect(2000);

        var r = Client().Send(HelperProtocol.Ping, null);

        Assert.True(r!.Ok);
    }

    [Fact]
    public void MissingRecipes_RefusesOps_PingWorks()
    {
        File.Delete(RecipesPath);
        StartServer();
        var client = Client();

        Assert.True(client.Send(HelperProtocol.Ping, null)!.Ok);
        Assert.False(client.Send(HelperProtocol.StopServices, "vendor")!.Ok);
        Assert.Empty(_actions.Stopped);
    }

    [Fact]
    public void RecipesFileChanged_ReloadedOnPing()
    {
        StartServer();
        var client = Client();
        Assert.False(client.Send(HelperProtocol.StopServices, "other")!.Ok);

        RecipeFile.Save(RecipesPath, new[] { Recipe("other") });
        File.SetLastWriteTimeUtc(RecipesPath, DateTime.UtcNow.AddMinutes(1));
        client.Send(HelperProtocol.Ping, null);

        Assert.True(client.Send(HelperProtocol.StopServices, "other")!.Ok);
    }

    [Fact]
    public void StrictAcl_CurrentUserCanConnect()
    {
        // The strict ACL admits interactive logons only; CI runners may not
        // carry that SID, in which case there is nothing meaningful to test.
        var interactive = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(interactive)) return;

        StartServer(HelperPipeSecurity.Create());

        // One connection only: under this ACL a non-SYSTEM test server may
        // not create a second pipe instance (the real helper runs as SYSTEM).
        Assert.Equal(HelperStatus.Running, Client().Status);
    }
}

public class HelperClientTests
{
    private static string NewPipe() => "BgToggle.Test." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Unavailable_ReturnsQuickly()
    {
        var client = new HelperClient(NewPipe(), () => Environment.ProcessId);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var r = client.Send(HelperProtocol.Ping, null);

        Assert.Null(r);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(HelperStatus.NotInstalled, client.Status);
    }

    [Fact]
    public void ServiceNotInstalled_DoesNotConnect()
    {
        var client = new HelperClient(NewPipe(), () => null);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Assert.Equal(HelperStatus.NotInstalled, client.Status);
        Assert.True(sw.ElapsedMilliseconds < 200, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void WrongServerPid_TreatedAsMissing()
    {
        var pipe = NewPipe();
        using var cts = new CancellationTokenSource();
        var run = new HelperServer(pipe, (req, _) => new HelperResponse(HelperProtocol.Version, true), null).RunAsync(cts.Token);
        var client = new HelperClient(pipe, () => -1);

        Assert.Null(client.Send(HelperProtocol.Ping, null));
        Assert.Equal(HelperStatus.NotInstalled, client.Status);
        cts.Cancel();
    }

    [Fact]
    public void ProtocolMismatch_Incompatible()
    {
        var pipe = NewPipe();
        using var cts = new CancellationTokenSource();
        var run = new HelperServer(pipe, (req, _) => new HelperResponse(99, false, "protocol mismatch: helper speaks 99"), null).RunAsync(cts.Token);
        var client = new HelperClient(pipe, () => Environment.ProcessId);

        Assert.Equal(HelperStatus.Incompatible, client.Status);
        Assert.Null(client.Send(HelperProtocol.StopServices, "vendor"));
        cts.Cancel();
    }

    [Fact]
    public void Running_WhenServerAnswers()
    {
        var pipe = NewPipe();
        using var cts = new CancellationTokenSource();
        var run = new HelperServer(pipe, (req, _) => new HelperResponse(HelperProtocol.Version, true), null).RunAsync(cts.Token);

        Assert.Equal(HelperStatus.Running, new HelperClient(pipe, () => Environment.ProcessId).Status);
        cts.Cancel();
    }
}
