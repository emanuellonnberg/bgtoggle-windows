using Xunit;

namespace BgToggle.Tests;

public class AppResolverTests
{
    [Fact]
    public void NoRecipe_LegacyKillForce_MapsToKillTree()
    {
        var app = new AppDefinition(Id: "x", DisplayName: "X", ExeName: "x", KillMethod: KillMethod.Force);
        var resolved = AppResolver.Resolve(app);

        Assert.Equal(ShutdownStrategy.KillTree, resolved.Shutdown);
        Assert.Equal(new[] { "x" }, resolved.ProcessNames);
        Assert.Empty(resolved.ServiceNames);
    }

    [Fact]
    public void NoRecipe_LegacyKillGraceful_MapsToGraceful()
    {
        var app = new AppDefinition(Id: "x", DisplayName: "X", ExeName: "x.exe", KillMethod: KillMethod.Graceful);
        var resolved = AppResolver.Resolve(app);

        Assert.Equal(ShutdownStrategy.Graceful, resolved.Shutdown);
        Assert.Equal(new[] { "x" }, resolved.ProcessNames); // .exe stripped
    }

    [Fact]
    public void Resolve_UnknownRecipeId_TreatedAsRecipeless()
    {
        var app = new AppDefinition(Id: "x", DisplayName: "X", ExeName: "x", RecipeId: "no-such-recipe-zzz");
        var resolved = AppResolver.Resolve(app);

        // Falls back to legacy KillMethod.Graceful default
        Assert.Equal(ShutdownStrategy.Graceful, resolved.Shutdown);
    }

    [Fact]
    public void Resolve_BuiltInRecipe_SteamUsesCommandStrategy()
    {
        // Steam recipe is shipped in recipes.json with shutdown=command, shutdownCommand="-shutdown"
        var app = new AppDefinition(Id: "steam", DisplayName: "Steam", RecipeId: "steam");
        var resolved = AppResolver.Resolve(app);

        Assert.Equal(ShutdownStrategy.Command, resolved.Shutdown);
        Assert.Equal("-shutdown", resolved.ShutdownCommand);
        Assert.Contains("steam", resolved.ProcessNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AppDefinitionLaunchPath_OverridesRecipeDefault()
    {
        var app = new AppDefinition(
            Id: "spotify",
            DisplayName: "Spotify",
            RecipeId: "spotify",
            LaunchPath: @"D:\custom\Spotify.exe");
        var resolved = AppResolver.Resolve(app);

        Assert.Equal(@"D:\custom\Spotify.exe", resolved.LaunchPath);
    }

    [Fact]
    public void Resolve_AppDefinitionAutostart_OverridesRecipeHint()
    {
        var app = new AppDefinition(
            Id: "spotify",
            DisplayName: "Spotify",
            RecipeId: "spotify",
            Autostart: AutostartMethod.StartupFolder,
            AutostartKey: "spotify.lnk");
        var resolved = AppResolver.Resolve(app);

        Assert.Equal(AutostartMethod.StartupFolder, resolved.Autostart);
        Assert.Equal("spotify.lnk", resolved.AutostartKey);
    }

    [Fact]
    public void Resolve_NoAutostartOnAppDefinition_FallsBackToFirstRecipeHint()
    {
        // Spotify recipe has autostartHints[0] = registryRunUser/Spotify
        var app = new AppDefinition(Id: "spotify", DisplayName: "Spotify", RecipeId: "spotify");
        var resolved = AppResolver.Resolve(app);

        Assert.Equal(AutostartMethod.RegistryRunUser, resolved.Autostart);
        Assert.Equal("Spotify", resolved.AutostartKey);
    }

    [Fact]
    public void Resolve_RecipeRequiresAdmin_PropagatesToResolved()
    {
        // razer-synapse recipe ships with requiresAdmin=true
        var app = new AppDefinition(Id: "razer", DisplayName: "Razer", RecipeId: "razer-synapse");
        var resolved = AppResolver.Resolve(app);

        Assert.True(resolved.RequiresAdmin);
        Assert.NotEmpty(resolved.ServiceNames);
    }
}
