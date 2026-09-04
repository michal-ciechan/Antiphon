using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0312 S3/S4 — the sweep that resolves the boot-reply watch, and the bounded recovery it
/// routes into.
///
/// <para>The negative controls here are not edge cases; each is a mistake this repo has already
/// paid for. A periodic liveness probe was measured and deleted TWICE (the pong probe on
/// 2026-07-23 for spending model turns on healthy idle sessions, and a TUI echo probe on
/// 2026-07-20 for false-positive-killing them), so "a slow-but-alive boot is never touched" and
/// "an unarmed session is never judged" carry the weight here.</para>
///
/// <para><c>NotInParallel</c> with no group key: the sweep is fleet-global against the shared test
/// Postgres, so it also walks every other suite's live sessions.</para>
/// </summary>
[Category("Integration")]
[NotInParallel]
public class BootReplyWatchdogTests
{
    private const int Deadline = 8;

    [Test]
    public async Task a_delivered_prompt_the_model_never_answers_raises_one_incident_naming_what_was_seen()
    {
        // P1's core. The prompt IS in the transcript — delivery is not the problem — and no
        // assistant, thinking, tool or turn-end row followed it.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        await scenario.ArmAsync();

        (await scenario.SweepAsync()).ShouldBe(1);

        await using var verify = CreateContext();
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == scenario.SessionId
                && i.Kind == AgentIncidentKind.LivenessProbeFailed);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Message.ShouldContain("Boot prompt confirmed at sequence");
        incident.Message.ShouldContain("no assistant, thinking, tool or turn-end row");
        incident.FailureReason.ShouldNotBeNull();
        incident.FailureReason.ShouldStartWith("bootSeq=");

