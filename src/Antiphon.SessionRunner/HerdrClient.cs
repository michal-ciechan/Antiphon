using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// Minimal raw-socket client for an already-running Herdr instance. Herdr uses one NDJSON request
/// per normal named-pipe connection; subscriptions are the deliberate exception and retain their
/// connection for push events.
/// </summary>
public sealed class HerdrClient
{
    private readonly HerdrSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public HerdrClient(IOptions<HerdrSettings> settings)
        : this(settings.Value)
    {
    }

    internal HerdrClient(HerdrSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Resolves Herdr's documented session/socket precedence to the Windows pipe name.</summary>
    public string ResolveSocketPath()
    {
        var session = _settings.Session;
        if (!string.IsNullOrWhiteSpace(session))
            return SocketPathForSession(session);

        var explicitPath = Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;

        var environmentSession = Environment.GetEnvironmentVariable("HERDR_SESSION");
        if (!string.IsNullOrWhiteSpace(environmentSession))
            return SocketPathForSession(environmentSession);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "herdr", "herdr.sock");
    }

    /// <summary>
    /// Connects and proves that the operator-run backend answers the expected protocol. A missing,
    /// stopped, unreachable, or incompatible Herdr is always an explicit exception — never a
    /// fallback to an existing PTY backend.
    /// </summary>
    public async Task<HerdrServerInfo> ConnectAndValidateAsync(CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var result = await SendRequestAsync("ping", new { }, cancellationToken);
        try
        {
            var type = result.GetProperty("type").GetString();
            var version = result.GetProperty("version").GetString();
            var protocol = result.GetProperty("protocol").GetInt32();
            if (!string.Equals(type, "pong", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(version))
                throw new HerdrProtocolException("Herdr returned a malformed ping response.");
            if (protocol != _settings.ExpectedProtocol)
                throw new HerdrProtocolMismatchException(_settings.ExpectedProtocol, protocol, version);

            return new HerdrServerInfo(version, protocol);
        }
        catch (KeyNotFoundException ex)
        {
            throw new HerdrProtocolException("Herdr returned a ping response without version or protocol.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new HerdrProtocolException("Herdr returned a malformed ping response.", ex);
        }
    }

    /// <summary>Sends one normal raw-socket request and returns its result object.</summary>
    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        EnsureEnabled();

        var requestId = Guid.NewGuid().ToString("N");
        await using var pipe = await ConnectPipeAsync(cancellationToken);
        var writer = CreateWriter(pipe);
        var reader = CreateReader(pipe);
        try
        {
            await WriteRequestAsync(writer, new HerdrRequest(requestId, method, parameters), cancellationToken);
            var response = await ReadResponseAsync(reader, cancellationToken);
            return RequireResult(response, requestId);
        }
        finally
        {
            // Herdr intentionally closes normal request pipes after its response. StreamWriter can
            // surface that expected close while flushing during Dispose; never let cleanup mask the
            // response/protocol outcome above.
            DisposeQuietly(writer);
            DisposeQuietly(reader);
        }
    }

    /// <summary>
    /// Opens Herdr's long-lived <c>events.subscribe</c> stream. The initial acknowledgement is
    /// consumed internally; each subsequent event envelope is yielded intact.
    /// </summary>
    public async IAsyncEnumerable<HerdrEvent> SubscribeEventsAsync(
        IReadOnlyCollection<HerdrSubscription> subscriptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        EnsureEnabled();

        // A subscription has no ping response on its own pipe. Prove compatibility before opening
        // the long-lived stream, rather than accepting an arbitrary server that happens to speak NDJSON.
        await ConnectAndValidateAsync(cancellationToken);

        var requestId = Guid.NewGuid().ToString("N");
        await using var pipe = await ConnectPipeAsync(cancellationToken);
        var writer = CreateWriter(pipe);
        var reader = CreateReader(pipe);
        try
        {
            await WriteRequestAsync(writer,
                new HerdrRequest(requestId, "events.subscribe", new { subscriptions }), cancellationToken);
            var acknowledgement = await ReadResponseAsync(reader, cancellationToken);
            _ = RequireResult(acknowledgement, requestId);

            while (true)
            {
                using var message = await ReadResponseAsync(reader, cancellationToken);
                var root = message.RootElement;
                if (!root.TryGetProperty("event", out var eventName)
                    || !root.TryGetProperty("data", out var data)
                    || eventName.ValueKind != JsonValueKind.String)
                {
                    throw new HerdrProtocolException("Herdr emitted a malformed subscription event.");
                }

                yield return new HerdrEvent(eventName.GetString()!, data.Clone());
            }
        }
        finally
        {
            DisposeQuietly(writer);
            DisposeQuietly(reader);
        }
    }

    private async Task<NamedPipeClientStream> ConnectPipeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new HerdrBackendUnavailableException("Herdr's named-pipe backend is only available on Windows.");

        var pipe = new NamedPipeClientStream(
            ".", ResolveSocketPath(), PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, _settings.ConnectTimeoutMs)));
            await pipe.ConnectAsync(timeout.Token);
            return pipe;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await pipe.DisposeAsync();
            throw new HerdrBackendUnavailableException(
                $"Herdr is unavailable: named pipe '{ResolveSocketPath()}' did not accept a connection within "
                + $"{_settings.ConnectTimeoutMs} ms.");
        }
        catch (IOException ex)
        {
            await pipe.DisposeAsync();
            throw new HerdrBackendUnavailableException(
                $"Herdr is unavailable: could not connect to named pipe '{ResolveSocketPath()}'.", ex);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    private static StreamWriter CreateWriter(Stream stream) => new(
        stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
    { AutoFlush = true };

    private static StreamReader CreateReader(Stream stream) => new(
        stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false,
        leaveOpen: true);

    private static void DisposeQuietly(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (IOException)
        {
            // Normal request pipes are server-closed before the client disposes its writer.
        }
    }

    private async Task WriteRequestAsync(StreamWriter writer, HerdrRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                throw new HerdrBackendUnavailableException("Herdr closed its named-pipe connection without a response.");
            return JsonDocument.Parse(line);
        }
        catch (IOException ex)
        {
            throw new HerdrBackendUnavailableException("Herdr's named-pipe connection failed while waiting for a response.", ex);
        }
        catch (JsonException ex)
        {
            throw new HerdrProtocolException("Herdr returned invalid JSON over its named pipe.", ex);
        }
    }

    private static JsonElement RequireResult(JsonDocument response, string requestId)
    {
        var root = response.RootElement;
        if (!root.TryGetProperty("id", out var id) || !string.Equals(id.GetString(), requestId, StringComparison.Ordinal))
            throw new HerdrProtocolException("Herdr returned a response with a missing or mismatched request id.");

        if (root.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            throw new HerdrApiException(code ?? "unknown", message ?? "Herdr returned an unspecified error.");
        }

        if (!root.TryGetProperty("result", out var result))
            throw new HerdrProtocolException("Herdr returned neither result nor error.");

        return result.Clone();
    }

    private void EnsureEnabled()
    {
        if (!_settings.Enabled)
            throw new HerdrBackendUnavailableException(
                "Herdr backend is disabled. Set SessionRunner:Herdr:Enabled=true before selecting it for a session.");
    }

    private static string SocketPathForSession(string session) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "herdr", "sessions", session, "herdr.sock");

    private sealed record HerdrRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params")] object? Parameters);
}

public sealed record HerdrServerInfo(string Version, int Protocol);

public sealed record HerdrSubscription([property: JsonPropertyName("type")] string Type);

public sealed record HerdrEvent(string Name, JsonElement Data);

public class HerdrProtocolException : Exception
{
    public HerdrProtocolException(string message, Exception? innerException = null) : base(message, innerException) { }
}

public sealed class HerdrProtocolMismatchException : HerdrProtocolException
{
    public HerdrProtocolMismatchException(int expected, int actual, string version)
        : base($"Herdr protocol mismatch: this runner requires {expected}, but Herdr {version} reports {actual}.") { }
}

public sealed class HerdrApiException : Exception
{
    public HerdrApiException(string code, string message) : base($"Herdr API error '{code}': {message}")
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class HerdrBackendUnavailableException : Exception
{
    public HerdrBackendUnavailableException(string message, Exception? innerException = null) : base(message, innerException) { }
}
