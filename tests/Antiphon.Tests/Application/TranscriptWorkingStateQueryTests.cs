using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class TranscriptWorkingStateQueryTests
{
    [Test]
    public async Task Empty_input_emits_no_sql()
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync();
        await using var db = f.CreateDb();
        (await SessionMessageQueueService.IsWorkingBatchAsync(db, [], default)).ShouldBeEmpty();
        f.Capture.Reads.ShouldBeEmpty();
    }

    [Test]
    public async Task Missing_and_empty_sessions_are_idle()
    {
        await CheckAsync(new Scenario(false, []));
        await using var f = await TranscriptHotPathFixture.CreateAsync();
        await using var db = f.CreateDb();
        // Preserve the old dictionary contract; duplicate requested IDs are rejected.
        await Should.ThrowAsync<ArgumentException>(() => SessionMessageQueueService.IsWorkingBatchAsync(
            db, [f.SessionIds[0], f.SessionIds[0]], default));
    }

    [Test]
    public async Task No_end_activity_is_working() => await CheckAsync(
        new(true, [new("AssistantText", 1)]), new(true, [new("UserPrompt", 1)]),
        new(true, [new("FutureActivityKind", 1)]));

    [Test]
    public async Task No_end_housekeeping_is_idle() => await CheckAsync(new Scenario(false,
    [
        new("TurnTitle", 1), new("QueuedUserPrompt", 2), new("QueueEnqueue", 3),
        new("QueueDequeue", 4), new("QueueRemove", 5), new("CompactBoundary", 6),
        new("UserPrompt", 7, Text: "<command-name>/model</command-name>"),
        new("UserPrompt", 8, Text: "<local-command-stdout>status"),
        new("UserPrompt", 9, Text: "This session is being continued from a previous conversation...")
    ]));

    [Test]
    public async Task Turn_end_and_restart_boundary_end_work() => await CheckAsync(
        new(false, [new("UserPrompt", 1), new("TurnEnd", 2)]),
        new(false, [new("AssistantText", 1), new("SessionRestartBoundary", 2)]),
        new(true, [new("TurnEnd", 1), new("AssistantText", 2)]));

    [Test]
    public async Task Interrupt_prefix_ends_work_but_mentions_do_not() => await CheckAsync(
        new(false, [new("AssistantText", 1), new("UserPrompt", 2, Text: "[Request interrupted by user]")]),
        new(true, [new("UserPrompt", 1, Text: "quote [Request interrupted in text")]),
        new(true, [new("UserPrompt", 1, Text: " [Request interrupted by user]")]),
        new(true, [new("UserPrompt", 1, Text: "[request interrupted by user]")]));

    [Test]
    public async Task Manual_compact_and_continuation_are_idle() => await CheckAsync(
        new(false, [new("UserPrompt", 1, Text: "/compact keep context"),
            new("CompactBoundary", 2, 20, "Compacted (manual)"),
            new("UserPrompt", 3, 10, "This session is being continued from a previous conversation...")]),
        // Equal/newer continuation time must also be inert: no timestamp override to hide a bug.
        new(false, [new("CompactBoundary", 1, 10, "(manual)"),
            new("UserPrompt", 2, 30, "This session is being continued from a previous conversation...")]),
        new(true, [new("CompactBoundary", 1, 10, "(manual)"),
            new("UserPrompt", 2, 30, " this session is being continued from a previous conversation...")]));

    [Test]
    public async Task Auto_and_unknown_compact_do_not_end_work() => await CheckAsync(
        new(true, [new("AssistantText", 1), new("CompactBoundary", 2, Text: "Compacted (auto)")]),
        new(true, [new("AssistantText", 1), new("CompactBoundary", 2)]),
        new(true, [new("AssistantText", 1), new("CompactBoundary", 2, Text: "Compacted (Manual)")]));

    [Test]
    public async Task Local_and_queue_records_are_inert_but_raw_slash_and_unknown_kinds_are_activity() => await CheckAsync(
        new(false, [new("TurnEnd", 1), new("UserPrompt", 2, Text: "<command-name>/model"),
            new("UserPrompt", 3, Text: "<local-command-stdout>ok"), new("QueuedUserPrompt", 4),
            new("QueueEnqueue", 5), new("QueueDequeue", 6), new("QueueRemove", 7)]),
        new(true, [new("TurnEnd", 1), new("UserPrompt", 2, Text: "/real user task")]),
        new(true, [new("TurnEnd", 1), new("FutureActivityKind", 2)]),
        new(true, [new("TurnEnd", 1), new("UserPrompt", 2, Text: " <command-name>/model")]),
        new(true, [new("TurnEnd", 1), new("UserPrompt", 2, Text: "<Command-name>/model")]));

    [Test]
    public async Task Stale_replay_requires_one_qualifying_row_not_independent_activity_maxima() => await CheckAsync(
        new(false, [new("AssistantText", 1, 100), new("TurnEnd", 2, 50), new("ToolResult", 3, 10)]),
        new(true, [new("AssistantText", 1, 100), new("TurnEnd", 2, 50),
            new("ToolResult", 3, 10), new("UserPrompt", 4, 60)]));

    [Test]
    public async Task Null_and_equal_timestamps_preserve_conservative_working() => await CheckAsync(
        new(true, [new("TurnEnd", 1, 50), new("AssistantText", 2, null)]),
        new(true, [new("TurnEnd", 1, null), new("AssistantText", 2, 10)]),
        new(true, [new("TurnEnd", 1, 50), new("AssistantText", 2, 50)]),
        new(false, [new("TurnEnd", 1, 50), new("TurnEnd", 2, null), new("AssistantText", 3, 10)]));

    [Test]
    public async Task End_sequence_and_timestamp_maxima_can_come_from_different_rows() => await CheckAsync(
        new(false, [new("TurnEnd", 1, 100), new("SessionRestartBoundary", 3, 20), new("AssistantText", 4, 50)]),
        new(false, [new("TurnEnd", 1, 100), new("AssistantText", 2, 110), new("TurnEnd", 3, 20)]),
        new(true, [new("TurnEnd", 1, 100), new("TurnEnd", 3, 20), new("AssistantText", 4, 110)]));

    private sealed record Row(string Kind, long Sequence, int? Seconds = 0, string? Text = null);
    private sealed record Scenario(bool Expected, Row[] Rows);

    private static async Task CheckAsync(params Scenario[] scenarios)
    {
        await using var f = await TranscriptHotPathFixture.CreateAsync();
        await using var db = f.CreateDb();
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < scenarios.Length; i++)
            foreach (var row in scenarios[i].Rows)
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = f.SessionIds[i], Kind = row.Kind,
                    Sequence = row.Sequence, Text = row.Text, CreatedAt = epoch,
                    Timestamp = row.Seconds is { } seconds ? epoch.AddSeconds(seconds) : null
                });
        // Unrequested rows must affect neither boundaries nor the existential activity check.
        db.TranscriptEntries.AddRange(
            new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.SessionIds[^1],
                Kind = "TurnEnd", Sequence = 9999, Timestamp = epoch.AddDays(1), CreatedAt = epoch },
            new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.SessionIds[^2],
                Kind = "AssistantText", Sequence = 99999, CreatedAt = epoch });
        await db.SaveChangesAsync();
        var expected = scenarios.Select((s, i) => (Id: f.SessionIds[i], s.Expected))
            .ToDictionary(x => x.Id, x => x.Expected);
        expected.Add(Guid.NewGuid(), false); // missing session
        expected.Add(f.SessionIds[^3], false); // existing, empty session
        foreach (var (id, working) in expected)
        {
            (await SessionMessageQueueService.IsWorkingAsync(db, id, default)).ShouldBe(working);
            (await TranscriptWorkingStateOracle.ReadAsync(db, [id], default))[id].ShouldBe(working);
        }
        var actual = await SessionMessageQueueService.IsWorkingBatchAsync(db, expected.Keys.ToArray(), default);
        var oracle = await TranscriptWorkingStateOracle.ReadAsync(db, expected.Keys.ToArray(), default);
        actual.Count.ShouldBe(expected.Count);
        oracle.Count.ShouldBe(expected.Count);
        foreach (var (id, working) in expected)
        {
            actual[id].ShouldBe(working);
            oracle[id].ShouldBe(working);
        }
    }
}
