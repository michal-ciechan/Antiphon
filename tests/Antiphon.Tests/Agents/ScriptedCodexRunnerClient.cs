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
    private long _sequence;
    private int _screenReads;
    private int _enters;
    private string? _lastBody;

    public Guid SessionId { get; private set; }

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
        return Task.FromResult(new SessionRunnerSessionDto(
            sessionId, 1234, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerSessionDto(
            sessionId, 1234, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, _sequence));

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerBufferDto(sessionId, RawOutput, _sequence));

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        var working = _screenReads++ < IndicatorScreenReads;
        if (working)
            _sequence++; // the TUI repaints the indicator while the turn runs

        return Task.FromResult(new SessionRunnerSnapshotDto(
            sessionId, RawOutput, working ? WorkingScreen : QuietScreen, _sequence, DateTime.UtcNow));
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

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerSessionDto(
            sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, _sequence));

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
