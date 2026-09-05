using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0080 S2: <see cref="RunnerGrokAdapter.WaitForTurnCompleteAsync"/> takes its verdict from
/// the tailed transcript when rows exist — Grok writes an explicit <c>turn_completed</c> for every
/// turn — and only falls back to the screen heuristic (whose patterns S1 found dead against real
/// Grok and corrected: decimal-seconds "Worked for 1.7s", plain-"grok" idle title) when it does not.
/// </summary>
[Category("Unit")]
public class RunnerGrokAdapterTurnCompleteTests
{
    [Test]
    public async Task A_TurnEnd_row_past_the_prompt_baseline_completes_the_turn_with_transcript_reply_text()
    {
        var client = new ScriptedRunnerClient();
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 15_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing", CancellationToken.None);
        client.SetTranscript(
            Row(client.SessionId, 1, TranscriptKinds.UserPrompt, text: "do the thing"),
            Row(client.SessionId, 2, TranscriptKinds.AssistantText, text: "the reply from the transcript"),
            Row(client.SessionId, 3, TranscriptKinds.TurnEnd, stopReason: "end_turn"));

        var started = DateTime.UtcNow;
        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.TurnCompleted.ShouldBeTrue();
        turn.ResponseText.ShouldBe("the reply from the transcript");
        // The quiet period is 60s and the screen never printed a done line — only the transcript
        // could have produced this verdict, and it must not have needed the quiet fallback.
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task A_cancelled_TurnEnd_row_is_still_a_completed_turn()
    {
        var client = new ScriptedRunnerClient();
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 15_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing", CancellationToken.None);
        client.SetTranscript(
            Row(client.SessionId, 1, TranscriptKinds.UserPrompt, text: "do the thing"),
            Row(client.SessionId, 2, TranscriptKinds.TurnEnd, stopReason: "cancelled"));

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        turn.TurnCompleted.ShouldBeTrue("an Esc interrupt is an explicit turn end for Grok");
    }

    [Test]
    public async Task The_corrected_screen_done_line_completes_when_no_transcript_exists()
    {
        var client = new ScriptedRunnerClient();
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 15_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing", CancellationToken.None);
        // The MEASURED done line — decimal seconds, which the shipped " for \d+s" regex never
        // matched (S1's dead-code finding). No transcript rows at all: fallback territory.
        client.RawOutput = "FAKE turn output\r\nWorked for 1.7s\r\n";

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        turn.TurnCompleted.ShouldBeTrue("the corrected done pattern must match real Grok's decimal seconds");
    }

    [Test]
    public async Task A_TurnEnd_at_or_below_the_baseline_does_not_complete_the_new_turn()
    {
        var client = new ScriptedRunnerClient
        {
            // The PREVIOUS turn's rows are already there when the new prompt is sent…
            AdvanceSequenceOnEveryBufferRead = true, // …and the screen never goes quiet.
        };
        client.SetTranscript(
            Row(client.SessionId, 1, TranscriptKinds.UserPrompt, text: "old prompt"),
            Row(client.SessionId, 2, TranscriptKinds.TurnEnd, stopReason: "end_turn"));

        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 1_500);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("the new prompt", CancellationToken.None);

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        turn.TurnCompleted.ShouldBeFalse(
            "an old TurnEnd below the baseline is history, not this turn's completion");
    }

