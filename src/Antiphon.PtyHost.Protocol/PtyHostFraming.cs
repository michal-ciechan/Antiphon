using System.Buffers.Binary;
using System.Text.Json;

namespace Antiphon.PtyHost.Protocol;

/// <summary>
/// Length-prefixed JSON framing for the pty-host pipe: 4-byte little-endian payload length,
/// then UTF-8 JSON of a <see cref="PtyHostMessage"/> (polymorphic via "$type").
/// </summary>
public static class PtyHostFraming
{
    /// <summary>Hard cap on a single frame; a bigger header means a corrupt stream, not real data.</summary>
    public const int MaxFrameBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async ValueTask WriteAsync(Stream stream, PtyHostMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Reads one frame; returns null on clean end-of-stream (peer disconnected).</summary>
    public static async ValueTask<PtyHostMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await TryReadExactlyAsync(stream, header, ct))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxFrameBytes)
            throw new InvalidDataException($"Invalid pty-host frame length {length}.");

        var payload = new byte[length];
        if (!await TryReadExactlyAsync(stream, payload, ct))
            throw new EndOfStreamException("Pipe closed mid-frame.");

        return JsonSerializer.Deserialize<PtyHostMessage>(payload, Options)
            ?? throw new InvalidDataException("Pty-host frame deserialized to null.");
    }

    private static async ValueTask<bool> TryReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            }
            catch (IOException) when (read == 0)
            {
                // A killed peer surfaces as a broken pipe rather than EOF; treat identically.
                return false;
            }

            if (n <= 0)
                return read != 0 ? throw new EndOfStreamException("Pipe closed mid-frame.") : false;
            read += n;
        }

        return true;
    }
}
