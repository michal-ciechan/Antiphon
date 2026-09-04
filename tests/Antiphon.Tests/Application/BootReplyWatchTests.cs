using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0312 S1 / CARD-0353 S1 — the shared boot-reply primitive, tested on its own because THREE
/// callers act on it: the task-scoped deadline arm, the session-scoped sweep, and the check
/// digest. A disagreement between them would have no single place to fix, which is the same
/// reason <see cref="TaskDeadlinePolicy"/> is shared.
///
/// <para>Every assertion is scoped to the session this test seeded (the shared-Postgres rule).</para>
/// </summary>
[Category("Integration")]
public class BootReplyWatchTests
{
    // ---- the pure evaluator ----------------------------------------------------------------------

    [Test]
    public void an_unarmed_watch_is_disarmed_whatever_the_rows_say()
    {
        BootReplyWatch.Evaluate(null, DateTime.UtcNow, DateTime.UtcNow, []).ShouldBe(
            BootReplyWatch.Status.Disarmed);
        BootReplyWatch.Evaluate(5, null, DateTime.UtcNow, []).ShouldBe(
            BootReplyWatch.Status.Disarmed);
    }

    [Test]
    [Arguments(TranscriptKinds.AssistantText)]
    [Arguments(TranscriptKinds.Thinking)]
    [Arguments(TranscriptKinds.ToolCall)]
    [Arguments(TranscriptKinds.ToolResult)]
    [Arguments(TranscriptKinds.TurnEnd)]
    public void any_model_produced_row_past_the_prompt_answers_the_watch(string kind)
    {
        var now = DateTime.UtcNow;
        BootReplyWatch.Evaluate(10, now.AddMinutes(-1), now, [new BootReplyWatch.Row(11, kind)])
            .ShouldBe(BootReplyWatch.Status.Answered, "even past the deadline, an answer is an answer");
    }

    [Test]
    [Arguments(TranscriptKinds.TurnTitle)]
    [Arguments(TranscriptKinds.QueuedUserPrompt)]
    [Arguments(TranscriptKinds.UserPrompt)]
    [Arguments(TranscriptKinds.QueueEnqueue)]
    [Arguments(TranscriptKinds.QueueDequeue)]
    [Arguments(TranscriptKinds.QueueRemove)]
    [Arguments(TranscriptKinds.SessionRestartBoundary)]
    public void rows_that_are_not_the_model_answering_never_close_the_watch(string kind)
    {
        // TurnTitle is the one that matters most: measured 2026-09-04 over the live corpus, 9
        // sessions carry a TurnTitle BEFORE their first model row, and CARD-0353 records a Grok
        // session whose summary title was the POINTER TEXT of its own unanswered prompt. Counting
        // it would mask exactly the stall this watches for.
        var now = DateTime.UtcNow;
        BootReplyWatch.Evaluate(10, now.AddMinutes(-1), now, [new BootReplyWatch.Row(11, kind)])
            .ShouldBe(BootReplyWatch.Status.Overdue);
    }

    [Test]
    public void an_inherited_row_at_or_below_the_boot_prompt_cannot_answer_it()
    {
        // The CARD-0077 trap: a reused warm session carries the PREVIOUS task's assistant rows.
        var now = DateTime.UtcNow;
        BootReplyWatch.Evaluate(10, now.AddMinutes(-1), now,
            [
                new BootReplyWatch.Row(9, TranscriptKinds.AssistantText),
                new BootReplyWatch.Row(10, TranscriptKinds.TurnEnd),
            ])
            .ShouldBe(BootReplyWatch.Status.Overdue, "only rows strictly past the prompt may answer");
    }

    [Test]
    public void inside_the_deadline_a_silent_session_is_waiting_not_overdue()
    {
        var now = DateTime.UtcNow;
        BootReplyWatch.Evaluate(10, now.AddSeconds(1), now, []).ShouldBe(BootReplyWatch.Status.Waiting);
    }

    // ---- the database predicate ------------------------------------------------------------------

