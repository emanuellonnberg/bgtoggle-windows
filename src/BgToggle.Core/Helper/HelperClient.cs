using System.IO.Pipes;

namespace BgToggle;

public enum HelperStatus { Running, NotInstalled, Incompatible }

public interface IHelperClient
{
    /// <summary>Cached; re-probed at most once a minute while not running.</summary>
    HelperStatus Status { get; }

    /// <summary>Null when the helper is unavailable, incompatible or times out. Never throws.</summary>
    HelperResponse? Send(string op, string? recipeId);
}

/// <summary>
/// Tray side of the helper pipe. Before trusting an answer it checks that
/// the pipe's server process is the one the service manager reports for the
/// helper service, so a process squatting on the pipe name is ignored.
/// </summary>
public sealed class HelperClient(string pipeName, Func<int?> expectedServerPid, int connectTimeoutMs = 500) : IHelperClient
{
    private static readonly TimeSpan ReprobeAfter = TimeSpan.FromSeconds(60);
    // Service stops wait up to 10 s each; give a multi-service recipe room.
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private volatile HelperStatus _status = HelperStatus.NotInstalled;
    private DateTime _lastProbeUtc = DateTime.MinValue;

    public HelperStatus Status
    {
        get
        {
            if (_status != HelperStatus.Running && DateTime.UtcNow - _lastProbeUtc >= ReprobeAfter)
            {
                // Don't stall the UI behind a long-running request.
                if (Monitor.TryEnter(_gate))
                {
                    try { Exchange(new HelperRequest(HelperProtocol.Version, HelperProtocol.Ping)); }
                    finally { Monitor.Exit(_gate); }
                }
            }
            return _status;
        }
    }

    public HelperResponse? Send(string op, string? recipeId)
    {
        if (Status != HelperStatus.Running) return null;
        lock (_gate) return Exchange(new HelperRequest(HelperProtocol.Version, op, recipeId));
    }

    private HelperResponse? Exchange(HelperRequest request)
    {
        _lastProbeUtc = DateTime.UtcNow;
        try
        {
            if (expectedServerPid() is not int expected)
                return Unavailable(null);

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            pipe.Connect(connectTimeoutMs);

            if (PipeNative.ServerPid(pipe) != expected)
                return Unavailable($"pipe '{pipeName}' is served by an unexpected process; ignoring it");

            HelperWire.Write(pipe, request);
            var read = Task.Run(() => HelperWire.Read<HelperResponse>(pipe));
            if (!read.Wait(ResponseTimeout))
                return Unavailable("helper did not answer in time");

            if (read.Result is not { } response)
                return Unavailable("helper closed the connection");

            if (response.Protocol != HelperProtocol.Version)
            {
                _status = HelperStatus.Incompatible;
                Logger.Warn($"Helper speaks protocol {response.Protocol}, tray speaks {HelperProtocol.Version}");
                return null;
            }

            _status = HelperStatus.Running;
            return response;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException
                                      or InvalidDataException or AggregateException)
        {
            return Unavailable(null);
        }
    }

    private HelperResponse? Unavailable(string? warning)
    {
        if (warning is not null) Logger.Warn(warning);
        _status = HelperStatus.NotInstalled;
        return null;
    }
}
