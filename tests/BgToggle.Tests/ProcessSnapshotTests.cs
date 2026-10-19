using System.Diagnostics;
using Xunit;

namespace BgToggle.Tests;

public class ProcessSnapshotTests
{
    [Fact]
    public void Contains_IsCaseInsensitive_AndIgnoresExeSuffix()
    {
        var snap = new ProcessSnapshot(new[] { "Spotify" });

        Assert.True(snap.Contains("spotify"));
        Assert.True(snap.Contains("Spotify.EXE"));
        Assert.False(snap.Contains("Discord"));
    }

    [Fact]
    public void ContainsAny_MatchesIfAnyNamePresent()
    {
        var snap = new ProcessSnapshot(new[] { "Discord" });

        Assert.True(snap.ContainsAny(new[] { "Slack", "Discord.exe" }));
        Assert.False(snap.ContainsAny(new[] { "Slack", "Teams" }));
    }

    [Fact]
    public void Take_IncludesCurrentProcess()
    {
        using var self = Process.GetCurrentProcess();

        Assert.True(ProcessSnapshot.Take().Contains(self.ProcessName));
    }

    [Fact]
    public void StripExe_RemovesOnlyTrailingExe()
    {
        Assert.Equal("foo", ExeNames.Strip("foo.exe"));
        Assert.Equal("foo", ExeNames.Strip("foo.EXE"));
        Assert.Equal("foo.exe.bak", ExeNames.Strip("foo.exe.bak"));
        Assert.Equal("foo", ExeNames.Strip("foo"));
    }
}

public class ProfileApplierDiffTests
{
    private static readonly Config Cfg = new(
        Apps: new()
        {
            new AppDefinition("spotify", "Spotify", ExeName: "Spotify"),
            new AppDefinition("discord", "Discord", ExeName: "Discord"),
            new AppDefinition("steam", "Steam", ExeName: "steam")
        },
        Profiles: new());

    [Fact]
    public void Diff_UsesGivenSnapshot()
    {
        // Spotify + Discord "running"; profile wants Discord + Steam.
        var snap = new ProcessSnapshot(new[] { "Spotify", "Discord" });
        var profile = new Profile("Gaming", new HashSet<string> { "discord", "steam" });

        var diff = ProfileApplier.Diff(profile, Cfg, snap);

        Assert.Equal(new[] { "spotify" }, diff.ToStop.Select(a => a.Id));
        Assert.Equal(new[] { "steam" }, diff.ToStart.Select(a => a.Id));
        Assert.Equal(new[] { "discord" }, diff.AlreadyOk.Select(a => a.Id));
    }
}
