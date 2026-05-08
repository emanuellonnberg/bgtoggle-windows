namespace BgToggle;

public record RecipeIssue(string RecipeId, string Severity, string Message)
{
    public override string ToString() => $"[{Severity}] {RecipeId}: {Message}";
}

/// <summary>
/// Sanity-checks loaded recipes against the local machine. Issues are
/// non-blocking — surfaced as a tray menu item and written to the log.
/// </summary>
public static class RecipeValidator
{
    public static List<RecipeIssue> Validate(IEnumerable<Recipe> recipes)
    {
        var issues = new List<RecipeIssue>();

        foreach (var r in recipes)
        {
            // Schema-level checks (CI catches these too, but recipes loaded
            // from a future user-overridden file might miss CI).
            if (r.ProcessNames.Length == 0)
                issues.Add(new RecipeIssue(r.Id, "ERROR", "no processNames"));

            if (r.Shutdown == ShutdownStrategy.Command && string.IsNullOrWhiteSpace(r.ShutdownCommand))
                issues.Add(new RecipeIssue(r.Id, "ERROR", "command strategy but no shutdownCommand"));

            if ((r.Shutdown == ShutdownStrategy.StopServiceOnly ||
                 r.Shutdown == ShutdownStrategy.StopServiceThenKill) &&
                (r.ServiceNames is null || r.ServiceNames.Length == 0))
            {
                issues.Add(new RecipeIssue(r.Id, "ERROR", "service-stop strategy but no serviceNames"));
            }

            // Local-machine checks. These are advisory: a service or path
            // missing on this box doesn't make the recipe wrong globally.
            if (r.ServiceNames is not null)
            {
                foreach (var svc in r.ServiceNames)
                {
                    if (!ServiceManager.Exists(svc))
                        issues.Add(new RecipeIssue(r.Id, "INFO ", $"service '{svc}' not installed on this machine"));
                }
            }

            if (!string.IsNullOrWhiteSpace(r.DefaultLaunchPath) && !File.Exists(r.DefaultLaunchPath))
                issues.Add(new RecipeIssue(r.Id, "INFO ", $"defaultLaunchPath does not exist: {r.DefaultLaunchPath}"));
        }

        return issues;
    }
}
