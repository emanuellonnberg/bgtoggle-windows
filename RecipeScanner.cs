using System.Diagnostics;

namespace BgToggle;

public record RecipeMatch(Recipe Recipe, string? DetectedLaunchPath, int RunningProcessCount);

/// <summary>
/// Walks running processes and matches against known recipes by process name.
/// For matches it tries to read MainModule.FileName for a launch path —
/// requires same-bitness or admin in some cases; if it fails, falls back to
/// the recipe's default install path.
/// </summary>
public static class RecipeScanner
{
    public static List<RecipeMatch> Scan()
    {
        var recipes = RecipeStore.Load();
        if (recipes.Count == 0) return new();

        // Build name → recipe lookup. ProcessNames are case-insensitive.
        var byProcess = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in recipes)
            foreach (var n in r.ProcessNames)
                byProcess[StripExe(n)] = r;

        var hits = new Dictionary<string, (int count, string? path)>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (!byProcess.TryGetValue(name, out var recipe)) continue;

                hits.TryGetValue(recipe.Id, out var entry);
                entry.count++;
                if (entry.path is null)
                {
                    try { entry.path = p.MainModule?.FileName; }
                    catch { /* access denied / 32-vs-64 — try next process */ }
                }
                hits[recipe.Id] = entry;
            }
            catch { /* process exited mid-scan etc */ }
            finally { p.Dispose(); }
        }

        var result = new List<RecipeMatch>();
        foreach (var (id, info) in hits)
        {
            var r = recipes.First(x => x.Id == id);
            result.Add(new RecipeMatch(r, info.path ?? r.DefaultLaunchPath, info.count));
        }
        result.Sort((a, b) => string.Compare(a.Recipe.DisplayName, b.Recipe.DisplayName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static string StripExe(string n) =>
        n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
}
