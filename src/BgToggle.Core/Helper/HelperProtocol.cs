using System.Text.Json;

namespace BgToggle;

/// <summary>
/// Shared constants for the tray ↔ helper pipe. The protocol version is
/// independent of the app version so a dev build can talk to an installed
/// helper as long as the wire format hasn't changed.
/// </summary>
public static class HelperProtocol
{
    public const int Version = 1;
    public const string PipeName = "BgToggle.Helper";
    public const string ServiceName = "BgToggleHelper";

    public const string Ping = "ping";
    public const string StopServices = "stopServices";
    public const string StartServices = "startServices";
    public const string KillProcesses = "killProcesses";

    public const int MaxMessageBytes = 64 * 1024;
}

/// <summary>Requests name a recipe id only — never paths, commands, services or processes.</summary>
public record HelperRequest(int Protocol, string Op, string? RecipeId = null);

public record HelperResponse(
    int Protocol,
    bool Ok,
    string? Error = null,
    List<string>? Log = null,
    string? AppVersion = null);

/// <summary>One UTF-8, camelCase JSON object per line.</summary>
public static class HelperWire
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static void Write<T>(Stream s, T message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        s.Write(bytes);
        s.WriteByte((byte)'\n');
        s.Flush();
    }

    /// <summary>
    /// Null on EOF before any byte. Throws <see cref="InvalidDataException"/>
    /// for oversized or non-JSON messages so a hostile client can't make the
    /// helper buffer without bound or crash it.
    /// </summary>
    public static T? Read<T>(Stream s) where T : class
    {
        var buffer = new MemoryStream();
        while (true)
        {
            var b = s.ReadByte();
            if (b < 0)
            {
                if (buffer.Length == 0) return null;
                break;
            }
            if (b == '\n') break;
            if (buffer.Length >= HelperProtocol.MaxMessageBytes)
                throw new InvalidDataException($"message exceeds {HelperProtocol.MaxMessageBytes} bytes");
            buffer.WriteByte((byte)b);
        }
        return Deserialize<T>(buffer.ToArray());
    }

    /// <summary>
    /// Async counterparts used by the helper and the tray. Waiting on async
    /// pipe I/O holds no thread-pool thread, so a few slow or idle clients
    /// can't starve the pool (blocking reads did, on 2-core machines).
    /// ConfigureAwait(false) because the tray calls these from the UI thread.
    /// </summary>
    public static async Task WriteAsync<T>(Stream s, T message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        var bytes = new byte[json.Length + 1];
        json.CopyTo(bytes, 0);
        bytes[^1] = (byte)'\n';
        await s.WriteAsync(bytes, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Same contract as <see cref="Read{T}"/>; one message per connection, so bytes after the newline are ignored.</summary>
    public static async Task<T?> ReadAsync<T>(Stream s, CancellationToken ct) where T : class
    {
        var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var n = await s.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (buffer.Length == 0) return null;
                break;
            }
            var newline = Array.IndexOf(chunk, (byte)'\n', 0, n);
            var take = newline >= 0 ? newline : n;
            if (buffer.Length + take > HelperProtocol.MaxMessageBytes)
                throw new InvalidDataException($"message exceeds {HelperProtocol.MaxMessageBytes} bytes");
            buffer.Write(chunk, 0, take);
            if (newline >= 0) break;
        }
        return Deserialize<T>(buffer.ToArray());
    }

    private static T Deserialize<T>(byte[] bytes) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw new InvalidDataException("empty message");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("message is not valid JSON: " + ex.Message, ex);
        }
    }
}
