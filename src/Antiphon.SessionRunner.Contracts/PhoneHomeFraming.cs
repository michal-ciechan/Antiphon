using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Antiphon.SessionRunner.Contracts;

public static class PhoneHomeFraming
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static int Utf8ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>
    /// Returns false when accepting <paramref name="incomingBytes"/> would exceed
    /// <paramref name="limit"/>, including the already-accumulated prefix. Callers must not
    /// allocate the whole body after a false result.
    /// </summary>
    public static bool CanAccept(int accumulatedUtf8Bytes, int incomingBytes, int limit)
    {
        if (limit <= 0 || incomingBytes < 0 || accumulatedUtf8Bytes < 0)
            return false;
        if (incomingBytes > limit || accumulatedUtf8Bytes > limit)
            return false;
        return accumulatedUtf8Bytes + incomingBytes <= limit;
    }

    public static async Task<PhoneHomeFrame?> ReadFrameAsync(
        WebSocket socket, int maxUtf8Bytes, CancellationToken ct)
    {
        var received = 0;
        byte[]? rented = null;
        MemoryStream? overflowSafe = null;
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(64 * 1024, maxUtf8Bytes));
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (!CanAccept(received, result.Count, maxUtf8Bytes))
                    throw new PhoneHomeTransportException(PhoneHomeProblemTypes.MessageTooLarge,
                        $"UTF-8 message exceeded {maxUtf8Bytes} bytes.");
                overflowSafe ??= new MemoryStream(Math.Min(maxUtf8Bytes, 64 * 1024));
                overflowSafe.Write(buffer, 0, result.Count);
                received += result.Count;
            } while (!result.EndOfMessage);

            return JsonSerializer.Deserialize<PhoneHomeFrame>(overflowSafe!.ToArray(), Json)
                ?? throw new JsonException("Phone-home frame was empty.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
            overflowSafe?.Dispose();
        }
    }

    public static async Task WriteFrameAsync(WebSocket socket, PhoneHomeFrame frame, int maxUtf8Bytes, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
        if (bytes.Length > maxUtf8Bytes)
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.MessageTooLarge,
                $"UTF-8 message exceeded {maxUtf8Bytes} bytes.");
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }
}

public sealed class PhoneHomeTransportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
