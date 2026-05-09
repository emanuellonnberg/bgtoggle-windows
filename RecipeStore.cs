using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BgToggle;

/// <summary>
/// Loads recipes from two sources, merged by id (local wins):
///   1. recipes.json next to the binary (bundled, ships with releases).
///   2. %APPDATA%\BgToggle\recipes.local.json (user/community drafts).
/// Env vars in paths (%LOCALAPPDATA%, %PROGRAMFILES(X86)%, etc.) are
/// expanded at load time.
/// </summary>
public static class RecipeStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string BundledPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "recipes.json");

    public static string LocalPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BgToggle", "recipes.local.json");

    /// <summary>Backwards-compat alias for tests / older callers.</summary>
    public static string RecipesPath => BundledPath;

    private static IReadOnlyList<Recipe>? _cached;

    public static IReadOnlyList<Recipe> Load(bool forceReload = false)
    {
        if (_cached is not null && !forceReload) return _cached;

        var bundled = LoadFile(BundledPath, "bundled");
        var local = LoadFile(LocalPath, "local");

        // Merge: start with bundled, overlay local by id.
        var byId = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in bundled) byId[r.Id] = r;
        foreach (var r in local) byId[r.Id] = r;

        _cached = byId.Values.Select(ExpandPaths).ToList();
        return _cached;
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

        Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);
        var doc = new RecipesFile(existing);
        File.WriteAllText(LocalPath, JsonSerializer.Serialize(doc, Options));
        Logger.Info($"Saved local recipe '{recipe.Id}' to {LocalPath}");
    }

    private static List<Recipe> LoadFile(string path, string label)
    {
        if (!File.Exists(path))
        {
            Debug.WriteLine($"{label} recipes file not found at {path}");
            return new();
        }
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<RecipesFile>(json, Options);
            return doc?.Recipes ?? new();
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

    private record RecipesFile([property: JsonPropertyName("recipes")] List<Recipe> Recipes);
}
