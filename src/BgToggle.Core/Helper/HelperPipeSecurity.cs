using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BgToggle;

public static class HelperPipeSecurity
{
    /// <summary>
    /// SYSTEM and Administrators get full control; interactive users may only
    /// read and write. Nobody else may connect, and only SYSTEM/Administrators
    /// can create further instances, so a normal process can't add a rogue
    /// instance of the pipe.
    /// </summary>
    public static PipeSecurity Create()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(Rule(WellKnownSidType.LocalSystemSid, PipeAccessRights.FullControl));
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinAdministratorsSid, PipeAccessRights.FullControl));
        security.AddAccessRule(Rule(WellKnownSidType.InteractiveSid, PipeAccessRights.ReadWrite));
        return security;
    }

    private static PipeAccessRule Rule(WellKnownSidType sid, PipeAccessRights rights) =>
        new(new SecurityIdentifier(sid, null), rights, AccessControlType.Allow);
}
