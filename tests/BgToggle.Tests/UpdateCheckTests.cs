using Xunit;

namespace BgToggle.Tests;

public class UpdateCheckTests
{
    private static LatestRelease Release(string tag, bool prerelease = false, bool draft = false) =>
        new(tag, "https://example/releases/" + tag, prerelease, draft);

    [Fact]
    public void Parse_ValidJson()
    {
        var r = UpdateCheck.Parse("""{"tag_name":"v0.3.0","html_url":"https://x","prerelease":false,"draft":false,"assets":[]}""");

        Assert.Equal(new LatestRelease("v0.3.0", "https://x", false, false), r);
    }

    [Fact]
    public void Parse_Garbage_Null()
    {
        Assert.Null(UpdateCheck.Parse("<html>rate limited</html>"));
        Assert.Null(UpdateCheck.Parse("""{"message":"API rate limit exceeded"}"""));
        Assert.Null(UpdateCheck.Parse(""));
    }

    [Fact]
    public void ParseTag_Variants()
    {
        Assert.Equal(new Version(0, 3, 0), UpdateCheck.ParseTag("v0.3.0"));
        Assert.Equal(new Version(0, 3, 0), UpdateCheck.ParseTag("0.3.0"));
        Assert.Null(UpdateCheck.ParseTag("v0.3.0-beta"));
        Assert.Null(UpdateCheck.ParseTag("release-1"));
        Assert.Null(UpdateCheck.ParseTag(""));
    }

    [Fact]
    public void IsNewer_Newer_True() =>
        Assert.True(UpdateCheck.IsNewer(Release("v0.3.0"), new Version(0, 2, 0)));

    [Fact]
    public void IsNewer_SameOrOlder_False()
    {
        Assert.False(UpdateCheck.IsNewer(Release("v0.2.0"), new Version(0, 2, 0, 0)));
        Assert.False(UpdateCheck.IsNewer(Release("v0.1.0"), new Version(0, 2, 0)));
    }

    [Fact]
    public void IsNewer_DevBuild_False() =>
        Assert.False(UpdateCheck.IsNewer(Release("v9.0.0"), new Version(0, 0, 0, 0)));

    [Fact]
    public void IsNewer_PrereleaseOrDraft_False()
    {
        Assert.False(UpdateCheck.IsNewer(Release("v0.3.0", prerelease: true), new Version(0, 2, 0)));
        Assert.False(UpdateCheck.IsNewer(Release("v0.3.0", draft: true), new Version(0, 2, 0)));
    }

    [Fact]
    public void IsNewer_UnparseableTag_False() =>
        Assert.False(UpdateCheck.IsNewer(Release("release-1"), new Version(0, 2, 0)));

    [Fact]
    public void ShouldCheck_Windows()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(UpdateCheck.ShouldCheck(null, now));
        Assert.False(UpdateCheck.ShouldCheck(now.AddHours(-23), now));
        Assert.True(UpdateCheck.ShouldCheck(now.AddHours(-25), now));
    }

    [Fact]
    public void Config_DefaultsCheckForUpdatesTrue()
    {
        var path = Path.Combine(Path.GetTempPath(), "bgtoggle-cfg-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{ "apps": [], "profiles": [] }""");
        try
        {
            Assert.True(ConfigStore.Load(path).CheckForUpdates);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AppState_Roundtrip_AndBadFileGivesDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), "bgtoggle-state-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var when = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
            AppStateStore.Save(new AppState(when, "v0.3.0"), path);
            Assert.Equal(new AppState(when, "v0.3.0"), AppStateStore.Load(path));

            File.WriteAllText(path, "not json");
            Assert.Equal(new AppState(), AppStateStore.Load(path));
        }
        finally { File.Delete(path); }
    }
}
