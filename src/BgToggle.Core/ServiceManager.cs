using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace BgToggle;

/// <summary>
/// Stop/start Windows services. Stopping a service that is not installed,
/// already stopped, or that we lack rights to control logs and continues —
/// callers iterate a list and don't want one bad name to abort the rest.
/// Most service stops require admin; if BgToggle is not elevated, expect
/// AccessDenied.
/// </summary>
public static class ServiceManager
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static void StopMany(IEnumerable<string> serviceNames, Action<string>? log = null)
    {
        foreach (var name in serviceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Stopped ||
                    sc.Status == ServiceControllerStatus.StopPending)
                {
                    log?.Invoke($"Service {name} already stopped");
                    continue;
                }

                if (!sc.CanStop)
                {
                    log?.Invoke($"Service {name} cannot be stopped (dependents or policy)");
                    continue;
                }

                log?.Invoke($"Stopping service {name}");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, DefaultTimeout);
            }
            catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception)
            {
                (log ?? Logger.Warn)($"Service {name} stop failed: {ex.Message} (admin required?)");
            }
            catch (Exception ex)
            {
                (log ?? Logger.Warn)($"Service {name} stop failed: {ex.Message}");
            }
        }
    }

    public static void StartMany(IEnumerable<string> serviceNames, Action<string>? log = null)
    {
        foreach (var name in serviceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Running ||
                    sc.Status == ServiceControllerStatus.StartPending) continue;

                log?.Invoke($"Starting service {name}");
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, DefaultTimeout);
            }
            catch (Exception ex)
            {
                (log ?? Logger.Warn)($"Service {name} start failed: {ex.Message}");
            }
        }
    }

    /// <summary>True if installed and running (or on its way there).</summary>
    public static bool IsRunning(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status is ServiceControllerStatus.Running
                or ServiceControllerStatus.StartPending
                or ServiceControllerStatus.ContinuePending;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// PID of the service's process, or null if missing, stopped or not
    /// queryable. Opens with SERVICE_QUERY_STATUS only so it works unelevated
    /// (ServiceController.ServiceHandle asks for far more access).
    /// </summary>
    public static int? ProcessId(string serviceName)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return null;
        try
        {
            var svc = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return null;
            try
            {
                var size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!QueryServiceStatusEx(svc, SC_STATUS_PROCESS_INFO, buffer, size, out _)) return null;
                    var status = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buffer);
                    return status.dwProcessId == 0 ? null : (int)status.dwProcessId;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const int SC_STATUS_PROCESS_INFO = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
            dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint, dwProcessId, dwServiceFlags;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr scm, string serviceName, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, IntPtr buffer, int bufferSize, out int bytesNeeded);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    public static bool Exists(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            _ = sc.Status; // throws if not installed
            return true;
        }
        catch
        {
            return false;
        }
    }
}
