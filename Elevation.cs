using System.Security.Principal;

namespace BgToggle;

public static class Elevation
{
    /// <summary>
    /// Under UAC an admin's normal token is filtered, so this is true only
    /// when actually elevated.
    /// </summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
