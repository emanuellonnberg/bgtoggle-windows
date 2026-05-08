using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BgToggle;

/// <summary>
/// Loads recipes.json from next to the binary. Env vars in paths
/// (%LOCALAPPDATA%, %PROGRAMFILES(X86)%, etc.) are expanded at load time.
/// </summary>
public static class RecipeStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string RecipesPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "recipes.json");

    private static IReadOnlyList<Recipe>? _cached;

    public static IReadOnlyList<Recipe> Load(bool forceReload = false)
    {
        if (_cached is not null && !forceReload) return _cached;

        if (!File.Exists(RecipesPath))
        {
            Debug.WriteLine($"recipes.json not found at {RecipesPath}");
            _cached = Array.Empty<Recipe>();
            return _cached;
        }

        try
        {
            var json = File.ReadAllText(RecipesPath);
            var doc = JsonSerializer.Deserialize<RecipesFile>(json, Options);
            var raw = doc?.Recipes ?? new List<Recipe>();
            _cached = raw.Select(ExpandPaths).ToList();
            return _cached;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load recipes.json: {ex.Message}");
            _cached = Array.Empty<Recipe>();
            return _cached;
        }
    }

    public static Recipe? Find(string id) =>
        Load().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    private static Recipe ExpandPaths(Recipe r) => r with
    {
        DefaultLaunchPath = Expand(r.DefaultLaunchPath)
    };

    private static string? Expand(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        // Environment.ExpandEnvironmentVariables handles %FOO% on Windows
        // including parens like %PROGRAMFILES(X86)%.
        return Environment.ExpandEnvironmentVariables(value);
    }

    private record RecipesFile([property: JsonPropertyName("recipes")] List<Recipe> Recipes);
}
