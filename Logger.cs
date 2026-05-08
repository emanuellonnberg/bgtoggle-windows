using System.Diagnostics;

namespace BgToggle;

/// <summary>
/// Append-only daily log under %APPDATA%\BgToggle\logs. Replaces the previous
/// Debug.WriteLine calls so service-stop failures and other quiet errors are
/// visible without attaching a debugger.
/// </summary>
public static class Logger
{
    private static readonly object _gate = new();

    public static string LogDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BgToggle", "logs");

    public static string CurrentLogPath =>
        Path.Combine(LogDir, $"bgtoggle-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERR ", message);
    public static void Error(string message, Exception ex) =>
        Write("ERR ", $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(CurrentLogPath, line + Environment.NewLine);
            }
        }
        catch { /* logging failure must not crash the app */ }

        Debug.WriteLine(line);
    }

    /// <summary>Delete log files older than `keepDays` days.</summary>
    public static void Prune(int keepDays = 14)
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var f in Directory.EnumerateFiles(LogDir, "bgtoggle-*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
            }
        }
        catch { /* best-effort */ }
    }
}
