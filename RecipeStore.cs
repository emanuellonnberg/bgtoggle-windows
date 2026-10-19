using System.Text.Json;

namespace BgToggle;

/// <summary>
/// Loads recipes from two sources, merged by id (local wins):
///   1. recipes.json next to the binary (bundled, ships with releases).
///   2. %APPDATA%\BgToggle\recipes.local.json (user/community drafts).
/// Env vars in paths (%LOCALAPPDATA%, %PROGRAMFILES(X86)%, etc.) are
/// expanded at load time.
/// </summary>
/// <param name="BundledIds">Ids whose effective recipe is the bundled one (case-insensitive).</param>
public record RecipeSet(IReadOnlyList<Recipe> Recipes, IReadOnlySet<string> BundledIds);

public static class RecipeStore
{
    public static JsonSerializerOptions Options => RecipeFile.Options;

    public static string BundledPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "recipes.json");

    public static string LocalPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BgToggle", "recipes.local.json");

    /// <summary>Backwards-compat alias for tests / older callers.</summary>
    public static string RecipesPath => BundledPath;

    private static RecipeSet? _cached;

    public static IReadOnlyList<Recipe> Load(bool forceReload = false)
    {
        if (_cached is not null && !forceReload) return _cached.Recipes;

        var set = LoadFrom(BundledPath, LocalPath);
        _cached = set with { Recipes = set.Recipes.Select(ExpandPaths).ToList() };
        return _cached.Recipes;
    }

    /// <summary>
    /// True if the effective recipe for this id came from the bundled file.
    /// Only those may be handed to the elevated helper, which acts on its
    /// own installed copy; a local override would make the two disagree.
    /// </summary>
    public static bool IsBundled(string recipeId)
    {
        Load();
        return _cached!.BundledIds.Contains(recipeId);
    }

    /// <summary>Merge bundled and local files by id (local wins). No caching, no env expansion.</summary>
    public static RecipeSet LoadFrom(string bundledPath, string localPath)
    {
        var bundled = LoadFile(bundledPath, "bundled");
        var local = LoadFile(localPath, "local");

        var byId = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in bundled) byId[r.Id] = r;
        foreach (var r in local) byId[r.Id] = r;

        var bundledIds = new HashSet<string>(bundled.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        bundledIds.ExceptWith(local.Select(r => r.Id));

        return new RecipeSet(byId.Values.ToList(), bundledIds);
    }

    public static Recipe? Find(string id) =>
        Load().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Append a new recipe (or replace by id) into the user's local file.
    /// The bundled file is never modified.
    /// </summary>
    public static void SaveLocal(Recipe recipe)
    {
        var existing = LoadFile(LocalPath, "local").ToList();
        existing.RemoveAll(r => string.Equals(r.Id, recipe.Id, StringComparison.OrdinalIgnoreCase));
        existing.Add(recipe);

        RecipeFile.Save(LocalPath, existing);
        Logger.Info($"Saved local recipe '{recipe.Id}' to {LocalPath}");
    }

    private static List<Recipe> LoadFile(string path, string label)
    {
        if (!File.Exists(path))
        {
            // The local file is optional; a missing bundled file is a broken install.
            if (label == "bundled") Logger.Warn($"{label} recipes file not found at {path}");
            return new();
        }
        try
        {
            return RecipeFile.Load(path);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load {label} recipes from {path}", ex);
            return new();
        }
    }

    private static Recipe ExpandPaths(Recipe r) => r with
    {
        DefaultLaunchPath = Expand(r.DefaultLaunchPath)
    };

    private static string? Expand(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Environment.ExpandEnvironmentVariables(value);
    }
}
