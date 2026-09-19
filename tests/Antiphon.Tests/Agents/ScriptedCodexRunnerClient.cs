using System.Net.Http;
using System.Runtime.CompilerServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.Agents;

/// <summary>
/// A scripted <see cref="ISessionRunnerClient"/> for the CARD-0108 Codex adapter tests: it models
/// the two behaviours the real TUI has and a plain stub does not.
///
/// <list type="bullet">
/// <item><b>An Enter that may or may not submit.</b> <see cref="ConfirmAfterEnters"/> says how many
/// CR writes it takes before a confirming <c>UserPrompt</c> row appears, so "the first CR folded and
/// the re-press submitted" — the measured 6/6 production shape — is a one-line setting.</item>
/// <item><b>A Working indicator with a lifecycle.</b> <see cref="IndicatorScreenReads"/> renders the
/// measured <c>Working (Ns • esc to interrupt)</c> line for the first N snapshot reads and advances
/// the output sequence while it does (the TUI repaints at ~1 Hz), then drops it and goes quiet.
/// Zero means a session that never visibly works — the stranded-composer shape.</item>
/// </list>
/// </summary>
internal sealed class ScriptedCodexRunnerClient : ISessionRunnerClient
{
    public const string WorkingScreen =
        "  codex\n  the answer so far\n\n• Working (7s • esc to interrupt)\n";

    public const string IdleScreen =
        "  codex\n  the answer so far\n\n  > \n  gpt-5.6-luna low · ~/tmp\n";

    private readonly List<SessionRunnerTranscriptEvent> _entries = new();
    private readonly List<string> _writes = new();
    private long _sequence;
    private int _screenReads;
    private int _startupIndex;
    private int _enters;
    private string? _lastBody;
    private DateTime? _acceptedStartedAt;
    private DateTime? _liveAcceptedStartedAt;
    private bool _exited;
    private int _waitingOnSnapshot;

    public Guid SessionId { get; set; }

    /// <summary>
    /// CARD-0574: opt-in startup frames. When set, <see cref="GetSnapshotAsync"/> returns
    /// these screens in order (last frame sticks) instead of the turn idle/working pair.
    /// </summary>
    public IReadOnlyList<string>? StartupScreens { get; set; }

    public TaskCompletionSource? SnapshotHold { get; set; }

    /// <summary>True while <see cref="GetSnapshotAsync"/> is blocked on <see cref="SnapshotHold"/>.</summary>
    public bool WaitingOnSnapshot => Volatile.Read(ref _waitingOnSnapshot) > 0;

    public IReadOnlyList<string> Writes => _writes;
    public List<DateTime> KillGenerationCalls { get; } = [];
    public int UnconditionalKills { get; private set; }
    public bool ReplacementAlive => !_exited;
    public bool KillGenerationNotFound { get; set; }
    public bool ReportNullAcceptedStartedAt { get; set; }
    public int ResizeCalls { get; private set; }
    public int SnapshotReads => _screenReads;
    public DateTime? AcceptedStartedAt => _acceptedStartedAt;
    public DateTime? LiveAcceptedStartedAt => _liveAcceptedStartedAt;

    public void PrimeAttach(Guid sessionId, DateTime? generation)
    {
        SessionId = sessionId;
        _acceptedStartedAt = generation;
        _liveAcceptedStartedAt = generation;
    }

    /// <summary>
    /// CARD-0574 R-61: restamp the live runner generation while a G1 wait is held.
    /// <paramref name="reported"/> stays on GetAsync so the G1 exit waiter can be coordinated
    /// separately from KillGeneration matching.
    /// </summary>
    public void RestampLive(DateTime generation, bool reported = false)
    {
        _liveAcceptedStartedAt = generation;
        if (reported)
            _acceptedStartedAt = generation;
        _exited = false;
    }

    /// <summary>Raw pty output; must be non-empty or the CARD-0052 visible-output guard blocks every verdict.</summary>
    public string RawOutput { get; set; } = "codex ready\n";

    /// <summary>CR writes needed before a confirming UserPrompt row appears. 0 disables auto-confirm.</summary>
    public int ConfirmAfterEnters { get; set; } = 1;

    /// <summary>Snapshot reads that render the Working indicator before it disappears.</summary>
    public int IndicatorScreenReads { get; set; }

    /// <summary>Makes GetTranscriptAsync throw, i.e. a session whose transcript is not observable at all.</summary>
    public bool ThrowOnTranscript { get; set; }

    /// <summary>
    /// GetTranscriptAsync calls that should fail once each, then succeed. Models a transient
    /// transport miss at the SendPromptAsync baseline capture (CARD-0113). Independent of
    /// <see cref="ThrowOnTranscript"/>, which throws forever.
    /// </summary>
    public int RemainingTranscriptFailures { get; set; }

