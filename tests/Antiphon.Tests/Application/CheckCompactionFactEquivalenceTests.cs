using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1754. The legacy Contains loop is the oracle for correlation and human-origin flags.
/// Windowed facts may drop text the policy never reads; the verdict must stay the same.
/// The source witness fails if the per-prompt token loop or the unfiltered text projection returns.
/// </summary>
[Category("Unit")]
public class CheckCompactionFactEquivalenceTests
{
    private static readonly DateTime Accepted = new(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc);

    [Test]
    public async Task Fact_load_does_not_token_scan_every_check_id()
    {
        var root = RepoRoot();
        var service = await File.ReadAllTextAsync(Path.Combine(
            root, "server", "Application", "Services", "CheckCompactionContinuationService.cs"));
        var facts = await File.ReadAllTextAsync(Path.Combine(
            root, "server", "Application", "Services", "CompactionTranscriptFacts.cs"));
        var source = service + facts;
        source.ShouldNotContain("tokens.Any(");
        source.ShouldNotContain("SelectMany(id => new[]");
        service.ShouldNotContain("t.Kind, t.Text, t.Timestamp");
        facts.ShouldNotContain("text.Contains(");
        facts.ShouldNotContain(".Contains(token");
    }

    [Test]
    public void Token_lookup_matches_contains_on_many_checks_and_prompts()
    {
        var ids = Enumerable.Range(0, 48).Select(_ => Guid.NewGuid()).ToArray();
        var rows = new List<CompactionFactInput>();
        var at = Accepted;
        long seq = 1;
        void Add(string kind, string? text, string? tool = null)
        {
            rows.Add(new CompactionFactInput(seq++, kind, text, at, at, "id-" + seq, tool));
            at = at.AddSeconds(1);
        }

        Add(TranscriptKinds.AssistantText, "assistant " + ids[0].ToString("D"));
        Add(TranscriptKinds.UserPrompt, "d " + ids[0].ToString("D"));
        Add(TranscriptKinds.UserPrompt, "n " + ids[1].ToString("N"));
        Add(TranscriptKinds.UserPrompt, "m " + DelegationReportFormatter.TaskMarker(ids[2]));
        Add(TranscriptKinds.UserPrompt, "embed aa" + ids[3].ToString("N") + "bb");
        Add(TranscriptKinds.UserPrompt, ids[4].ToString("D").ToUpperInvariant());
        Add(TranscriptKinds.UserPrompt, ids[5].ToString("N")[..31]);
        Add(TranscriptKinds.UserPrompt, "[antiphon-task:" + ids[6].ToString("N")[..8]);
        Add(TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(ids[7]) + " " + ids[8].ToString("D"));
        Add(TranscriptKinds.UserPrompt, null);
        Add(TranscriptKinds.UserPrompt, "");
        Add(TranscriptKinds.UserPrompt, "no token here at all !!");
        var matchedPrompt = "";
        for (var i = 0; i < 40; i++)
        {
            var id = ids[i % ids.Length];
            var body = (i % 3) switch
            {
                0 => id.ToString("D"),
                1 => id.ToString("N"),
                _ => DelegationReportFormatter.TaskMarker(id),
            };
            var prompt = "prompt " + i + " " + body + " " + new string('x', 80);
            if (i == 3)
                matchedPrompt = prompt;
            Add(TranscriptKinds.UserPrompt, prompt);
            Add(TranscriptKinds.AssistantText, new string('y', 200));
            Add(TranscriptKinds.Thinking, "think " + id.ToString("N"));
            Add(TranscriptKinds.ToolCall, "read", "tool-" + i);
        }

        var humans = new[]
        {
            "ok",
            "d " + ids[0].ToString("D"),
            matchedPrompt,
        };
        AssertSameFacts(rows, ids, humans);
    }

