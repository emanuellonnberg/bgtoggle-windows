using System.Runtime.InteropServices;
using System.Text;

namespace BgToggle;

/// <summary>
/// Polite close for processes that may have no visible window. Tray apps
/// usually keep only hidden top-level windows, so Process.MainWindowHandle
/// is zero and CloseMainWindow does nothing. Like `taskkill` without /F, we
/// post WM_CLOSE to every top-level window the process owns.
/// </summary>
public static class WindowCloser
{
    private const uint WM_CLOSE = 0x0010;

    // Per-thread IME windows exist in nearly every GUI process and aren't
    // the app's own; closing them does nothing useful.
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "IME", "MSCTFIME UI"
    };

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>All top-level windows (visible or hidden) owned by the process.</summary>
    public static List<IntPtr> FindTopLevel(int pid)
    {
        var result = new List<IntPtr>();
        var cls = new StringBuilder(64);
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner != (uint)pid) return true;

            cls.Clear();
            GetClassName(hWnd, cls, cls.Capacity);
            if (!IgnoredClasses.Contains(cls.ToString()))
                result.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Post WM_CLOSE to each of the process's top-level windows. Returns how
    /// many posts succeeded; UIPI blocks posts to elevated processes from a
    /// non-elevated BgToggle, so this can be zero even when windows exist.
    /// </summary>
    public static int PostClose(int pid)
    {
        var posted = 0;
        foreach (var hWnd in FindTopLevel(pid))
        {
            if (PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                posted++;
        }
        return posted;
    }
}
