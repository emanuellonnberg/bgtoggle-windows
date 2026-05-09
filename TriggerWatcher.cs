namespace BgToggle;

public enum TriggerEvent { Running, Exited }

/// <summary>
/// Polls running processes every <see cref="PollIntervalMs"/> ms and fires
/// a callback whenever a tracked exe transitions from not-running to
/// running, or vice versa. Uses a WinForms Timer so callbacks land on the
/// UI thread, which is required because callbacks call ApplyProfile and
/// touch the tray menu.
///
/// At startup, the watcher primes its internal state from the current
/// process snapshot WITHOUT firing any callbacks — otherwise restarting
/// BgToggle while a game is already running would re-apply the trigger
/// profile every launch.
/// </summary>
public sealed class TriggerWatcher : IDisposable
{
    public int PollIntervalMs { get; set; } = 3000;

    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Func<IReadOnlyList<Trigger>> _triggersProvider;
    private readonly Func<string, bool> _isRunning;
    private readonly Action<Trigger, TriggerEvent> _onEvent;
    private readonly Dictionary<string, bool> _state =
        new(StringComparer.OrdinalIgnoreCase);

    public TriggerWatcher(
        Func<IReadOnlyList<Trigger>> triggersProvider,
        Func<string, bool> isRunning,
        Action<Trigger, TriggerEvent> onEvent)
    {
        _triggersProvider = triggersProvider;
        _isRunning = isRunning;
        _onEvent = onEvent;
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        _timer.Interval = PollIntervalMs;
        Prime();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>
    /// Snapshot current running state for every active trigger without
    /// firing callbacks. Called once at start, and again whenever the
    /// trigger list changes (so a newly-added trigger doesn't fire for an
    /// app that's already been running for hours).
    /// </summary>
    public void Prime()
    {
        _state.Clear();
        foreach (var t in _triggersProvider())
        {
            if (!t.Enabled) continue;
            _state[t.ExeName] = _isRunning(t.ExeName);
        }
    }

    /// <summary>
    /// Visible for tests. Performs one polling pass and fires the event
    /// callback for any transitions.
    /// </summary>
    public void PollOnce() => Poll();

    private void Poll()
    {
        foreach (var t in _triggersProvider())
        {
            if (!t.Enabled)
            {
                _state.Remove(t.ExeName);
                continue;
            }

            var nowRunning = _isRunning(t.ExeName);
            _state.TryGetValue(t.ExeName, out var prev);

            if (nowRunning == prev && _state.ContainsKey(t.ExeName)) continue;

            _state[t.ExeName] = nowRunning;
            try
            {
                _onEvent(t, nowRunning ? TriggerEvent.Running : TriggerEvent.Exited);
            }
            catch (Exception ex)
            {
                Logger.Error($"Trigger callback for '{t.ExeName}' threw", ex);
            }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
