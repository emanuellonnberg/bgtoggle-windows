using System.IO.Pipes;

namespace BgToggle;

/// <summary>
/// Accepts tray connections on the helper pipe. One request per connection.
/// Connections are served concurrently with a short read timeout, so a
/// client that connects and says nothing can't block everyone else; the
/// requests themselves are handled one at a time.
/// </summary>
public sealed class HelperServer
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    // Bounds threads and pipe instances a local user can pin at once.
    private const int MaxConcurrentConnections = 16;

    private readonly string _pipeName;
    private readonly PipeSecurity? _security;
    private readonly Func<HelperRequest, int, HelperResponse> _handle;
    private readonly object _handleGate = new();
    private int _activeConnections;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentConnections);

    /// <summary>Connections currently being served (test hook).</summary>
    internal int ActiveConnections => Volatile.Read(ref _activeConnections);

    private readonly string? _recipesPath;
    private readonly IHelperActions? _actions;
    private readonly string _appVersion = "";
    private HelperRequestHandler? _handler;
    private DateTime? _recipesStamp;

    /// <param name="recipesPath">
    /// The installed recipes.json. Reloaded when its write time changes
    /// (checked on each ping); missing or corrupt means every recipe op is
    /// refused while ping keeps working.
    /// </param>
    public HelperServer(string pipeName, string recipesPath, IHelperActions actions,
                        string appVersion, PipeSecurity? security)
    {
        _pipeName = pipeName;
        _security = security;
        _recipesPath = recipesPath;
        _actions = actions;
        _appVersion = appVersion;
        ReloadIfChanged();
        _handle = HandleWithRecipes;
    }

    /// <summary>Test hook: serve with an arbitrary handler.</summary>
    internal HelperServer(string pipeName, Func<HelperRequest, int, HelperResponse> handle, PipeSecurity? security)
    {
        _pipeName = pipeName;
        _security = security;
        _handle = handle;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _slots.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            NamedPipeServerStream? server = null;
            try
            {
                // FirstPipeInstance on the first instance makes creation fail
                // if someone else already owns the name (pipe squatting).
                // Non-zero buffers: replies never block on a client that
                // doesn't read them.
                var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
                server = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options,
                    HelperProtocol.MaxMessageBytes, HelperProtocol.MaxMessageBytes, _security);
                first = false;

                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                _slots.Release();
                break;
            }
            catch (Exception ex)
            {
                // E.g. a client that connected and closed before the wait
                // completed ("pipe is being closed"). Never let the loop die:
                // the service would stay RUNNING with nobody listening.
                server?.Dispose();
                _slots.Release();
                if (server is null)
                {
                    Logger.Error($"Could not create pipe '{_pipeName}'", ex);
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
                continue;
            }

            var connected = server;
            _ = Task.Run(() => Serve(connected));
        }
    }

    private void Serve(NamedPipeServerStream server)
    {
        Interlocked.Increment(ref _activeConnections);
        try { ServeCore(server); }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
            _slots.Release();
        }
    }

    private void ServeCore(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                var clientPid = PipeNative.ClientPid(server);
                var clientSession = clientPid is int pid ? PipeNative.SessionOf(pid) : null;

                var read = Task.Run(() => HelperWire.Read<HelperRequest>(server));
                // WhenAny, not Wait: Wait would rethrow a bad request as an
                // AggregateException and skip the "bad request" reply.
                if (Task.WhenAny(read, Task.Delay(ReadTimeout)).Result != read)
                    return;                            // disposing the pipe unblocks the read

                HelperResponse response;
                if (read.IsFaulted)
                    response = new HelperResponse(HelperProtocol.Version, false, "bad request");
                else if (read.Result is not { } request)
                    return;                            // client left without asking
                else if (clientSession is not int session)
                    response = new HelperResponse(HelperProtocol.Version, false, "cannot identify client");
                else
                    lock (_handleGate) response = _handle(request, session);

                HelperWire.Write(server, response);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Client vanished mid-conversation; nothing to tell it.
            }
            catch (Exception ex)
            {
                Logger.Error("Helper request failed", ex);
            }
        }
    }

    private HelperResponse HandleWithRecipes(HelperRequest request, int clientSession)
    {
        if (request.Op == HelperProtocol.Ping) ReloadIfChanged();
        return _handler!.Handle(request, clientSession);
    }

    private void ReloadIfChanged()
    {
        DateTime? stamp = File.Exists(_recipesPath) ? File.GetLastWriteTimeUtc(_recipesPath!) : null;
        if (_handler is not null && stamp == _recipesStamp) return;
        _recipesStamp = stamp;

        var allow = HelperAllowList.Empty;
        string? error = null;
        try
        {
            allow = new HelperAllowList(RecipeFile.Load(_recipesPath!));
            Logger.Info($"Helper loaded {allow.Count} recipe(s) from {_recipesPath}");
        }
        catch (Exception ex)
        {
            error = $"installed recipes.json could not be loaded ({ex.GetType().Name}); reinstall BgToggle";
            Logger.Error($"Helper could not load recipes from {_recipesPath}; refusing recipe operations", ex);
        }
        _handler = new HelperRequestHandler(allow, _actions!, _appVersion, error);
    }
}
