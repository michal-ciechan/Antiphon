using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0135: <see cref="TranscriptPromptSpan"/> counts <c>QueuedUserPrompt</c> alongside
/// <c>UserPrompt</c>. The cases below pin the bound, the sequence order (the S2.4 trap does not
/// transfer), the housekeeping filter, and <see cref="TranscriptPromptSpan.PromptRow.Kind"/>.
/// Settlement and the delivery watchdog share this type; they cannot disagree.
/// </summary>
[Category("Integration")]
public class TranscriptPromptSpanTests
{
    private const string Wrapper =
        "<command-name>/compact</command-name>\n            <command-message>compact</command-message>\n"
        + "            <command-args>Keep only what serves the new task.</command-args>";

    [Test]
    public async Task a_queued_row_after_dispatch_is_a_turn_prompt()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "the drained brief",
            sequence: 1, timestamp: dispatchedAt.AddMinutes(1));

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        var queued = span.TurnPrompts.ShouldHaveSingleItem();
        queued.Text.ShouldBe("the drained brief");
        queued.Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
        (await TranscriptPromptSpan.HasTurnPromptSinceAsync(db, sessionId, dispatchedAt, CancellationToken.None))
            .ShouldBeTrue();
    }

    [Test]
    public async Task a_queued_row_enqueued_before_dispatch_is_excluded_even_with_a_later_sequence()
    {
        // D2: the bound is the enqueue clock. A body typed before this task existed cannot be
        // its brief, however late it drains (and however high its sequence).
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt, "inherited history",
            sequence: 1, timestamp: dispatchedAt.AddMinutes(-20));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "queued before this dispatch",
            sequence: 2, timestamp: dispatchedAt.AddMinutes(-1));

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldBeEmpty();
    }

    [Test]
    public async Task a_queued_row_with_a_null_timestamp_is_kept()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "untimestamped queued brief",
            sequence: 1, timestamp: null);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldHaveSingleItem().Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
    }

    [Test]
    public async Task the_s24_timestamp_skew_does_not_reorder_the_span()
    {
        // Verbatim from tests/Antiphon.Tests/Agents/Fixtures/queued-command.jsonl: a queued
        // attachment stamped 17:58:06.291Z sits at a higher sequence than a typed user record
        // stamped 17:58:37.774Z. CARD-0132 S2.4's trap is ranking those two against each other
        // by timestamp; this type orders by Sequence, so the typed row stays first. Goes red
        // the day someone adds a timestamp-based ordering here.
        var dispatchedAt = new DateTime(2026, 8, 21, 17, 50, 0, DateTimeKind.Utc);
        var (sessionId, _) = await SeedSessionAsync(dispatchedAt);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt, "tool_result that arrived first in file order",
            sequence: 1, timestamp: new DateTime(2026, 8, 21, 17, 58, 37, 774, DateTimeKind.Utc));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            "The complete completion note that Claude queued while it was busy.",
            sequence: 2, timestamp: new DateTime(2026, 8, 21, 17, 58, 6, 291, DateTimeKind.Utc));

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.Count.ShouldBe(2);
        span.TurnPrompts[0].Kind.ShouldBe(TranscriptKinds.UserPrompt);
        span.TurnPrompts[0].Sequence.ShouldBe(1);
        span.TurnPrompts[1].Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
        span.TurnPrompts[1].Sequence.ShouldBe(2);
        span.TurnPrompts[0].Timestamp.ShouldNotBeNull();
        span.TurnPrompts[1].Timestamp.ShouldNotBeNull();
        span.TurnPrompts[0].Timestamp!.Value.ShouldBeGreaterThan(span.TurnPrompts[1].Timestamp!.Value);
    }

    [Test]
    public async Task a_queued_row_with_housekeeping_text_is_filtered_the_same_as_typed()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        var at = dispatchedAt.AddMinutes(1);
        await SeedEntryAsync(sessionId, TranscriptKinds.QueuedUserPrompt, Wrapper, 1, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            TranscriptKinds.CompactionContinuationPromptPrefix + " that ran out of context.",
            2, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            "<task-notification>\n<summary>done</summary>\n</task-notification>",
            3, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "a real queued brief",
            4, at);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldHaveSingleItem().Text.ShouldBe("a real queued brief");
        span.Notifications.ShouldHaveSingleItem().Text.ShouldContain("<task-notification>");
    }

    [Test]
    public async Task prompt_row_kind_reports_the_rows_real_kind_on_both()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        var at = dispatchedAt.AddMinutes(1);
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, "typed brief", 1, at);
        await SeedEntryAsync(sessionId, TranscriptKinds.QueuedUserPrompt, "queued brief", 2, at);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.Count.ShouldBe(2);
        span.TurnPrompts[0].Kind.ShouldBe(TranscriptKinds.UserPrompt);
        span.TurnPrompts[1].Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
    }

    [Test]
    public async Task queue_operation_rows_are_invisible_to_the_prompt_span()
    {
        // CARD-0292 S3: enqueue is still not a prompt. The span is UserPrompt + QueuedUserPrompt
        // only — ChannelReplyDispatcher owning-prompt / window-cap reads the same two kinds.
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        var at = dispatchedAt.AddMinutes(1);
        await SeedEntryAsync(sessionId, TranscriptKinds.QueueEnqueue, "Hi", 1, at);
        await SeedEntryAsync(sessionId, TranscriptKinds.QueueDequeue, "Hi", 2, at);
        await SeedEntryAsync(sessionId, TranscriptKinds.QueueRemove, "Hi", 3, at);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldBeEmpty();
        (await TranscriptPromptSpan.HasTurnPromptSinceAsync(db, sessionId, dispatchedAt, CancellationToken.None))
            .ShouldBeFalse();
    }

    [Test]
    public async Task C714_Grok_completion_is_housekeeping()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await MarkKindAsync(sessionId, AgentKind.Grok);
        var at = dispatchedAt.AddMinutes(1);
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, "the real brief", 1, at);
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, Card0714Transcript.Reminder, 2, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "<task-notification>\n<tool-use-id>toolu_span</tool-use-id>\n</task-notification>",
            3, at);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldHaveSingleItem().Text.ShouldBe("the real brief");
        span.Notifications.ShouldHaveSingleItem().Text.ShouldContain("<task-notification>");
        span.RawPrompts.ShouldContain(row => row.Text == Card0714Transcript.Reminder);
    }

    [Test]
    public async Task C714_Grok_housekeeping_does_not_count_as_started()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await MarkKindAsync(sessionId, AgentKind.Grok);
        var rulesId = Guid.NewGuid();
        var header = GrokRulesRefreshService.Header(rulesId);
        await using (var db = CreateContext())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = rulesId,
                AgentSessionId = sessionId,
                Sequence = 1,
                Origin = QueuedMessageOrigin.System,
                Status = QueuedMessageStatus.Sent,
                CreatedAt = dispatchedAt,
                SentAt = dispatchedAt,
                DeliveryAttempts = 1,
                RulesRefreshKey = "launch:" + Guid.NewGuid().ToString("N"),
                Body = header,
            });
            await db.SaveChangesAsync();
        }

        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, header, 1, dispatchedAt.AddMinutes(1));
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, Card0714Transcript.Reminder, 2, dispatchedAt.AddMinutes(2));

        await using var verify = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(verify, sessionId, dispatchedAt, CancellationToken.None);
        span.TurnPrompts.ShouldBeEmpty();
        span.RawPrompts.Count.ShouldBe(2);
        (await TranscriptPromptSpan.HasTurnPromptSinceAsync(verify, sessionId, dispatchedAt, CancellationToken.None))
            .ShouldBeFalse();
    }

    // CARD-1969 slice 2: the prompt-span read must not re-read a whole session
    // (docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md §6, W-1..W-9). Every
    // witness except W-1 "select" uses its own PromptCache, so no other test's entries reach it.

    [Test]
    [Arguments(400, "load")]
    [Arguments(1600, "load")]
    [Arguments(400, "select")]
    public async Task C1969s2_unchanged_second_read_materialises_no_prompt_text(int n, string via)
    {
        var seeded = await SeedPromptsAsync(n);
        var counter = new PromptTextReadCounter();
        await using var db = CreateContext(counter);
        if (via == "select")
        {
            // Through the selector, so through PromptCache.Shared, on a fresh session id.
            var task = new AgentTask { Id = Guid.NewGuid(), DispatchedAt = seeded.DispatchedAt };
            var before = await TaskReportTurnSelector.SelectAsync(db, seeded.SessionId, task, CancellationToken.None);
            counter.Rows.ShouldBe(n);
            counter.Reset();
            var after = await TaskReportTurnSelector.SelectAsync(db, seeded.SessionId, task, CancellationToken.None);
            counter.Rows.ShouldBe(0);
            after.Kind.ShouldBe(before.Kind);
            after.NextRealPromptSequence.ShouldBe(before.NextRealPromptSequence);
            return;
        }

        var cache = NewCache();
        var first = await LoadAsync(db, seeded, cache);
        counter.Rows.ShouldBe(n);
        counter.Reset();
        var second = await LoadAsync(db, seeded, cache);
        counter.Rows.ShouldBe(0);
        ShouldBeSeeded(second.RawPrompts, seeded.Prompts);
        second.TurnPrompts.Select(r => r.Sequence).ShouldBe(first.TurnPrompts.Select(r => r.Sequence));
    }

    [Test]
    public async Task C1969s2_new_prompt_is_observed()
    {
        var seeded = await SeedPromptsAsync(400);
        var cache = NewCache();
        await using var db = CreateContext();
        (await LoadAsync(db, seeded, cache)).RawPrompts.Count.ShouldBe(400);

        const string text = "c1969 appended after the first load";
        var sequence = await AppendPromptAsync(seeded.SessionId, text, seeded.DispatchedAt.AddMinutes(5));
        var second = await LoadAsync(db, seeded, cache);
        second.RawPrompts.ShouldContain(r => r.Sequence == sequence && r.Text == text);
        second.TurnPrompts.ShouldContain(r => r.Sequence == sequence && r.Text == text);
        second.RawPrompts.Count.ShouldBe(401);
    }

    [Test]
    public async Task C1969s2_new_prompt_does_not_reread_the_prefix()
    {
        var seeded = await SeedPromptsAsync(400);
        var cache = NewCache();
        var counter = new PromptTextReadCounter();
        await using var db = CreateContext(counter);
        await LoadAsync(db, seeded, cache);
        counter.Rows.ShouldBe(400);

        const string text = "c1969 tail prompt";
        var sequence = await AppendPromptAsync(seeded.SessionId, text, seeded.DispatchedAt.AddMinutes(5));
        counter.Reset();
        var second = await LoadAsync(db, seeded, cache);
        counter.Rows.ShouldBe(1);
        second.RawPrompts.Count.ShouldBe(401);
        second.RawPrompts[^1].Sequence.ShouldBe(sequence);
        second.RawPrompts[^1].Text.ShouldBe(text);
        ShouldBeSeeded(second.RawPrompts.Take(400).ToList(), seeded.Prompts);
    }

    [Test]
    public async Task C1969s2_deleted_prompt_is_absent()
    {
        var seeded = await SeedPromptsAsync(400);
        var cache = NewCache();
        await using var db = CreateContext();
        await LoadAsync(db, seeded, cache);

        // A middle delete through a second context. The seeded prompt rows have no children
        // on the Restrict foreign key, so the row can go directly.
        var deleted = seeded.Prompts.Single(p => p.Sequence == 200);
        await using (var other = CreateContext())
        {
            (await other.TranscriptEntries
                    .Where(t => t.AgentSessionId == seeded.SessionId && t.Sequence == deleted.Sequence)
                    .ExecuteDeleteAsync())
                .ShouldBe(1);
        }

        var second = await LoadAsync(db, seeded, cache);
        second.RawPrompts.ShouldNotContain(r => r.Sequence == deleted.Sequence);
        second.RawPrompts.ShouldNotContain(r => r.Text == deleted.Text);
        second.RawPrompts.Count.ShouldBe(399);
    }

    [Test]
    public async Task C1969s2_rebased_top_row_is_observed()
    {
        var seeded = await SeedPromptsAsync(400);
        var cache = NewCache();
        await using var db = CreateContext();
        (await LoadAsync(db, seeded, cache)).RawPrompts[^1].Text.ShouldBe(seeded.Prompts[^1].Text);

        // Same sequence, new row id, new text: max(Sequence) and the count do not move.
        const string text = "c1969 replaced top prompt";
        var top = seeded.Prompts[^1].Sequence;
        await using (var other = CreateContext())
        {
            (await other.TranscriptEntries
                    .Where(t => t.AgentSessionId == seeded.SessionId && t.Sequence == top)
                    .ExecuteDeleteAsync())
                .ShouldBe(1);
            other.TranscriptEntries.Add(NewEntry(
                seeded.SessionId, top, TranscriptKinds.UserPrompt, text, seeded.DispatchedAt.AddMinutes(5)));
            await other.SaveChangesAsync();
        }

        var second = await LoadAsync(db, seeded, cache);
        second.RawPrompts.Count.ShouldBe(400);
        second.RawPrompts[^1].Sequence.ShouldBe(top);
        second.RawPrompts[^1].Text.ShouldBe(text);
    }

    [Test]
    public async Task C1969s2_null_timestamp_below_a_later_pre_dispatch_sequence_stays()
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, "c1969 untimestamped", 1, null);
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, "c1969 before dispatch", 2, dispatchedAt.AddMinutes(-1));
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, "c1969 after dispatch", 3, dispatchedAt.AddMinutes(1));

        var cache = NewCache();
        await using var db = CreateContext();
        for (var load = 0; load < 2; load++)
        {
            var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, cache, CancellationToken.None);
            span.TurnPrompts.Select(r => r.Sequence).ShouldBe(new long[] { 1, 3 });
            span.RawPrompts.Select(r => r.Sequence).ShouldBe(new long[] { 1, 2, 3 });
        }
    }

    [Test]
    public async Task C1969s2_prompt_committed_after_the_rows_is_never_hidden()
    {
        var seeded = await SeedPromptsAsync(400);
        const string text = "c1969 committed after the rows";
        var insert = new InsertAfterPromptReadInterceptor(
            seeded.SessionId, text, seeded.DispatchedAt.AddMinutes(5));
        var cache = NewCache();
        await using var db = CreateContext(insert);

        var first = await LoadAsync(db, seeded, cache);
        first.RawPrompts.Count.ShouldBe(400);
        insert.Inserted.ShouldBe(1);

        var second = await LoadAsync(db, seeded, cache);
        second.RawPrompts.ShouldContain(r => r.Sequence == insert.InsertedSequence && r.Text == text);
        second.TurnPrompts.ShouldContain(r => r.Sequence == insert.InsertedSequence && r.Text == text);
        second.RawPrompts.Count.ShouldBe(401);
        ShouldBeStrictlyIncreasing(second.RawPrompts);
    }

    [Test]
    public async Task C1969s2c_concurrent_loads_never_return_a_torn_or_stale_list()
    {
        const int seededCount = 200;
        const int appendCount = 40;
        var seeded = await SeedPromptsAsync(seededCount);
        var firstAppended = seededCount + Assistants + 1L;
        var cache = NewCache();
        var committed = 0;

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < appendCount; i++)
            {
                var sequence = firstAppended + i;
                await AppendPromptAsync(
                    seeded.SessionId, PromptText(sequence, 0), seeded.DispatchedAt.AddMinutes(5), sequence);
                Volatile.Write(ref committed, i + 1);
            }
        });

        var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            await using var db = CreateContext();
            for (var load = 0; load < 20; load++)
            {
                var k = Volatile.Read(ref committed);
                var span = await LoadAsync(db, seeded, cache);
                ShouldBeStrictlyIncreasing(span.RawPrompts);
                var sequences = span.RawPrompts.Select(r => r.Sequence).ToHashSet();
                for (var s = 1L; s <= seededCount; s++)
                    sequences.ShouldContain(s);
                for (var i = 0; i < k; i++)
                    sequences.ShouldContain(firstAppended + i);
                foreach (var row in span.RawPrompts)
                    row.Text.ShouldBe(PromptText(row.Sequence, 0));
            }
        })).ToList();

        await Task.WhenAll(readers.Append(writer));
        Volatile.Read(ref committed).ShouldBe(appendCount);

        await using var final = CreateContext();
        var last = await LoadAsync(final, seeded, cache);
        last.RawPrompts.Count.ShouldBe(seededCount + appendCount);
        ShouldBeStrictlyIncreasing(last.RawPrompts);
    }

    [Test]
    [Arguments("entry-ceiling")]
    [Arguments("total-bound")]
    [Arguments("entry-cap")]
    [Arguments("idle")]
    public async Task C1969s2_eviction_returns_the_full_list(string bound)
    {
        const int n = 40;
        var now = 1_000_000L;
        var a = await SeedPromptsAsync(n, textLength: 100);
        var cache = bound switch
        {
            "entry-ceiling" => NewCache(maxEntryChars: 1_000),
            "total-bound" => NewCache(maxTotalChars: 6_000),
            "entry-cap" => NewCache(maxEntries: 1),
            "idle" => NewCache(nowMs: () => now),
            _ => throw new ArgumentOutOfRangeException(nameof(bound), bound, null),
        };
        var counter = new PromptTextReadCounter();
        await using var db = CreateContext(counter);

        switch (bound)
        {
            case "entry-ceiling":
                await LoadAsync(db, a, cache);
                break;
            case "total-bound":
            case "entry-cap":
                var b = await SeedPromptsAsync(n, textLength: 100);
                await LoadAsync(db, a, cache);
                await LoadAsync(db, b, cache);
                break;
            case "idle":
                await LoadAsync(db, a, cache);
                now += 121_000;
                break;
        }

        counter.Reset();
        var last = await LoadAsync(db, a, cache);
        counter.Rows.ShouldBe(n);
        ShouldBeSeeded(last.RawPrompts, a.Prompts);

        var (entries, chars) = cache.Snapshot();
        switch (bound)
        {
            case "entry-ceiling":
                entries.ShouldBe(0);
                chars.ShouldBe(0);
                break;
            case "total-bound":
                chars.ShouldBeLessThanOrEqualTo(6_000);
                break;
            case "entry-cap":
                entries.ShouldBeLessThanOrEqualTo(1);
                break;
            case "idle":
                entries.ShouldBeLessThanOrEqualTo(1);
                break;
        }
    }

    private const int Assistants = 2000;

    private sealed record SeededPrompts(
        Guid SessionId, DateTime DispatchedAt, IReadOnlyList<TranscriptPromptSpan.PromptRow> Prompts);

    private static TranscriptPromptSpan.PromptCache NewCache(
        int maxEntries = 256,
        long maxEntryChars = 4_000_000,
        long maxTotalChars = 32_000_000,
        Func<long>? nowMs = null) =>
        new(maxEntries, maxEntryChars, maxTotalChars, TimeSpan.FromSeconds(120),
            nowMs ?? (() => Environment.TickCount64));

    private static Task<TranscriptPromptSpan.Result> LoadAsync(
        AppDbContext db, SeededPrompts seeded, TranscriptPromptSpan.PromptCache cache) =>
        TranscriptPromptSpan.LoadAsync(db, seeded.SessionId, seeded.DispatchedAt, cache, CancellationToken.None);

    private static string PromptText(long sequence, int length)
    {
        var text = $"c1969 prompt {sequence:D6} ";
        return length <= text.Length ? text : text + new string('p', length - text.Length);
    }

    /// <summary>
    /// <paramref name="n"/> UserPrompt rows at sequences 1..n (distinct text and ids, timestamps
    /// after dispatch), then 2,000 AssistantText rows the prompt projection must not return.
    /// One EF batch.
    /// </summary>
    private static async Task<SeededPrompts> SeedPromptsAsync(int n, int textLength = 0)
    {
        var (sessionId, dispatchedAt) = await SeedSessionAsync();
        var prompts = new List<TranscriptPromptSpan.PromptRow>(n);
        await using var db = CreateContext();
        for (var i = 1L; i <= n; i++)
        {
            var at = dispatchedAt.AddMilliseconds(i);
            var text = PromptText(i, textLength);
            prompts.Add(new TranscriptPromptSpan.PromptRow(i, text, at, TranscriptKinds.UserPrompt));
            db.TranscriptEntries.Add(NewEntry(sessionId, i, TranscriptKinds.UserPrompt, text, at));
        }

        for (var j = 1L; j <= Assistants; j++)
        {
            db.TranscriptEntries.Add(NewEntry(
                sessionId, n + j, TranscriptKinds.AssistantText, "c1969 assistant " + j,
                dispatchedAt.AddMilliseconds(n + j)));
        }

        await db.SaveChangesAsync();
        return new SeededPrompts(sessionId, dispatchedAt, prompts);
    }

    /// <summary>One UserPrompt through its own context, at the stored max + 1 unless pinned.</summary>
    private static async Task<long> AppendPromptAsync(
        Guid sessionId, string text, DateTime timestamp, long? sequence = null)
    {
        await using var db = CreateContext();
        var at = sequence ?? (await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
        db.TranscriptEntries.Add(NewEntry(sessionId, at, TranscriptKinds.UserPrompt, text, timestamp));
        await db.SaveChangesAsync();
        return at;
    }

    private static void ShouldBeSeeded(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> actual,
        IReadOnlyList<TranscriptPromptSpan.PromptRow> seeded)
    {
        actual.Count.ShouldBe(seeded.Count);
        for (var i = 0; i < seeded.Count; i++)
        {
            actual[i].Sequence.ShouldBe(seeded[i].Sequence);
            actual[i].Text.ShouldBe(seeded[i].Text);
            actual[i].Kind.ShouldBe(seeded[i].Kind);
        }
    }

    private static void ShouldBeStrictlyIncreasing(IReadOnlyList<TranscriptPromptSpan.PromptRow> rows)
    {
        for (var i = 1; i < rows.Count; i++)
            rows[i].Sequence.ShouldBeGreaterThan(rows[i - 1].Sequence);
    }

    private static bool IsPromptTextRead(string sql) =>
        sql.Contains("TranscriptEntries", StringComparison.Ordinal)
        && sql.Contains("\"Text\"", StringComparison.Ordinal)
        && sql.Contains("\"Timestamp\"", StringComparison.Ordinal);

    /// <summary>Reader rows of the prompt projection: the rows that carry prompt text.</summary>
    private sealed class PromptTextReadCounter : DbCommandInterceptor
    {
        private int _rows;

        public int Rows => Volatile.Read(ref _rows);

        public void Reset() => Volatile.Write(ref _rows, 0);

        public override InterceptionResult DataReaderDisposing(
            DbCommand command, DataReaderDisposingEventData eventData, InterceptionResult result)
        {
            if (IsPromptTextRead(command.CommandText))
                Interlocked.Add(ref _rows, eventData.ReadCount);
            return result;
        }
    }

    /// <summary>
    /// W-7: when the FIRST prompt-text reader closes, commit one UserPrompt at the stored
    /// max + 1 through a second context and connection, then disarm. Under READ COMMITTED that
    /// row is outside the projection's snapshot and inside any later statement's.
    /// </summary>
    private sealed class InsertAfterPromptReadInterceptor(Guid sessionId, string text, DateTime timestamp)
        : DbCommandInterceptor
    {
        private int _armed = 1;

        public int Inserted { get; private set; }

        public long InsertedSequence { get; private set; }

        public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
            DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
        {
            if (IsPromptTextRead(command.CommandText) && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                InsertedSequence = await AppendPromptAsync(sessionId, text, timestamp);
                Inserted++;
            }

            return result;
        }

        public override InterceptionResult DataReaderClosing(
            DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
        {
            if (IsPromptTextRead(command.CommandText) && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                InsertedSequence = AppendPromptAsync(sessionId, text, timestamp).GetAwaiter().GetResult();
                Inserted++;
            }

            return result;
        }
    }

    private static async Task MarkKindAsync(Guid sessionId, AgentKind kind)
    {
        await using var db = CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.AgentKind = kind;
        await db.SaveChangesAsync();
    }

    private static async Task<(Guid SessionId, DateTime DispatchedAt)> SeedSessionAsync(
        DateTime? dispatchedAt = null)
    {
        var sessionId = Guid.NewGuid();
        var at = dispatchedAt ?? DateTime.UtcNow.AddMinutes(-11);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = at,
            StartedAt = at,
            LastSeenAt = at,
        });
        await db.SaveChangesAsync();
        return (sessionId, at);
    }

    private static async Task SeedEntryAsync(
        Guid sessionId, string kind, string? text, long sequence, DateTime? timestamp)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(NewEntry(sessionId, sequence, kind, text, timestamp));
        await db.SaveChangesAsync();
    }

    private static TranscriptEntry NewEntry(
        Guid sessionId, long sequence, string kind, string? text, DateTime? timestamp) =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = $"span-{Guid.NewGuid():N}",
            Role = kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt
                ? "user"
                : "assistant",
            Text = text,
            Timestamp = timestamp,
            CreatedAt = timestamp ?? DateTime.UtcNow,
        };

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static AppDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(TestDbFixture.CreateDbContextOptions(TestDbFixture.ConnectionString, interceptors));
}
