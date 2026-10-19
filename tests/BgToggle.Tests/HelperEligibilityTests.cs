using Xunit;

namespace BgToggle.Tests;

public class HelperEligibilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bgtoggle-recipes-" + Guid.NewGuid().ToString("N"));
    private string Bundled => Path.Combine(_dir, "bundled.json");
    private string Local => Path.Combine(_dir, "local.json");

    public HelperEligibilityTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static Recipe R(string id, string display = "X") => new(id, display, new[] { id });

    [Fact]
    public void LoadFrom_BundledOnlyId_IsBundled()
    {
        RecipeFile.Save(Bundled, new[] { R("a") });
        RecipeFile.Save(Local, Array.Empty<Recipe>());

        var set = RecipeStore.LoadFrom(Bundled, Local);

        Assert.Equal(new[] { "a" }, set.BundledIds);
    }

    [Fact]
    public void LoadFrom_LocalOverride_IsNotBundled()
    {
        RecipeFile.Save(Bundled, new[] { R("a", "Bundled A") });
        RecipeFile.Save(Local, new[] { R("a", "Local A") });

        var set = RecipeStore.LoadFrom(Bundled, Local);

        Assert.DoesNotContain("a", set.BundledIds);
        Assert.Equal("Local A", Assert.Single(set.Recipes).DisplayName);
    }

    [Fact]
    public void LoadFrom_LocalOnlyId_IsNotBundled()
    {
        RecipeFile.Save(Bundled, new[] { R("a") });
        RecipeFile.Save(Local, new[] { R("b") });

        var set = RecipeStore.LoadFrom(Bundled, Local);

        Assert.Equal(new[] { "a" }, set.BundledIds);
        Assert.Equal(new[] { "a", "b" }, set.Recipes.Select(r => r.Id).Order());
    }

    [Fact]
    public void LoadFrom_MissingLocal_Ok()
    {
        RecipeFile.Save(Bundled, new[] { R("a") });

        var set = RecipeStore.LoadFrom(Bundled, Local);

        Assert.Equal(new[] { "a" }, set.BundledIds);
    }

    [Fact]
    public void LoadFrom_BundledIdsAreCaseInsensitive()
    {
        RecipeFile.Save(Bundled, new[] { R("Razer") });

        Assert.Contains("razer", RecipeStore.LoadFrom(Bundled, Local).BundledIds);
    }

    private static readonly AppDefinition App = new("vendor", "Vendor", RecipeId: "vendor");

    [Fact]
    public void Resolve_BundledRecipe_IsEligible()
    {
        var resolved = AppResolver.Resolve(App, R("vendor"), recipeIsBundled: true);

        Assert.True(resolved.HelperEligible);
        Assert.Equal("vendor", resolved.RecipeId);
    }

    [Fact]
    public void Resolve_LocalRecipe_NotEligible() =>
        Assert.False(AppResolver.Resolve(App, R("vendor"), recipeIsBundled: false).HelperEligible);

    [Fact]
    public void Resolve_NoRecipe_NotEligible()
    {
        var resolved = AppResolver.Resolve(App, null, recipeIsBundled: true);

        Assert.False(resolved.HelperEligible);
        Assert.Null(resolved.RecipeId);
    }
}
