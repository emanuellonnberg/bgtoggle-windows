using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BgToggle;

/// <summary>Who is on the other end of a pipe, and which session they run in.</summary>
internal static class PipeNative
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    public static int? ClientPid(PipeStream pipe) =>
        GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) ? (int)pid : null;

    public static int? ServerPid(PipeStream pipe) =>
        GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) ? (int)pid : null;

    public static int? SessionOf(int pid) =>
        ProcessIdToSessionId((uint)pid, out var session) ? (int)session : null;
}
