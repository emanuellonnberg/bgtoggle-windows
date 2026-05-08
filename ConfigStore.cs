using System.Text.Json;
using System.Text.Json.Serialization;

namespace BgToggle;

public static class ConfigStore
{
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BgToggle", "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static Config Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var def = Default();
            Save(def);
            return def;
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<Config>(json, Options) ?? Default();
    }

    public static void Save(Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
    }

    private static Config Default() => new(
        Apps: new List<AppDefinition>(),
        Profiles: new List<Profile>
        {
            new("All off", new HashSet<string>()),
            new("Everything", new HashSet<string>())
        }
    );
}
