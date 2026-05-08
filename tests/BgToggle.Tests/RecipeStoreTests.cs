using Xunit;

namespace BgToggle.Tests;

public class RecipeStoreTests
{
    [Fact]
    public void Load_FindsBuiltInRecipes()
    {
        var recipes = RecipeStore.Load(forceReload: true);
        Assert.NotEmpty(recipes);
        Assert.Contains(recipes, r => r.Id == "spotify");
        Assert.Contains(recipes, r => r.Id == "onedrive");
        Assert.Contains(recipes, r => r.Id == "razer-synapse");
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.NotNull(RecipeStore.Find("STEAM"));
        Assert.NotNull(RecipeStore.Find("steam"));
    }

    [Fact]
    public void Load_ExpandsEnvironmentVariables()
    {
        var spotify = RecipeStore.Find("spotify");
        Assert.NotNull(spotify);
        // Recipe ships with %APPDATA%\Spotify\Spotify.exe — ensure %APPDATA% was replaced.
        Assert.NotNull(spotify!.DefaultLaunchPath);
        Assert.DoesNotContain("%APPDATA%", spotify.DefaultLaunchPath);
        Assert.DoesNotContain("%LOCALAPPDATA%", spotify.DefaultLaunchPath);
    }

    [Fact]
    public void Validator_BuiltInRecipes_HaveNoSchemaErrors()
    {
        var recipes = RecipeStore.Load(forceReload: true);
        var issues = RecipeValidator.Validate(recipes);
        // INFO-level issues (service not installed locally, path missing) are
        // expected; ERROR-level ones are not.
        var errors = issues.Where(i => i.Severity == "ERROR").ToList();
        Assert.True(errors.Count == 0, "Schema errors found: " + string.Join("; ", errors));
    }
}