        // The watch is spent for this episode: the recovery ladder owns it now, and re-raising the
        // same silence every tick would be an alarm, not a signal.
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        session.BootReplyDueAt.ShouldBeNull();
        (await scenario.SweepAsync()).ShouldBe(0, "a second tick must not raise the same episode again");
    }

    [Test]
    public async Task a_standing_agent_goes_through_the_existing_restart_ladder()
    {
        // S4: no new relaunch ladder. ConsecutiveFailures is what drives the EXISTING Backoff and
        // FreshAfterResumeFailures, so incrementing it is the whole integration.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        await scenario.ArmAsync();

        await scenario.SweepAsync();

        await using var verify = CreateContext();
        var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == scenario.AgentId);
        state.ConsecutiveFailures.ShouldBe(1);
        state.LivenessLatchedAt.ShouldBeNull("one failure is not a latch");
    }

    [Test]
    public async Task the_third_consecutive_failure_latches_the_mechanism_off_instead_of_restarting()
    {
        // The hard stop. A third probe-driven restart is the 2026-07 restart loop by another
        // route, so the mechanism stops and says so at Error.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true, consecutiveFailures: 2);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        await scenario.ArmAsync();

        await scenario.SweepAsync();

        await using var verify = CreateContext();
        var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == scenario.AgentId);
        state.LivenessLatchedAt.ShouldNotBeNull();
        state.ConsecutiveFailures.ShouldBe(2, "a latched mechanism stops driving the ladder");
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == scenario.SessionId && i.Kind == AgentIncidentKind.LivenessProbeFailed);
        incident.Severity.ShouldBe(AlertSeverity.Error);
        incident.Message.ShouldContain("stopped restarting");
    }

    // ---- negative controls -----------------------------------------------------------------------

    [Test]
    public async Task a_slow_but_alive_boot_that_answers_before_the_deadline_is_never_touched()
    {
        // N2. The failure mode of a too-short deadline is killing healthy sessions, which is
        // exactly why the number is measured rather than chosen.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: Deadline - 1);
        await scenario.ArmAsync();

        (await scenario.SweepAsync()).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == scenario.SessionId)).ShouldBeFalse();
        (await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId))
            .BootReplyDueAt.ShouldNotBeNull("still waiting, still armed");
    }

    [Test]
    public async Task an_answered_boot_turn_disarms_cleanly_and_raises_nothing()
    {
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        await scenario.ArmAsync();
        await scenario.SeedRowAsync(TranscriptKinds.AssistantText, minutesAgo: 1);

        (await scenario.SweepAsync()).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == scenario.SessionId)).ShouldBeFalse();
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        session.BootPromptSequence.ShouldBeNull();
        session.BootReplyDueAt.ShouldBeNull();
    }

    [Test]
    public async Task a_session_with_no_transcript_ground_truth_is_neither_armed_nor_judged()
    {
        // N3. OpenCode/Raw deliver blind, so there is nothing to judge silence against and a
        // screen-only verdict one rung up is what CARD-0055/CARD-0264 forbid.
        await using var scenario = new Scenario(AgentKind.OpenCode);
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: 30);

        (await scenario.SweepAsync()).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == scenario.SessionId)).ShouldBeFalse();
    }

    [Test]
    public async Task a_session_that_has_already_answered_on_this_launch_is_never_armed_at_all()
    {
        // N4 at the SESSION scope. A warm delegate session that answered its previous task is not
        // on a boot turn, so the session-scoped watch declines it entirely and the task-scoped arm
        // (which measures from DispatchedAt, not StartedAt) is the one that judges the new brief.
        // The sequence bound that makes THAT safe is pinned in BootReplyWatchTests — this asserts
        // the sweep never invents a watch for a session whose launch has visibly produced work.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedRowAsync(TranscriptKinds.AssistantText, minutesAgo: 200);
        await scenario.SeedRowAsync(TranscriptKinds.TurnEnd, minutesAgo: 199);
        await scenario.SeedPromptAsync(minutesAgo: 30);

        (await scenario.SweepAsync()).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId))
            .BootReplyDueAt.ShouldBeNull();
    }

    [Test]
    public async Task a_redraw_with_no_model_row_is_still_overdue()
    {
        // N7. The CARD-0055 rule, restated one rung up: screen movement is wedge evidence, not
        // reply evidence. The only rows here are queue housekeeping and a title.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        await scenario.ArmAsync();
        await scenario.SeedRowAsync(TranscriptKinds.QueueEnqueue, minutesAgo: 20);
        await scenario.SeedRowAsync(TranscriptKinds.TurnTitle, minutesAgo: 19);

        (await scenario.SweepAsync()).ShouldBe(1);
    }

    [Test]
    public async Task a_session_owned_by_an_open_delegate_task_is_left_to_the_deadline_sweep()
    {
        // ONE RECOVERY PER POPULATION. CARD-0353 S2's boot arm fails the task, kills the session,
        // retries once and tells the parent; raising here as well would be two mechanisms killing
        // the same session for the same reason.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: false);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        await scenario.ArmAsync();
        await scenario.SeedOpenTaskAsync();

        (await scenario.SweepAsync()).ShouldBe(0);

        await using var verify = CreateContext();
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == scenario.SessionId)).ShouldBeFalse();
    }

    [Test]
    public async Task a_zero_deadline_disables_the_sweep_entirely()
    {
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        await scenario.ArmAsync();

        (await scenario.SweepAsync(deadlineMinutes: 0)).ShouldBe(0);
    }

    [Test]
    public async Task an_unarmed_live_session_is_re_derived_rather_than_left_unwatched()
    {
        // P5's other half: a watch a restart lost (or a launch path that typed outside the
        // message queue) is re-derived from the same predicate on the same prompt-anchored clock.
        await using var scenario = new Scenario();
        await scenario.SeedAgentAsync(alwaysOn: true);
        await scenario.SeedPromptAsync(minutesAgo: 30);
        // Deliberately NOT armed.

        (await scenario.SweepAsync()).ShouldBe(1);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly AgentKind _kind;
        private long _seq;

        public Scenario(AgentKind kind = AgentKind.ClaudeCode)
        {
            _kind = kind;
            SessionId = Guid.NewGuid();
        }

        public Guid SessionId { get; }
        public Guid AgentId { get; private set; }

        public async Task SeedAgentAsync(bool alwaysOn, int consecutiveFailures = 0)
        {
            AgentId = Guid.NewGuid();
            var name = $"bw-{AgentId:N}"[..16];
            var now = DateTime.UtcNow;
            await using var db = CreateContext();
            db.Agents.Add(new Agent
            {
                Id = AgentId,
                Name = name,
                Slug = name,
                WorkingDirectory = Path.GetTempPath(),
                Details = "CARD-0312 boot-reply watchdog test agent.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Frontier,
                AlwaysOn = alwaysOn,
                PersistentSessionId = SessionId.ToString("D"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            if (consecutiveFailures > 0)
            {
                db.AgentSupervisionStates.Add(new AgentSupervisionState
                {
                    AgentId = AgentId,
                    ConsecutiveFailures = consecutiveFailures,
                    UpdatedAt = now,
                });
            }

            await EnsureSessionAsync(db);
            await db.SaveChangesAsync();
        }

        public Task SeedPromptAsync(int minutesAgo) =>
            SeedRowAsync(TranscriptKinds.UserPrompt, minutesAgo, "the brief");

        public async Task SeedRowAsync(string kind, int minutesAgo, string? text = null)
        {
            await using var db = CreateContext();
            await EnsureSessionAsync(db);
            var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = ++_seq,
                Kind = kind,
                Uuid = $"bwd-{Guid.NewGuid():N}",
                Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                Text = text,
                Timestamp = at,
                CreatedAt = at,
                StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
            });
            await db.SaveChangesAsync();
        }

        public async Task ArmAsync()
        {
            await using var db = CreateContext();
            await BootReplyWatch.TryArmAsync(db, SessionId, Deadline, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        public async Task SeedOpenTaskAsync()
        {
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow.AddMinutes(-60);
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "boot-reply watchdog stand-down",
                Goal = "owned by the deadline sweep",
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = SessionId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await db.SaveChangesAsync();
            TaskId = id;
        }

        public Guid? TaskId { get; private set; }

        public async Task<int> SweepAsync(int deadlineMinutes = Deadline)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            var provider = services.BuildServiceProvider();
            var sweep = new BootReplyWatchdogService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new DelegationSettings
                {
                    BootModelWaitDeadlineMinutes = deadlineMinutes,
                }),
                TimeProvider.System,
                NullLogger<BootReplyWatchdogService>.Instance);
            // The sweep is fleet-global against the shared test Postgres, so its own return value
            // also counts whatever else is live right now. Every assertion here is about THIS
            // scenario's session, so the number returned is the delta on that session alone.
            var before = await CountMineAsync();
            await sweep.SweepAsync(CancellationToken.None);
            await provider.DisposeAsync();
            return await CountMineAsync() - before;
        }

        private async Task<int> CountMineAsync()
        {
            await using var db = CreateContext();
            return await db.AgentIncidents.CountAsync(
                i => i.SessionId == SessionId && i.Kind == AgentIncidentKind.LivenessProbeFailed);
        }

        private async Task EnsureSessionAsync(AppDbContext db)
        {
            if (await db.AgentSessions.AnyAsync(s => s.Id == SessionId))
                return;
            var started = DateTime.UtcNow.AddHours(-6);
            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "bootwatchdog-test",
                AgentKind = _kind,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = started,
                StartedAt = started,
                LastSeenAt = DateTime.UtcNow,
            });
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentIncidents.Where(i => i.SessionId == SessionId).ExecuteDeleteAsync();
            if (AgentId != Guid.Empty)
                await db.AgentIncidents.Where(i => i.AgentId == AgentId).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == SessionId).ExecuteDeleteAsync();
            if (TaskId is Guid taskId)
                await db.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            if (AgentId != Guid.Empty)
            {
                await db.AgentSupervisionStates.Where(s => s.AgentId == AgentId).ExecuteDeleteAsync();
                await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
            }
        }
    }
}
