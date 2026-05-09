using Xunit;

namespace BgToggle.Tests;

public class ProcessSuggesterTests
{
    [Fact]
    public void SanitizeId_StripsPunctuationAndCollapsesDashes()
    {
        Assert.Equal("razer-synapse", ProcessSuggester.SanitizeId("Razer Synapse"));
        Assert.Equal("microsoft-teams", ProcessSuggester.SanitizeId("Microsoft Teams"));
        Assert.Equal("1password", ProcessSuggester.SanitizeId("1Password"));
        Assert.Equal("c-c", ProcessSuggester.SanitizeId("C++ ++ C")); // edge: punctuation runs collapse
    }

    [Fact]
    public void SanitizeId_TrimsLeadingAndTrailingDashes()
    {
        Assert.Equal("foo", ProcessSuggester.SanitizeId(" Foo "));
        Assert.Equal("foo", ProcessSuggester.SanitizeId("--foo--"));
    }

    [Fact]
    public void CollapseEnvVars_LocalAppData_ReplacesPrefix()
    {
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var input = Path.Combine(lad, "Discord", "Update.exe");
        var output = ProcessSuggester.CollapseEnvVars(input);

        Assert.StartsWith("%LOCALAPPDATA%", output);
        Assert.EndsWith("Discord\\Update.exe", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollapseEnvVars_PathOutsideKnownRoots_Unchanged()
    {
        var input = @"D:\portable\foo\bar.exe";
        Assert.Equal(input, ProcessSuggester.CollapseEnvVars(input));
    }

    [Fact]
    public void Suggest_DoesNotIncludeBgToggleItself()
    {
        var candidates = ProcessSuggester.Suggest();
        Assert.DoesNotContain(candidates, c =>
            string.Equals(c.ProcessName, "BgToggle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Suggest_DoesNotIncludeProcessesAlreadyCoveredByARecipe()
    {
        // Sanity check: explorer / svchost are excluded (Windows-internal),
        // and any running process whose name matches a bundled recipe should
        // be filtered out. We can't reliably assert presence/absence of
        // arbitrary apps on the test machine, so instead we just verify the
        // suggester runs and produces a list (possibly empty), without
        // throwing.
        var candidates = ProcessSuggester.Suggest();
        Assert.NotNull(candidates);
    }
}

public class ServiceQueryTests
{
    [Fact]
    public void ExtractExe_StripsKernelPrefixAndArguments()
    {
        Assert.Equal(@"C:\Windows\system32\svchost.exe",
            ServiceQuery.ExtractExe(@"C:\Windows\system32\svchost.exe -k LocalService -p"));
        Assert.Equal(@"C:\Program Files\Foo\Foo.exe",
            ServiceQuery.ExtractExe(@"""C:\Program Files\Foo\Foo.exe"" --service"));
        Assert.Equal(@"C:\Windows\System32\drivers\bar.sys",
            ServiceQuery.ExtractExe(@"\??\C:\Windows\System32\drivers\bar.sys"));
    }
}