    [Test]
    public async Task A_first_turn_with_no_transcript_yet_still_uses_baseline_zero()
    {
        var client = new ScriptedRunnerClient { RemainingTranscriptFailures = 1 };
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 15_000);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing", CancellationToken.None);
        client.SetTranscript(
            Row(client.SessionId, 1, TranscriptKinds.UserPrompt, text: "do the thing"),
            Row(client.SessionId, 2, TranscriptKinds.AssistantText, text: "the first-turn reply from the transcript"),
            Row(client.SessionId, 3, TranscriptKinds.TurnEnd, stopReason: "end_turn"));

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        turn.TurnCompleted.ShouldBeTrue();
        turn.ResponseText.ShouldBe("the first-turn reply from the transcript");
    }

    [Test]
    public async Task A_transient_fetch_failure_on_a_later_turn_preserves_the_baseline_instead_of_replaying_the_previous_reply()
    {
        var client = new ScriptedRunnerClient();
        var adapter = NewAdapter(client, doneQuietMs: 60_000, doneMaxWaitMs: 1_500);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing", CancellationToken.None);
        client.SetTranscript(
            Row(client.SessionId, 1, TranscriptKinds.UserPrompt, text: "do the thing"),
            Row(client.SessionId, 2, TranscriptKinds.AssistantText, text: "reply one — must not leak into turn two"),
            Row(client.SessionId, 3, TranscriptKinds.TurnEnd, stopReason: "end_turn"));
        var first = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        first.ResponseText.ShouldBe("reply one — must not leak into turn two");

        client.RemainingTranscriptFailures = 1;
        await adapter.SendPromptAsync("the new prompt", CancellationToken.None);

        var stale = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        stale.TurnCompleted.ShouldBeFalse(
            "a missed capture on turn 2+ must keep the last-known floor; the previous TurnEnd is history");

        client.SetTranscript(
            Row(client.SessionId, 1, TranscriptKinds.UserPrompt, text: "do the thing"),
            Row(client.SessionId, 2, TranscriptKinds.AssistantText, text: "reply one — must not leak into turn two"),
            Row(client.SessionId, 3, TranscriptKinds.TurnEnd, stopReason: "end_turn"),
            Row(client.SessionId, 4, TranscriptKinds.UserPrompt, text: "the new prompt"),
            Row(client.SessionId, 5, TranscriptKinds.AssistantText, text: "reply two — the real answer"),
            Row(client.SessionId, 6, TranscriptKinds.TurnEnd, stopReason: "end_turn"));
        var second = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        second.TurnCompleted.ShouldBeTrue();
        second.ResponseText.ShouldBe("reply two — the real answer");
    }

    [Test]
    public async Task Quiet_fallback_does_not_complete_an_empty_snapshot()
    {
        var client = new ScriptedRunnerClient();
        var adapter = NewAdapter(client, doneQuietMs: 200, doneMaxWaitMs: 800);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("do the thing", CancellationToken.None);
        // No transcript rows, no done line, empty snapshot: the CARD-0080 quiet
        // fallback must not resurrect CARD-0052 (empty+quiet → TurnCompleted true).
        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.TurnCompleted.ShouldBeFalse(
            "screen quiet on an empty snapshot is not a completed turn");
    }

    private static RunnerGrokAdapter NewAdapter(ISessionRunnerClient client, int doneQuietMs, int doneMaxWaitMs) =>
        new(
            client,
            Options.Create(new AgentRegistrySettings
            {
                GrokDoneQuietPeriodMs = doneQuietMs,
                GrokDoneMaxWaitMs = doneMaxWaitMs,
            }),
            Options.Create(new SupervisionSettings
            {
                // The submit path is CARD-0055/0056's territory; these tests drive the turn wait.
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = false },
            }));

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "grok",
        Kind: AgentKind.Grok,
        Exe: "grok.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid());

    private static SessionRunnerTranscriptEvent Row(
        Guid sessionId, long seq, string kind, string? text = null, string? stopReason = null) =>
        new(
            sessionId, seq, kind, $"uuid-{seq}", null, DateTimeOffset.UtcNow, null, text,
            null, null, null, null, stopReason);

    private sealed class ScriptedRunnerClient : ISessionRunnerClient
    {
        private IReadOnlyList<SessionRunnerTranscriptEvent> _entries = [];
        private long _sequence;

        public Guid SessionId { get; private set; }
        public string RawOutput { get; set; } = "";
        public bool AdvanceSequenceOnEveryBufferRead { get; set; }
        public int RemainingTranscriptFailures { get; set; }

        public void SetTranscript(params SessionRunnerTranscriptEvent[] entries) => _entries = entries;

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

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
        {
            if (AdvanceSequenceOnEveryBufferRead)
                _sequence++;
            return Task.FromResult(new SessionRunnerBufferDto(sessionId, RawOutput, _sequence));
        }

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, RawOutput, RawOutput, _sequence, DateTime.UtcNow));

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
        {
            if (RemainingTranscriptFailures > 0)
            {
                RemainingTranscriptFailures--;
                throw new InvalidOperationException("transient transcript failure");
            }

            return Task.FromResult(new SessionRunnerTranscriptDto(
                sessionId, _entries, _entries.Count == 0 ? 0 : _entries[^1].Sequence));
        }

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            _sequence++;
            return Task.CompletedTask;
        }

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, _sequence));

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
