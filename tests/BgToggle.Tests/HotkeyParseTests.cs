using System.Reflection;
using Xunit;

namespace BgToggle.Tests;

/// <summary>
/// HotkeyManager.TryParse is private; reflect to test it without exercising
/// the user32 RegisterHotKey path (which would need a real message loop).
/// </summary>
public class HotkeyParseTests
{
    private static (bool ok, uint mods, uint vk) Parse(string spec)
    {
        var m = typeof(HotkeyManager).GetMethod("TryParse",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        object[] args = { spec, 0u, 0u };
        var ok = (bool)m!.Invoke(null, args)!;
        return (ok, (uint)args[1], (uint)args[2]);
    }

    [Fact]
    public void Parse_CtrlAlt1_Succeeds()
    {
        var (ok, mods, vk) = Parse("Ctrl+Alt+1");
        Assert.True(ok);
        Assert.Equal((uint)Keys.D1, vk);
        Assert.NotEqual(0u, mods & 0x0002); // MOD_CONTROL
        Assert.NotEqual(0u, mods & 0x0001); // MOD_ALT
    }

    [Fact]
    public void Parse_WinShiftG_Succeeds()
    {
        var (ok, mods, vk) = Parse("Win+Shift+G");
        Assert.True(ok);
        Assert.Equal((uint)Keys.G, vk);
        Assert.NotEqual(0u, mods & 0x0008); // MOD_WIN
        Assert.NotEqual(0u, mods & 0x0004); // MOD_SHIFT
    }

    [Fact]
    public void Parse_NoModifier_Fails()
    {
        var (ok, _, _) = Parse("F12");
        Assert.False(ok);
    }

    [Fact]
    public void Parse_Empty_Fails()
    {
        var (ok, _, _) = Parse("");
        Assert.False(ok);
    }

    [Fact]
    public void Parse_BogusKey_Fails()
    {
        var (ok, _, _) = Parse("Ctrl+NotAKey");
        Assert.False(ok);
    }
}
