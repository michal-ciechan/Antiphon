using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class SessionRunnerHttpClient : ISessionRunnerClient
{
    private static readonly TimeSpan CapabilityProbeTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CapabilityProbeTimeout = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Named client for the /events SSE stream: registered with an INFINITE timeout, because
    /// HttpClient.Timeout covers the whole response body and the default 100 s tore the stream
    /// down every 100 s (events in the reconnect gaps were lost). Liveness comes from the runner's
    /// keepalive comments plus the idle watchdog in <see cref="StreamEventsAsync"/> instead.
    /// </summary>
    public const string EventStreamClientName = "session-runner-events";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SessionRunnerSettings _settings;
    private readonly object _capabilityGate = new();
    private RunnerCapabilitiesDto? _cachedCapabilities;
    private DateTimeOffset _capabilitiesProbedAt = DateTimeOffset.MinValue;
    private Task? _capabilityProbe;

    public SessionRunnerHttpClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        IOptions<SessionRunnerSettings> settings)
    {
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        if (await GetTranscriptCapabilityMismatchAsync(spec.Kind, ct) is { } mismatch)
            throw new RunnerCapabilityMismatchException(mismatch.Message);

        // CARD-0160: refuse a herdr launch before POSTing unless the runner advertises "herdr".
        // An old runner would ignore the unknown Backend field and silently launch a pty-host —
        // never silently remap (CARD-0111 §6). Null capabilities = no evidence = refuse.
        var backendWire = BackendWire(spec.Backend);
        if (backendWire == SessionBackends.Herdr
            && await GetSessionBackendCapabilityMismatchAsync(ct) is { } herdrMismatch)
        {
            throw new RunnerCapabilityMismatchException(herdrMismatch);
        }

        var request = new RunnerLaunchRequest(
            sessionId,
            spec.Exe,
            spec.Args,
            spec.Env,
            spec.Cwd,
            spec.Cols,
            spec.Rows,
            spec.MemoryLimitMb,
            // Claude writes the per-cwd JSONL we discover-and-tail; Grok persists its ACP update
            // stream to a deterministic per-session path (CARD-0080 S2); Codex writes a rollout
            // JSONL that has to be discovered under the same CARD-0006 rules as Claude's, because
            // it honours no session-id flag (CARD-0099 S1). OpenCode/Raw have no structured
            // transcript, so their sessions stay screen-only.
            TranscriptEnabled: TranscriptEnabledFor(spec.Kind),
            TranscriptFormat: TranscriptFormatFor(spec.Kind),
            Backend: backendWire,
            Herdr: spec.Herdr);
        var response = await _httpClient.PostAsJsonAsync("sessions", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return Map(await response.Content.ReadFromJsonAsync<RunnerSessionDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty start response."));
    }

    /// <summary>
    /// Null for PtyHost on purpose — it is the pre-herdr default, so a new server in front of an
    /// old runner asks for exactly what that runner already does.
    /// </summary>
    public static string? BackendWire(SessionBackend backend) =>
        backend == SessionBackend.Herdr ? SessionBackends.Herdr : null;

    /// <summary>
    /// Returns an error message when the runner cannot host herdr. Null capabilities or a list
    /// lacking "herdr" both refuse — never fall back to pty-host.
    /// </summary>
    public async Task<string?> GetSessionBackendCapabilityMismatchAsync(CancellationToken ct)
    {
        await EnsureCapabilitiesProbedAsync(ct);
        RunnerCapabilitiesDto? cached;
        lock (_capabilityGate)
            cached = _cachedCapabilities;

        if (cached?.SessionBackends is { } backends
            && backends.Contains(SessionBackends.Herdr, StringComparer.OrdinalIgnoreCase))
            return null;

        var supported = cached?.SessionBackends is { Count: > 0 } listed
            ? string.Join(", ", listed)
            : "none (older runner or probe failed)";
        var build = DescribeBuild(cached?.Build);
        return $"The session runner at :17204 cannot host a herdr session — it reports SessionBackends={supported}{build}. "
            + "Launching anyway would silently open a pty-host (CARD-0160 / CARD-0112). Rebuild and restart it: "
            + "pwsh -File scripts/restart-session-runner.ps1.";
    }

    private async Task EnsureCapabilitiesProbedAsync(CancellationToken ct)
    {
        Task? probe;
        RunnerCapabilitiesDto? cached;
        lock (_capabilityGate)
        {
            var stale = DateTimeOffset.UtcNow - _capabilitiesProbedAt >= CapabilityProbeTtl;
            if (stale && (_capabilityProbe is null || _capabilityProbe.IsCompleted))
            {
                _capabilitiesProbedAt = DateTimeOffset.UtcNow;
                _capabilityProbe = ProbeCapabilitiesAsync();
            }

            probe = _capabilityProbe;
            cached = _cachedCapabilities;
        }

        if (cached is null && probe is not null)
            await probe.WaitAsync(ct);
    }

    /// <summary>Which agent kinds get a runner-side transcript tailer (see StartAsync's mapping).</summary>
    public static bool TranscriptEnabledFor(AgentKind kind) =>
        ProviderContractCatalog.For(kind).Transcript.State == AgentTuiCapabilityState.Supported;

    /// <summary>
    /// Which tailer the runner should use. Null for Claude on purpose — it is the pre-Grok default,
    /// so a new server in front of an old runner asks for exactly what that runner already does.
    /// </summary>
    public static string? TranscriptFormatFor(AgentKind kind)
    {
        var transcript = ProviderContractCatalog.For(kind).Transcript;
        if (transcript.State != AgentTuiCapabilityState.Supported)
            return null;
        // Claude's Format is the pre-Grok runner default; sending it would break old runners.
        return transcript.Format == TranscriptFormats.Claude ? null : transcript.Format;
    }

    /// <summary>
    /// Null on ANY failure — an old runner without the endpoint, an unreachable one, a malformed
    /// body. "I could not find out" must be indistinguishable from "this client cannot say", because
    /// the caller's conservative branch is the only correct answer to both: guessing modern here
    /// would size bodies for a pty that may be stripping every paste marker.
    /// </summary>
    public async Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<RunnerCapabilitiesDto>("capabilities", JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or JsonException
                                       or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<string?> GetHealthAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync("health", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return $"{(int)response.StatusCode} {body}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Uses one cached capability snapshot per TTL. The first launch waits for its bounded probe so
    /// an explicit refusal is caught before a process starts; later stale snapshots refresh in the
    /// background, mirroring PtyDeliveryProfile. A missing/failed answer remains no evidence.
    /// </summary>
    public async Task<RunnerCapabilityMismatch?> GetTranscriptCapabilityMismatchAsync(AgentKind kind, CancellationToken ct)
    {
        var format = TranscriptFormatFor(kind);
        if (format is null)
            return null;

        Task? probe;
        RunnerCapabilitiesDto? cached;
        lock (_capabilityGate)
        {
            var stale = DateTimeOffset.UtcNow - _capabilitiesProbedAt >= CapabilityProbeTtl;
            if (stale && (_capabilityProbe is null || _capabilityProbe.IsCompleted))
            {
                _capabilitiesProbedAt = DateTimeOffset.UtcNow;
                _capabilityProbe = ProbeCapabilitiesAsync();
            }

            probe = _capabilityProbe;
            cached = _cachedCapabilities;
        }

        // No snapshot means the first caller gets the answer (or its bounded no-answer) before it
        // decides. Once one exists, refresh is intentionally background-only and bounded by the TTL.
        if (cached is null && probe is not null)
            await probe.WaitAsync(ct);

        lock (_capabilityGate)
            cached = _cachedCapabilities;

        if (cached?.TranscriptFormats is not { } formats
            || formats.Contains(format, StringComparer.OrdinalIgnoreCase))
            return null;

        var supported = formats.Count == 0 ? "none" : string.Join(", ", formats);
        var build = DescribeBuild(cached.Build);
        return new RunnerCapabilityMismatch(
            format,
            cached,
            $"The session runner at :17204 cannot tail a '{format}' transcript — it reports support for {supported}{build}. "
            + "Launching anyway would bind no transcript, and the delivery watchdog would read that as \"never started\" "
            + "and kill a working session 10 minutes later (CARD-0112). Rebuild and restart it: "
            + "pwsh -File scripts/restart-session-runner.ps1.");
    }

    private async Task ProbeCapabilitiesAsync()
    {
        RunnerCapabilitiesDto? result;
        using var deadline = new CancellationTokenSource(CapabilityProbeTimeout);
        try
        {
            result = await GetCapabilitiesAsync(deadline.Token);
        }
        catch
        {
            // GetCapabilitiesAsync deliberately makes ordinary transport/malformed-body failures
            // null. This is only a last-resort guard for a future client implementation.
            result = null;
        }

        lock (_capabilityGate)
            _cachedCapabilities = result;
    }

    private static string DescribeBuild(RunnerBuildDto? build)
    {
        if (build is null)
            return string.Empty;

        var commit = build.CommitSha is { Length: > 0 } sha ? $"{sha[..Math.Min(7, sha.Length)]}" : "an unknown commit";
        return $" and was built from {commit} on {build.AssemblyWriteTimeUtc:yyyy-MM-dd HH:mm} (running since {build.ProcessStartUtc:HH:mm})";
    }

    public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        var sessions = await _httpClient.GetFromJsonAsync<IReadOnlyList<RunnerSessionDto>>("sessions", JsonOptions, ct)
            ?? [];
        return sessions.Select(Map).ToList();
    }

    public async Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Map(await _httpClient.GetFromJsonAsync<RunnerSessionDto>($"sessions/{sessionId:D}", JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty session response."));

    public async Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var buffer = await _httpClient.GetFromJsonAsync<RunnerBufferDto>($"sessions/{sessionId:D}/buffer", JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty buffer response.");
        return new SessionRunnerBufferDto(buffer.SessionId, buffer.Buffer, buffer.LastSequence);
    }

    public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        var snapshot = await _httpClient.GetFromJsonAsync<RunnerSnapshotDto>($"sessions/{sessionId:D}/snapshot", JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty snapshot response.");
        return new SessionRunnerSnapshotDto(
            snapshot.SessionId,
            snapshot.RawOutput,
            snapshot.RenderedScreen,
            snapshot.LastSequence,
            snapshot.StartedAt);
    }

    public async Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        var transcript = await _httpClient.GetFromJsonAsync<RunnerTranscriptDto>($"sessions/{sessionId:D}/transcript", JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty transcript response.");
        return new SessionRunnerTranscriptDto(
            transcript.SessionId,
            transcript.Entries.Select(MapTranscript).ToList(),
            transcript.LastSequence);
    }

    public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"sessions/{sessionId:D}/input",
            new RunnerInputRequest(input),
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
    {
        var response = await _httpClient.PostAsync($"sessions/{sessionId:D}/clear-live-buffer", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"sessions/{sessionId:D}/resize",
            new RunnerResizeRequest(cols, rows),
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
    {
        var response = await _httpClient.PostAsync($"sessions/{sessionId:D}/kill", null, ct);
        response.EnsureSuccessStatusCode();
        return Map(await response.Content.ReadFromJsonAsync<RunnerSessionDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty kill response."));
    }

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Idle watchdog: the timer resets on every received line (keepalives count), so it only
        // fires when the runner has gone genuinely silent — half-open TCP, hung process — and the
        // pump should reconnect. This replaces HttpClient.Timeout for the stream.
        var idle = TimeSpan.FromSeconds(Math.Max(5, _settings.EventStreamIdleTimeoutSeconds));
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idleCts.CancelAfter(idle);

        var client = _httpClientFactory.CreateClient(EventStreamClientName);
        client.BaseAddress = _httpClient.BaseAddress;
        using var request = new HttpRequestMessage(HttpMethod.Get, "events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idleCts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(idleCts.Token);
        using var reader = new StreamReader(stream);
        string? eventName = null;
        var data = new System.Text.StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(idleCts.Token);
            if (line is null)
                break;
            idleCts.CancelAfter(idle);

            if (line.StartsWith(':'))
                continue; // SSE comment — the runner's keepalive; only resets the watchdog

            if (line.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(eventName) && data.Length > 0)
                {
                    var parsed = ParseEvent(eventName, data.ToString());
                    if (parsed is not null)
                        yield return parsed;
                }

                eventName = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventName = line["event: ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line["data: ".Length..]);
            }
        }
    }

    private static SessionRunnerEvent? ParseEvent(string eventName, string json)
    {
        if (eventName == SessionRunnerEventNames.SessionOutput)
        {
            var output = JsonSerializer.Deserialize<RunnerOutputEvent>(json, JsonOptions);
            return output is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    output.SessionId,
                    Output: new SessionRunnerOutputEvent(output.SessionId, output.Sequence, output.Text));
        }

        if (eventName == SessionRunnerEventNames.SessionExited)
        {
            var exited = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(json, JsonOptions);
            return exited is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    exited.SessionId,
                    Exited: new SessionRunnerExitedEvent(
                        exited.SessionId,
                        exited.ExitCode,
                        MapExitReason(exited.ExitReason),
                        exited.LastSequence));
        }

        if (eventName == SessionRunnerEventNames.SessionAdopted)
        {
            var adopted = JsonSerializer.Deserialize<RunnerSessionAdoptedEvent>(json, JsonOptions);
            return adopted is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    adopted.SessionId,
                    Adopted: new SessionRunnerAdoptedEvent(adopted.SessionId, adopted.Pid, adopted.LastSequence));
        }

        if (eventName == SessionRunnerEventNames.SessionStarted)
        {
            var started = JsonSerializer.Deserialize<RunnerSessionStartedEvent>(json, JsonOptions);
            return started is null
                ? null
                : new SessionRunnerEvent(eventName, started.SessionId);
        }

        if (eventName == SessionRunnerEventNames.SessionTranscript)
        {
            var entry = JsonSerializer.Deserialize<RunnerTranscriptEvent>(json, JsonOptions);
            return entry is null
                ? null
                : new SessionRunnerEvent(eventName, entry.SessionId, Transcript: MapTranscript(entry));
        }

        if (eventName == SessionRunnerEventNames.SessionTranscriptFault)
        {
            var fault = JsonSerializer.Deserialize<RunnerTranscriptFaultEvent>(json, JsonOptions);
            return fault is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    fault.SessionId,
                    TranscriptFault: new SessionRunnerTranscriptFaultEvent(
                        fault.SessionId, fault.Kind, fault.Detail, fault.CandidatePath,
                        fault.UnboundSeconds, fault.Repeat));
        }

        if (eventName == SessionRunnerEventNames.SessionTranscriptBound)
        {
            var bound = JsonSerializer.Deserialize<RunnerTranscriptBoundEvent>(json, JsonOptions);
            return bound is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    bound.SessionId,
                    TranscriptBound: new SessionRunnerTranscriptBoundEvent(
                        bound.SessionId, bound.TranscriptPath, bound.How));
        }

        if (eventName == SessionRunnerEventNames.SessionAgentStatus)
        {
            var status = JsonSerializer.Deserialize<RunnerAgentStatusEvent>(json, JsonOptions);
            return status is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    status.SessionId,
                    AgentStatus: new SessionRunnerAgentStatusEvent(
                        status.SessionId, status.AgentStatus, status.PreviousAgentStatus, status.ObservedAtUtc));
        }

        return null;
    }

    private static SessionRunnerTranscriptEvent MapTranscript(RunnerTranscriptEvent e) =>
        new(
            e.SessionId,
            e.Sequence,
            e.Kind,
            e.Uuid,
            e.ParentUuid,
            e.Timestamp,
            e.Role,
            e.Text,
            e.ToolName,
            e.ToolInput,
            e.ToolUseId,
            e.ToolIsError,
            e.StopReason,
            e.ApiCallId,
            e.InputTokens,
            e.OutputTokens,
            e.CacheReadTokens,
            e.CacheCreationTokens,
            e.IsApiError,
            e.ApiErrorClass,
            e.ApiErrorStatus,
            e.Model,
            e.ModelCalls);

    private static SessionRunnerSessionDto Map(RunnerSessionDto dto) =>
        new(
            dto.SessionId,
            dto.Pid,
            dto.StartedAt,
            dto.Status,
            dto.ExitCode,
            MapExitReason(dto.ExitReason),
            dto.LastSequence,
            dto.HostPid,
            dto.Adopted,
            dto.AgentStatus,
            dto.AgentStatusSinceUtc,
            dto.TranscriptBound,
            dto.TranscriptBindHow);

    private static AgentExitReason MapExitReason(string reason) =>
        Enum.TryParse<AgentExitReason>(reason, ignoreCase: true, out var parsed)
            ? parsed
            : AgentExitReason.Unknown;
}
