namespace BgToggle;

/// <summary>What the helper may touch for one installed recipe.</summary>
public record AllowedRecipe(string Id, IReadOnlyList<string> ServiceNames, IReadOnlyList<string> ProcessNames);

/// <summary>
/// The helper's whole authority: recipe ids from the installed (admin-only)
/// recipes.json, each mapped to the services and process names it may act
/// on. Leave-alone services are removed here so no request can reach them.
/// </summary>
public sealed class HelperAllowList
{
    private readonly Dictionary<string, AllowedRecipe> _byId = new(StringComparer.OrdinalIgnoreCase);

    public HelperAllowList(IEnumerable<Recipe> bundled)
    {
        foreach (var r in bundled)
        {
            var leaveAlone = r.ServicesToLeaveAlone ?? Array.Empty<string>();
            var services = (r.ServiceNames ?? Array.Empty<string>())
                .Except(leaveAlone, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var processes = r.ProcessNames.Select(ExeNames.Strip).ToArray();
            _byId[r.Id] = new AllowedRecipe(r.Id, services, processes);
        }
    }

    public static HelperAllowList Empty { get; } = new(Array.Empty<Recipe>());

    public int Count => _byId.Count;

    public bool TryGet(string recipeId, out AllowedRecipe recipe) =>
        _byId.TryGetValue(recipeId, out recipe!);
}