    [Test]
    public void Judged_window_keeps_legacy_verdicts_on_a_large_transcript()
    {
        var ids = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        var episode = Episode(ids, ids[0], DelegationReportFormatter.TaskMarker(ids[0]));
        var humans = Array.Empty<string>();
        VerdictsMatch(episode, ids, humans);
        var judged = CompactionTranscriptFacts.BuildJudged(episode, Accepted, ids, humans);
        judged.Any(f => f.Text != null && f.Text.Contains("HISTORICAL-ASSISTANT", StringComparison.Ordinal))
            .ShouldBeFalse();
        judged.Single(f => f.Sequence == 1001).IsCorrelatedCheck.ShouldBeTrue();
        judged.Single(f => f.Sequence == 1001).Text.ShouldNotBeNull();

        var embedded = Episode(ids, ids[1], "zz" + ids[1].ToString("N") + "qq");
        VerdictsMatch(embedded, ids, humans);
        CompactionContinuationPolicy.Evaluate(
            Scope(), CompactionTranscriptFacts.BuildJudged(embedded, Accepted, ids, humans), 10, When(embedded))
            .Overdue.ShouldBeTrue();

        var humanBody = "please review the board carefully " + DelegationReportFormatter.TaskMarker(ids[0]);
        var humanOwned = Episode(ids, ids[0], humanBody);
        VerdictsMatch(humanOwned, ids, [humanBody]);

        var unmatched = Episode(ids, ids[0], DelegationReportFormatter.TaskMarker(ids[0]), toolResult: false);
        VerdictsMatch(unmatched, ids, humans);

        var manual = Episode(ids, ids[0], DelegationReportFormatter.TaskMarker(ids[0]), boundary: "Context compacted (manual)");
        VerdictsMatch(manual, ids, humans);

        var progress = Episode(ids, ids[0], DelegationReportFormatter.TaskMarker(ids[0])).ToList();
        progress.Add(Row(1006, TranscriptKinds.AssistantText, "still working", Accepted.AddHours(3)));
        VerdictsMatch(progress, ids, humans);

        var interrupted = History(ids);
        interrupted.Add(Row(1000, TranscriptKinds.TurnEnd, null, Accepted.AddHours(2)));
        interrupted.Add(Row(1001, TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(ids[0]), Accepted.AddHours(2).AddSeconds(1)));
        interrupted.Add(Row(1002, TranscriptKinds.UserPrompt, "[Request interrupted by user]", Accepted.AddHours(2).AddSeconds(2)));
        interrupted.Add(Row(1003, TranscriptKinds.UserPrompt, "a person typed something else", Accepted.AddHours(2).AddSeconds(3)));
        interrupted.Add(Row(1004, TranscriptKinds.CompactBoundary, "Context compacted (auto)", Accepted.AddHours(2).AddSeconds(4), "boundary-1"));
        interrupted.Add(Row(1005, TranscriptKinds.UserPrompt, TranscriptKinds.CompactionContinuationPromptPrefix + " summary", Accepted.AddHours(2).AddSeconds(5), "cont-1"));
        VerdictsMatch(interrupted, ids, humans);

        var conflicted = Episode(ids, ids[0], DelegationReportFormatter.TaskMarker(ids[0])).ToList();
        conflicted[4] = conflicted[4] with
        {
            EventTime = Accepted.AddHours(4),
            CreatedAt = Accepted.AddHours(4),
        };
        conflicted[5] = conflicted[5] with
        {
            EventTime = Accepted.AddMinutes(30),
            CreatedAt = Accepted.AddMinutes(30),
        };
        VerdictsMatch(conflicted, ids, humans);
    }

    private static void VerdictsMatch(
        IReadOnlyList<CompactionFactInput> rows, IReadOnlyList<Guid> ids, IReadOnlyList<string> humans)
    {
        var legacy = Legacy(rows, ids, humans);
        var judged = CompactionTranscriptFacts.BuildJudged(rows, Accepted, ids, humans);
        var when = When(rows);
        CompactionContinuationPolicy.Evaluate(Scope(), judged, 10, when)
            .ShouldBe(CompactionContinuationPolicy.Evaluate(Scope(), legacy, 10, when));
    }

    private static void AssertSameFacts(
        IReadOnlyList<CompactionFactInput> rows, IReadOnlyList<Guid> ids, IReadOnlyList<string> humans)
    {
        var built = CompactionTranscriptFacts.Build(rows, ids, humans);
        var legacy = Legacy(rows, ids, humans);
        built.Length.ShouldBe(legacy.Length);
        for (var i = 0; i < built.Length; i++)
        {
            built[i].Sequence.ShouldBe(legacy[i].Sequence);
            built[i].Kind.ShouldBe(legacy[i].Kind);
            built[i].Text.ShouldBe(legacy[i].Text);
            built[i].EventTime.ShouldBe(legacy[i].EventTime);
            built[i].CreatedAt.ShouldBe(legacy[i].CreatedAt);
            built[i].NativeId.ShouldBe(legacy[i].NativeId);
            built[i].ToolUseId.ShouldBe(legacy[i].ToolUseId);
            built[i].IsCorrelatedCheck.ShouldBe(legacy[i].IsCorrelatedCheck);
            built[i].IsHumanOrigin.ShouldBe(legacy[i].IsHumanOrigin);
        }
    }

