namespace BgToggle;

public record RunningProcess(int Pid, int SessionId, string Name);

/// <summary>
/// Which processes the LocalSystem helper may end for a client. A process in
/// the client's own session may go if the recipe lists its name. In session 0
/// only the recipe's own service host processes may go (several watchdog
/// recipes list them, and a timed-out service stop leaves them running).
/// Nothing in another user's session is ever touched.
/// </summary>
public static class KillRule
{
    public static bool MayKill(
        RunningProcess p,
        int clientSessionId,
        IReadOnlySet<string> listedNames,
        IReadOnlySet<int> recipeServicePids)
    {
        if (p.SessionId == 0)
            return recipeServicePids.Contains(p.Pid);

        return p.SessionId == clientSessionId
            && clientSessionId != 0
            && listedNames.Contains(ExeNames.Strip(p.Name));
    }
}
