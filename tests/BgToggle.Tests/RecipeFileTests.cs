using Xunit;

namespace BgToggle.Tests;

public class RecipeFileTests
{
    [Fact]
    public void Load_BundledFile_ParsesRecipes()
    {
        var recipes = RecipeFile.Load(Path.Combine(AppContext.BaseDirectory, "recipes.json"));
        Assert.Contains(recipes, r => r.Id == "razer-synapse" && r.Shutdown == ShutdownStrategy.StopServiceThenKill);
    }

    [Fact]
    public void Load_MissingFile_Throws() =>
        Assert.Throws<FileNotFoundException>(() => RecipeFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));

    [Fact]
    public void ServiceProcessId_RunningService_HasPid() =>
        Assert.True(ServiceManager.ProcessId("RpcSs") > 0);

    [Fact]
    public void ServiceProcessId_MissingService_IsNull() =>
        Assert.Null(ServiceManager.ProcessId("bgtoggle-test-no-such-service"));
}