    [Test]
    public async Task a_prompt_with_nothing_after_it_is_a_boot_turn()
    {
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 5));

        var boot = await scenario.LoadBootTurnAsync(clockMinutesAgo: 10);

        boot.ShouldNotBeNull();
        boot.PromptCount.ShouldBe(1);
    }

    [Test]
    public async Task one_model_row_since_the_clock_ends_the_boot_turn()
    {
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 5),
            (TranscriptKinds.Thinking, "…", 4));

        (await scenario.LoadBootTurnAsync(clockMinutesAgo: 10)).ShouldBeNull();
        (await scenario.HasModelReplySinceAsync(clockMinutesAgo: 10))
            .ShouldBeTrue("the cheap EXISTS must agree with LoadBootTurnAsync");
    }

    [Test]
    public async Task a_prompt_alone_is_not_a_model_reply_for_the_cheap_exists()
    {
        // D2: the sweep's self-heal must not load every row+text just to learn the session is
        // still silent. The EXISTS is false here; LoadBootTurnAsync then reads only prompt rows.
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 5));

        (await scenario.HasModelReplySinceAsync(clockMinutesAgo: 10)).ShouldBeFalse();
        (await scenario.LoadBootTurnAsync(clockMinutesAgo: 10)).ShouldNotBeNull();
    }

    [Test]
    public async Task rows_before_the_launch_clock_are_invisible()
    {
        // A warm-pool session's inherited history. Nothing is ever failed for a stall it inherited.
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the previous task", 60),
            (TranscriptKinds.AssistantText, "the previous answer", 59),
            (TranscriptKinds.TurnEnd, null, 58),
            (TranscriptKinds.UserPrompt, "this task's brief", 5));

        var boot = await scenario.LoadBootTurnAsync(clockMinutesAgo: 10);

        boot.ShouldNotBeNull("the inherited answer predates the clock and cannot close this boot turn");
        boot.PromptCount.ShouldBe(1);
    }

    [Test]
    public async Task a_refinement_typed_into_a_still_silent_session_restarts_the_wait()
    {
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 30),
            (TranscriptKinds.UserPrompt, "actually, also do this", 5));

        var boot = await scenario.LoadBootTurnAsync(clockMinutesAgo: 40);

        boot.ShouldNotBeNull();
        boot.PromptCount.ShouldBe(2);
        (DateTime.UtcNow - boot.PromptAt).ShouldBeLessThan(
            TimeSpan.FromMinutes(10), "the clock runs from the LATEST prompt — it is a new request");
    }

    [Test]
    public async Task housekeeping_prompt_records_are_neither_evidence_nor_disqualifiers()
    {
        // CARD-0041: the four records a manual /compact writes are not prompts anybody typed.
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 30),
            (TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.LocalCommandStdoutPrefix}Compacted</local-command-stdout>", 5));

        var boot = await scenario.LoadBootTurnAsync(clockMinutesAgo: 40);

        boot.ShouldNotBeNull();
        boot.PromptCount.ShouldBe(1, "the housekeeping record is skipped, not counted as a refinement");
        (DateTime.UtcNow - boot.PromptAt).ShouldBeGreaterThan(TimeSpan.FromMinutes(25));
    }

    [Test]
    public async Task a_session_that_has_written_nothing_is_not_a_boot_turn()
    {
        // "No prompt at all" is FailNeverStartedAsync's question, not this one.
        await using var scenario = new Scenario();

        (await scenario.LoadBootTurnAsync(clockMinutesAgo: 10)).ShouldBeNull();
    }

    // ---- arming ----------------------------------------------------------------------------------

    [Test]
    public async Task arming_stamps_the_row_from_the_prompt_clock_not_from_now()
    {
        // One clock. An arm that happened late (a sweep re-deriving after a restart) must not
        // silently extend the wait past what the task-scoped arm would allow.
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 30));

        var due = await scenario.ArmAsync(deadlineMinutes: 8);

        due.ShouldNotBeNull();
        due.Value.ShouldBeLessThan(DateTime.UtcNow, "prompt + 8m is already past on a 30-minute-old prompt");
        var row = await scenario.LoadSessionAsync();
        row.BootPromptSequence.ShouldNotBeNull();
        row.BootReplyDueAt.ShouldBe(due);
    }

    [Test]
    public async Task arming_a_session_that_already_answered_clears_the_watch_instead()
    {
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 30));
        await scenario.ArmAsync(deadlineMinutes: 8);
        await scenario.SeedEntriesAsync((TranscriptKinds.AssistantText, "here you go", 1));

        (await scenario.ArmAsync(deadlineMinutes: 8)).ShouldBeNull();

        var row = await scenario.LoadSessionAsync();
        row.BootPromptSequence.ShouldBeNull();
        row.BootReplyDueAt.ShouldBeNull();
    }

    [Test]
    public async Task a_zero_deadline_disarms_rather_than_arming_at_zero()
    {
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 30));

        (await scenario.ArmAsync(deadlineMinutes: 0)).ShouldBeNull();

        (await scenario.LoadSessionAsync()).BootReplyDueAt.ShouldBeNull();
    }

    [Test]
    public async Task the_watch_survives_being_rebuilt_from_the_row_alone()
    {
        // CARD-0312 P5, and the CARD-0331 mistake it avoids: the expectation is stamped on the
        // SESSION ROW, so a service that is dropped and rebuilt re-derives it and still fires.
        await using var scenario = new Scenario();
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 30));
        await scenario.ArmAsync(deadlineMinutes: 8);

        await using var rebuilt = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = await rebuilt.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        var status = await BootReplyWatch.EvaluateSessionAsync(
            rebuilt, session, DateTime.UtcNow, CancellationToken.None);

        status.ShouldBe(BootReplyWatch.Status.Overdue);
    }

    // ---- the settings the whole mechanism hangs on -----------------------------------------------

    [Test]
    public void the_boot_deadline_is_one_setting_and_it_orders_correctly_against_the_others()
    {
        // CARD-0312 N6 and CARD-0353's shared-primitive rule in one assertion. The boot arm's
        // whole value is being TIGHTER than the delivery watchdog and the general model wait; if
        // that ordering inverts, the mechanism is dead code that never fires first.
        var settings = new DelegationSettings();

        settings.BootModelWaitDeadlineMinutes.ShouldBe(8, customMessage:
            "measured 2026-09-04 over 660 sessions since 2026-08-20: p50 4.7s, p90 9.7s, p99 30.4s, "
            + "healthy max 170.4s (the one larger figure is the 2026-09-03 xAI incident this "
            + "deadline exists to catch). 8 minutes is ~2.8x the healthy maximum.");
        settings.BootModelWaitDeadlineMinutes.ShouldBeLessThan(settings.DeliveryFailTimeoutMinutes);
        settings.DeliveryFailTimeoutMinutes.ShouldBeLessThan(settings.ModelWaitDeadlineMinutes);
        settings.BootStallRepeatHoldMinutes.ShouldBe(30);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private sealed class Scenario : IAsyncDisposable
    {
        public Guid SessionId { get; } = Guid.NewGuid();

        private long _seq;
        private bool _seeded;

        public async Task SeedEntriesAsync(params (string Kind, string? Text, int MinutesAgo)[] entries)
        {
            await using var db = CreateContext();
            await EnsureSessionAsync(db);
            foreach (var (kind, text, minutesAgo) in entries)
            {
                var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = SessionId,
                    Sequence = ++_seq,
                    Kind = kind,
                    Uuid = $"bootwatch-{Guid.NewGuid():N}",
                    Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                    Text = text,
                    Timestamp = at,
                    CreatedAt = at,
                });
            }

            await db.SaveChangesAsync();
        }

        public async Task<BootReplyWatch.BootTurn?> LoadBootTurnAsync(int clockMinutesAgo)
        {
            await using var db = CreateContext();
            await EnsureSessionAsync(db);
            await db.SaveChangesAsync();
            return await BootReplyWatch.LoadBootTurnAsync(
                db, SessionId, DateTime.UtcNow.AddMinutes(-clockMinutesAgo), CancellationToken.None);
        }

        public async Task<bool> HasModelReplySinceAsync(int clockMinutesAgo)
        {
            await using var db = CreateContext();
            await EnsureSessionAsync(db);
            await db.SaveChangesAsync();
            return await BootReplyWatch.HasModelReplySinceAsync(
                db, SessionId, DateTime.UtcNow.AddMinutes(-clockMinutesAgo), CancellationToken.None);
        }

        public async Task<DateTime?> ArmAsync(int deadlineMinutes)
        {
            await using var db = CreateContext();
            var due = await BootReplyWatch.TryArmAsync(
                db, SessionId, deadlineMinutes, CancellationToken.None);
            await db.SaveChangesAsync();
            return due;
        }

        public async Task<AgentSession> LoadSessionAsync()
        {
            await using var db = CreateContext();
            return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
        }

        private async Task EnsureSessionAsync(AppDbContext db)
        {
            if (_seeded || await db.AgentSessions.AnyAsync(s => s.Id == SessionId))
            {
                _seeded = true;
                return;
            }

            var started = DateTime.UtcNow.AddHours(-4);
            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "bootwatch-test",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = started,
                StartedAt = started,
                LastSeenAt = DateTime.UtcNow,
            });
            _seeded = true;
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == SessionId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }
}
