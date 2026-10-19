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

    private readonly string _pipeName;
    private readonly PipeSecurity? _security;
    private readonly Func<HelperRequest, int, HelperResponse> _handle;
    private readonly object _handleGate = new();

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
            NamedPipeServerStream server;
            try
            {
                // FirstPipeInstance on the first instance makes creation fail
                // if someone else already owns the name (pipe squatting).
                var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
                server = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options, 0, 0, _security);
                first = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Error($"Could not create pipe '{_pipeName}'", ex);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }

            _ = Task.Run(() => Serve(server));
        }
    }

    private void Serve(NamedPipeServerStream server)
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
        try
        {
            allow = new HelperAllowList(RecipeFile.Load(_recipesPath!));
            Logger.Info($"Helper loaded {allow.Count} recipe(s) from {_recipesPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Helper could not load recipes from {_recipesPath}; refusing recipe operations", ex);
        }
        _handler = new HelperRequestHandler(allow, _actions!, _appVersion);
    }
}
