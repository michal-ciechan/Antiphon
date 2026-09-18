using System.Net;
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
    private readonly GrokRulesSettings _rulesSettings;
    private readonly TimeProvider _clock;
    private readonly object _capabilityGate = new();
    private RunnerCapabilitiesDto? _cachedCapabilities;
    private DateTimeOffset _capabilitiesProbedAt = DateTimeOffset.MinValue;
    private Task? _capabilityProbe;

    public SessionRunnerHttpClient(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        IOptions<SessionRunnerSettings> settings,
        IOptions<GrokRulesSettings>? rulesSettings = null,
        TimeProvider? clock = null)
    {
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _rulesSettings = rulesSettings?.Value ?? new();
        _clock = clock ?? TimeProvider.System;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
    }

    /// <summary>
    /// CARD-0511 D-1. One launch decision, one probe. <c>Capabilities</c> null with
    /// <c>Unreachable</c> null is the older-runner "no evidence" answer (no endpoint);
    /// <c>Unreachable</c> set means nobody answered, which is a different refusal (D-2).
    /// </summary>
    private sealed record RunnerCapabilityProbe(
        RunnerCapabilitiesDto? Capabilities, string Identity, Exception? Unreachable);

    private const string UnreachableMessage =
        "The session runner at :17204 did not answer GET /capabilities, so this launch cannot be "
        + "decided on evidence. That is a transport failure (the runner may be restarting), not a "
        + "stale build; the ordinary retry ladder paces it.";

    /// <summary>
    /// Takes ONE bounded, uncached <c>GET /capabilities</c> for a single launch decision and
    /// refreshes the watchdog snapshot with the answer. The gate exists for exactly one event -- a
    /// runner being replaced -- and that is the one event a TTL cache cannot see: the 5-minute
    /// stale-while-revalidate snapshot both wasted a doomed attempt against a runner that had
    /// already been rebuilt and (the mirror hole) let a stale positive launch onto a downgraded
    /// runner (CARD-0511 / CARD-0160).
    /// </summary>
    private async Task<RunnerCapabilityProbe> ProbeForDecisionAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CapabilityProbeTimeout);
        try
        {
            using var response = await _httpClient.GetAsync("capabilities", deadline.Token);
            // A runner that predates the endpoint answers 404. That is "this runner cannot say",
            // which every gate already has a defined answer for -- not a failure to reach it.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new RunnerCapabilityProbe(null, RunnerIdentity.Unknown, null);
            response.EnsureSuccessStatusCode();
            var capabilities = await response.Content
                .ReadFromJsonAsync<RunnerCapabilitiesDto>(JsonOptions, deadline.Token);
            if (capabilities is null)
            {
                return new RunnerCapabilityProbe(null, RunnerIdentity.Unknown,
                    new InvalidOperationException("The session runner returned an empty capabilities body."));
            }

            lock (_capabilityGate)
            {
                _cachedCapabilities = capabilities;
                _capabilitiesProbedAt = _clock.GetUtcNow();
            }

            return new RunnerCapabilityProbe(capabilities, RunnerIdentity.Describe(capabilities.Build), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or JsonException
                                       or TaskCanceledException or OperationCanceledException
                                       && !ct.IsCancellationRequested)
        {
            return new RunnerCapabilityProbe(null, RunnerIdentity.Unknown, ex);
        }
    }

    /// <summary>Does this spec need POSITIVE capability evidence to be decided at all?</summary>
    private static bool NeedsPositiveEvidence(AgentLaunchSpec spec) =>
        spec.GrokRulesPayload is not null
        || BackendWire(spec.Backend) == SessionBackends.Herdr
        || !string.IsNullOrWhiteSpace(spec.Herdr?.TabLabel)
        || spec.AcceptedStartedAt is not null;

    public async Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        GrokRulesLaunchValidation.Validate(spec, _rulesSettings);
        // CARD-0511 D-1: exactly one probe per StartAsync, even when no gate needs its answer.
        // Every gate below is a pure function of THIS answer, never of a snapshot taken for some
        // earlier decision.
        var probe = await ProbeForDecisionAsync(ct);
        var capabilities = probe.Capabilities;
        var decidedBuild = capabilities?.Build;
        if (probe.Unreachable is { } unreachable && NeedsPositiveEvidence(spec))
            throw new RunnerUnreachableException(UnreachableMessage, unreachable);

        if (spec.GrokRulesPayload is not null
            && capabilities?.Features?.Contains(GrokRulesTransport.Capability, StringComparer.Ordinal) != true)
            throw new ConflictException("Selected runner does not advertise grokRulesFileV1.", "grok_rules_transport_unsupported");
        if (TranscriptMismatch(capabilities, spec.Kind) is { } mismatch)
            throw new RunnerCapabilityMismatchException(mismatch.Message, decidedBuild);

        // CARD-0160: refuse a herdr launch before POSTing unless the runner advertises "herdr".
        // An old runner would ignore the unknown Backend field and silently launch a pty-host —
        // never silently remap (CARD-0111 §6). Null capabilities = no evidence = refuse.
        var backendWire = BackendWire(spec.Backend);
        if (backendWire == SessionBackends.Herdr
            && HerdrBackendMismatch(capabilities) is { } herdrMismatch)
        {
            throw new RunnerCapabilityMismatchException(herdrMismatch, decidedBuild);
        }

        if (!string.IsNullOrWhiteSpace(spec.Herdr?.TabLabel)
            && NamedTabMismatch(capabilities) is { } namedMismatch)
        {
            throw new RunnerCapabilityMismatchException(namedMismatch, decidedBuild);
        }

        if (spec.AcceptedStartedAt is not null
            && GenerationMismatch(capabilities) is { } generationMismatch)
        {
            throw new RunnerCapabilityMismatchException(generationMismatch, decidedBuild);
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
            Herdr: spec.Herdr,
            GrokRulesPayload: spec.GrokRulesPayload,
            CommandLineBudgetChars: spec.CommandLineBudgetChars,
            VerificationBinding: spec.VerificationBinding,
            AcceptedStartedAt: spec.AcceptedStartedAt);
        var response = await _httpClient.PostAsJsonAsync("sessions", request, JsonOptions, ct);
        // CARD-0341: a runner refusal (herdr_gkp_env_missing, pane_occupied, …) carries its reason
        // in problem-details; surface that as the typed exception so the launch path stores the
        // runner's detail in FailureReason rather than "status code does not indicate success".
        await ThrowForRunnerProblemAsync(response, ct);
        response.EnsureSuccessStatusCode();
        var started = Map(await response.Content.ReadFromJsonAsync<RunnerSessionDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty start response."));
        if (spec.AcceptedStartedAt is { } expected
            && !SessionGeneration.Equal(expected, started.AcceptedStartedAt))
        {
            throw new ConflictException(
                "The session runner did not echo the accepted launch generation.",
                SessionGeneration.NotEchoed);
        }

        return started;
    }

    /// <summary>
    /// Null for PtyHost on purpose — it is the pre-herdr default, so a new server in front of an
    /// old runner asks for exactly what that runner already does.
    /// </summary>
    public async Task<VerificationCustodyStatus> ReadVerificationCustodyAsync(
        VerificationExecutionBinding binding, bool seal, CancellationToken ct)
    {
        var path = $"sessions/{binding.Generation.SessionId:D}/executions/{binding.ExecutionId:D}/";
        using var response = seal
            ? await _httpClient.PostAsJsonAsync(path + "seal", binding, JsonOptions, ct)
            : await _httpClient.GetAsync(path + "custody?acceptedStartedAt="
                + Uri.EscapeDataString(binding.Generation.AcceptedStartedAt.ToString("O")), ct);
        await ThrowForRunnerProblemAsync(response, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<VerificationCustodyStatus>(JsonOptions, ct)
            ?? throw new ConflictException("verification_custody_missing_response");
    }

    public static string? BackendWire(SessionBackend backend) =>
        backend == SessionBackend.Herdr ? SessionBackends.Herdr : null;

    /// <summary>
    /// Returns an error message when the runner cannot host herdr. CARD-0511 D-1: decided on its
    /// own fresh probe, never on a snapshot taken for an earlier decision.
    /// </summary>
    public async Task<string?> GetSessionBackendCapabilityMismatchAsync(CancellationToken ct) =>
        HerdrBackendMismatch((await ProbeForDecisionAsync(ct)).Capabilities);

    /// <summary>
    /// Null capabilities or a list lacking "herdr" both refuse — never fall back to pty-host.
    /// </summary>
    private static string? HerdrBackendMismatch(RunnerCapabilitiesDto? capabilities)
    {
        if (capabilities?.SessionBackends is { } backends
            && backends.Contains(SessionBackends.Herdr, StringComparer.OrdinalIgnoreCase))
            return null;

        var supported = capabilities?.SessionBackends is { Count: > 0 } listed
            ? string.Join(", ", listed)
            : "none (older runner or probe failed)";
        var build = DescribeBuild(capabilities?.Build);
        return $"The session runner at :17204 cannot host a herdr session — it reports SessionBackends={supported}{build}. "
            + "Launching anyway would silently open a pty-host (CARD-0160 / CARD-0112). Rebuild and restart it: "
            + "pwsh -File scripts/restart-session-runner.ps1.";
    }

    private static string? NamedTabMismatch(RunnerCapabilitiesDto? capabilities)
    {
        if (capabilities?.Features is { } features
            && features.Contains(RunnerCapabilityFeatures.HerdrNamedTabPlacement, StringComparer.OrdinalIgnoreCase))
            return null;

        var build = DescribeBuild(capabilities?.Build);
        return $"The session runner does not advertise {RunnerCapabilityFeatures.HerdrNamedTabPlacement}{build}. "
            + "Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1.";
    }

    private static string? GenerationMismatch(RunnerCapabilitiesDto? capabilities)
    {
        if (capabilities?.Features is { } features
            && features.Contains(RunnerCapabilityFeatures.SessionGenerationV1, StringComparer.OrdinalIgnoreCase))
            return null;

        var build = DescribeBuild(capabilities?.Build);
        return $"The session runner does not advertise {RunnerCapabilityFeatures.SessionGenerationV1}{build}. "
            + "Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1.";
    }

    /// <summary>
    /// The transcript gate's null semantics are the opposite of the others: "this runner cannot
    /// say" launches (CARD-0112/CARD-0160), only an EXPLICIT list without the format refuses.
    /// </summary>
    private static RunnerCapabilityMismatch? TranscriptMismatch(RunnerCapabilitiesDto? capabilities, AgentKind kind)
    {
        var format = TranscriptFormatFor(kind);
        if (format is null)
            return null;
        if (capabilities?.TranscriptFormats is not { } formats
            || formats.Contains(format, StringComparer.OrdinalIgnoreCase))
            return null;

        var supported = formats.Count == 0 ? "none" : string.Join(", ", formats);
        var build = DescribeBuild(capabilities.Build);
        return new RunnerCapabilityMismatch(
            format,
            capabilities,
            $"The session runner at :17204 cannot tail a '{format}' transcript — it reports support for {supported}{build}. "
            + "Launching anyway would bind no transcript, and the delivery watchdog would read that as \"never started\" "
            + "and kill a working session 10 minutes later (CARD-0112). Rebuild and restart it: "
            + "pwsh -File scripts/restart-session-runner.ps1.");
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
    /// The dispatcher's transcript watchdog (NOT a launch decision - those take a fresh probe per
    /// CARD-0511 D-1) keeps one cached snapshot per TTL. The first caller waits for its bounded
    /// probe so an explicit refusal is seen before it decides; later stale snapshots refresh in the
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
            var stale = _clock.GetUtcNow() - _capabilitiesProbedAt >= CapabilityProbeTtl;
            if (stale && (_capabilityProbe is null || _capabilityProbe.IsCompleted))
            {
                _capabilitiesProbedAt = _clock.GetUtcNow();
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

        return TranscriptMismatch(cached, kind);
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
        {
            // CARD-0511 D-6: a failed probe means "I could not find out", never "no capabilities".
            // Overwriting a good snapshot with null used to serve that false verdict to the
            // dispatcher's watchdog for the whole 5-minute TTL, because _capabilitiesProbedAt is
            // stamped BEFORE the probe. Keep the last good answer and mark the cache stale so the
            // next call probes again.
            if (result is not null)
                _cachedCapabilities = result;
            else
                _capabilitiesProbedAt = DateTimeOffset.MinValue;
        }
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
        using var response = await _httpClient.GetAsync($"sessions/{sessionId:D}/snapshot", ct);
        await EnsureRunnerSuccessAsync(response, ct);
        var snapshot = await response.Content.ReadFromJsonAsync<RunnerSnapshotDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty snapshot response.");
        return new SessionRunnerSnapshotDto(
            snapshot.SessionId,
            snapshot.RawOutput,
            snapshot.RenderedScreen,
            snapshot.LastSequence,
            snapshot.StartedAt,
            snapshot.AcceptedStartedAt);
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
        await EnsureRunnerSuccessAsync(response, ct);
    }

    public async Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                $"sessions/{sessionId:D}/conditional-input",
                request,
                JsonOptions,
                ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unknown, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            return new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unsupported, null, null);
        }

        try
        {
            await ThrowForRunnerProblemAsync(response, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RunnerConditionalInputResult>(JsonOptions, ct)
                ?? new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unknown, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RunnerProblemException)
        {
            return new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unknown, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence);
        }
        finally
        {
            response.Dispose();
        }
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
        await EnsureRunnerSuccessAsync(response, ct);
    }

    public async Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
    {
        var response = await _httpClient.PostAsync($"sessions/{sessionId:D}/kill", null, ct);
        await EnsureRunnerSuccessAsync(response, ct);
        return Map(await response.Content.ReadFromJsonAsync<RunnerSessionDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty kill response."));
    }

    public async Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"sessions/{sessionId:D}/kill-generation",
            new RunnerKillGenerationRequest(expectedAcceptedStartedAt),
            JsonOptions,
            ct);
        await ThrowForRunnerProblemAsync(response, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunnerKillGenerationResult>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty kill-generation response.");
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
                        exited.LastSequence,
                        exited.AcceptedStartedAt));
        }

        if (eventName == SessionRunnerEventNames.SessionAdopted)
        {
            var adopted = JsonSerializer.Deserialize<RunnerSessionAdoptedEvent>(json, JsonOptions);
            return adopted is null
                ? null
                : new SessionRunnerEvent(
                    eventName,
                    adopted.SessionId,
                    Adopted: new SessionRunnerAdoptedEvent(
                        adopted.SessionId, adopted.Pid, adopted.LastSequence, adopted.AcceptedStartedAt));
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

    public async Task<HerdrPaneInspectDto> InspectHerdrPaneAsync(string paneId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        var response = await _httpClient.GetAsync($"herdr/panes/{Uri.EscapeDataString(paneId)}", ct);
        await ThrowForRunnerProblemAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<HerdrPaneInspectDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty inspect response.");
    }

    public async Task<HerdrPaneDisposalPreview> PreviewHerdrPaneDisposalAsync(
        HerdrPaneDisposalPreviewRequest request, CancellationToken ct)
    {
        await RequirePaneDisposalCapabilityAsync(ct);
        using var response = await _httpClient.PostAsJsonAsync("herdr/pane-disposals/preview", request, JsonOptions, ct);
        return await ReadPaneDisposalAsync<HerdrPaneDisposalPreview>(response, ct);
    }

    public async Task<HerdrPaneDisposalReceipt> DisposeHerdrPaneAsync(
        HerdrPaneDisposalRequest request, CancellationToken ct)
    {
        await RequirePaneDisposalCapabilityAsync(ct);
        if ((await GetCapabilitiesAsync(ct))?.Features?.Contains(HerdrPaneDisposalCodes.BestEffortCapability, StringComparer.Ordinal) != true)
            throw new ConflictException("The runner cannot execute best-effort pane disposal.", HerdrPaneDisposalCodes.GuardUnavailable);
        using var response = await _httpClient.PostAsJsonAsync("herdr/pane-disposals", request, JsonOptions, ct);
        return await ReadPaneDisposalAsync<HerdrPaneDisposalReceipt>(response, ct);
    }

    public async Task<HerdrPaneDisposalReceipt> GetHerdrPaneDisposalAsync(Guid operationId, CancellationToken ct)
    {
        await RequirePaneDisposalCapabilityAsync(ct);
        using var response = await _httpClient.GetAsync($"herdr/pane-disposals/{operationId:D}", ct);
        return await ReadPaneDisposalAsync<HerdrPaneDisposalReceipt>(response, ct);
    }

    public async Task<HerdrPaneDisposalPreview> GetHerdrPaneDisposalPreviewAsync(Guid previewId, CancellationToken ct)
    {
        await RequirePaneDisposalCapabilityAsync(ct);
        using var response = await _httpClient.GetAsync($"herdr/pane-disposals/previews/{previewId:D}", ct);
        return await ReadPaneDisposalAsync<HerdrPaneDisposalPreview>(response, ct);
    }

    private async Task RequirePaneDisposalCapabilityAsync(CancellationToken ct)
    {
        if ((await GetCapabilitiesAsync(ct))?.Features?.Contains(HerdrPaneDisposalCodes.Capability, StringComparer.Ordinal) != true)
            throw new ConflictException("The runner does not advertise pane disposal inspection.", HerdrProblemTypes.Refused);
    }

    private static async Task<T> ReadPaneDisposalAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                ?? throw new InvalidOperationException("Runner returned an empty disposal response.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        var code = body.TryGetProperty("type", out var type) ? type.GetString() : "herdr_disposal_failed";
        var detail = body.TryGetProperty("detail", out var message) ? message.GetString() : "Runner refused pane disposal.";
        var extensions = new Dictionary<string, object?>();
        foreach (var name in new[] { "operationId", "receipt" })
            if (body.TryGetProperty(name, out var value)) extensions[name] = value.Clone();
        throw new HerdrPaneDisposalException((int)response.StatusCode, detail ?? "Runner refused pane disposal.",
            code ?? "herdr_disposal_failed", extensions);
    }

    public async Task<HerdrPlacementCheckResult> CheckHerdrPlacementAsync(
        HerdrPlacementCheckRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await _httpClient.PostAsJsonAsync("herdr/placement/check", request, JsonOptions, ct);
        await ThrowForRunnerProblemAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<HerdrPlacementCheckResult>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty placement-check response.");
    }

    public async Task<SessionRunnerSessionDto> AttachHerdrAsync(HerdrAttachRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AcceptedStartedAt is not null)
        {
            // CARD-0511 D-1: attach is a launch decision too, and takes its own probe.
            var probe = await ProbeForDecisionAsync(ct);
            if (probe.Unreachable is { } unreachable)
                throw new RunnerUnreachableException(UnreachableMessage, unreachable);
            if (GenerationMismatch(probe.Capabilities) is { } generationMismatch)
                throw new RunnerCapabilityMismatchException(generationMismatch, probe.Capabilities?.Build);
        }

        var response = await _httpClient.PostAsJsonAsync("sessions/attach", request, JsonOptions, ct);
        await ThrowForRunnerProblemAsync(response, ct);
        return Map(await response.Content.ReadFromJsonAsync<RunnerSessionDto>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Session runner returned an empty attach response."));
    }

    /// <summary>
    /// CARD-0213: map the runner's RFC 9457 problem-details onto typed server exceptions.
    /// 404 → <see cref="NotFoundException"/>-shaped <see cref="RunnerProblemException"/>,
    /// 409 → <see cref="ConflictException"/>, 503 → <see cref="ServiceUnavailableException"/>.
    /// </summary>
    public static async Task ThrowForRunnerProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? type = null;
        string? detail = null;
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                    type = typeEl.GetString();
                if (json.TryGetProperty("detail", out var detailEl) && detailEl.ValueKind == JsonValueKind.String)
                    detail = detailEl.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Body is not problem+json.
        }

        detail ??= response.ReasonPhrase ?? $"Session runner returned {(int)response.StatusCode}.";
        var code = string.IsNullOrWhiteSpace(type) ? "herdr_launch_failed" : type;

        throw (int)response.StatusCode switch
        {
            404 => new RunnerProblemException(404, detail, code),
            409 => new ConflictException(detail, code),
            503 => new ServiceUnavailableException(
                detail,
                string.Equals(code, "herdr_launch_failed", StringComparison.Ordinal)
                    ? HerdrProblemTypes.Unreachable
                    : code),
            _ => new HttpRequestException($"Session runner returned {(int)response.StatusCode}: {detail}"),
        };
    }

    /// <summary>
    /// CARD-0186 S3: a 503 with problem-type <see cref="HerdrProblemTypes.Unreachable"/> (or a
    /// bare 503 on these endpoints) is a deferral, never a 500-shaped transport failure.
    /// </summary>
    private static async Task EnsureRunnerSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
        {
            var type = await TryReadProblemTypeAsync(response, ct);
            if (type is null
                || string.Equals(type, HerdrProblemTypes.Unreachable, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceUnavailableException(
                    "Herdr is unreachable.", HerdrProblemTypes.Unreachable);
            }
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> TryReadProblemTypeAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            if (json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String)
            {
                return type.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Body is not problem+json; treat as a bare 503.
        }

        return null;
    }

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
            dto.TranscriptBindHow,
            dto.TranscriptUnboundReason,
            dto.Backend,
            dto.Pending,
            dto.HerdrVerifiedAtUtc,
            dto.HerdrOrigin,
            dto.GrokRulesReceipt,
            dto.AcceptedStartedAt,
            dto.LabelObservation is { Version: 1, Intent.Version: 1 } observation ? observation : null);

    private static AgentExitReason MapExitReason(string reason) =>
        Enum.TryParse<AgentExitReason>(reason, ignoreCase: true, out var parsed)
            ? parsed
            : AgentExitReason.Unknown;
}
