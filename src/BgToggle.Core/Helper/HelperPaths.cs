namespace BgToggle;

public static class HelperPaths
{
    /// <summary>
    /// The helper logs next to itself in Program Files (admin-only), not in
    /// ProgramData, where a user can pre-create the folder and redirect
    /// SYSTEM's writes with a junction.
    /// </summary>
    public static string LogDir(string installDir) => Path.Combine(installDir, "logs");
}
