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

    internal HerdrSettings Settings => _settings;

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

    // --- typed wrappers (CARD-0160 B2 / plan §8). agent.prompt is deliberately not wrapped. ---

    public async Task<IReadOnlyList<HerdrWorkspaceInfo>> WorkspaceListAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync("workspace.list", new { }, cancellationToken);
        return DeserializeRequired<HerdrWorkspaceListEnvelope>(result, "workspace.list").Workspaces;
    }

    public Task<HerdrWorkspaceCreateResult> WorkspaceCreateAsync(
        string? cwd,
        string? label,
        CancellationToken cancellationToken) =>
        WorkspaceCreateAsync(cwd, env: null, label, cancellationToken);

    public async Task<HerdrWorkspaceCreateResult> WorkspaceCreateAsync(
        string? cwd,
        IReadOnlyDictionary<string, string>? env,
        string? label,
        CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync(
            "workspace.create",
            new HerdrWorkspaceCreateParams(Cwd: cwd, Label: label, Env: env),
            cancellationToken);
        var envelope = DeserializeRequired<HerdrWorkspaceCreateEnvelope>(result, "workspace.create");
        if (envelope.Tab is null
            || envelope.RootPane is null
            || string.IsNullOrWhiteSpace(envelope.Tab.TabId)
            || string.IsNullOrWhiteSpace(envelope.RootPane.PaneId))
        {
            throw new HerdrProtocolException(
                "Herdr returned a workspace.create result without tab and root_pane.");
        }

        return new HerdrWorkspaceCreateResult(envelope.Workspace, envelope.Tab, envelope.RootPane);
    }

    public async Task WorkspaceReportMetadataAsync(
        string workspaceId,
        IReadOnlyDictionary<string, string?> tokens,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(tokens);
        await SendRequestAsync(
            "workspace.report_metadata",
            new HerdrWorkspaceReportMetadataParams(workspaceId, HerdrSources.Antiphon, tokens),
            cancellationToken);
    }

    public async Task<HerdrTabCreateResult> TabCreateAsync(
        string workspaceId,
        string? cwd,
        IReadOnlyDictionary<string, string>? env,
        string? label,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var result = await SendRequestAsync(
            "tab.create",
            new HerdrTabCreateParams(WorkspaceId: workspaceId, Cwd: cwd, Env: env, Label: label),
            cancellationToken);
        var envelope = DeserializeRequired<HerdrTabCreateEnvelope>(result, "tab.create");
        return new HerdrTabCreateResult(envelope.Tab, envelope.RootPane);
    }

    public async Task<HerdrPaneInfo> PaneSplitAsync(
        string targetPaneId,
        string direction,
        double? ratio,
        string? cwd,
        IReadOnlyDictionary<string, string>? env,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPaneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(direction);
        var result = await SendRequestAsync(
            "pane.split",
            new HerdrPaneSplitParams(
                Direction: direction,
                TargetPaneId: targetPaneId,
                Ratio: ratio,
                Cwd: cwd,
                Env: env),
            cancellationToken);
        return DeserializeRequired<HerdrPaneEnvelope>(result, "pane.split").Pane;
    }

    public async Task PaneRenameAsync(string paneId, string? label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        await SendRequestAsync(
            "pane.rename",
            new HerdrPaneRenameParams(paneId, label),
            cancellationToken);
    }

    public async Task TabRenameAsync(string tabId, string? label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        await SendRequestAsync(
            "tab.rename",
            new HerdrTabRenameParams(tabId, label),
            cancellationToken);
    }

    public async Task PaneReportMetadataAsync(
        string paneId,
        IReadOnlyDictionary<string, string?>? tokens,
        string? title,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        await SendRequestAsync(
            "pane.report_metadata",
            new HerdrPaneReportMetadataParams(paneId, HerdrSources.Antiphon, tokens, title),
            cancellationToken);
    }

    /// <summary>Send display-only pane metadata. This must never take agent lifecycle authority.</summary>
    public async Task PaneReportMetadataAsync(
        HerdrPaneReportMetadataParams parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.PaneId);
        await SendRequestAsync("pane.report_metadata", parameters, cancellationToken);
    }

    public async Task PaneReportAgentSessionAsync(
        string paneId,
        string agent,
        string? agentSessionId,
        string? agentSessionPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);
        await SendRequestAsync(
            "pane.report_agent_session",
            new HerdrPaneReportAgentSessionParams(
                paneId, HerdrSources.Antiphon, agent, agentSessionId, agentSessionPath),
            cancellationToken);
    }

    public async Task<HerdrAgentInfo> AgentStartAsync(
        string name,
        string kind,
        string paneId,
        IReadOnlyList<string>? args,
        long? timeoutMs,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        var result = await SendRequestAsync(
            "agent.start",
            new HerdrAgentStartParams(name, kind, paneId, args, timeoutMs),
            cancellationToken);
        return DeserializeRequired<HerdrAgentStartedEnvelope>(result, "agent.start").Agent;
    }

    /// <summary>CARD-0211: live agents only. A name-less (K5 passively-detected) agent deserialises with <c>Name = null</c>.</summary>
    public async Task<IReadOnlyList<HerdrAgentInfo>> AgentListAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync("agent.list", new { }, cancellationToken);
        return DeserializeRequired<HerdrAgentListEnvelope>(result, "agent.list").Agents;
    }

    /// <summary>
    /// CARD-0211: rename (or clear with <paramref name="name"/> null) the live agent identified
    /// by pane id or unique live name. Result envelope is opaque.
    /// </summary>
    public async Task AgentRenameAsync(string target, string? name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        await SendRequestAsync(
            "agent.rename",
            new HerdrAgentRenameParams(target, name),
            cancellationToken);
    }

    public async Task<HerdrPaneInfo> PaneGetAsync(string paneId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        var result = await SendRequestAsync("pane.get", new HerdrPaneTargetParams(paneId), cancellationToken);
        return DeserializeRequired<HerdrPaneEnvelope>(result, "pane.get").Pane;
    }

    public async Task<IReadOnlyList<HerdrPaneInfo>> PaneListAsync(
        string? workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync(
            "pane.list",
            new HerdrPaneListParams(workspaceId),
            cancellationToken);
        return DeserializeRequired<HerdrPaneListEnvelope>(result, "pane.list").Panes;
    }

    public async Task<HerdrPaneProcessInfo> PaneProcessInfoAsync(
        string paneId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        var result = await SendRequestAsync(
            "pane.process_info",
            new HerdrPaneProcessInfoParams(paneId),
            cancellationToken);
        return DeserializeRequired<HerdrPaneProcessInfoEnvelope>(result, "pane.process_info").ProcessInfo;
    }

    public async Task<HerdrPaneReadResult> PaneReadAsync(
        string paneId,
        string source,
        bool stripAnsi,
        int? lines,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var result = await SendRequestAsync(
            "pane.read",
            new HerdrPaneReadParams(paneId, source, stripAnsi, lines),
            cancellationToken);
        return DeserializeRequired<HerdrPaneReadEnvelope>(result, "pane.read").Read;
    }

    public async Task PaneSendTextAsync(string paneId, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        ArgumentNullException.ThrowIfNull(text);
        await SendRequestAsync(
            "pane.send_text",
            new HerdrPaneSendTextParams(paneId, text),
            cancellationToken);
    }

    public async Task PaneSendKeysAsync(
        string paneId,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        ArgumentNullException.ThrowIfNull(keys);
        await SendRequestAsync(
            "pane.send_keys",
            new HerdrPaneSendKeysParams(paneId, keys),
            cancellationToken);
    }

    public async Task PaneCloseAsync(string paneId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        await SendRequestAsync("pane.close", new HerdrPaneTargetParams(paneId), cancellationToken);
    }

    public async Task TabCloseAsync(string tabId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        await SendRequestAsync("tab.close", new HerdrTabTargetParams(tabId), cancellationToken);
    }

    private T DeserializeRequired<T>(JsonElement result, string method)
    {
        try
        {
            var value = result.Deserialize<T>(_jsonOptions);
            if (value is null)
                throw new HerdrProtocolException($"Herdr returned a null '{method}' result.");
            return value;
        }
        catch (JsonException ex)
        {
            throw new HerdrProtocolException($"Herdr returned a malformed '{method}' result.", ex);
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
            // Subscribe-only: herdr suffixes the error id as "<id>:sub:N:probe" (CARD-0162 E2).
            // Accept equality or a "{requestId}:" prefix so pane_not_found surfaces as
            // HerdrApiException instead of a protocol mismatch.
            _ = RequireResult(acknowledgement, requestId, allowSuffixedId: true);

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

    private static JsonElement RequireResult(
        JsonDocument response,
        string requestId,
        bool allowSuffixedId = false)
    {
        var root = response.RootElement;
        if (!root.TryGetProperty("id", out var idElement))
            throw new HerdrProtocolException("Herdr returned a response with a missing or mismatched request id.");

        var responseId = idElement.GetString();
        var idMatches = string.Equals(responseId, requestId, StringComparison.Ordinal)
            || (allowSuffixedId
                && responseId is not null
                && responseId.StartsWith(requestId + ":", StringComparison.Ordinal));
        if (!idMatches)
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

/// <summary>
/// One <c>events.subscribe</c> entry. Subscribe types are dotted (<c>pane.closed</c>); the wire
/// event name on the stream is underscored (<c>pane_closed</c>) — both measured (CARD-0162).
/// <see cref="PaneId"/> is required by schema for <c>pane.agent_status_changed</c> and omitted
/// when null for type-only global entries.
/// </summary>
public sealed record HerdrSubscription(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("pane_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PaneId = null);

public sealed record HerdrEvent(string Name, JsonElement Data);

/// <summary>
/// Subscribe TYPE (dotted) ↔ wire EVENT name pairs measured against herdr 0.8.2.
/// Status events are also dotted on the live stream (CARD-0163 R9, 2026-08-26), despite the
/// schema's underscored event type. Consumers must use these constants — never ad-hoc string matches.
/// </summary>
public static class HerdrEventTypes
{
    public const string PaneClosedSubscribe = "pane.closed";
    public const string PaneClosedWire = "pane_closed";

    public const string PaneExitedSubscribe = "pane.exited";
    public const string PaneExitedWire = "pane_exited";

    public const string PaneAgentStatusChangedSubscribe = "pane.agent_status_changed";
    /// <summary>Schema spelling retained for compatibility with older herdr implementations.</summary>
    public const string PaneAgentStatusChangedWire = "pane_agent_status_changed";
    /// <summary>Measured live-stream spelling (herdr 0.8.2, CARD-0163 R9).</summary>
    public const string PaneAgentStatusChangedWireDotted = "pane.agent_status_changed";
}

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

/// <summary>CARD-0187 / CARD-0224: herdr-lane launch failed (wrong kind, detection timeout, non-PowerShell shell, occupied pane).</summary>
public sealed class HerdrLaunchException : Exception
{
    public const string CodePaneOccupied = "pane_occupied";
    public const string CodePaneShell = "pane_shell";
    public const string CodeDetectTimeout = "detect_timeout";

    public HerdrLaunchException(string message, string? code = null) : base(message)
    {
        Code = code;
    }

    public string? Code { get; }
}
