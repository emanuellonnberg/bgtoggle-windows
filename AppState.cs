using System.Text.Json;

namespace BgToggle;

/// <summary>
/// Bookkeeping the app writes for itself, kept out of config.json so it
/// never collides with the user's hand edits.
/// </summary>
public record AppState(DateTime? LastUpdateCheckUtc = null, string? LastNotifiedVersion = null);

public static class AppStateStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BgToggle", "state.json");

    public static AppState Load() => Load(DefaultPath);

    public static void Save(AppState state) => Save(state, DefaultPath);

    /// <summary>Never throws: a missing or broken file is just a fresh state.</summary>
    internal static AppState Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? new AppState()
                : new AppState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppState();
        }
    }

    internal static void Save(AppState state, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state));
    }
}
