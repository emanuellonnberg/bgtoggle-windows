using System.Runtime.InteropServices;

namespace BgToggle;

/// <summary>
/// Registers global hotkeys via user32.RegisterHotKey. Each hotkey is bound
/// to a callback. Uses a hidden message-only NativeWindow to receive
/// WM_HOTKEY without showing a top-level form.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        _window = new MessageWindow(OnHotkey);
    }

    /// <summary>
    /// Register a hotkey from a string like "Ctrl+Alt+1" or "Win+Shift+G".
    /// Returns false if the spec is invalid or the OS rejects the binding
    /// (commonly: another app already owns it).
    /// </summary>
    public bool Register(string spec, Action callback)
    {
        if (!TryParse(spec, out var mods, out var vk))
        {
            Logger.Warn($"Hotkey spec invalid: '{spec}'");
            return false;
        }

        var id = _nextId++;
        if (!RegisterHotKey(_window.Handle, id, mods | MOD_NOREPEAT, vk))
        {
            var err = Marshal.GetLastWin32Error();
            Logger.Warn($"RegisterHotKey('{spec}') failed (Win32 {err}); maybe in use by another app");
            return false;
        }
        _callbacks[id] = callback;
        Logger.Info($"Registered hotkey '{spec}' as id {id}");
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys.ToList())
            UnregisterHotKey(_window.Handle, id);
        _callbacks.Clear();
    }

    private void OnHotkey(int id)
    {
        if (_callbacks.TryGetValue(id, out var cb))
        {
            try { cb(); }
            catch (Exception ex) { Logger.Error($"Hotkey {id} callback threw", ex); }
        }
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }

    private static bool TryParse(string spec, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var m = parts[i].ToLowerInvariant();
            mods |= m switch
            {
                "ctrl" or "control" => (uint)MOD_CONTROL,
                "alt" => (uint)MOD_ALT,
                "shift" => (uint)MOD_SHIFT,
                "win" or "windows" or "meta" => (uint)MOD_WIN,
                _ => 0u
            };
            if (mods == 0 && i == 0) return false;
        }

        var keyStr = parts[^1];
        Keys key;
        // Bare digits map to D0..D9. Check first because Enum.TryParse("1")
        // would otherwise resolve to Keys.LButton (raw value 1).
        if (keyStr.Length == 1 && char.IsDigit(keyStr[0]))
        {
            key = Keys.D0 + (keyStr[0] - '0');
        }
        else if (!Enum.TryParse(keyStr, ignoreCase: true, out key))
        {
            return false;
        }
        vk = (uint)key;
        return true;
    }

    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action<int> _onHotkey;

        public MessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            // HWND_MESSAGE = -3 → message-only window (no taskbar entry, no UI).
            var cp = new CreateParams { Parent = (IntPtr)(-3) };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                _onHotkey(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }
}
