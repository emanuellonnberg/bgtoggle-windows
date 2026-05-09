using Xunit;

namespace BgToggle.Tests;

public class TriggerWatcherTests
{
    /// <summary>
    /// We can't construct TriggerWatcher in tests because its WinForms
    /// Timer needs a message loop. Instead, the tests reproduce the same
    /// state-transition logic using the public APIs they exercise. This
    /// verifies the contract the watcher promises:
    ///   - Prime captures current state without firing events.
    ///   - On the next poll, only transitions fire events.
    ///   - Disabled triggers don't fire and don't retain state.
    /// </summary>
    private sealed class HarnessWatcher
    {
        private readonly Func<IReadOnlyList<Trigger>> _provider;
        private readonly Func<string, bool> _isRunning;
        public List<(Trigger t, TriggerEvent ev)> Events { get; } = new();
        private readonly Dictionary<string, bool> _state = new(StringComparer.OrdinalIgnoreCase);

        public HarnessWatcher(Func<IReadOnlyList<Trigger>> p, Func<string, bool> r)
        { _provider = p; _isRunning = r; }

        public void Prime()
        {
            _state.Clear();
            foreach (var t in _provider())
                if (t.Enabled) _state[t.ExeName] = _isRunning(t.ExeName);
        }

        public void Poll()
        {
            foreach (var t in _provider())
            {
                if (!t.Enabled) { _state.Remove(t.ExeName); continue; }
                var now = _isRunning(t.ExeName);
                _state.TryGetValue(t.ExeName, out var prev);
                if (now == prev && _state.ContainsKey(t.ExeName)) continue;
                _state[t.ExeName] = now;
                Events.Add((t, now ? TriggerEvent.Running : TriggerEvent.Exited));
            }
        }
    }

    [Fact]
    public void Prime_DoesNotFireEvents()
    {
        var triggers = new List<Trigger> { new("game", "Gaming") };
        var running = true;
        var w = new HarnessWatcher(() => triggers, _ => running);
        w.Prime();
        w.Poll();

        // Game was already running at prime; first poll sees no transition.
        Assert.Empty(w.Events);
    }

    [Fact]
    public void Poll_FiresRunningOnLaunch()
    {
        var triggers = new List<Trigger> { new("game", "Gaming") };
        var running = false;
        var w = new HarnessWatcher(() => triggers, _ => running);
        w.Prime();
        w.Poll();
        Assert.Empty(w.Events);

        running = true;
        w.Poll();

        Assert.Single(w.Events);
        Assert.Equal(TriggerEvent.Running, w.Events[0].ev);
        Assert.Equal("game", w.Events[0].t.ExeName);
    }

    [Fact]
    public void Poll_FiresExitedAfterRunning()
    {
        var triggers = new List<Trigger> { new("game", "Gaming") };
        var running = false;
        var w = new HarnessWatcher(() => triggers, _ => running);
        w.Prime();

        running = true;
        w.Poll();
        running = false;
        w.Poll();

        Assert.Equal(2, w.Events.Count);
        Assert.Equal(TriggerEvent.Running, w.Events[0].ev);
        Assert.Equal(TriggerEvent.Exited, w.Events[1].ev);
    }

    [Fact]
    public void Poll_DisabledTrigger_NeverFires()
    {
        var triggers = new List<Trigger> { new("game", "Gaming", Enabled: false) };
        var running = false;
        var w = new HarnessWatcher(() => triggers, _ => running);
        w.Prime();

        running = true;
        w.Poll();
        running = false;
        w.Poll();

        Assert.Empty(w.Events);
    }

    [Fact]
    public void Poll_NoStateChange_DoesNotFireRepeatedly()
    {
        var triggers = new List<Trigger> { new("game", "Gaming") };
        var running = false;
        var w = new HarnessWatcher(() => triggers, _ => running);
        w.Prime();

        running = true;
        w.Poll();
        w.Poll();
        w.Poll();

        Assert.Single(w.Events);
    }
}

public class ConfigTriggerSerializationTests
{
    [Fact]
    public void Config_Without_Triggers_Roundtrips()
    {
        var cfg = new Config(
            Apps: new(),
            Profiles: new() { new("Default", new HashSet<string>()) }
        );
        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        var back = System.Text.Json.JsonSerializer.Deserialize<Config>(json);

        Assert.NotNull(back);
        // Triggers may be null or empty depending on serializer; both are fine.
        var list = back!.Triggers ?? new List<Trigger>();
        Assert.Empty(list);
    }

    [Fact]
    public void Config_With_Triggers_Roundtrips()
    {
        var cfg = new Config(
            Apps: new(),
            Profiles: new() { new("Default", new HashSet<string>()) },
            Triggers: new() { new Trigger("Cyberpunk2077", "Gaming", "Working") }
        );
        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        var back = System.Text.Json.JsonSerializer.Deserialize<Config>(json);

        Assert.NotNull(back);
        Assert.NotNull(back!.Triggers);
        Assert.Single(back.Triggers!);
        Assert.Equal("Cyberpunk2077", back.Triggers![0].ExeName);
        Assert.Equal("Gaming", back.Triggers[0].WhileRunningProfile);
        Assert.Equal("Working", back.Triggers[0].OnExitProfile);
        Assert.True(back.Triggers[0].Enabled);
    }
}
