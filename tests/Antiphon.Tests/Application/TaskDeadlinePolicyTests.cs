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
/// CARD-0020 S2/S3 — the shared deadline policy, tested on its own because both callers act on it:
/// the dispatcher's sweep FAILS a task on <c>Breached</c> and the attention projection lists one on
/// <c>WorthSurfacing</c>. A disagreement between the two would have no single place to fix.
///
/// <para>The measurement that shaped these tests is in
/// <c>docs/superpowers/plans/2026-08-20-card-0020-stall-backstop-plan.md</c> §3.1: <c>UserPrompt</c>
/// is not one phase, and every one of the six longest <c>UserPrompt</c>-headed gaps in ten days —
/// up to 45.5 HOURS — was a <c>/compact</c> housekeeping record. So the three housekeeping negatives
/// below are the point of the design, not edge cases: a deadline keyed on the raw Kind would have
/// failed three healthy sessions a day and a half early.</para>
///
/// <para>Every assertion is scoped to the session this test seeded (the shared-Postgres rule), and
/// the policy only ever reads rows for one session id, so no test here needs the sweep suites'
/// <c>NotInParallel</c>.</para>
/// </summary>
[Category("Integration")]
public class TaskDeadlinePolicyTests
{
    // ---- the ceiling -----------------------------------------------------------------------------

    [Test]
    public async Task a_task_nowhere_near_a_limit_costs_one_comparison_and_returns_nothing()
    {
        // The cheap gate: 5 minutes in, no clock is within its preview fraction, so the policy must
        // not even look at the transcript. This is what keeps it off a 5 s tick's hot path.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 5);

        (await scenario.EvaluateAsync(task)).ShouldBeNull();
    }

