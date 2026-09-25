using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class SessionStateProjectionTests
{
    [Test] public Task No_end_activity_is_working() => CheckAsync([new("UserPrompt", Expected: true), new("FutureActivity", Expected: true)]);
    [Test] public Task No_end_housekeeping_is_idle() => CheckAsync([new("TurnTitle"), new("QueueEnqueue"), new("QueuedUserPrompt"), new("QueueRemove"), new("QueueDequeue")]);
    [Test] public Task Turn_end_and_restart_end_work() => CheckAsync([new("AssistantText", Expected: true), new("TurnEnd"), new("UserPrompt", Expected: true), new("SessionRestartBoundary")]);
    [Test] public Task Interrupt_prefix_is_ordinal_and_exact() => CheckAsync([new("UserPrompt", Text: " [Request interrupted", Expected: true), new("UserPrompt", Text: "[request interrupted", Expected: true), new("UserPrompt", Text: "[Request interrupted")]);
    [Test] public Task Manual_compact_and_continuation_end_work() => CheckAsync([new("UserPrompt", Text: "/compact", Expected: true), new("CompactBoundary", 20, "(manual)"), new("UserPrompt", 30, "This session is being continued from a previous conversation")]);
    [Test] public Task Auto_and_unknown_compact_leave_working() => CheckAsync([new("AssistantText", Expected: true), new("CompactBoundary", Text: "(auto)", Expected: true), new("CompactBoundary", Text: "(Manual)", Expected: true), new("CompactBoundary", Expected: true)]);
    [Test] public Task Local_wrappers_are_inert_raw_slash_is_activity() => CheckAsync([new("TurnEnd"), new("UserPrompt", Text: "<command-name>/model"), new("UserPrompt", Text: "<local-command-stdout>ok"), new("UserPrompt", Text: "/real task", Expected: true)]);
    [Test] public Task Activity_sequence_and_time_must_belong_to_one_row() => CheckAsync([new("AssistantText", 100, Expected: true), new("TurnEnd", 50), new("AssistantText", 10), new("UserPrompt", 60, Expected: true)]);
    [Test] public Task Null_and_equal_timestamps_are_conservative() => CheckAsync([new("TurnEnd", 50), new("AssistantText", 49), new("AssistantText", 50, Expected: true), new("TurnEnd", null), new("AssistantText", 10), new("AssistantText", null, Expected: true)]);
    [Test] public Task End_maxima_are_independent_and_never_decrease() => CheckAsync([new("TurnEnd", 100), new("SessionRestartBoundary", 20), new("AssistantText", 50), new("AssistantText", 110, Expected: true)]);

    [Test]
    public async Task Empty_missing_and_duplicate_ids_keep_their_contract()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        (await f.Store.ReadBatchAsync([], default)).ShouldBeEmpty(); f.SeedQueries.ShouldBe(0);
        var missing = Guid.NewGuid();
        var states = await f.Store.ReadBatchAsync([f.SessionId, missing], default);
        states[f.SessionId].Readiness.ShouldBe(SessionStateReadiness.Ready);
        states[f.SessionId].Working.ShouldBeFalse();
        states[missing].Readiness.ShouldBe(SessionStateReadiness.Missing);
        f.SeedQueries.ShouldBe(1);
        await Should.ThrowAsync<ArgumentException>(() => f.Store.ReadBatchAsync([missing, missing], default));
    }

    [Test]
    public async Task Metadata_keeps_arrival_order_separate_from_producer_time()
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(5, "TurnTitle", 100), f.Event(1, "UserPrompt", 50), f.Event(2, "TurnEnd", 20)]);
        var state = await f.Store.ReadAsync(f.SessionId, default);
        state.Count.ShouldBe(3); state.LastSequence.ShouldBe(7); state.LastKind.ShouldBe("TurnEnd");
        state.LastTurnTitleSequence.ShouldBe(5); state.LastUserPromptSequence.ShouldBe(6); state.LastTurnEndSequence.ShouldBe(7);
        state.NewestEffectiveTimestamp.ShouldBe(new DateTime(2026, 1, 1, 0, 1, 40, DateTimeKind.Utc));
        state.LastTimestamp.ShouldBe(new DateTime(2026, 1, 1, 0, 0, 20, DateTimeKind.Utc));
        await Should.ThrowAsync<InvalidOperationException>(() => Task.FromResult(state.Append(awaitedRows())));
        IEnumerable<TranscriptEntry> awaitedRows() => [new() { AgentSessionId = f.SessionId, Sequence = 7 }];
    }

    private sealed record Row(string Kind, int? Seconds = 0, string? Text = null, bool Expected = false);
    private static async Task CheckAsync(Row[] rows)
    {
        await using var f = await SessionStateTestFixture.CreateAsync();
        long sequence = 0;
        foreach (var row in rows)
        {
            var result = await f.Runtime.PersistTranscriptAsync(f.SessionId, [f.Event(++sequence, row.Kind, row.Seconds, row.Text)]);
            result.LastStoredSeq.ShouldBe(sequence);
            var state = await f.Store.ReadAsync(f.SessionId, default);
            state.Working.ShouldBe(row.Expected);
            await using var db = f.Db();
            var oracle = await TranscriptWorkingStateOracle.ReadAsync(db, [f.SessionId], default);
            state.Working.ShouldBe(oracle[f.SessionId]);
            // A new DI container has no inherited state and must seed the same accumulated facts.
            await using var restarted = f.NewContainer();
            var seeded = await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<SessionStateStore>(restarted).ReadAsync(f.SessionId, default);
            seeded.Working.ShouldBe(state.Working); seeded.EndSequence.ShouldBe(state.EndSequence);
            seeded.EndTimestamp.ShouldBe(state.EndTimestamp); seeded.ActivityTimestamp.ShouldBe(state.ActivityTimestamp);
            seeded.Count.ShouldBe(state.Count); seeded.LastSequence.ShouldBe(state.LastSequence);
        }
    }
}
