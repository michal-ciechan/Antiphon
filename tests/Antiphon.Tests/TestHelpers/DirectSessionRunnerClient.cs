using System.Runtime.CompilerServices;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class DirectSessionRunnerClient : ISessionRunnerClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private SessionRunnerRuntime _runtime;
    private readonly Antiphon.SessionRunner.SessionRunnerSettings _runnerSettings;
    private readonly HerdrClient? _herdrClient;
    private readonly bool _codexTranscript;
    private readonly bool _claudeTranscript;

    /// <summary>
    /// CARD-0186 S4: production runner teardown detaches without killing. Tests that simulate a
    /// runner restart need that shape; the default still kills so a forgotten session cannot leak.
    /// </summary>
    public bool KillOnDispose { get; set; } = true;

    /// <param name="ptyBackend">
    /// Which pseudoconsole the detached pty-hosts this client spawns should use (<c>inbox</c> /
    /// <c>modern</c>), or null to leave it to the environment.
    ///
    /// <para>CARD-0045 slice 3: a host-mediated test could not declare a backend at all before this.
    /// <c>PtyAgentRunner</c>'s per-instance override lives three processes away — here →
    /// <c>SessionRunnerRuntime</c> → a detached <c>Antiphon.PtyHost</c> whose <c>HostSession</c>
    /// built a bare runner and inherited the TEST PROCESS's environment — so a test asserting an
    /// inbox-conhost fact silently ran on whatever the launching shell had exported. Now it says
    /// which one it means, and the runtime states it on the host's command line.</para>
    /// </param>
    /// <param name="codexTranscript">
    /// Opt in to the production Codex rollout tailer for this client (default off — see
    /// <see cref="StartAsync"/> for why). The one caller that sets it is
    /// <c>CodexAdapterIntegrationTests</c>, which is headed, spends real model turns, and gives its
    /// session a UNIQUE temp cwd so the tailer's C2 evidence is exact against the real
    /// <c>~/.codex/sessions</c> — the only way to observe a full <c>-Kind Codex</c> round trip
    /// (CARD-0108 S3).
    /// </param>
    /// <param name="claudeTranscript">
    /// CARD-0168 B-tier: opt in to the production Claude JSONL tailer. Same safety argument as
    /// <paramref name="codexTranscript"/> — the caller MUST give the session a unique temp cwd
    /// (C2) so discovery cannot bind a stranger's conversation under <c>~/.claude/projects</c>.
    /// Isolated <c>CLAUDE_CONFIG_DIR</c> is the preferred pairing (ForClaude) and must also be
    /// set on THIS process so the in-proc tailer looks at the same root the child writes to.
    /// </param>
    /// <param name="herdrClient">
    /// CARD-0168 S5: when non-null, herdr-backend launches on this in-proc runtime talk to this
    /// client (live herdr or FakeHerdrServer). Null keeps the pre-herdr pty-host-only runtime.
    /// </param>
    public DirectSessionRunnerClient(
        string sessionLogPath,
        string? ptyBackend = null,
        bool codexTranscript = false,
        bool claudeTranscript = false,
        HerdrClient? herdrClient = null)
    {
        _codexTranscript = codexTranscript;
        _claudeTranscript = claudeTranscript;
        _herdrClient = herdrClient;
        _runnerSettings = new Antiphon.SessionRunner.SessionRunnerSettings
        {
            SessionLogPath = sessionLogPath,
            // Tests must not strand detached hosts for the production 24 h linger.
            PtyHostLingerHours = 0.02,
            PtyBackend = ptyBackend,
        };
        _runtime = BuildRuntime();
    }

    /// <summary>
    /// CARD-0186 S4: drop the in-proc runtime without killing children (the production runner
    /// restart shape) and re-adopt from sidecars / pty-host manifests.
    /// </summary>
    public async Task SimulateRunnerRestartAsync(CancellationToken ct = default)
    {
        KillOnDispose = false;
        await _runtime.DisposeAsync();
        _runtime = BuildRuntime();
        await _runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), ct);
    }

    public Task AdoptOrphanedHostsAsync(CancellationToken ct = default) =>
        _runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), ct);

    private SessionRunnerRuntime BuildRuntime() =>
        new(
            Options.Create(_runnerSettings),
            NullLogger<SessionRunnerRuntime>.Instance,
            _herdrClient);

    public async Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        // Grok gets the production transcript mapping (CARD-0080 S2) so the real GrokTranscriptTailer
        // runs inside this in-proc runtime — it can, because its transcript path is deterministic.
        // Claude and Codex deliberately stay transcript-DISABLED here: both use DISCOVERY, so their
        // tailers would search the real ~/.claude/projects and ~/.codex/sessions of the machine
        // running the tests. The fakeclaude tests that need transcript rows pump them explicitly
        // instead, and CodexTranscriptTailerTests drives the real Codex tailer against a temp
        // sessions root of its own.
        //
        // Opt-in exceptions (unique temp cwd so C2 is exact, never a recency guess):
        //   * `codexTranscript` (CARD-0108 S3) — headed Codex round-trip.
        //   * `claudeTranscript` (CARD-0168 B-tier) — real-CLI stub-proxy canaries that assert
        //     CARD-0006 bind + CARD-0055 transcript-confirm against a real Claude JSONL.
        var kind = spec.Kind;
        var isGrok = kind == Antiphon.Server.Domain.Enums.AgentKind.Grok;
        var isCodex = _codexTranscript && kind == Antiphon.Server.Domain.Enums.AgentKind.Codex;
        var isClaude = _claudeTranscript && kind == Antiphon.Server.Domain.Enums.AgentKind.ClaudeCode;
        var transcriptEnabled = isGrok || isCodex || isClaude;
        string? transcriptFormat = isGrok
            ? TranscriptFormats.Grok
            : isCodex
                ? TranscriptFormats.Codex
                : null; // Claude is the pre-Grok default — null keeps old runners' meaning.
        var request = new RunnerLaunchRequest(
            sessionId,
            spec.Exe,
            spec.Args,
            spec.Env,
            spec.Cwd,
            spec.Cols,
            spec.Rows,
            spec.MemoryLimitMb,
            TranscriptEnabled: transcriptEnabled,
            TranscriptFormat: transcriptFormat,
            Backend: SessionRunnerHttpClient.BackendWire(spec.Backend),
            Herdr: spec.Herdr);

        return Map(await _runtime.StartAsync(request, ct));
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(_runtime.List().Select(Map).ToList());
    }

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Map(_runtime.Get(sessionId)));
    }

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var buffer = _runtime.GetBuffer(sessionId);
        return Task.FromResult(new SessionRunnerBufferDto(buffer.SessionId, buffer.Buffer, buffer.LastSequence));
    }

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = _runtime.GetSnapshot(sessionId);
        return Task.FromResult(new SessionRunnerSnapshotDto(
            snapshot.SessionId,
            snapshot.RawOutput,
            snapshot.RenderedScreen,
            snapshot.LastSequence,
            snapshot.StartedAt));
    }

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transcript = _runtime.GetTranscript(sessionId);
        return Task.FromResult(new SessionRunnerTranscriptDto(
            transcript.SessionId,
            transcript.Entries.Select(MapTranscript).ToList(),
            transcript.LastSequence));
    }

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _runtime.SendInputAsync(sessionId, input, ct);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        _runtime.ClearLiveBufferAsync(sessionId, ct);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        _runtime.ResizeAsync(sessionId, cols, rows, ct);

    public async Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        Map(await _runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), ct));

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = _runtime.Subscribe(ct);
        await foreach (var evt in reader.ReadAllAsync(ct))
        {
            var parsed = ParseEvent(evt.EventName, evt.Json);
            if (parsed is not null)
                yield return parsed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (KillOnDispose)
        {
            foreach (var session in _runtime.List())
            {
                if (session.Status is "Running" or "Starting")
                {
                    try
                    {
                        await _runtime.KillAsync(session.SessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
                    }
                    catch
                    {
                        // Teardown must not mask the test's own failure.
                    }
                }
            }
        }

        await _runtime.DisposeAsync();
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

        return null;
    }

    /// <summary>
    /// Every field the runner produced, including the API-call attribution and the four usage
    /// counters. Those five were dropped here until CARD-0084 S6, and the loss was silent in exactly
    /// the way that matters: a task settled through this client rolled up ZERO tokens, so its cost
    /// came out 0.00 and every price assertion downstream passed by agreeing about nothing. Usage
    /// rides the TurnEnd row for Grok and the assistant rows for Claude, and
    /// <see cref="Antiphon.Server.Application.Services.DelegationUsageRollup"/> groups by ApiCallId —
    /// so dropping either half is enough to zero the bill.
    /// </summary>
    private static SessionRunnerTranscriptEvent MapTranscript(RunnerTranscriptEvent e) =>
        new(
            e.SessionId, e.Sequence, e.Kind, e.Uuid, e.ParentUuid, e.Timestamp,
            e.Role, e.Text, e.ToolName, e.ToolInput, e.ToolUseId, e.ToolIsError, e.StopReason,
            e.ApiCallId, e.InputTokens, e.OutputTokens, e.CacheReadTokens, e.CacheCreationTokens,
            e.IsApiError, e.ApiErrorClass, e.ApiErrorStatus, e.Model, e.ModelCalls);

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
            dto.Backend,
            dto.Pending,
            dto.HerdrVerifiedAtUtc);

    private static AgentExitReason MapExitReason(string reason) =>
        Enum.TryParse<AgentExitReason>(reason, ignoreCase: true, out var parsed)
            ? parsed
            : AgentExitReason.Unknown;
}
