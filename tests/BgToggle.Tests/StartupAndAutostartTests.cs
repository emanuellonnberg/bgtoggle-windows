using Microsoft.Win32;
using Xunit;

namespace BgToggle.Tests;

public class StartupArgsTests
{
    [Fact]
    public void Parse_Setup() =>
        Assert.Equal(new StartupArgs(true, false, true, false),
            StartupArgs.Parse(new[] { "--first-run", "--start-at-logon=off", "--exit-after-setup" }));

    [Fact]
    public void Parse_LogonOn() =>
        Assert.True(StartupArgs.Parse(new[] { "--first-run", "--start-at-logon=on" }).StartAtLogon);

    [Fact]
    public void Parse_Empty() =>
        Assert.Equal(new StartupArgs(false, null, false, false), StartupArgs.Parse(Array.Empty<string>()));

    [Fact]
    public void Parse_BadLogonValue_Null() =>
        Assert.Null(StartupArgs.Parse(new[] { "--start-at-logon=maybe" }).StartAtLogon);

    [Fact]
    public void Parse_UninstallCleanup() =>
        Assert.True(StartupArgs.Parse(new[] { "--uninstall-cleanup", "--whatever" }).UninstallCleanup);
}

/// <summary>Uses a throwaway HKCU subtree, never the real Run key.</summary>
public class TrayAutostartAndRestoreTests : IDisposable
{
    private readonly string _root = @"Software\BgToggleTest\" + Guid.NewGuid().ToString("N");
    private readonly string _startup = Path.Combine(Path.GetTempPath(), "bgtoggle-startup-" + Guid.NewGuid().ToString("N"));
    private string RunPath => _root + @"\Run";
    private string StashPath => _root + @"\Stash";

    public TrayAutostartAndRestoreTests() => Directory.CreateDirectory(_startup);

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false); } catch { }
        try { Directory.Delete(_startup, recursive: true); } catch { }
    }

    private object? RunValue(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunPath);
        return k?.GetValue(name);
    }

    [Fact]
    public void TrayAutostart_SetOnOff()
    {
        TrayAutostart.Set(true, @"C:\x\BgToggle.exe", RunPath);
        Assert.Equal("\"C:\\x\\BgToggle.exe\"", RunValue(TrayAutostart.ValueName));
        Assert.True(TrayAutostart.IsEnabled(RunPath));

        TrayAutostart.Set(false, @"C:\x\BgToggle.exe", RunPath);
        Assert.False(TrayAutostart.IsEnabled(RunPath));
    }

    [Fact]
    public void RestoreAll_MovesStashToRun_AndRenamesStartupFiles()
    {
        using (var stash = Registry.CurrentUser.CreateSubKey(StashPath)) stash.SetValue("A", "a.exe");
        File.WriteAllText(Path.Combine(_startup, "foo.lnk.bgtoggle.disabled"), "");

        var restored = AutostartManager.RestoreAll(Registry.CurrentUser, RunPath, StashPath, _startup);

        Assert.Equal(2, restored);
        Assert.Equal("a.exe", RunValue("A"));
        using (var stash = Registry.CurrentUser.OpenSubKey(StashPath)) Assert.Empty(stash!.GetValueNames());
        Assert.True(File.Exists(Path.Combine(_startup, "foo.lnk")));
    }

    [Fact]
    public void RestoreAll_ExistingRunValue_NotOverwritten()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunPath)) run.SetValue("A", "new.exe");
        using (var stash = Registry.CurrentUser.CreateSubKey(StashPath)) stash.SetValue("A", "old.exe");

        var restored = AutostartManager.RestoreAll(Registry.CurrentUser, RunPath, StashPath, null);

        Assert.Equal(0, restored);
        Assert.Equal("new.exe", RunValue("A"));
        using (var stash = Registry.CurrentUser.OpenSubKey(StashPath)) Assert.Empty(stash!.GetValueNames());
    }

    [Fact]
    public void RestoreAll_NothingStashed_ReturnsZero() =>
        Assert.Equal(0, AutostartManager.RestoreAll(Registry.CurrentUser, RunPath, StashPath, _startup));
}