    /// <summary>Rendered screen when the indicator is not showing; the failure-look reads this.</summary>
    public string QuietScreen { get; set; } = IdleScreen;

    public int Enters => _enters;

    /// <summary>Non-CR writes. More than one means the body was RE-TYPED, which CARD-0055/0108 forbid.</summary>
    public int BodyWrites { get; private set; }

    public void Seed(params SessionRunnerTranscriptEvent[] entries)
    {
        _entries.AddRange(entries);
        _sequence = Math.Max(_sequence, entries.Length == 0 ? 0 : entries[^1].Sequence);
    }

    public SessionRunnerTranscriptEvent Append(string kind, string? text = null, string? stopReason = null)
    {
        var seq = (_entries.Count == 0 ? 0 : _entries[^1].Sequence) + 1;
        var row = new SessionRunnerTranscriptEvent(
            SessionId, seq, kind, $"uuid-{seq}", null, DateTimeOffset.UtcNow, null, text,
            null, null, null, null, stopReason);
        _entries.Add(row);
        return row;
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        SessionId = sessionId;
        _acceptedStartedAt = spec.AcceptedStartedAt ?? DateTime.UtcNow;
        _liveAcceptedStartedAt = _acceptedStartedAt;
        return Task.FromResult(Dto());
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(
            SessionId == Guid.Empty ? [] : [Dto()]);

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(Dto(sessionId));

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerBufferDto(sessionId, RawOutput, _sequence));

    public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        if (SnapshotHold is not null)
        {
            Interlocked.Increment(ref _waitingOnSnapshot);
            try
            {
                await SnapshotHold.Task.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _waitingOnSnapshot);
            }
        }

        _screenReads++;
        string screen;
        if (StartupScreens is { Count: > 0 } frames)
        {
            var idx = Math.Min(_startupIndex, frames.Count - 1);
            screen = frames[idx];
            if (_startupIndex < frames.Count - 1)
                _startupIndex++;
        }
        else
        {
            var working = (_screenReads - 1) < IndicatorScreenReads;
            if (working)
                _sequence++;
            screen = working ? WorkingScreen : QuietScreen;
        }

        return new SessionRunnerSnapshotDto(
            sessionId, RawOutput, screen, _sequence, _acceptedStartedAt ?? DateTime.UtcNow,
            _acceptedStartedAt);
    }

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        if (ThrowOnTranscript)
            throw new InvalidOperationException("no transcript for this session");
        if (RemainingTranscriptFailures > 0)
        {
            RemainingTranscriptFailures--;
            throw new InvalidOperationException("transient transcript failure");
        }

        return Task.FromResult(new SessionRunnerTranscriptDto(
            sessionId,
            _entries.ToList(),
            _entries.Count == 0 ? 0 : _entries[^1].Sequence));
    }

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        _writes.Add(input);
        _sequence++;
        if (input == "\r")
        {
            _enters++;
            if (ConfirmAfterEnters > 0 && _enters >= ConfirmAfterEnters && _lastBody is not null)
            {
                Append(TranscriptKinds.UserPrompt, _lastBody);
                _lastBody = null;
            }
        }
        else
        {
            BodyWrites++;
            _lastBody = input;
        }

        return Task.CompletedTask;
    }

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
    {
        ResizeCalls++;
        return Task.CompletedTask;
    }

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
    {
        UnconditionalKills++;
        _exited = true;
        return Task.FromResult(Dto(sessionId));
    }

    public Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct)
    {
        KillGenerationCalls.Add(expectedAcceptedStartedAt);
        if (KillGenerationNotFound)
        {
            throw new HttpRequestException(
                "Response status code does not indicate success: 404 (Not Found).");
        }

        if (_liveAcceptedStartedAt is { } live
            && !SessionGeneration.Equal(live, expectedAcceptedStartedAt))
        {
            return Task.FromResult(new RunnerKillGenerationResult(
                sessionId, false, KillGenerationOutcomes.Mismatch, live));
        }

        _exited = true;
        return Task.FromResult(new RunnerKillGenerationResult(
            sessionId, true, KillGenerationOutcomes.Killed, expectedAcceptedStartedAt));
    }

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    private SessionRunnerSessionDto Dto(Guid? sessionId = null) =>
        new(
            sessionId ?? SessionId,
            1234,
            _acceptedStartedAt ?? DateTime.UtcNow,
            _exited ? "Exited" : "Running",
            _exited ? 0 : null,
            _exited ? AgentExitReason.KilledByRequest : AgentExitReason.Unknown,
            _sequence,
            AcceptedStartedAt: ReportNullAcceptedStartedAt ? null : _acceptedStartedAt);
}
