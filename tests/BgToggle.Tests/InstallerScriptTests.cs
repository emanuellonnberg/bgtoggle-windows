using Xunit;

namespace BgToggle.Tests;

/// <summary>
/// The installer can't run in tests; these pin the lines that carry its
/// security and upgrade behavior so they can't silently disappear.
/// </summary>
public class InstallerScriptTests
{
    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "app_toggler.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "installer", "BgToggle.iss"));
    }

    [Fact]
    public void InstallDir_CannotBeChosen()
    {
        // The LocalSystem helper runs from {app}; it must stay admin-only.
        var s = Script();
        Assert.Contains("DisableDirPage=yes", s);
        Assert.Contains("must be installed in", s);   // PrepareToInstall guard against /DIR=
    }

    [Fact]
    public void Upgrade_KeepsUsersStartAtLogonChoice()
    {
        var s = Script();
        Assert.Contains("Result := 'keep'", s);
        Assert.Contains("Check: not IsUpgrade", s);
    }
}
