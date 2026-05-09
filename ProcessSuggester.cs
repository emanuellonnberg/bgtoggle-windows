using System.Diagnostics;

namespace BgToggle;

/// <summary>
/// One running-process candidate plus a heuristic-derived recipe.
/// </summary>
public record ProcessCandidate(
    string ProcessName,
    int Pid,
    string? ExePath,
    string? InstallDir,
    string? ProductName,
    string[] SiblingProcessNames,
    List<ServiceQuery.ServiceInfo> CandidateServices,
    RunKeyQuery.RunEntry? AutostartEntry,
    Recipe Suggested,
    string Rationale
);

/// <summary>
/// Walks running processes and proposes a recipe for each one not already
/// covered by an existing recipe. Heuristics:
///   - Skip Windows / system processes (path under %WINDIR%).
///   - Group candidates by install dir.
///   - If services live under the install dir → stopServiceThenKill.
///   - Else if multiple sibling procs run from the install dir → killTree.
///   - Else default to graceful (caller can flip to killTree if it's a
///     known tray-trap pattern after testing).
///   - Autostart hint inferred from a HKCU/HKLM Run entry pointing at the exe.
/// </summary>
public static class ProcessSuggester
{
    private static readonly HashSet<string> WindowsRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)
    };

    /// <summary>Process names that are part of Windows itself.</summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "MemCompression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "fontdrvhost", "dwm", "explorer", "RuntimeBroker",
        "SearchHost", "SearchIndexer", "ShellExperienceHost", "StartMenuExperienceHost",
        "TextInputHost", "ApplicationFrameHost", "ctfmon", "sihost", "taskhostw",
        "conhost", "WmiPrvSE", "spoolsv", "audiodg", "MoUsoCoreWorker",
        "SecurityHealthService", "SecurityHealthSystray", "Defender",
        "MsMpEng", "NisSrv", "OpenWith", "BgToggle"
    };

    public static List<ProcessCandidate> Suggest()
    {
        var recipes = RecipeStore.Load();
        var coveredProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in recipes)
            foreach (var p in r.ProcessNames)
                coveredProcessNames.Add(StripExe(p));

        // Snapshot all processes with metadata where readable.
        var all = new List<(Process p, string? exe, string? dir, string? product)>();
        foreach (var p in Process.GetProcesses())
        {
            string? exe = null, dir = null, product = null;
            try
            {
                exe = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    dir = Path.GetDirectoryName(exe);
                    try { product = FileVersionInfo.GetVersionInfo(exe).ProductName; }
                    catch { /* manifest unreadable */ }
                }
            }
            catch
            {
                // Access denied for elevated procs from a non-elevated tray, etc.
            }
            all.Add((p, exe, dir, product));
        }

        // Group by InstallDir for sibling detection.
        var byDir = all
            .Where(t => !string.IsNullOrEmpty(t.dir))
            .GroupBy(t => t.dir!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                          StringComparer.OrdinalIgnoreCase);

        // Each tuple becomes one candidate, but de-dupe by process name +
        // install dir so 17 helper procs aren't 17 separate candidates.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ProcessCandidate>();

        foreach (var (p, exe, dir, product) in all
            .OrderBy(t => t.p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Excluded.Contains(p.ProcessName)) continue;
                if (coveredProcessNames.Contains(p.ProcessName)) continue;
                if (string.IsNullOrEmpty(exe)) continue;
                if (IsUnderWindowsRoot(exe)) continue;

                var key = $"{p.ProcessName}|{dir}";
                if (!seen.Add(key)) continue;

                var siblings = (dir is null || !byDir.TryGetValue(dir, out var sibs))
                    ? new[] { p.ProcessName }
                    : sibs;
                var services = dir is null ? new() : ServiceQuery.ServicesUnder(dir);
                var run = RunKeyQuery.FindForExe(exe);

                var (recipe, rationale) = BuildRecipe(p.ProcessName, product, exe, dir, siblings, services, run);
                candidates.Add(new ProcessCandidate(
                    ProcessName: p.ProcessName,
                    Pid: p.Id,
                    ExePath: exe,
                    InstallDir: dir,
                    ProductName: product,
                    SiblingProcessNames: siblings,
                    CandidateServices: services,
                    AutostartEntry: run,
                    Suggested: recipe,
                    Rationale: rationale
                ));
            }
            finally { p.Dispose(); }
        }

        return candidates;
    }

    private static (Recipe, string) BuildRecipe(
        string processName,
        string? productName,
        string exe,
        string? dir,
        string[] siblings,
        List<ServiceQuery.ServiceInfo> services,
        RunKeyQuery.RunEntry? autostart)
    {
        var displayName = !string.IsNullOrWhiteSpace(productName) ? productName! : processName;
        var id = SanitizeId(displayName);

        ShutdownStrategy strategy;
        var rationale = new List<string>();
        string[]? svcNames = null;

        if (services.Count > 0)
        {
            strategy = ShutdownStrategy.StopServiceThenKill;
            svcNames = services.Select(s => s.Name).ToArray();
            rationale.Add($"Found {services.Count} service(s) under install dir → stopServiceThenKill");
        }
        else if (siblings.Length > 1)
        {
            strategy = ShutdownStrategy.KillTree;
            rationale.Add($"Multi-process ({siblings.Length} sibling exes from same install dir) → killTree");
        }
        else
        {
            strategy = ShutdownStrategy.Graceful;
            rationale.Add("Single process, no related service → graceful (flip to killTree if it's a tray-trap)");
        }

        AutostartCandidate[]? autostartHints = null;
        if (autostart is not null)
        {
            autostartHints = new[] { new AutostartCandidate(autostart.Method, autostart.Name) };
            rationale.Add($"Run-key entry '{autostart.Name}' under {autostart.Method} → autostart hint");
        }

        // Re-collapse the launch path to env-var form so the recipe is
        // portable across machines.
        var portablePath = CollapseEnvVars(exe);

        var recipe = new Recipe(
            Id: id,
            DisplayName: displayName,
            ProcessNames: siblings,
            DefaultLaunchPath: portablePath,
            DefaultLaunchArgs: null,
            Shutdown: strategy,
            ShutdownCommand: null,
            ServiceNames: svcNames,
            ServicesToLeaveAlone: null,
            AutostartHints: autostartHints,
            Notes: "Auto-suggested from running process; verify and edit before shipping.",
            RequiresAdmin: svcNames is { Length: > 0 }
        );

        return (recipe, string.Join("\n", rationale));
    }

    public static string SanitizeId(string s)
    {
        var lower = s.ToLowerInvariant();
        var chars = lower.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var collapsed = System.Text.RegularExpressions.Regex.Replace(new string(chars), "-+", "-");
        return collapsed.Trim('-');
    }

    /// <summary>
    /// Replace common install-dir prefixes with their %ENVVAR% form so a
    /// suggested recipe is portable to other machines / users.
    /// </summary>
    public static string CollapseEnvVars(string path)
    {
        var pairs = new (string env, string value)[]
        {
            ("%LOCALAPPDATA%",       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            ("%APPDATA%",            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            ("%PROGRAMFILES(X86)%",  Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
            ("%PROGRAMFILES%",       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
            ("%USERPROFILE%",        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        };
        foreach (var (env, value) in pairs)
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (path.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                return env + path[value.Length..];
        }
        return path;
    }

    private static bool IsUnderWindowsRoot(string exe)
    {
        foreach (var root in WindowsRoots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            if (exe.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string StripExe(string n) =>
        n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
}
