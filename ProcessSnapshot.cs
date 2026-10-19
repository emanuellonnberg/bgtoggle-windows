using System.Diagnostics;

namespace BgToggle;

public static class ExeNames
{
    /// <summary>"Foo.exe" → "Foo". Process names never carry the suffix.</summary>
    public static string Strip(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}

/// <summary>
/// Names of all running processes at one moment. Process.GetProcessesByName
/// enumerates every process on each call, so checking 30 apps × several
/// names one by one means dozens of full enumerations; take one snapshot
/// per pass instead.
/// </summary>
public sealed class ProcessSnapshot
{
    private readonly HashSet<string> _names;

    public ProcessSnapshot(IEnumerable<string> processNames)
    {
        _names = new HashSet<string>(processNames.Select(ExeNames.Strip), StringComparer.OrdinalIgnoreCase);
    }

    public static ProcessSnapshot Take()
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); }
            catch { /* exited mid-enumeration */ }
            finally { p.Dispose(); }
        }
        return new ProcessSnapshot(names);
    }

    public bool Contains(string exeName) => _names.Contains(ExeNames.Strip(exeName));

    public bool ContainsAny(IEnumerable<string> exeNames) => exeNames.Any(Contains);
}
