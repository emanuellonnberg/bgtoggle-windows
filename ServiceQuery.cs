using Microsoft.Win32;

namespace BgToggle;

/// <summary>
/// Reads the local Windows service registry to expose each service's
/// ImagePath. ServiceController doesn't surface this, and WMI is slow.
/// </summary>
public static class ServiceQuery
{
    public record ServiceInfo(string Name, string DisplayName, string ImagePath);

    public static List<ServiceInfo> All()
    {
        var list = new List<ServiceInfo>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root is null) return list;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var s = root.OpenSubKey(name);
                    if (s is null) continue;
                    var image = s.GetValue("ImagePath") as string ?? "";
                    var disp = s.GetValue("DisplayName") as string ?? name;
                    if (string.IsNullOrWhiteSpace(image)) continue;
                    list.Add(new ServiceInfo(name, disp, image));
                }
                catch { /* one bad subkey shouldn't abort the scan */ }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ServiceQuery.All failed", ex);
        }
        return list;
    }

    /// <summary>
    /// Find services whose executable lives under the given install dir.
    /// Used to suggest a recipe's `serviceNames` from a candidate exe's path.
    /// </summary>
    public static List<ServiceInfo> ServicesUnder(string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return new();
        var prefix = NormalizePath(installDir).TrimEnd('\\') + "\\";
        return All()
            .Where(s => NormalizePath(ExtractExe(s.ImagePath))
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Strip kernel-mode prefixes and leading quotes from an ImagePath.</summary>
    public static string ExtractExe(string imagePath)
    {
        var p = imagePath.Trim();
        if (p.StartsWith("\\??\\")) p = p[4..];
        // "C:\path\foo.exe" -arg1 -arg2  →  C:\path\foo.exe
        if (p.StartsWith("\""))
        {
            var end = p.IndexOf('"', 1);
            if (end > 0) return p.Substring(1, end - 1);
        }
        var space = p.IndexOf(' ');
        return space < 0 ? p : p[..space];
    }

    private static string NormalizePath(string p)
    {
        try { return Path.GetFullPath(p); }
        catch { return p; }
    }
}
