using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using Row = Antiphon.Server.Application.Services.TranscriptPromptSpan.PromptRow;
using Boundary = Antiphon.Server.Application.Services.TaskReportTurnSelector.BoundaryRow;
using Tool = Antiphon.Server.Application.Services.TaskReportTurnSelector.ToolSiblingRow;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class TaskReportFloorIndexTests
{
    private static readonly DateTime Stamp = new(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Floor_matches_original_scan_for_seeded_sessions()
    {
        var random = new Random(1910);
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Raw })
        for (var trial = 0; trial < 80; trial++)
        {
            var task = new AgentTask { Id = Guid.Parse("00001910-0000-0000-0000-000000000001"),
                DispatchedAt = trial % 3 == 0 ? Stamp : null,
                RepliedAtSequence = trial % 5 == 0 ? 10 : null };
            var marker = DelegationReportFormatter.TaskMarker(task.Id);
            var rows = new List<Row>();
            var tools = new List<Tool>();
            var boundaries = new List<Boundary>();
            long sequence = 0;
            // Includes empty sessions, ties, prompts before the first boundary,
            // both owning kinds, local wrappers/echoes and unmarked barriers.
            for (var i = 0; i < trial * 2; i++)
            {
                sequence += random.Next(0, 4);
                var text = (i % 7) switch
                {
                    0 => " \t<command-name>/status</command-name>",
                    1 => "/status",
                    2 => TranscriptKinds.CompactionContinuationPromptPrefix + " continued",
                    3 => "unmarked",
                    _ => marker,
                };
                rows.Add(new Row(sequence, text, i % 3 == 0 ? Stamp : Stamp.AddSeconds(1),
                    i % 4 == 0 ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.UserPrompt));
                if (i % 3 == 0) tools.Add(new Tool(sequence + 1, "api" + i % 4));
                if (i % 2 == 0) boundaries.Add(new Boundary(sequence + 2, TranscriptKinds.TurnEnd,
                    i % 10 == 0 ? "max_tokens" : i % 11 == 0 ? "cancelled" : "end_turn",
                    i % 9 == 0 ? null : "api" + i % 4));
            }
            boundaries = boundaries.OrderByDescending(b => b.Sequence).ToList();
            var invoked = new HashSet<string>(StringComparer.Ordinal) { "/status" };
            var index = TaskReportTurnSelector.CreateRealPromptIndex(rows, kind, new HashSet<Guid>(), invoked);
            for (var i = 0; i < boundaries.Count; i++)
                TaskReportTurnSelector.PhantomTransparentFloor(boundaries, i, index, tools, task)
                    .ShouldBe(OriginalFloor(boundaries, i, rows, tools, task, kind, invoked));

            // Exercise strict lower/upper bounds at the first and last row,
            // including duplicate sequences and null floor, independently of floors.
            foreach (var before in rows.Select(r => r.Sequence).Append(long.MaxValue).Append(0))
            foreach (var after in new long?[] { null, 0, rows.FirstOrDefault()?.Sequence, rows.LastOrDefault()?.Sequence })
                index.NewestBetween(after, before).ShouldBe(OriginalNewest(rows, after, before, kind, invoked));
            foreach (var after in rows.Select(r => r.Sequence).Append(-1))
                index.NextSequence(after).ShouldBe(rows.FirstOrDefault(r => r.Sequence > after && IsReal(r, kind, invoked))?.Sequence);
        }
    }

    [Test]
    public void Floor_classifies_each_row_once_for_twenty_thousand_rows_and_two_thousand_boundaries()
    {
        var task = new AgentTask { Id = Guid.Parse("00001910-0000-0000-0000-000000000001") };
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var rows = Enumerable.Range(0, 20_000)
            .Select(i => new Row(i * 10L, marker, Stamp, TranscriptKinds.UserPrompt)).ToArray();
        var boundaries = Enumerable.Range(1, 2_000).Reverse()
            .Select(i => new Boundary(i * 100L - 8, TranscriptKinds.TurnEnd, "end_turn", "api" + i)).ToArray();
        var tools = Enumerable.Range(1, 2_000).Select(i => new Tool(i * 100L - 9, "api" + i)).ToArray();
        var classifications = 0;
        var index = TaskReportTurnSelector.CreateRealPromptIndex(rows, AgentKind.ClaudeCode,
            new HashSet<Guid>(), new HashSet<string>(), () => classifications++);
        TaskReportTurnSelector.PhantomTransparentFloor(boundaries, 0, index, tools, task).ShouldBeNull();
        classifications.ShouldBe(rows.Length);
    }

    private static bool IsReal(Row row, AgentKind kind, IReadOnlySet<string> invoked) =>
        (row.Kind == TranscriptKinds.UserPrompt || row.Kind == TranscriptKinds.QueuedUserPrompt)
        && !TaskReportHousekeeping.IsAllowlistedPrompt(kind, row.Text, new HashSet<Guid>())
        && !TranscriptPromptSpan.IsHousekeepingPrompt(row.Text, invoked);

    private static Row? OriginalNewest(IReadOnlyList<Row> rows, long? after, long before,
        AgentKind kind, IReadOnlySet<string> invoked)
    {
        Row? newest = null;
        foreach (var row in rows)
        {
            if (after is long floor && row.Sequence <= floor) continue;
            if (row.Sequence >= before) break;
            if (IsReal(row, kind, invoked)) newest = row;
        }
        return newest;
    }

    // Private pre-CARD-1910 algorithm: no production index/lookups in this oracle.
    private static long? OriginalFloor(IReadOnlyList<Boundary> boundaries, int index,
        IReadOnlyList<Row> rows, IReadOnlyList<Tool> tools, AgentTask task,
        AgentKind kind, IReadOnlySet<string> invoked)
    {
        long? floor = null;
        for (var i = boundaries.Count - 1; i > index; i--)
        {
            var end = boundaries[i];
            var owner = OriginalNewest(rows, floor, end.Sequence, kind, invoked);
            var next = owner is null ? null : rows.FirstOrDefault(r => r.Sequence > owner.Sequence && IsReal(r, kind, invoked))?.Sequence;
            if (end.StopReason == "end_turn" && !string.IsNullOrEmpty(end.ApiCallId)
                && owner is not null
                && (task.DispatchedAt is null || owner.Timestamp is null || owner.Timestamp > task.DispatchedAt)
                && (task.RepliedAtSequence is null || owner.Sequence > task.RepliedAtSequence)
                && owner.Text is string text && text.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal)
                && tools.Any(t => t.ApiCallId == end.ApiCallId && t.Sequence > owner.Sequence && (next is null || t.Sequence < next)))
                continue;
            floor = end.Sequence;
        }
        return floor;
    }
}
