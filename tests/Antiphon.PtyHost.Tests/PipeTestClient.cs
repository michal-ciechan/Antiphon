using System.IO.Pipes;
using System.Text;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost.Tests;

/// <summary>Test-side pipe client speaking the pty-host framing, with timeout-guarded reads.</summary>
public sealed class PipeTestClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly NamedPipeClientStream _pipe;

    private PipeTestClient(NamedPipeClientStream pipe) => _pipe = pipe;

    public static async Task<PipeTestClient> ConnectAsync(string pipeName, TimeSpan? timeout = null)
    {
        var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        await pipe.ConnectAsync(cts.Token);
        return new PipeTestClient(pipe);
    }

    /// <summary>Connect with retries while the host process is still starting its pipe server.</summary>
    public static async Task<PipeTestClient> ConnectWithRetryAsync(string pipeName, TimeSpan overallTimeout)
    {
        var deadline = DateTime.UtcNow + overallTimeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return await ConnectAsync(pipeName, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException)
            {
                last = ex;
                await Task.Delay(200);
            }
        }

        throw new TimeoutException($"Could not connect to pipe '{pipeName}' within {overallTimeout}.", last);
    }

    public async Task SendAsync(PtyHostMessage message)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        await PtyHostFraming.WriteAsync(_pipe, message, cts.Token);
    }

    public async Task<PtyHostMessage> ReadAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        try
        {
            return await PtyHostFraming.ReadAsync(_pipe, cts.Token)
                ?? throw new EndOfStreamException("Host closed the pipe.");
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Timed out waiting for a frame from the host.");
        }
    }

    /// <summary>
    /// Reads frames until one of type <typeparamref name="T"/> arrives, failing fast on
    /// <see cref="ErrorMessage"/> so protocol errors surface as test failures with the host's text.
    /// Other frame types (e.g. interleaved Output) are skipped.
    /// </summary>
    public async Task<T> ExpectAsync<T>(TimeSpan? timeout = null) where T : PtyHostMessage
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"Timed out waiting for {typeof(T).Name}.");

            var message = await ReadAsync(remaining);
            switch (message)
            {
                case T match:
                    return match;
                case ErrorMessage error:
                    throw new InvalidOperationException($"Host error {error.Code}: {error.Message}");
            }
        }
    }

    /// <summary>
    /// Accumulates Output frames until the combined text satisfies <paramref name="predicate"/>.
    /// Returns the collected chunks in order (sequence, text).
    /// </summary>
    public async Task<List<OutputMessage>> CollectOutputUntilAsync(
        Func<string, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        var chunks = new List<OutputMessage>();
        var text = new StringBuilder();
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException(
                    $"Timed out waiting for output predicate. Collected so far: {text}");

            var message = await ReadAsync(remaining);
            if (message is ErrorMessage error)
                throw new InvalidOperationException($"Host error {error.Code}: {error.Message}");
            if (message is not OutputMessage output)
                continue;

            chunks.Add(output);
            text.Append(output.Chunk);
            if (predicate(text.ToString()))
                return chunks;
        }
    }

    public ValueTask DisposeAsync() => _pipe.DisposeAsync();
}
