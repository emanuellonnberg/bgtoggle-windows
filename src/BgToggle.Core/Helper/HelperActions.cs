namespace BgToggle;

/// <summary>The OS operations the helper performs; faked in tests.</summary>
public interface IHelperActions
{
    void StopServices(IReadOnlyList<string> names, Action<string> log);
    void StartServices(IReadOnlyList<string> names, Action<string> log);
    int? ServiceProcessId(string serviceName);
    IReadOnlyList<RunningProcess> AllProcesses();
    /// <summary>May throw (e.g. the process already exited).</summary>
    void KillTree(int pid);
}
