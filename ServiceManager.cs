using System.Diagnostics;
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
                Debug.WriteLine($"Service {name} stop failed: {ex.Message}");
                log?.Invoke($"Service {name} stop failed: {ex.Message} (admin required?)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Service {name} stop failed: {ex.Message}");
                log?.Invoke($"Service {name} stop failed: {ex.Message}");
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
                Debug.WriteLine($"Service {name} start failed: {ex.Message}");
                log?.Invoke($"Service {name} start failed: {ex.Message}");
            }
        }
    }

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
