using System.Text;
using Xunit;

namespace BgToggle.Tests;

public class HelperProtocolTests
{
    [Fact]
    public void Wire_Roundtrip()
    {
        var ms = new MemoryStream();
        var sent = new HelperRequest(1, HelperProtocol.StopServices, "razer-synapse");

        HelperWire.Write(ms, sent);
        ms.Position = 0;

        Assert.Equal(sent, HelperWire.Read<HelperRequest>(ms));
    }

    [Fact]
    public void Wire_UsesCamelCaseJsonLines()
    {
        var ms = new MemoryStream();
        HelperWire.Write(ms, new HelperRequest(1, HelperProtocol.Ping));

        Assert.StartsWith("{\"protocol\":1,\"op\":\"ping\"", Encoding.UTF8.GetString(ms.ToArray()));
        Assert.EndsWith("\n", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public void Wire_Eof_ReturnsNull() =>
        Assert.Null(HelperWire.Read<HelperRequest>(new MemoryStream()));

    [Fact]
    public void Wire_Oversize_Throws()
    {
        var ms = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 70_000) + "\n"));
        Assert.Throws<InvalidDataException>(() => HelperWire.Read<HelperRequest>(ms));
    }

    [Fact]
    public void Wire_NotJson_Throws()
    {
        var ms = new MemoryStream(Encoding.UTF8.GetBytes("hello\n"));
        Assert.Throws<InvalidDataException>(() => HelperWire.Read<HelperRequest>(ms));
    }

    private static Recipe Vendor(string[]? services = null, string[]? leaveAlone = null) => new(
        "vendor", "Vendor", new[] { "razer.exe" },
        Shutdown: ShutdownStrategy.StopServiceThenKill,
        ServiceNames: services ?? new[] { "A", "B" }, ServicesToLeaveAlone: leaveAlone);

    [Fact]
    public void AllowList_RemovesLeaveAlone()
    {
        var allow = new HelperAllowList(new[] { Vendor(leaveAlone: new[] { "b" }) });

        Assert.True(allow.TryGet("VENDOR", out var r));
        Assert.Equal(new[] { "A" }, r.ServiceNames);
        Assert.Equal(new[] { "razer" }, r.ProcessNames);
    }

    [Fact]
    public void AllowList_UnknownId_False() =>
        Assert.False(new HelperAllowList(new[] { Vendor() }).TryGet("other", out _));

    private static readonly IReadOnlySet<string> Listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "razer" };
    private static readonly IReadOnlySet<int> ServicePids = new HashSet<int> { 500 };

    [Fact]
    public void KillRule_ClientSession_ListedName_Allowed() =>
        Assert.True(KillRule.MayKill(new RunningProcess(10, 1, "Razer"), 1, Listed, ServicePids));

    [Fact]
    public void KillRule_ClientSession_UnlistedName_Denied() =>
        Assert.False(KillRule.MayKill(new RunningProcess(10, 1, "notepad"), 1, Listed, ServicePids));

    [Fact]
    public void KillRule_OtherUserSession_Denied() =>
        Assert.False(KillRule.MayKill(new RunningProcess(10, 2, "razer"), 1, Listed, ServicePids));

    [Fact]
    public void KillRule_Session0_ServicePid_Allowed() =>
        Assert.True(KillRule.MayKill(new RunningProcess(500, 0, "RzSvc"), 1, Listed, ServicePids));

    [Fact]
    public void KillRule_Session0_ListedNameButNotServicePid_Denied() =>
        Assert.False(KillRule.MayKill(new RunningProcess(10, 0, "razer"), 1, Listed, ServicePids));

    [Fact]
    public void KillRule_ClientInSession0_OnlyServicePids()
    {
        Assert.False(KillRule.MayKill(new RunningProcess(10, 0, "razer"), 0, Listed, ServicePids));
        Assert.True(KillRule.MayKill(new RunningProcess(500, 0, "RzSvc"), 0, Listed, ServicePids));
    }
}
