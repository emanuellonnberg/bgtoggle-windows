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
