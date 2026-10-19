using System.Text.Json;
using System.Text.RegularExpressions;

namespace BgToggle;

public record LatestRelease(string TagName, string HtmlUrl, bool Prerelease, bool Draft);

/// <summary>
/// "Is there a newer GitHub release?" Only ever links to the release page;
/// nothing is downloaded or installed automatically.
/// </summary>
public static partial class UpdateCheck
{
    public const string LatestReleaseUrl =
        "https://api.github.com/repos/emanuellonnberg/bgtoggle-windows/releases/latest";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Null for anything that isn't a release object (rate-limit JSON, HTML, empty).</summary>
    public static LatestRelease? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) return null;
            if (!root.TryGetProperty("html_url", out var url) || url.ValueKind != JsonValueKind.String) return null;
            return new LatestRelease(tag.GetString()!, url.GetString()!, Flag(root, "prerelease"), Flag(root, "draft"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v1.2.3" or "1.2.3"; anything else (suffixes, other schemes) is null.</summary>
    public static Version? ParseTag(string tag)
    {
        var m = TagPattern().Match(tag);
        return m.Success ? Version.Parse(m.Groups[1].Value) : null;
    }

    public static bool IsNewer(LatestRelease release, Version current)
    {
        if (release.Draft || release.Prerelease) return false;
        if (IsDevBuild(current)) return false;
        if (ParseTag(release.TagName) is not { } latest) return false;
        return latest > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
    }

    /// <summary>Local builds are versioned 0.0.0 and never check.</summary>
    public static bool IsDevBuild(Version current) =>
        current.Major == 0 && current.Minor == 0 && current.Build <= 0;

    public static bool ShouldCheck(DateTime? lastUtc, DateTime nowUtc) =>
        lastUtc is null || nowUtc - lastUtc.Value >= Interval;

    public static async Task<LatestRelease?> FetchLatestAsync(Version current)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BgToggle/{current.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return Parse(await http.GetStringAsync(LatestReleaseUrl));
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^[vV]?(\d+\.\d+\.\d+)$")]
    private static partial Regex TagPattern();
}
