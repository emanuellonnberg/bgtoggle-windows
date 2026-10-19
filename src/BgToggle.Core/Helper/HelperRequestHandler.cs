namespace BgToggle;

/// <summary>
/// Validates one request and runs it. Everything the helper does flows
/// through here, so the rules live in one place: protocol must match, the
/// recipe id must be in the installed allow-list, and kills obey
/// <see cref="KillRule"/>.
/// </summary>
public sealed class HelperRequestHandler(HelperAllowList allow, IHelperActions actions, string appVersion)
{
    public HelperResponse Handle(HelperRequest request, int clientSessionId)
    {
        if (request.Protocol != HelperProtocol.Version)
            return Fail($"protocol mismatch: helper speaks {HelperProtocol.Version}");

        if (request.Op == HelperProtocol.Ping)
            return new HelperResponse(HelperProtocol.Version, true, AppVersion: appVersion);

        if (request.Op is not (HelperProtocol.StopServices or HelperProtocol.StartServices or HelperProtocol.KillProcesses))
            return Fail($"unknown op '{request.Op}'");

        if (request.RecipeId is null || !allow.TryGet(request.RecipeId, out var recipe))
            return Fail($"recipe '{request.RecipeId}' is not in the installed recipes");

        var log = new List<string>();
        switch (request.Op)
        {
            case HelperProtocol.StopServices:
                actions.StopServices(recipe.ServiceNames, log.Add);
                return Done(true, log);

            case HelperProtocol.StartServices:
                actions.StartServices(recipe.ServiceNames, log.Add);
                return Done(true, log);

            default:
                return Done(Kill(recipe, clientSessionId, log), log);
        }
    }

    private bool Kill(AllowedRecipe recipe, int clientSessionId, List<string> log)
    {
        var listed = new HashSet<string>(recipe.ProcessNames, StringComparer.OrdinalIgnoreCase);
        var servicePids = recipe.ServiceNames
            .Select(actions.ServiceProcessId)
            .OfType<int>()
            .ToHashSet();

        var targets = actions.AllProcesses()
            .Where(p => KillRule.MayKill(p, clientSessionId, listed, servicePids))
            .ToList();
        if (targets.Count == 0)
        {
            log.Add($"{recipe.Id}: nothing to kill");
            return true;
        }

        var failures = 0;
        foreach (var p in targets)
        {
            try
            {
                actions.KillTree(p.Pid);
                log.Add($"killed PID {p.Pid} ({p.Name}) in session {p.SessionId}");
            }
            catch (Exception ex)
            {
                failures++;
                log.Add($"PID {p.Pid} ({p.Name}): {ex.Message}");
            }
        }
        return failures < targets.Count;
    }

    private static HelperResponse Fail(string error) =>
        new(HelperProtocol.Version, false, Error: error);

    private static HelperResponse Done(bool ok, List<string> log) =>
        new(HelperProtocol.Version, ok, Log: log);
}
