using Microsoft.Win32;

namespace BgToggle;

/// <summary>
/// Reads HKCU and HKLM Run keys so the recipe suggester can match a
/// candidate exe to an existing autostart entry and emit a hint.
/// </summary>
public static class RunKeyQuery
{
    public record RunEntry(AutostartMethod Method, string Name, string Value);

    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static List<RunEntry> All()
    {
        var list = new List<RunEntry>();
        Read(Registry.CurrentUser, AutostartMethod.RegistryRunUser, list);
        Read(Registry.LocalMachine, AutostartMethod.RegistryRun, list);
        return list;
    }

    /// <summary>
    /// First Run entry whose value looks like it points at the given exe
    /// (full path match, or basename substring as a softer fallback).
    /// </summary>
    public static RunEntry? FindForExe(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        var entries = All();
        var fileName = Path.GetFileName(exePath);

        return entries.FirstOrDefault(e =>
                e.Value.Contains(exePath, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault(e =>
                e.Value.Contains(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static void Read(RegistryKey root, AutostartMethod method, List<RunEntry> list)
    {
        try
        {
            using var k = root.OpenSubKey(RunPath);
            if (k is null) return;
            foreach (var name in k.GetValueNames())
            {
                if (k.GetValue(name) is string v)
                    list.Add(new RunEntry(method, name, v));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"RunKeyQuery.Read({method}) failed", ex);
        }
    }
}
