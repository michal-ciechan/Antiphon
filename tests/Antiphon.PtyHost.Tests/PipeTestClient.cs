using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost.Tests;

/// <summary>Test-side pipe client speaking the pty-host framing, with timeout-guarded reads.</summary>
public sealed class PipeTestClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly NamedPipeClientStream _pipe;
    private readonly Func<string>? _hostDiagnostics;

    private PipeTestClient(NamedPipeClientStream pipe, Func<string>? hostDiagnostics)
    {
        _pipe = pipe;
        _hostDiagnostics = hostDiagnostics;
    }

    public static async Task<PipeTestClient> ConnectAsync(string pipeName, TimeSpan? timeout = null,
        Func<string>? hostDiagnostics = null)
    {
        var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        await pipe.ConnectAsync(cts.Token);
        return new PipeTestClient(pipe, hostDiagnostics);
    }

    /// <summary>Connect with retries while the host process is still starting its pipe server.</summary>
    public static async Task<PipeTestClient> ConnectWithRetryAsync(string pipeName, TimeSpan overallTimeout,
        Func<string>? hostDiagnostics = null)
    {
        var deadline = DateTime.UtcNow + overallTimeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return await ConnectAsync(pipeName, TimeSpan.FromSeconds(2), hostDiagnostics);
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
        catch (IOException ex)
        {
            string diagnostics;
            try { diagnostics = _hostDiagnostics?.Invoke() ?? "Host diagnostics were not supplied."; }
            catch (Exception diagnosticFailure)
            {
                diagnostics = $"Host diagnostics unavailable: {diagnosticFailure.GetType().Name}.";
            }
            throw new EndOfStreamException($"Host closed the pipe. {diagnostics}", ex);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Timed out waiting for a frame from the host.");
        }
    }

    /// <summary>Capture evidence before fixture teardown removes the detached host's only log.</summary>
    public static string DescribeHost(Process host, string? logPath, string? stderr = null)
    {
        string state;
        try
        {
            // EOF can precede the final process signal. Bound that race; a live host stays live.
            state = host.WaitForExit(500) ? $"exit code={host.ExitCode}" : "still running; exit code=unavailable";
        }
        catch (Exception ex) { state = $"exit code=unavailable ({ex.GetType().Name})"; }

        const int tailBytes = 8192;
        string logTail;
        try
        {
            using var stream = new FileStream(logPath!, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - tailBytes), SeekOrigin.Begin);
            var buffer = new byte[tailBytes];
            var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            logTail = Encoding.UTF8.GetString(buffer, 0, count);
        }
        catch (Exception ex) { logTail = $"unavailable ({ex.GetType().Name})"; }
        var stderrTail = stderr is null ? "not captured (detached host; see host log)"
            : stderr.Length <= tailBytes ? stderr : stderr[^tailBytes..];
        return $"host pid={host.Id}; {state}; stderr tail: {stderrTail}; log tail ({logPath}): {logTail}";
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
