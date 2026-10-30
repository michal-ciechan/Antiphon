using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class TaskCeilingHoldTests
{
    [Test]
    [Arguments(AgentTaskRole.Review)]
    [Arguments(AgentTaskRole.Code)]
    [Arguments(AgentTaskRole.Deploy)]
    [Arguments(AgentTaskRole.Plan)]
    [Arguments(AgentTaskRole.Check)]
    public async Task C1610_Working_ceiling_holds(AgentTaskRole role)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(role);
        await h.SweepAsync();
        var task = await h.TaskAsync();
        task.Status.ShouldBe(AgentTaskStatus.Working, "a working ceiling breach asks for a decision");
        task.CompletedAt.ShouldBeNull();
        task.FailureReason.ShouldBeNull();
        task.CeilingHoldEpisodeId.ShouldNotBeNull();
        task.CeilingAcknowledgedAt.ShouldBeNull();
        task.CeilingHoldAttempt.ShouldBe(task.Attempt);
        task.CeilingHoldSessionId.ShouldBe(h.SessionId);
        task.CeilingHoldDispatchedAt.ShouldBe(task.DispatchedAt);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TranscriptKinds.AssistantText)]
    [Arguments(TranscriptKinds.ToolCall)]
    public async Task C1610_Phase_beats_ceiling(string kind)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(ageMinutes: 1000, kind: kind,
            lastAgeMinutes: kind == TranscriptKinds.ToolCall ? 91 : 21);
        await h.SweepAsync();
        var task = await h.TaskAsync();
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldNotBeNull();
        task.FailureReason.ShouldContain(kind == TranscriptKinds.ToolCall ? "90-minute deadline" : "20-minute deadline",
            customMessage: "a breached real phase wins even when the ceiling fraction is larger");
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TranscriptKinds.AssistantText)]
    [Arguments(TranscriptKinds.ToolCall)]
    public async Task C1610_Held_task_keeps_phase_supervision(string kind)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await h.SweepAsync();
        (await h.TaskAsync()).CeilingHoldEpisodeId.ShouldNotBeNull();
        await h.AppendAsync(kind, "work");
        h.Clock.Advance(TimeSpan.FromMinutes(kind == TranscriptKinds.ToolCall ? 91 : 21));
        await h.SweepAsync();
        var failed = await h.TaskAsync();
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain(kind == TranscriptKinds.ToolCall ? "90-minute deadline" : "20-minute deadline");
        failed.CeilingAcknowledgedAt.ShouldNotBeNull();
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("done")]
    [Arguments("blocked")]
    [Arguments("failed")]
    public async Task C1610_Normal_report_settles_hold(string verdict)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await h.SweepAsync();
        (await h.TaskAsync()).CeilingHoldEpisodeId.ShouldNotBeNull();
        await h.FinishAsync(verdict);
        var settled = await h.TaskAsync();
        settled.Status.ShouldBe(verdict == "done" ? AgentTaskStatus.Succeeded
            : verdict == "blocked" ? AgentTaskStatus.Blocked : AgentTaskStatus.Failed);
        settled.Result.ShouldContain("Completed the authorized work.");
        settled.CeilingAcknowledgedAt.ShouldNotBeNull();
        settled.RepliedAtSequence.ShouldBeNull();
        await using var db = h.Context();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.TaskId
            && (e.Type == AgentTaskEventType.Completed || e.Type == AgentTaskEventType.Blocked || e.Type == AgentTaskEventType.Failed)))
            .ShouldBe(1);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("report")]
    [Arguments("retry")]
    [Arguments("cancel")]
    [Arguments("generation")]
    public async Task C1610_Hold_race_preserves_identity(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        h.Dispatcher.CatchUpOverride = async (_, ct) =>
        {
            h.Dispatcher.CatchUpOverride = null;
            if (arm == "generation")
            {
                await using var db = h.Context();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId, ct);
                session.StartedAt = h.Clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
            }
            else if (arm == "report") await h.FinishAsync();
            else
            {
                using var scope = h.Services.CreateScope();
                var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
                if (arm == "retry") await tasks.RetryAsync(h.TaskId, ct);
                else await tasks.CancelAsync(h.TaskId, ct);
            }
        };
        await h.SweepAsync();
        var current = await h.TaskAsync();
        current.CeilingHoldEpisodeId.ShouldBeNull("the stale evaluation cannot create an episode");
        current.Status.ShouldBe(arm == "report" ? AgentTaskStatus.Succeeded : arm == "retry" ? AgentTaskStatus.Queued
            : arm == "cancel" ? AgentTaskStatus.Canceled : AgentTaskStatus.Working);
        if (arm is "report" or "generation") h.Stopper.Killed.ShouldBeEmpty();
        else h.Stopper.Killed.ShouldHaveSingleItem().ShouldBe(h.SessionId);
    }

    [Test]
    [Arguments("custody")]
    [Arguments("restart")]
    public async Task C1610_Hold_preserves_custody_and_restart_identity(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        var reservation = Guid.NewGuid();
        await using (var db = h.Context())
        {
            db.WorkspaceUseReservations.Add(new WorkspaceUseReservation { Id = reservation, TaskId = h.TaskId,
                SessionId = h.SessionId, CanonicalPath = Path.GetTempPath(), CommonDirectory = Path.GetTempPath(),
                SourceFullRef = "refs/heads/ceiling-test", Kind = WorkspaceReservationKind.Launch,
                CreatedAt = h.Clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync();
        }
        await h.SweepAsync();
        var held = await h.TaskAsync();
        held.CeilingHoldEpisodeId.ShouldNotBeNull();
        if (arm == "restart")
        {
            var rebuilt = OverdueSweepHarness.Create(s => { s.DefaultTimeoutMinutes = 240; s.RolePolicy.Clear();
                s.ModelWaitDeadlineMinutes = 20; s.LocalExecutionDeadlineMinutes = 90; }, clock: h.Clock,
                connectionString: h.ConnectionString);
            await using var owned = rebuilt.Provider;
            await rebuilt.Dispatcher.FailOverdueTasksAsync(CancellationToken.None);
            rebuilt.Stopper.Killed.ShouldBeEmpty();
        }
        else await h.SweepAsync();
        var current = await h.TaskAsync();
        current.CeilingHoldEpisodeId.ShouldBe(held.CeilingHoldEpisodeId);
        current.CompletedAt.ShouldBeNull();
        await using var verify = h.Context();
        (await verify.WorkspaceUseReservations.SingleAsync(r => r.Id == reservation)).Active.ShouldBeTrue();
        (await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).Status.ShouldBe(SessionStatus.Running);
        (await verify.TaskSupervisionNotices.CountAsync(n => n.TaskId == h.TaskId
            && n.Purpose == TaskSupervisionNoticePurpose.CeilingParent)).ShouldBe(1);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("question")]
    [Arguments("quota")]
    [Arguments("routing")]
    public async Task C1610_Non_ceiling_blocks_remain_excluded(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await h.MutateAsync(t => { t.Status = AgentTaskStatus.Blocked;
            t.FailureReason = arm; if (arm == "quota") t.FailureCode = AgentTaskFailureCode.SubscriptionQuotaExceeded; });
        await h.SweepAsync();
        var task = await h.TaskAsync();
        task.Status.ShouldBe(AgentTaskStatus.Blocked);
        task.FailureReason.ShouldBe(arm);
        task.CeilingHoldEpisodeId.ShouldBeNull();
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("stage-absolute")]
    [Arguments("local")]
    [Arguments("pipeline")]
    [Arguments("check")]
    public async Task C1610_Hold_keeps_occupancy(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(arm == "check" ? AgentTaskRole.Check : AgentTaskRole.Code);
        await h.SweepAsync();
        var held = await h.TaskAsync();
        held.CeilingHoldEpisodeId.ShouldNotBeNull();
        if (arm == "check")
        {
            await CheckOccupantAsync(h);
            return;
        }
        await using var db = h.Context();
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Role = AgentTaskRole.Code,
                Status = AgentTaskStatus.Working, Title = "occupant", Goal = "work", WorkingDirectory = Path.GetTempPath(),
                CreatedAt = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-5) });
        }
        await db.SaveChangesAsync();
        var settings = new DelegationSettings { MaxOpenTasks = 4, MaxConcurrentTasks = 4 };
        settings.RolePolicy["Code"].RecommendedInFlight = 4;
        var opts = Options.Create(settings);
        if (arm == "stage-absolute")
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var refused = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
                new DelegationOpenGate(db, opts).EnsureCanCreateAsync(null, AgentTaskRole.Code, false, CancellationToken.None));
            refused.StatusCode.ShouldBe(409);
            var rows = await db.AgentTasks.Select(t => new TaskPopulationRow(t.Id, t.ProjectId, t.Role, t.Status,
                t.CreatedAt, t.CapacityWaitRetained, t.Title)).ToListAsync();
            var population = DispatchConcurrencyPolicy.Count(rows, null);
            population.Parallel.ShouldBe(4);
            population.Roles[AgentTaskRole.Code].Parallel.ShouldBe(4);
        }
        else if (arm == "local")
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Role = AgentTaskRole.Code,
                Status = AgentTaskStatus.Queued, Title = "fifth", Goal = "work", WorkingDirectory = Path.GetTempPath() });
            await db.SaveChangesAsync();
            // The real dispatch entry point counts its held Working row before selecting candidates.
            var configured = h.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DelegationSettings>>().Value;
            configured.MaxConcurrentTasks = 4;
            await h.Dispatcher.DispatchQueuedAsync(CancellationToken.None);
            await db.Entry(await db.AgentTasks.SingleAsync(t => t.Id == id)).ReloadAsync();
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id)).Status.ShouldBe(AgentTaskStatus.Queued);
        }
        else
        {
            for (var pass = 0; pass < 2; pass++)
            {
                await using var fresh = h.Context();
                var dto = await new AgentTaskPipelineStatusService(fresh, opts,
                    new AreaMapLoader(opts, NullLogger<AreaMapLoader>.Instance), h.Clock).GetAsync(CancellationToken.None);
                dto.InFlightAgainstCap.ShouldBe(4);
                dto.Stages.Single(s => s.Role == AgentTaskRole.Code).InFlightCount.ShouldBe(4);
            }
        }
        h.Stopper.Killed.ShouldBeEmpty();
    }

    private static async Task CheckOccupantAsync(TaskCeilingHarness h)
    {
        await using var db = h.Context();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.TaskId);
        var agentId = Guid.NewGuid();
        var now = h.Clock.GetUtcNow().UtcDateTime;
        db.Agents.Add(new Agent { Id = agentId, Name = "held check", Slug = "held-" + agentId.ToString("N"),
            Kind = AgentKind.ClaudeCode, AlwaysOn = true, Status = AgentStatus.Running,
            StandingSpecialistRole = AgentTaskRole.Check, StandingSpecialistOwnerId = agentId,
            PersistentSessionId = h.SessionId.ToString("D"), WorkingDirectory = Path.GetTempPath() });
        task.AgentId = agentId;
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.StandingAgentId = agentId;
        var episodeId = Guid.NewGuid();
        db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = agentId, ActiveCompactionRecoveryId = episodeId, UpdatedAt = now });
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery { Id = episodeId, PhysicalAgentId = agentId,
            SessionId = h.SessionId, AcceptedStartedAt = session.StartedAt, BoundaryIdentity = "bound-held",
            BoundaryCreatedAt = now.AddMinutes(-20), ContinuationCreatedAt = now.AddMinutes(-20),
            ConfiguredThresholdMinutes = 10, DetectedAt = now.AddMinutes(-11), State = CheckCompactionRecoveryState.Stopped,
            OwningCheckTaskId = h.TaskId, AttemptId = Guid.NewGuid(), StopOutcomeAt = now });
        await db.SaveChangesAsync();
        var runner = new FakeSessionRunnerClient();
        await new CheckCompactionContinuationService(db, h.Clock, Options.Create(new DelegationSettings()),
            new CheckCompactionContinuationGate(), NullLogger<CheckCompactionContinuationService>.Instance, runner)
            .SweepAsync(CancellationToken.None);
        await db.Entry(task).ReloadAsync();
        task.Status.ShouldBe(AgentTaskStatus.Failed, "the existing qualified recovery still selects its held owning Check");
        task.FailureCode.ShouldBe(AgentTaskFailureCode.CompactionRecoveryRetiredGeneration);
        runner.KillCalls.ShouldBe(0);
        h.Stopper.Killed.ShouldBeEmpty();
    }
}