    private static List<CompactionFactInput> Episode(
        Guid[] ids, Guid owner, string owningText, bool toolResult = true, string boundary = "Context compacted (auto)")
    {
        var rows = History(ids);
        var promptAt = Accepted.AddHours(2);
        rows.Add(Row(1000, TranscriptKinds.TurnEnd, null, promptAt.AddSeconds(-1)));
        rows.Add(Row(1001, TranscriptKinds.UserPrompt, owningText, promptAt));
        rows.Add(Row(1002, TranscriptKinds.ToolCall, "read", promptAt.AddSeconds(1), tool: "tool-1"));
        if (toolResult)
            rows.Add(Row(1003, TranscriptKinds.ToolResult, "ok", promptAt.AddSeconds(2), tool: "tool-1"));
        rows.Add(Row(1004, TranscriptKinds.CompactBoundary, boundary, promptAt.AddSeconds(3), "boundary-1"));
        rows.Add(Row(1005, TranscriptKinds.UserPrompt,
            TranscriptKinds.CompactionContinuationPromptPrefix + " summary", promptAt.AddSeconds(4), "cont-1"));
        _ = owner;
        return rows;
    }

    private static List<CompactionFactInput> History(Guid[] ids)
    {
        var rows = new List<CompactionFactInput>
        {
            Row(1, TranscriptKinds.TurnEnd, null, Accepted),
        };
        for (var i = 0; i < 30; i++)
        {
            var id = ids[i % ids.Length];
            var token = (i % 3) switch
            {
                0 => id.ToString("D"),
                1 => id.ToString("N"),
                _ => DelegationReportFormatter.TaskMarker(id),
            };
            var at = Accepted.AddMinutes(i + 1);
            rows.Add(Row(10 + (i * 2), TranscriptKinds.UserPrompt, "HISTORICAL-PROMPT " + token, at));
            rows.Add(Row(11 + (i * 2), TranscriptKinds.AssistantText, "HISTORICAL-ASSISTANT " + new string('a', 100), at.AddSeconds(1)));
        }

        return rows;
    }

    private static CompactionFactInput Row(
        long sequence, string kind, string? text, DateTime at, string? native = null, string? tool = null) =>
        new(sequence, kind, text, at, at, native, tool);

    private static DateTime When(IReadOnlyList<CompactionFactInput> rows) =>
        rows.Max(r => r.CreatedAt).AddMinutes(10);

    private static CompactionScopeSnapshot Scope() =>
        CompactionScopeSnapshot.EligibleSeat(Guid.Parse("cea73d57-3072-4cc2-8cb0-ab859e7d3415"), Accepted);

    private static CompactionTranscriptFact[] Legacy(
        IReadOnlyList<CompactionFactInput> rows, IReadOnlyList<Guid> checkIds, IReadOnlyList<string> humanBodies)
    {
        var tokens = checkIds.SelectMany(id => new[]
        {
            id.ToString("D"), id.ToString("N"), DelegationReportFormatter.TaskMarker(id),
        }).ToArray();
        var humanMatchable = humanBodies.Where(PromptSubmissionMatch.RequiresTextMatch).ToArray();
        return rows.OrderBy(row => row.Sequence).Select(row => new CompactionTranscriptFact(
            row.Sequence,
            row.Kind,
            row.Text,
            row.EventTime,
            row.CreatedAt,
            row.NativeId,
            row.ToolUseId,
            IsCorrelatedCheck: row.Kind == TranscriptKinds.UserPrompt
                && row.Text is not null
                && tokens.Any(token => row.Text.Contains(token, StringComparison.Ordinal)),
            IsHumanOrigin: row.Kind == TranscriptKinds.UserPrompt
                && row.Text is not null
                && humanMatchable.Any(body => PromptSubmissionMatch.IsCompleteIn(body, row.Text)))).ToArray();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        dir.ShouldNotBeNull();
        return dir!.FullName;
    }
}
