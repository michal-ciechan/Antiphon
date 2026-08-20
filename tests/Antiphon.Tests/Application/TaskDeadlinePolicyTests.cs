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
            RolePolicy = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = new DelegationSettings.RolePolicyEntry { TimeoutMinutes = 0 },
            },
        });

        verdict.ShouldBeNull("0 is the documented off switch on every one of the three limits");
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
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync((kind, "mid-turn", 30));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.ModelWait);
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(20));
        verdict.Breached.ShouldBeTrue("30 minutes waiting on the model is past the 20-minute deadline");
        verdict.Summary.ShouldContain("waiting on the model", customMessage: "the phase must be named");
    }

    [Test]
    public async Task a_running_local_tool_takes_the_much_longer_local_execution_deadline()
    {
        // The whole reason a flat timeout is wrong: a build or a test suite is legitimately long
        // (measured max 5 311 s), so the SAME 30 minutes that fails a model wait must not fail this.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync((TranscriptKinds.ToolCall, "dotnet build", 30));

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.LocalExecution);
        verdict.Limit.ShouldBe(TimeSpan.FromMinutes(90));
        verdict.Breached.ShouldBeFalse("30 minutes into a build is a build, not a stall");
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
    public async Task an_unrecognised_last_kind_gets_the_ceiling_and_nothing_tighter()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 60);
        await scenario.SeedEntriesAsync((TranscriptKinds.TurnTitle, "Doing the thing", 30));

        (await scenario.EvaluateAsync(task)).ShouldBeNull(
            "60 of 240 minutes is nowhere near the ceiling, and no phase applies");
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
