using System.Diagnostics;

namespace BgToggle;

/// <summary>The real thing, used by the installed helper service.</summary>
public sealed class SystemHelperActions : IHelperActions
{
    public void StopServices(IReadOnlyList<string> names, Action<string> log) => ServiceManager.StopMany(names, log);

    public void StartServices(IReadOnlyList<string> names, Action<string> log) => ServiceManager.StartMany(names, log);

    public int? ServiceProcessId(string serviceName) => ServiceManager.ProcessId(serviceName);

    public IReadOnlyList<RunningProcess> AllProcesses()
    {
        var list = new List<RunningProcess>();
        foreach (var p in Process.GetProcesses())
        {
            try { list.Add(new RunningProcess(p.Id, p.SessionId, p.ProcessName)); }
            catch { /* exited mid-enumeration */ }
            finally { p.Dispose(); }
        }
        return list;
    }

    public void KillTree(int pid)
    {
        using var p = Process.GetProcessById(pid);
        p.Kill(entireProcessTree: true);
    }
}

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
