using System.Text.Json;
using System.Text.Json.Serialization;

namespace BgToggle;

/// <summary>
/// Reads and writes one <c>{ "recipes": [...] }</c> file. Shared by the tray
/// (bundled + local merge) and the helper (bundled file only), so both
/// parse recipes exactly the same way.
/// </summary>
public static class RecipeFile
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Throws <see cref="FileNotFoundException"/> if missing and
    /// <see cref="JsonException"/> if malformed; callers decide how to degrade.
    /// </summary>
    public static List<Recipe> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RecipesDocument>(json, Options)?.Recipes ?? new();
    }

    public static void Save(string path, IEnumerable<Recipe> recipes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new RecipesDocument(recipes.ToList()), Options));
    }

    private record RecipesDocument([property: JsonPropertyName("recipes")] List<Recipe> Recipes);
}
