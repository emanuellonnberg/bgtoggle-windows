using Xunit;

namespace BgToggle.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bgtoggle-tests-" + Guid.NewGuid().ToString("N"));
    private string ConfigFile => Path.Combine(_dir, "config.json");

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_MissingFile_CreatesDefaultWithAllAppsEverything()
    {
        var cfg = ConfigStore.Load(ConfigFile);

        Assert.True(File.Exists(ConfigFile));
        var everything = Assert.Single(cfg.Profiles, p => p.Name == "Everything");
        Assert.True(everything.AllApps);
        var allOff = Assert.Single(cfg.Profiles, p => p.Name == "All off");
        Assert.False(allOff.AllApps);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsConfigLoadException()
    {
        File.WriteAllText(ConfigFile, "{ \"apps\": [ oops ");

        var ex = Assert.Throws<ConfigLoadException>(() => ConfigStore.Load(ConfigFile));
        Assert.Equal(ConfigFile, ex.Path);
    }

    [Fact]
    public void Load_MissingArrays_NormalizedToEmpty()
    {
        File.WriteAllText(ConfigFile, "{ \"activeProfile\": \"Working\" }");

        var cfg = ConfigStore.Load(ConfigFile);

        Assert.NotNull(cfg.Apps);
        Assert.NotNull(cfg.Profiles);
        Assert.Equal("Working", cfg.ActiveProfile);
    }

    [Fact]
    public void Load_LegacyEmptyEverythingProfile_MigratedToAllApps()
    {
        File.WriteAllText(ConfigFile, """
            {
              "apps": [ { "id": "spotify", "displayName": "Spotify", "exeName": "Spotify" } ],
              "profiles": [
                { "name": "All off", "appIds": [] },
                { "name": "Everything", "appIds": [] }
              ]
            }
            """);

        var cfg = ConfigStore.Load(ConfigFile);

        Assert.True(cfg.Profiles.Single(p => p.Name == "Everything").AllApps);
        Assert.False(cfg.Profiles.Single(p => p.Name == "All off").AllApps);
        // Migration is persisted so it only happens once.
        Assert.Contains("\"allApps\": true", File.ReadAllText(ConfigFile));
    }

    [Fact]
    public void Load_EverythingProfileWithApps_NotMigrated()
    {
        File.WriteAllText(ConfigFile, """
            { "apps": [], "profiles": [ { "name": "Everything", "appIds": ["spotify"] } ] }
            """);

        var cfg = ConfigStore.Load(ConfigFile);

        Assert.False(cfg.Profiles.Single().AllApps);
    }

    [Fact]
    public void Save_ThenLoad_Roundtrips_AndLeavesNoTempFile()
    {
        var cfg = new Config(
            Apps: new() { new AppDefinition("a", "A", ExeName: "a") },
            Profiles: new() { new Profile("P", new HashSet<string> { "a" }, Hotkey: "Ctrl+Alt+1") },
            ActiveProfile: "P");

        ConfigStore.Save(cfg, ConfigFile);
        ConfigStore.Save(cfg with { ActiveProfile = null }, ConfigFile); // overwrite path
        var back = ConfigStore.Load(ConfigFile);

        Assert.Null(back.ActiveProfile);
        Assert.Equal("a", Assert.Single(back.Apps).Id);
        Assert.Contains("a", Assert.Single(back.Profiles).AppIds);
        Assert.Equal(new[] { "config.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}

public class ProfileApplierTests
{
    private static readonly AppDefinition NotRunning =
        new("ghost", "Ghost", ExeName: "bgtoggle-test-no-such-process-zzz");

    [Fact]
    public void Diff_AllAppsProfile_StartsEveryApp()
    {
        var cfg = new Config(new() { NotRunning }, new());
        var diff = ProfileApplier.Diff(new Profile("Everything", new HashSet<string>(), AllApps: true), cfg);

        Assert.Equal("ghost", Assert.Single(diff.ToStart).Id);
    }

    [Fact]
    public void Diff_EmptyProfile_StartsNothing()
    {
        var cfg = new Config(new() { NotRunning }, new());
        var diff = ProfileApplier.Diff(new Profile("All off", new HashSet<string>()), cfg);

        Assert.Empty(diff.ToStart);
    }
}
