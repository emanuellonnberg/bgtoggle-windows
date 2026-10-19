namespace BgToggle.Tests;

/// <summary>Records what the helper asked the OS to do; never touches it.</summary>
public sealed class FakeActions : IHelperActions
{
    public List<string> Stopped { get; } = new();
    public List<string> Started { get; } = new();
    public List<int> Killed { get; } = new();
    public List<RunningProcess> Processes { get; } = new();
    public Dictionary<string, int> ServicePids { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<int> FailKill { get; } = new();

    public int CallCount => Stopped.Count + Started.Count + Killed.Count;

    public void StopServices(IReadOnlyList<string> names, Action<string> log)
    {
        Stopped.AddRange(names);
        log($"stopped {string.Join(",", names)}");
    }

    public void StartServices(IReadOnlyList<string> names, Action<string> log)
    {
        Started.AddRange(names);
        log($"started {string.Join(",", names)}");
    }

    public int? ServiceProcessId(string serviceName) =>
        ServicePids.TryGetValue(serviceName, out var pid) ? pid : null;

    public IReadOnlyList<RunningProcess> AllProcesses() => Processes;

    public void KillTree(int pid)
    {
        if (FailKill.Contains(pid)) throw new InvalidOperationException("process has exited");
        Killed.Add(pid);
    }
}

/// <summary>Stands in for the helper pipe; answers OK while "running".</summary>
public sealed class FakeHelperClient : IHelperClient
{
    public HelperStatus Status { get; set; } = HelperStatus.Running;
    public List<(string Op, string? RecipeId)> Calls { get; } = new();

    public HelperResponse? Send(string op, string? recipeId)
    {
        if (Status != HelperStatus.Running) return null;
        Calls.Add((op, recipeId));
        return new HelperResponse(HelperProtocol.Version, true, Log: new() { $"fake {op}" });
    }
}