    [Test]
    public async Task a_task_past_the_role_ceiling_is_breached_on_the_wall_clock()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 300);
        // Idle: a finished turn. Nothing phase-aware applies, so the ceiling is the only clock.
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 290),
            (TranscriptKinds.TurnEnd, null, 280));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.Ceiling);
        verdict.Breached.ShouldBeTrue();
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(240), "role Code ships the 240-minute ceiling");
        verdict.Summary.ShouldContain("240-minute ceiling for role Code", customMessage:
            "the reason must name the clock that fired without opening the session");
    }

    [Test]
    public async Task an_unarmed_ceiling_and_unarmed_phases_evaluate_nothing_at_all()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 5_000);
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 4_900));

        var verdict = await scenario.EvaluateAsync(task, new DelegationSettings
        {
            DefaultTimeoutMinutes = 0,
            ModelWaitDeadlineMinutes = 0,
            LocalExecutionDeadlineMinutes = 0,
            // CARD-0353 S1 added a fourth limit, so "all off" now needs four zeros.
            BootModelWaitDeadlineMinutes = 0,
            RolePolicy = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = new DelegationSettings.RolePolicyEntry { TimeoutMinutes = 0 },
            },
        });

        verdict.ShouldBeNull("0 is the documented off switch on every one of the four limits");
    }

    [Test]
    public async Task a_role_with_no_policy_entry_falls_back_to_the_default_ceiling()
    {
        // Custom and Check ship without a RolePolicy entry. Unconfigured is not unwatched.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 300, role: AgentTaskRole.Custom);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 290),
            (TranscriptKinds.TurnEnd, null, 280));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(240), "DefaultTimeoutMinutes covers an unlisted role");
        verdict.Breached.ShouldBeTrue();
    }

    // ---- the phase arm ---------------------------------------------------------------------------

    [Test]
    [Arguments(TranscriptKinds.UserPrompt)]
    [Arguments(TranscriptKinds.ToolResult)]
    [Arguments(TranscriptKinds.Thinking)]
    [Arguments(TranscriptKinds.AssistantText)]
    public async Task all_four_mid_turn_kinds_that_owe_a_token_take_the_model_wait_deadline(string kind)
    {
        // One phase, four kinds: after a prompt or a tool result it is the first token that is late,
        // and mid-stream it is the next chunk. Measured maxima 217s / 1478s / 33s / 146s.
        //
        // CARD-0353 S1 note: the session must have PRODUCED something for this to be the general
        // arm at all — a lone prompt with nothing after it is a BOOT turn and takes the tighter
        // clock (the test below). The earlier assistant row here is what makes this a mid-TASK
        // model wait rather than a first token that never came.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.AssistantText, "work already done", 40),
            (kind, "mid-turn", 30));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.ModelWait);
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(20));
        verdict.Breached.ShouldBeTrue("30 minutes waiting on the model is past the 20-minute deadline");
        verdict.Summary.ShouldContain("waiting on the model", customMessage: "the phase must be named");
    }

    // ---- the boot arm (CARD-0353 S1) --------------------------------------------------------------

    [Test]
    public async Task a_prompt_with_nothing_after_it_takes_the_tighter_boot_deadline()
    {
        // The whole point: the general 20-minute arm is conservative because a mid-task session
        // may hold real work (CARD-0056), and it fails without killing. A boot turn has produced
        // nothing, so there is nothing to protect and no reason to wait 20 minutes.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 30));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.BootModelWait);
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(8));
        verdict.Breached.ShouldBeTrue();
        verdict.Summary.ShouldContain("FIRST token", customMessage:
            "the reason must say which token is late");
        verdict.Summary.ShouldContain("boot turn", customMessage:
            "and that nothing it did would be lost — that is what licenses the kill");
    }

    [Test]
    [Arguments(TranscriptKinds.Thinking)]
    [Arguments(TranscriptKinds.AssistantText)]
    [Arguments(TranscriptKinds.ToolCall)]
    [Arguments(TranscriptKinds.TurnEnd)]
    public async Task one_model_row_after_the_prompt_drops_back_to_the_general_arm(string produced)
    {
        // The boot arm can only ever TIGHTEN, so any evidence the model spoke ends it. TurnEnd is
        // in this list deliberately: measured 2026-09-04, two Codex sessions answered their boot
        // prompt with an API-error TurnEnd in ~1s and then sat in CARD-0072's retry ladder for 43
        // minutes; treating that as silence would have killed sessions the ladder was reviving.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (produced, "something", 40),
            (TranscriptKinds.UserPrompt, "and now this", 30));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.ModelWait);
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Test]
    public async Task inherited_rows_before_the_launch_resume_do_not_end_the_boot_turn()
    {
        // CARD-0340 S2: a resumed launch's boot turn starts at the resume, so the rows the session
        // wrote before it belong to the launch that was interrupted.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 120);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the first attempt", 110),
            (TranscriptKinds.AssistantText, "the first attempt's answer", 100),
            (TranscriptKinds.UserPrompt, "the brief, retyped after the resume", 30));
        await scenario.SetLaunchResumedAsync(minutesAgo: 40);

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.BootModelWait);
    }

    [Test]
    public async Task a_disarmed_boot_deadline_falls_back_to_the_general_model_wait()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the brief", 30));

        var verdict = await scenario.EvaluateAsync(
            task, new DelegationSettings { BootModelWaitDeadlineMinutes = 0 });

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.ModelWait, customMessage:
            "<= 0 disables the boot arm and leaves the general one exactly as it was");
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Test]
    public async Task a_compaction_record_after_the_prompt_is_still_a_boot_turn()
    {
        // A housekeeping prompt is neither evidence nor a disqualifier — CARD-0041's rule, shared
        // with TranscriptPromptSpan so the two cannot disagree.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 30),
            (TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.TaskNotificationPrefix}a background agent came back", 29));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.BootModelWait);
    }

    [Test]
    public async Task a_running_local_tool_takes_the_much_longer_local_execution_deadline()
    {
        // The whole reason a flat timeout is wrong: a build or a test suite is legitimately long
        // (measured max 5 311 s). 75 minutes of it is worth SURFACING and nothing more — the same
        // 75 minutes with a model-wait tail would have been failed nearly an hour earlier.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 80);
        await scenario.SeedEntriesAsync((TranscriptKinds.ToolCall, "dotnet build", 75));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.LocalExecution);
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(90));
        verdict.Breached.ShouldBeFalse("75 minutes into a build is a build, not a stall");
        verdict.WorthSurfacing.ShouldBeTrue();
        verdict.Summary.ShouldContain("waiting on a local tool");
    }

    [Test]
    public async Task a_finished_turn_gets_the_ceiling_and_nothing_tighter()
    {
        // TurnEnd is deliberately absent from ClassifyPhase: the idle tail is measured in DAYS and
        // is PastExpectedIdle's business. The two conditions partition the open tasks by design.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 200);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 190),
            (TranscriptKinds.TurnEnd, null, 180));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.Ceiling);
        verdict.Breached.ShouldBeFalse("200 of 240 minutes is worth surfacing, not failing");
        verdict.WorthSurfacing.ShouldBeTrue();
    }

    [Test]
    public void an_unclassified_tail_arms_no_phase_deadline_at_all()
    {
        // ClassifyPhase directly: TurnEnd, the boundaries and anything added to TranscriptKinds in
        // future fall through to a ZERO limit, which the caller reads as "no phase applies". A new
        // kind must be classified deliberately rather than inherit the tightest clock we have.
        var modelWait = TimeSpan.FromMinutes(20);
        var localExecution = TimeSpan.FromMinutes(90);

        foreach (var kind in new[]
        {
            TranscriptKinds.TurnEnd, TranscriptKinds.TurnTitle, TranscriptKinds.CompactBoundary,
            TranscriptKinds.SessionRestartBoundary, "SomethingAddedNextYear",
        })
        {
            var (phase, limit) = TaskDeadlinePolicy.ClassifyPhase(kind, modelWait, localExecution);
            phase.ShouldBe(TaskDeadlinePolicy.DeadlineKind.Ceiling, $"{kind} arms no phase clock");
            limit.ShouldBe(TimeSpan.Zero, $"{kind} arms no phase clock");
        }
    }

    // ---- the three housekeeping negatives (plan section 3.1) -------------------------------------

    [Test]
    public async Task a_local_command_stdout_tail_is_not_a_stalled_model_call()
    {
        // THE measured defect. All six UserPrompt-headed gaps over 10 minutes in the live corpus —
        // up to 163 650 s, 45.5 hours — are this record, left behind by a /compact. A deadline keyed
        // on the raw Kind would have failed those sessions a day and a half early.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 55),
            (TranscriptKinds.TurnEnd, null, 50),
            (TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.LocalCommandStdoutPrefix}Compacted (ctrl+o to see full summary)</local-command-stdout>",
                45));

        (await scenario.EvaluateAsync(task)).ShouldBeNull(
            "IsWorkingAsync already excludes it, so the phase arm never opens");
    }

    [Test]
    public async Task an_interrupt_marker_is_the_turns_end_not_a_stalled_model_call()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 55),
            (TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.InterruptedPromptPrefix} by user]", 45));

        (await scenario.EvaluateAsync(task)).ShouldBeNull(
            "an aborted turn writes no TurnEnd; the marker IS the end (CARD-0041)");
    }

    [Test]
    public async Task a_compaction_continuation_prompt_is_not_a_stalled_model_call()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 55),
            (TranscriptKinds.TurnEnd, null, 50),
            (TranscriptKinds.CompactBoundary,
                $"Context compacted {TranscriptKinds.ManualCompactMarker}", 46),
            (TranscriptKinds.UserPrompt,
                $"{TranscriptKinds.CompactionContinuationPromptPrefix} that ran out of context. The summary…",
                45));

        (await scenario.EvaluateAsync(task)).ShouldBeNull(
            "nobody typed it and no TurnEnd is coming for it (CARD-0041)");
    }

    // ---- the warm-pool cap -----------------------------------------------------------------------

    [Test]
    public async Task a_warm_pool_session_is_never_charged_for_a_stall_it_inherited()
    {
        // A reused delegate's session carries the PREVIOUS task's tail. Without the cap the phase
        // clock reads 200 minutes on a task that has existed for 18, and the new task is failed for
        // time it did not own before it has had a chance to answer.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 18);
        await scenario.SeedEntriesAsync((TranscriptKinds.UserPrompt, "the PREVIOUS task's brief", 200));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.ModelWait);
        verdict.Breached.ShouldBeFalse(
            "the inherited entry is 200 minutes old and the task is 18; only the 18 are its own");
        verdict.Elapsed.ShouldBeLessThan(TimeSpan.FromMinutes(20));
        verdict.LastEntryAge.ShouldNotBeNull();
        verdict.LastEntryAge.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(100),
            "the row still REPORTS the real age — it just is not charged for it");
    }

    // ---- section 3.3: arrival order is not time order --------------------------------------------

    [Test]
    public async Task the_last_entry_is_the_newest_by_TIME_not_by_sequence()
    {
        // Stored sequences are ARRIVAL-ordered: a catch-up sync rebases entries it missed PAST the
        // session's max, so the highest Sequence is not always the newest record. Measured over the
        // live corpus 2026-08-20: 195 of 71 801 adjacent pairs (0.27%) run backwards in time, 72 of
        // them by more than a minute. Here the ToolCall arrived last and so holds the top sequence,
        // but its own timestamp is 40 minutes old while the real tail is 2 minutes old.
        await using var scenario = new Scenario();
        await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 60),
            (TranscriptKinds.AssistantText, "still going", 2),
            (TranscriptKinds.ToolCall, null, 40));

        var last = await scenario.LastEntryAsync();

        last.ShouldNotBeNull();
        last.Kind.ShouldBe(
            TranscriptKinds.AssistantText,
            "the record's own timestamp survives reordering; its stored sequence does not");
    }

    [Test]
    public async Task a_backfilled_sequence_cannot_manufacture_a_stall()
    {
        // The same shape through the whole policy, with the local-execution deadline pulled down to
        // 5 minutes so that reading Sequence alone WOULD breach: a 40-minute-old ToolCall against a
        // 5-minute limit. The tie-break is the only thing standing between this healthy session and
        // a failed task, and the override direction is safe by construction — a strictly later
        // timestamp can only ever make a stall look younger, never older.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, "the brief", 60),
            (TranscriptKinds.AssistantText, "still going", 2),
            (TranscriptKinds.ToolCall, null, 40));

        var verdict = await scenario.EvaluateAsync(
            task, new DelegationSettings { LocalExecutionDeadlineMinutes = 5 });

        verdict.ShouldBeNull(
            "the real tail is two minutes old, and nothing here is within 80% of any deadline");
    }

    // ---- the numbers themselves ------------------------------------------------------------------

    [Test]
    public void the_shipped_deadlines_are_the_measured_ones_and_the_ceiling_is_not_60()
    {
        var settings = new DelegationSettings();

        // 240 = ~3x the measured p99 of 88.6 minutes. Emphatically NOT the 60 this field declared
        // for a year while being read NOWHERE: 5 of 247 successful tasks (2.0%) ran past 60 minutes
        // and the longest Succeeded task ran 2 732, so giving the dead default teeth would have
        // killed real work on the day it shipped. This is the one decision on this card that a
        // future edit is most likely to make casually, so it is pinned.
        settings.RolePolicy["Code"].TimeoutMinutes.ShouldBe(240);
        settings.RolePolicy["Debug"].TimeoutMinutes.ShouldBe(240);
        settings.DefaultTimeoutMinutes.ShouldBe(
            240, "Custom and Check have no RolePolicy entry — unconfigured is not unwatched");

        // ~3x the measured maxima: 217 s for a first token after a prompt, 1 478 s after a tool
        // result, 5 311 s for a local tool. The card's own proposal of ~60 s sits between p95 (41 s)
        // and p99 (163 s) — roughly 1 turn in 25.
        settings.ModelWaitDeadlineMinutes.ShouldBe(20);
        settings.LocalExecutionDeadlineMinutes.ShouldBe(90);

        // And the preview band the attention row uses, which is what makes Overdue a warning a human
        // can still act on rather than a report of a task that has already been failed.
        TaskDeadlinePolicy.PreviewFraction.ShouldBe(0.8);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly List<Guid> _tasks = [];
        private long _seq;

        public async Task<AgentTask> SeedTaskAsync(
            int dispatchedMinutesAgo, AgentTaskRole role = AgentTaskRole.Code)
        {
            var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
            await using var db = CreateContext();
            if (!await db.AgentSessions.AnyAsync(s => s.Id == _sessionId))
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = _sessionId,
                    DefinitionName = "deadline-test",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = Path.GetTempPath(),
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = dispatched,
                    StartedAt = dispatched,
                    LastSeenAt = DateTime.UtcNow,
                });
            }

            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "deadline policy test",
                Goal = "run past a deadline",
                Role = role,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = _sessionId,
                Status = AgentTaskStatus.Working,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            _tasks.Add(id);
            return task;
        }

        /// <param name="entries">Kind, text, and how many minutes ago the record is stamped.</param>
        public async Task SeedEntriesAsync(params (string Kind, string? Text, int MinutesAgo)[] entries)
        {
            await using var db = CreateContext();
            foreach (var (kind, text, minutesAgo) in entries)
            {
                var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = _sessionId,
                    Sequence = ++_seq,
                    Kind = kind,
                    Uuid = $"deadline-{Guid.NewGuid():N}",
                    Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                    Text = text,
                    Timestamp = at,
                    CreatedAt = at,
                    StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
                });
            }

            await db.SaveChangesAsync();
        }

        public async Task SetLaunchResumedAsync(int minutesAgo)
        {
            await using var db = CreateContext();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == _sessionId);
            session.LaunchResumedAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
            await db.SaveChangesAsync();
        }

        public async Task<TaskDeadlinePolicy.LastEntry?> LastEntryAsync()
        {
            await using var db = CreateContext();
            return await TaskDeadlinePolicy.LoadLastEntryAsync(db, _sessionId, CancellationToken.None);
        }

        public async Task<TaskDeadlinePolicy.Verdict?> EvaluateAsync(
            AgentTask task, DelegationSettings? settings = null)
        {
            await using var db = CreateContext();
            return await TaskDeadlinePolicy.EvaluateAsync(
                db, task, DateTime.UtcNow, settings ?? new DelegationSettings(), CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == _sessionId).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == _sessionId).ExecuteDeleteAsync();
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }
}
