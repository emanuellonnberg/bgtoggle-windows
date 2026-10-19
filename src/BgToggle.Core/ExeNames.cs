namespace BgToggle;

public static class ExeNames
{
    /// <summary>"Foo.exe" → "Foo". Process names never carry the suffix.</summary>
    public static string Strip(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
