using System.Text.Json;
using System.Text.Json.Serialization;

namespace BgToggle;

/// <summary>Config file exists but couldn't be read or parsed.</summary>
public class ConfigLoadException(string path, Exception inner)
    : Exception($"Could not load {path}: {inner.Message}", inner)
{
    public string Path { get; } = path;
}

public static class ConfigStore
{
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BgToggle", "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static Config Load() => Load(ConfigPath);

    /// <summary>
    /// Load config from disk, creating a default one if missing.
    /// Throws <see cref="ConfigLoadException"/> if the file exists but is
    /// unreadable or not valid JSON — callers decide whether to fall back.
    /// </summary>
    public static Config Load(string path)
    {
        if (!File.Exists(path))
        {
            var def = Default();
            Save(def, path);
            return def;
        }

        Config? loaded;
        try
        {
            var json = File.ReadAllText(path);
            loaded = JsonSerializer.Deserialize<Config>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new ConfigLoadException(path, ex);
        }

        var config = Normalize(loaded ?? Default());
        if (MigrateEverythingProfile(ref config))
        {
            Logger.Info("Migrated empty 'Everything' profile to allApps");
            Save(config, path);
        }
        return config;
    }

    public static void Save(Config config) => Save(config, ConfigPath);

    /// <summary>
    /// Write to a temp file then rename over the target, so a crash or full
    /// disk mid-write can't leave a truncated config behind.
    /// </summary>
    public static void Save(Config config, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Copy an unparseable config aside so a later Save can't destroy the
    /// user's hand edits. Returns the backup path, or null if the copy failed.
    /// </summary>
    public static string? BackupBroken(string path)
    {
        try
        {
            var backup = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not back up broken config {path}", ex);
            return null;
        }
    }

    public static Config Default() => new(
        Apps: new List<AppDefinition>(),
        Profiles: new List<Profile>
        {
            new("All off", new HashSet<string>()),
            new("Everything", new HashSet<string>(), AllApps: true)
        }
    );

    /// <summary>JSON can omit fields the records declare non-null.</summary>
    private static Config Normalize(Config c) => c with
    {
        Apps = c.Apps ?? new(),
        Profiles = (c.Profiles ?? new())
            .Select(p => p with { AppIds = p.AppIds ?? new HashSet<string>() })
            .ToList()
    };

    /// <summary>
    /// Older default configs shipped an "Everything" profile with no apps,
    /// which actually stopped everything. Nobody means that by the name, so
    /// convert it to an all-apps profile.
    /// </summary>
    private static bool MigrateEverythingProfile(ref Config config)
    {
        var idx = config.Profiles.FindIndex(p =>
            string.Equals(p.Name, "Everything", StringComparison.OrdinalIgnoreCase) &&
            !p.AllApps && p.AppIds.Count == 0);
        if (idx < 0) return false;

        config.Profiles[idx] = config.Profiles[idx] with { AllApps = true };
        return true;
    }
}
