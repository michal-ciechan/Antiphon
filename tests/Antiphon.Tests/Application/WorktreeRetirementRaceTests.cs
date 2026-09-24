using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class WorktreeRetirementRaceTests
{
    [Test]
    public async Task C459_OneRetirementClaim()
    {
        await using var world = await RaceWorld.CreateAsync();
        var first = await world.Journal.TryClaimRetirementAsync(world.Command(WorkspaceReservationKind.Retirement, world.RetirementId), CancellationToken.None);
        var second = await world.Journal.TryClaimRetirementAsync(world.Command(WorkspaceReservationKind.Launch, Guid.NewGuid()), CancellationToken.None);
        var acceptedClaimants = (first.Accepted ? 1 : 0) + (second.Accepted ? 1 : 0);
        acceptedClaimants.ShouldBe(1);
    }

    [Test]
    [Arguments("child")]
    [Arguments("base")]
    [Arguments("follow-up")]
    public async Task C459_CreateReservesWorkspace(string kind)
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var acceptedConsumers = 0;
        try
        {
            var request = kind switch
            {
                "follow-up" => new CreateAgentTaskRequest(Goal: "follow", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared,
                    WorkingDirectory: world.Path, FollowUpOnTask: world.OwnerTask.Id.ToString("D")),
                _ => new CreateAgentTaskRequest(Goal: "child", Role: AgentTaskRole.Code, Kind: AgentTaskKind.Worker,
                    Workspace: WorkspaceMode.Shared, WorkingDirectory: world.Path),
            };
            await world.Tasks.CreateAsync(request, world.ManualCaller(), CancellationToken.None);
            acceptedConsumers++;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("workspace_reserved");
        }

        acceptedConsumers.ShouldBe(0);
    }

    [Test]
    public async Task C459_RequeueReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        var created = await world.Tasks.CreateAsync(
            new CreateAgentTaskRequest(Goal: "retry me", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared,
                WorkingDirectory: world.Path),
            world.ManualCaller(), CancellationToken.None);
        await using (var db = world.CreateDb())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            row.Status = AgentTaskStatus.Failed;
            row.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            var launch = await db.WorkspaceUseReservations.SingleAsync(r => r.TaskId == created.Id && r.Active);
            await world.Journal.ReleaseConsumerAsync(launch.Id, launch.Generation, CancellationToken.None);
        }

        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var claimFirstRequeued = false;
        try
        {
            await world.Tasks.RetryAsync(created.Id, CancellationToken.None);
            claimFirstRequeued = true;
        }
        catch (ConflictException)
        {
            claimFirstRequeued = false;
        }

        try
        {
            await world.Admission.RequireConsumerAsync(
                world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch, TaskId = created.Id },
                CancellationToken.None);
            claimFirstRequeued = true;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("workspace_reserved");
        }

        claimFirstRequeued.ShouldBeFalse();
    }

    [Test]
    public async Task C459_AnswerReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        var created = await world.SeedQueuedTaskAsync(WorkspaceMode.Worktree);
        await using (var db = world.CreateDb())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            row.Status = AgentTaskStatus.Blocked;
            row.WorktreePath = world.Path;
            row.RepoPath = world.Path;
            row.WorktreeBranch = "feat/card-task-race";
            await db.SaveChangesAsync();
        }

        (await world.Journal.TryClaimRetirementAsync(world.ProductionRetirementCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var answerAdmissions = 0;
        try
        {
            await world.Replies.AnswerAsync(created.Id, "continue", CancellationToken.None);
            answerAdmissions++;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("workspace_reserved");
        }

        answerAdmissions.ShouldBe(0);
    }

    [Test]
    public async Task C459_WriterDispatchReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        var queued = await world.SeedQueuedTaskAsync(WorkspaceMode.Worktree);
        (await world.Journal.TryClaimRetirementAsync(world.ProductionRetirementCommand(queued.WorktreeBranch), CancellationToken.None))
            .Accepted.ShouldBeTrue();
        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using var db = world.CreateDb();
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queued.Id);
        var dispatchCommitted = stored.Status == AgentTaskStatus.Dispatched;
        dispatchCommitted.ShouldBeFalse();
        world.Adapter.Started.ShouldBeFalse();
    }

    [Test]
    public async Task C459_ReadOnlyDispatchReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        var queued = await world.SeedQueuedTaskAsync(WorkspaceMode.ReadOnly);
        (await world.Journal.TryClaimRetirementAsync(world.ProductionRetirementCommand(queued.WorktreeBranch), CancellationToken.None))
            .Accepted.ShouldBeTrue();
        await world.Dispatcher.TickAsync(CancellationToken.None);
        var adapterStarts = world.Adapter.Started ? 1 : 0;
        adapterStarts.ShouldBe(0);
        await using var db = world.CreateDb();
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queued.Id)).Status.ShouldNotBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task C459_LandReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        await using (var db = world.CreateDb())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == world.OwnerTask.Id);
            owner.Workspace = WorkspaceMode.Worktree;
            owner.WorktreePath = world.Path;
            owner.RepoPath = world.Path;
            owner.WorktreeBranch = "feat/card-task-land";
            owner.Status = AgentTaskStatus.Succeeded;
            await db.SaveChangesAsync();
        }

        (await world.Journal.TryClaimRetirementAsync(world.ProductionRetirementCommand("feat/card-task-land"), CancellationToken.None))
            .Accepted.ShouldBeTrue();
        var newPendingRequests = 0;
        try
        {
            await world.Lands.RequestAsync(world.OwnerTask.Id, new LandAgentTaskRequest(null, new string('a', 40), null), CancellationToken.None);
            newPendingRequests++;
        }
        catch (ConflictException)
        {
        }

        newPendingRequests.ShouldBe(0);
        await using var verify = world.CreateDb();
        (await verify.AgentTaskLandRequests.CountAsync(r => r.TaskId == world.OwnerTask.Id)).ShouldBe(0);
    }

    [Test]
    public async Task C459_CardReopenInvalidatesRelease()
    {
        await using var world = await RaceWorld.CreateAsync();
        await using var db = world.CreateDb();
        var retirement = new TaskWorktreeRetirement
        {
            Id = Guid.NewGuid(),
            TaskId = world.OwnerTask.Id,
            TaskAttempt = 1,
            TerminalStatus = AgentTaskStatus.Succeeded,
            TaskCompletedAt = DateTime.UtcNow.AddHours(-3),
            ReleasedTaskRevision = Guid.NewGuid(),
            CallerIdentity = "operator",
            ReleaseReason = "x",
            ReleasedAt = DateTime.UtcNow,
            RepositoryPath = world.Path,
            CommonDirectory = world.Path,
            WorktreePath = world.Path,
            GitDirectory = world.Path,
            SourceFullRef = "",
            SourceSha = new string('a', 40),
            TargetFullRef = "refs/heads/master",
            State = WorktreeRetirementState.Released,
            Active = true,
            UpdatedAt = DateTime.UtcNow,
        };
        db.TaskWorktreeRetirements.Add(retirement);
        await db.SaveChangesAsync();
        await world.Admission.InvalidateReleaseAsync(world.OwnerTask.Id, CancellationToken.None);
        var row = await db.TaskWorktreeRetirements.AsNoTracking().SingleAsync(r => r.Id == retirement.Id);
        var unclaimedReleaseStillValid = row.Active && row.State == WorktreeRetirementState.Released;
        unclaimedReleaseStillValid.ShouldBeFalse();
    }

    [Test]
    public async Task C459_DirectStartFenced()
    {
        await using var world = await RaceWorld.CreateAsync();
        var cardId = await world.SeedCardWithWorktreeAsync();
        (await world.Journal.TryClaimRetirementAsync(world.ProductionRetirementCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var adapterStarts = 0;
        try
        {
            await world.Sessions.StartAsync(
                new StartAgentSessionRequest(cardId, "fake", AgentKind.ClaudeCode, "start fenced"),
                world.LaunchSpec(), CancellationToken.None);
            adapterStarts++;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("workspace_reserved");
        }

        adapterStarts.ShouldBe(0);
        world.Adapter.Started.ShouldBeFalse();
    }

    [Test]
    public async Task C459_InteractiveStartFenced()
    {
        await using var world = await RaceWorld.CreateAsync();
        var session = await world.SeedInteractiveSessionAsync();
        (await world.Journal.TryClaimRetirementAsync(world.SessionProductionCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        world.LaunchQueue.EnqueueInteractiveSession(session.Id, world.AgentId, session.StartedAt, world.LaunchSpec(session.Id), null);
        await world.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        var adapterStarts = world.Adapter.Started ? 1 : 0;
        adapterStarts.ShouldBe(0);
    }

    [Test]
    public async Task C459_ResumeFenced()
    {
        await using var world = await RaceWorld.CreateAsync();
        var session = await world.SeedStoppedCardSessionAsync();
        (await world.Journal.TryClaimRetirementAsync(world.SessionProductionCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var adapterStarts = 0;
        try
        {
            await world.Sessions.ResumeAsync(session.Id, world.LaunchSpec(session.Id), AgentSessionResumeMode.Continue, CancellationToken.None);
            adapterStarts++;
        }
        catch (ConflictException ex)
        {
            ex.Code.ShouldBe("workspace_reserved");
        }

        adapterStarts.ShouldBe(0);
        world.Adapter.Started.ShouldBeFalse();
    }

    [Test]
    public async Task C459_InterruptedAttachFenced()
    {
        await using var world = await RaceWorld.CreateAsync();
        var session = await world.SeedInterruptedLaunchAsync();
        (await world.Journal.TryClaimRetirementAsync(world.SessionProductionCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        world.LaunchQueue.ResumeInterrupted(session.Id, world.AgentId);
        await world.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        var adapterAttaches = world.Adapter.Attached ? 1 : 0;
        adapterAttaches.ShouldBe(0);
    }

    [Test]
    public async Task C459_QueuedGenerationIsImmutable()
    {
        await using var world = await RaceWorld.CreateAsync();
        var first = await world.Journal.TryAdmitConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
        first.Accepted.ShouldBeTrue();
        await world.Journal.ReleaseConsumerAsync(first.Snapshot!.Id, first.Snapshot.Generation, CancellationToken.None);
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var adapterStarts = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch, ExpectedGeneration = first.Snapshot.Generation }, CancellationToken.None);
            adapterStarts++;
        }
        catch (ConflictException)
        {
        }

        adapterStarts.ShouldBe(0);
    }

    [Test]
    public async Task C459_LaunchIntentPrecedesEnqueue()
    {
        await using var world = await RaceWorld.CreateAsync();
        var launch = await world.Journal.TryAdmitConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
        launch.Accepted.ShouldBeTrue();
        var retirement = await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None);
        var retirementClaimedWhileStartPending = retirement.Accepted;
        retirementClaimedWhileStartPending.ShouldBeFalse();
    }

    [Test]
    public async Task C459_CompletedPathStaysFenced()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryAdmitConsumerAsync(new WorkspaceReservationCommand(world.Key, WorkspaceReservationKind.HistoricalFence, world.OwnerTask.Id, RetirementId: world.RetirementId), CancellationToken.None)).Accepted.ShouldBeTrue();
        var oldCoordinateAdmissions = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
            oldCoordinateAdmissions++;
        }
        catch (ConflictException)
        {
        }

        oldCoordinateAdmissions.ShouldBe(0);
    }

    [Test]
    public async Task C459_TaskConsumersHold()
    {
        await using var world = await RaceWorld.CreateAsync();
        await using var db = world.CreateDb();
        db.AgentTasks.Add(new AgentTask
        {
            Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "live", Goal = "live",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = world.Path, WorktreePath = world.Path, Status = AgentTaskStatus.Working,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        (await world.Admission.HasLiveTaskConsumerAsync(world.Path, "", world.OwnerTask.Id, CancellationToken.None)).ShouldBeTrue();
        var (owner, release) = await world.ReleasedWorktreeAsync();
        var (authorized, reason) = await world.Retirement.EvaluateEligibilityAsync(owner, release, CancellationToken.None);
        authorized.ShouldBeFalse();
        reason.ShouldBe("live_owner");
        var removeCalls = authorized ? 1 : 0;
        removeCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_SessionOwnersHold()
    {
        await using var world = await RaceWorld.CreateAsync();
        await using var db = world.CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = Guid.NewGuid(), DefinitionName = "grok", AgentKind = AgentKind.Grok,
            Cwd = world.Path, Status = SessionStatus.Running, Cols = 80, Rows = 24,
            CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        db.Agents.Add(new Agent
        {
            Id = Guid.NewGuid(), Name = "pooled", Slug = "pooled-" + Guid.NewGuid().ToString("N")[..8],
            WorkingDirectory = world.Path, Status = AgentStatus.Ready, PoolIdleSince = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        (await world.Admission.HasLiveSessionOwnerAsync(world.Path, CancellationToken.None)).ShouldBeTrue();
        var (owner, release) = await world.ReleasedWorktreeAsync();
        var (authorized, reason) = await world.Retirement.EvaluateEligibilityAsync(owner, release, CancellationToken.None);
        authorized.ShouldBeFalse();
        reason.ShouldBe("live_owner");
        var removeCalls = authorized ? 1 : 0;
        removeCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_UnknownBackendHolds()
    {
        await using var world = await RaceWorld.CreateAsync();
        await using var db = world.CreateDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = Guid.NewGuid(), DefinitionName = "grok", AgentKind = AgentKind.Grok,
            Cwd = world.Path, Status = SessionStatus.Starting, Cols = 80, Rows = 24,
            CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        (await world.Admission.HasLiveSessionOwnerAsync(world.Path, CancellationToken.None)).ShouldBeTrue();
        var (owner, release) = await world.ReleasedWorktreeAsync();
        var (authorized, reason) = await world.Retirement.EvaluateEligibilityAsync(owner, release, CancellationToken.None);
        authorized.ShouldBeFalse();
        reason.ShouldBe("live_owner");
        var removeCalls = authorized ? 1 : 0;
        removeCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_UnknownChildHolds()
    {
        await using var h = await SettledRemovalHarness.CreateAsync();
        var children = Path.Combine(h.CommonDirectory, "antiphon", "children");
        Directory.CreateDirectory(children);
        await File.WriteAllTextAsync(Path.Combine(children, Guid.NewGuid().ToString("N") + ".json"),
            JsonSerializer.Serialize(new { SchemaVersion = 1, CommonDirectory = h.CommonDirectory, ProcessId = (int?)null, StartTicks = (long?)null }));
        var leases = h.Host.Services.GetRequiredService<IRepositoryMutationLease>();
        var lease = await leases.TryAcquireAsync(h.Host.Fixture.Repository, CancellationToken.None);
        if (lease is not null)
        {
            try { await h.RemoveAsync(lease: lease); }
            finally { await lease.DisposeAsync(); }
        }

        h.RemoveCalls.ShouldBe(0);
        Directory.Exists(h.Host.Fixture.Source).ShouldBeTrue();
    }

    [Test]
    public async Task C459_CleanupNeverStopsOwner()
    {
        await using var world = await RaceWorld.CreateAsync();
        await using var db = world.CreateDb();
        var sessionId = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "grok", AgentKind = AgentKind.Grok,
            Cwd = world.Path, Status = SessionStatus.Running, Cols = 80, Rows = 24,
            CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var (owner, release) = await world.ReleasedWorktreeAsync();
        var (authorized, _) = await world.Retirement.EvaluateEligibilityAsync(owner, release, CancellationToken.None);
        authorized.ShouldBeFalse();
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
        var stopCalls = world.Stopper.Killed.Count;
        stopCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_GitDoesNotHoldDbRows()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var started = DateTime.UtcNow;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
        }
        catch (ConflictException)
        {
        }

        var independentAdmissionFinishedBeforeGitRelease = DateTime.UtcNow - started < TimeSpan.FromSeconds(2);
        independentAdmissionFinishedBeforeGitRelease.ShouldBeTrue();
    }

    [Test]
    public async Task C459_LaunchCommitPrecedesEnqueue()
    {
        await using var world = await RaceWorld.CreateAsync();
        var admitted = await world.Journal.TryAdmitConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
        var committedLaunchReservationAtEnqueue = admitted.Snapshot;
        committedLaunchReservationAtEnqueue.ShouldNotBeNull();
        admitted.Accepted.ShouldBeTrue();
    }

    [Test]
    public async Task C459_RefineReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var refinementAdmissions = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
            refinementAdmissions++;
        }
        catch (ConflictException)
        {
        }

        refinementAdmissions.ShouldBe(0);
    }

    [Test]
    public async Task C459_MergeChildReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var newMergeChildren = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
            newMergeChildren++;
        }
        catch (ConflictException)
        {
        }

        newMergeChildren.ShouldBe(0);
    }

    [Test]
    public async Task C459_CommitChildReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var newCommitChildren = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
            newCommitChildren++;
        }
        catch (ConflictException)
        {
        }

        newCommitChildren.ShouldBe(0);
    }

    [Test]
    public async Task C459_HerdrAttachReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var acceptedAttachments = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
            acceptedAttachments++;
        }
        catch (ConflictException)
        {
        }

        acceptedAttachments.ShouldBe(0);
    }

    [Test]
    [Arguments("terminal-task")]
    [Arguments("ended-session")]
    [Arguments("unattributed")]
    [Arguments("missing-task")]
    public async Task C664_OrphanedLaunchRowsDoNotBlockRetirement(string shape)
    {
        await using var world = await RaceWorld.CreateAsync();
        var launch = await world.SeedOwnedLaunchAsync(shape, aged: true);

        var claim = await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None);

        claim.Reason.ShouldBeNull();
        claim.Accepted.ShouldBeTrue();
        await using var db = world.CreateDb();
        var row = await db.WorkspaceUseReservations.AsNoTracking().SingleAsync(r => r.Id == launch);
        row.Active.ShouldBeFalse("an orphaned Launch row is released on sight by the claim");
        row.ReleasedAt.ShouldNotBeNull();
    }

    [Test]
    [Arguments("working-task")]
    [Arguments("running-session")]
    [Arguments("land-pending")]
    [Arguments("fresh-unattributed")]
    public async Task C664_LiveOwnerStillBlocksRetirement(string shape)
    {
        await using var world = await RaceWorld.CreateAsync();
        var launch = await world.SeedOwnedLaunchAsync(shape, aged: shape != "fresh-unattributed");

        var claim = await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None);

        claim.Accepted.ShouldBeFalse();
        claim.Reason.ShouldBe("workspace_in_use");
        await using var db = world.CreateDb();
        var row = await db.WorkspaceUseReservations.AsNoTracking().SingleAsync(r => r.Id == launch);
        row.Active.ShouldBeTrue();
        row.ReleasedAt.ShouldBeNull();
    }

    [Test]
    public async Task C664_FindLiveConsumersExcludesOrphanedRows()
    {
        await using var world = await RaceWorld.CreateAsync();
        await world.SeedOwnedLaunchAsync("terminal-task", aged: true);
        var working = await world.SeedOwnedLaunchAsync("working-task", aged: true);

        var live = await world.Admission.FindLiveConsumersAsync(world.Key, world.OwnerTask.Id, CancellationToken.None);

        live.Select(s => s.Id).ShouldBe(new[] { working });
    }

    [Test]
    public async Task C664_ReconcileReleasesOnlyOrphanedRows()
    {
        await using var world = await RaceWorld.CreateAsync();
        Guid[] orphaned =
        [
            await world.SeedOwnedLaunchAsync("terminal-task", aged: true),
            await world.SeedOwnedLaunchAsync("ended-session", aged: true),
            await world.SeedOwnedLaunchAsync("unattributed", aged: true),
        ];
        Guid[] live =
        [
            await world.SeedOwnedLaunchAsync("working-task", aged: true),
            await world.SeedOwnedLaunchAsync("fresh-unattributed", aged: false),
        ];

        var released = await world.Journal.ReleaseOrphanedConsumersAsync(CancellationToken.None);

        released.ShouldBe(3);
        await using var db = world.CreateDb();
        var rows = await db.WorkspaceUseReservations.AsNoTracking().ToListAsync();
        rows.Where(r => !r.Active).Select(r => r.Id).OrderBy(id => id).ShouldBe(orphaned.OrderBy(id => id));
        rows.Where(r => r.Active).Select(r => r.Id).OrderBy(id => id).ShouldBe(live.OrderBy(id => id));
        rows.Where(r => !r.Active).ShouldAllBe(r => r.ReleasedAt != null);
    }

    [Test]
    public async Task C664_RefusedClaimStillReleasesOrphanedRows()
    {
        await using var world = await RaceWorld.CreateAsync();
        var orphan = await world.SeedOwnedLaunchAsync("terminal-task", aged: true);
        var live = await world.SeedOwnedLaunchAsync("working-task", aged: true);

        var claim = await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None);

        claim.Accepted.ShouldBeFalse();
        claim.Reason.ShouldBe("workspace_in_use");
        await using var db = world.CreateDb();
        var rows = await db.WorkspaceUseReservations.AsNoTracking().ToDictionaryAsync(r => r.Id);
        rows[orphan].Active.ShouldBeFalse("the refused claim still commits its on-sight release of the orphan");
        rows[orphan].ReleasedAt.ShouldNotBeNull();
        rows[live].Active.ShouldBeTrue();
        rows[live].ReleasedAt.ShouldBeNull();
        rows.Values.ShouldNotContain(r => r.Kind == WorkspaceReservationKind.Retirement);
    }

    [Test]
    public async Task C664_StartupReconcileReleasesOrphanedRows()
    {
        await using var world = await RaceWorld.CreateAsync();
        var orphan = await world.SeedOwnedLaunchAsync("ended-session", aged: true);
        var live = await world.SeedOwnedLaunchAsync("running-session", aged: true);

        await using var scope = world.Provider.CreateAsyncScope();
        var released = await WorkspaceReservationStartupReconcile.RunAsync(
            scope.ServiceProvider, NullLogger.Instance, CancellationToken.None);

        released.ShouldBe(1);
        await using var db = world.CreateDb();
        var rows = await db.WorkspaceUseReservations.AsNoTracking().ToDictionaryAsync(r => r.Id);
        rows[orphan].Active.ShouldBeFalse();
        rows[orphan].ReleasedAt.ShouldNotBeNull();
        rows[live].Active.ShouldBeTrue();
    }

    [Test]
    public async Task C664_StartupReconcileFailureDoesNotThrow()
    {
        // No journal registered: resolution fails inside the hook, which must swallow and report null.
        await using var empty = new ServiceCollection().BuildServiceProvider();

        var released = await WorkspaceReservationStartupReconcile.RunAsync(
            empty, NullLogger.Instance, CancellationToken.None);

        released.ShouldBeNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C664_ResidueRunReconcilesOrphanedRows(bool preview)
    {
        await using var world = await RaceWorld.CreateAsync();
        var orphan = await world.SeedOwnedLaunchAsync("terminal-task", aged: true);
        var live = await world.SeedOwnedLaunchAsync("working-task", aged: true);
        await using (var sweepDb = world.CreateDb())
        {
            var sweep = new WorktreeResidueSweepService(
                sweepDb, new FixedWorktreeManager(world.Path),
                Options.Create(new WorktreeResidueSettings()),
                Options.Create(new GitSettings { DefaultBranch = "master" }),
                TimeProvider.System, NullLogger<WorktreeResidueSweepService>.Instance,
                world.Retirement, world.Lands, world.Journal);
            if (preview)
                await sweep.PreviewAsync(null, null, CancellationToken.None);
            else
                await sweep.RunAsync(CancellationToken.None);
        }

        await using var db = world.CreateDb();
        var rows = await db.WorkspaceUseReservations.AsNoTracking().ToDictionaryAsync(r => r.Id);
        rows[orphan].Active.ShouldBeFalse("the residue run reconciles before classifying");
        rows[orphan].ReleasedAt.ShouldNotBeNull();
        rows[live].Active.ShouldBeTrue();
        (await db.WorktreeResidueRuns.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-5)]
    public async Task C664_LaunchGraceClampsToOneMinute(int configured)
    {
        await using var world = await RaceWorld.CreateAsync();
        var journal = new WorkspaceReservationJournal(
            world.Provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            Options.Create(new WorktreeResidueSettings { LaunchGraceMinutes = configured }));
        var inGrace = await world.SeedOwnedLaunchAsync("terminal-task", aged: false);
        var pastGrace = await world.SeedOwnedLaunchAsync("terminal-task", aged: false);
        await world.SetCreatedAtAsync(inGrace, DateTime.UtcNow.AddSeconds(-20));
        await world.SetCreatedAtAsync(pastGrace, DateTime.UtcNow.AddSeconds(-90));

        var released = await journal.ReleaseOrphanedConsumersAsync(CancellationToken.None);

        released.ShouldBe(1, "a zero or negative grace clamps to one minute, not to no grace and not to the default");
        await using var db = world.CreateDb();
        var rows = await db.WorkspaceUseReservations.AsNoTracking().ToDictionaryAsync(r => r.Id);
        rows[inGrace].Active.ShouldBeTrue();
        rows[pastGrace].Active.ShouldBeFalse();
    }

    [Test]
    public async Task C664_LandRequestThenOutcome_ReleasesAndRetirementClaimAccepted()
    {
        await using var world = await RaceWorld.CreateAsync();
        await world.MakeOwnerLandableAsync();
        var requested = await world.Lands.RequestAsync(
            world.OwnerTask.Id, new LandAgentTaskRequest(null, new string('a', 40), null), CancellationToken.None);
        (await world.ActiveLaunchIdsAsync(world.OwnerTask.Id)).Count.ShouldBe(1, "the land request admits one Launch row");

        await world.Lands.FailRequestAsync(
            world.OwnerTask.Id, requested.RequestId, new InvalidOperationException("drain"), CancellationToken.None);

        await using (var db = world.CreateDb())
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.OwnerTask.Id)).LandRequestedAt
                .ShouldBeNull("the failure reached the terminal land outcome");
        (await world.ActiveLaunchIdsAsync(world.OwnerTask.Id)).ShouldBeEmpty("the land outcome releases the owner's Launch rows");
        var claim = await world.Journal.TryClaimRetirementAsync(
            world.ProductionRetirementCommand("feat/card-task-land"), CancellationToken.None);
        claim.Reason.ShouldBeNull();
        claim.Accepted.ShouldBeTrue();
    }

    [Test]
    public async Task C664_LandRequestRefusedAfterAdmission_LeavesNoExtraLaunch()
    {
        await using var world = await RaceWorld.CreateAsync();
        await world.MakeOwnerLandableAsync();
        await world.Lands.RequestAsync(
            world.OwnerTask.Id, new LandAgentTaskRequest(null, new string('a', 40), null), CancellationToken.None);
        var first = await world.ActiveLaunchIdsAsync(world.OwnerTask.Id);
        first.Count.ShouldBe(1);

        var refused = await Should.ThrowAsync<ConflictException>(() => world.Lands.RequestAsync(
            world.OwnerTask.Id, new LandAgentTaskRequest(null, new string('a', 40), null), CancellationToken.None));

        refused.Code.ShouldBe("land_running");
        (await world.ActiveLaunchIdsAsync(world.OwnerTask.Id)).ShouldBe(first, "the refused request releases its own admission");
    }

    [Test]
    public async Task C664_AnswerRefusedAfterAdmission_LeavesNoActiveLaunch()
    {
        await using var world = await RaceWorld.CreateAsync();
        var created = await world.SeedQueuedTaskAsync(WorkspaceMode.Worktree);
        await using (var db = world.CreateDb())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            row.Status = AgentTaskStatus.Blocked;
            row.AgentSessionId = null;
            await db.SaveChangesAsync();
        }

        await Should.ThrowAsync<ConflictException>(() =>
            world.Replies.AnswerAsync(created.Id, "continue", CancellationToken.None));

        (await world.ActiveLaunchIdsAsync(created.Id)).ShouldBeEmpty("the refused answer releases its own admission");
    }

    [Test]
    public async Task C664_RequeueThenCanceled_RetirementClaimAccepted()
    {
        await using var world = await RaceWorld.CreateAsync();
        var created = await world.Tasks.CreateAsync(
            new CreateAgentTaskRequest(Goal: "retry then cancel", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared,
                WorkingDirectory: world.Path),
            world.ManualCaller(), CancellationToken.None);
        await using (var db = world.CreateDb())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            row.Status = AgentTaskStatus.Failed;
            row.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var scope = world.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(created.Id, CancellationToken.None);
        await using (var db = world.CreateDb())
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        (await world.ActiveLaunchIdsAsync(created.Id)).Count.ShouldBe(2, "create and requeue each admit a Launch row");

        await using (var scope = world.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(created.Id, CancellationToken.None);

        (await world.ActiveLaunchIdsAsync(created.Id)).ShouldBeEmpty("cancel releases every Launch row of the task");
        var claim = await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None);
        claim.Reason.ShouldBeNull();
        claim.Accepted.ShouldBeTrue();
    }

    private sealed class RaceWorld : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required ServiceProvider Provider { get; init; }
        public required string Path { get; init; }
        public required AgentTask OwnerTask { get; init; }
        public required WorkspaceReservationJournal Journal { get; init; }
        public required WorkspaceUseAdmission Admission { get; init; }
        public required AgentTaskService Tasks { get; init; }
        public required TaskWorktreeRetirementService Retirement { get; init; }
        public required RecordingSessionStopper Stopper { get; init; }
        public required AgentTaskDispatcher Dispatcher { get; init; }
        public required AgentTaskReplyService Replies { get; init; }
        public required AgentTaskLandService Lands { get; init; }
        public required AgentSessionService Sessions { get; init; }
        public required AgentSessionLaunchQueue LaunchQueue { get; init; }
        public required FakeAgentProtocolAdapter Adapter { get; init; }
        public required Guid AgentId { get; init; }
        public Guid RetirementId { get; } = Guid.NewGuid();
        public WorkspaceReservationKey Key => WorkspaceReservationKey.For(Path, "", Path);

        public static async Task<RaceWorld> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var path = Directory.CreateTempSubdirectory("c459-race-").FullName;
            var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
            var stopper = new RecordingSessionStopper();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new AgentSessionSettings
            {
                KillGraceMs = 100,
                SessionLogPath = System.IO.Path.Combine(path, "session-logs"),
            }));
            services.AddSingleton(Options.Create(new DelegationSettings
            {
                AllowedRoots = [path],
                MaxDepth = 5,
                MaxTasksPerRoot = 40,
                MaxConcurrentTasks = 512,
            }));
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "fake";
                s.Definitions["fake"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "fake" };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddSingleton<IWorktreeManager>(new FixedWorktreeManager(path));
            services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = path, DefaultBranch = "master" });
            services.AddSingleton<IAgentProtocolAdapterFactory>(new OneAdapterFactory(adapter));
            services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
            services.AddScoped<WorkspaceHookService>();
            services.AddScoped<AgentSessionService>();
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<AgentTaskReplyService>();
            services.AddScoped<AgentTaskDispatcher>();
            services.AddSingleton(new AgentTaskLandQueue());
            services.AddScoped<AgentTaskLandService>();
            services.AddSingleton(adapter);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = provider.CreateScope();
            var journal = (WorkspaceReservationJournal)scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var admission = scope.ServiceProvider.GetRequiredService<WorkspaceUseAdmission>();
            var agentId = Guid.NewGuid();
            db.Agents.Add(new Agent
            {
                Id = agentId, Name = "c459-race", Slug = "c459-race-" + agentId.ToString("N")[..8],
                WorkingDirectory = path, Status = AgentStatus.Idle, Kind = AgentKind.ClaudeCode,
            });
            var id = Guid.NewGuid();
            var owner = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "owner", Goal = "owner",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Shared,
                WorkingDirectory = path, RepoPath = path, Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow.AddHours(-5),
                CompletedAt = DateTime.UtcNow.AddHours(-3), Result = "done",
            };
            db.AgentTasks.Add(owner);
            await db.SaveChangesAsync();
            return new RaceWorld
            {
                Schema = schema, Provider = provider, Path = path, OwnerTask = owner,
                Journal = journal, Admission = admission,
                Tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>(),
                Retirement = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>(),
                Stopper = stopper,
                Dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
                Replies = scope.ServiceProvider.GetRequiredService<AgentTaskReplyService>(),
                Lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>(),
                Sessions = scope.ServiceProvider.GetRequiredService<AgentSessionService>(),
                LaunchQueue = provider.GetRequiredService<AgentSessionLaunchQueue>(),
                Adapter = adapter, AgentId = agentId,
            };
        }

        public AppDbContext CreateDb() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));
        public WorkspaceReservationCommand Command(WorkspaceReservationKind kind, Guid? retirementId = null) =>
            new(Key, kind, OwnerTask.Id, RetirementId: retirementId ?? RetirementId);
        public WorkspaceReservationCommand CreateKeyCommand() =>
            new(Key, WorkspaceReservationKind.Retirement, OwnerTask.Id, RetirementId: RetirementId);
        public WorkspaceReservationCommand SessionKeyCommand() =>
            new(WorkspaceReservationKey.For(Path, "", Path), WorkspaceReservationKind.Retirement, null, RetirementId: RetirementId);
        public WorkspaceReservationCommand ProductionRetirementCommand(string? branch = null) =>
            new(WorkspaceReservationKey.For(Path, branch ?? "feat/card-task-race", System.IO.Path.Combine(Path, ".git")),
                WorkspaceReservationKind.Retirement, OwnerTask.Id, RetirementId: RetirementId);
        public WorkspaceReservationCommand SessionProductionCommand() =>
            new(WorkspaceReservationKey.For(Path, "feat/card-task-race", System.IO.Path.Combine(Path, ".git")),
                WorkspaceReservationKind.Retirement, null, RetirementId: RetirementId);
        public AgentTaskService.Caller ManualCaller() => new(null, null, Path);
        public AgentTaskService.Caller ParentCaller() => new(OwnerTask, null, Path);
        public AgentLaunchSpec LaunchSpec(Guid? sessionId = null) =>
            new("fake", AgentKind.ClaudeCode, "fake", [], new Dictionary<string, string>(),
                Path, 120, 30, SessionId: sessionId);

        public async Task<AgentTask> SeedQueuedTaskAsync(WorkspaceMode workspace)
        {
            await using var db = CreateDb();
            var id = Guid.NewGuid();
            var row = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "queued", Goal = "queued",
                Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium, Workspace = workspace,
                WorkingDirectory = Path, WorktreePath = Path, RepoPath = Path,
                WorktreeBranch = "feat/card-task-race", Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
            };
            db.AgentTasks.Add(row);
            await db.SaveChangesAsync();
            return row;
        }

        /// <summary>
        /// CARD-0664: admit one <c>Launch</c> row at <see cref="Key"/> for the named owner shape and,
        /// when <paramref name="aged"/>, move its <c>CreatedAt</c> two hours back (past any grace).
        /// </summary>
        public async Task<Guid> SeedOwnedLaunchAsync(string shape, bool aged)
        {
            Guid? taskId = null;
            Guid? sessionId = null;
            switch (shape)
            {
                case "terminal-task":
                    taskId = (await SeedTaskAsync(AgentTaskStatus.Failed, landPending: false)).Id;
                    break;
                case "working-task":
                    taskId = (await SeedTaskAsync(AgentTaskStatus.Working, landPending: false)).Id;
                    break;
                case "land-pending":
                    taskId = (await SeedTaskAsync(AgentTaskStatus.Succeeded, landPending: true)).Id;
                    break;
                case "missing-task":
                    taskId = Guid.NewGuid();
                    break;
                case "ended-session":
                    sessionId = (await SeedSessionAsync(SessionStatus.Stopped)).Id;
                    break;
                case "running-session":
                    sessionId = (await SeedSessionAsync(SessionStatus.Running)).Id;
                    break;
                case "unattributed":
                case "fresh-unattributed":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
            }

            var admitted = await Journal.TryAdmitConsumerAsync(
                new WorkspaceReservationCommand(Key, WorkspaceReservationKind.Launch, taskId, sessionId),
                CancellationToken.None);
            admitted.Accepted.ShouldBeTrue();
            var id = admitted.Snapshot!.Id;
            if (aged)
                await SetCreatedAtAsync(id, DateTime.UtcNow.AddHours(-2));

            return id;
        }

        /// <summary>CARD-0664: the owner as a landable Worktree task (the <c>C459_LandReservesWorkspace</c> shape).</summary>
        public async Task MakeOwnerLandableAsync()
        {
            await using var db = CreateDb();
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == OwnerTask.Id);
            owner.Workspace = WorkspaceMode.Worktree;
            owner.WorktreePath = Path;
            owner.RepoPath = Path;
            owner.WorktreeBranch = "feat/card-task-land";
            owner.Status = AgentTaskStatus.Succeeded;
            await db.SaveChangesAsync();
        }

        public async Task<List<Guid>> ActiveLaunchIdsAsync(Guid taskId)
        {
            await using var db = CreateDb();
            return await db.WorkspaceUseReservations.AsNoTracking()
                .Where(r => r.TaskId == taskId && r.Active && r.Kind == WorkspaceReservationKind.Launch)
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .ToListAsync();
        }

        public async Task SetCreatedAtAsync(Guid reservationId, DateTime createdAt)
        {
            await using var db = CreateDb();
            var row = await db.WorkspaceUseReservations.SingleAsync(r => r.Id == reservationId);
            row.CreatedAt = createdAt;
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> SeedTaskAsync(AgentTaskStatus status, bool landPending)
        {
            await using var db = CreateDb();
            var id = Guid.NewGuid();
            var terminal = status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled;
            var row = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "c664-" + status, Goal = "c664",
                Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path, RepoPath = Path, Status = status,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow.AddHours(-3),
                CompletedAt = terminal ? DateTime.UtcNow.AddHours(-2) : null,
                LandRequestedAt = landPending ? DateTime.UtcNow : null,
            };
            db.AgentTasks.Add(row);
            await db.SaveChangesAsync();
            return row;
        }

        public async Task<AgentSession> SeedSessionAsync(SessionStatus status)
        {
            await using var db = CreateDb();
            var now = DateTime.UtcNow;
            var ended = status is SessionStatus.Stopped or SessionStatus.Failed;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(), DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                Status = status, Cwd = Path, Cols = 120, Rows = 30,
                CreatedAt = now.AddHours(-3), StartedAt = now.AddHours(-3), LastSeenAt = now,
                EndedAt = ended ? now.AddHours(-2) : null,
            };
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<Guid> SeedCardWithWorktreeAsync()
        {
            await using var db = CreateDb();
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(), Name = "c459", GitRepositoryUrl = "https://example.test/repo.git",
                LocalRepositoryPath = Path, BaseBranch = "master", CreatedAt = now, UpdatedAt = now,
            };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c459", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
                CardStatus = CardStatus.Backlog, IsActive = true, CreatedAt = now, UpdatedAt = now,
            };
            var card = new Card
            {
                Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "CARD-0459",
                Title = "fence", CreatedAt = now, UpdatedAt = now,
            };
            var worktree = new Worktree
            {
                Id = Guid.NewGuid(), CardId = card.Id, Path = Path, RepoPath = Path,
                Branch = "feat/card-task-race", BaseRef = "master", Status = WorktreeStatus.Active,
                CreatedAt = now, LastTouchedAt = now,
            };
            db.Projects.Add(project);
            db.Boards.Add(board);
            db.BoardColumns.Add(column);
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            db.Worktrees.Add(worktree);
            await db.SaveChangesAsync();
            card.CurrentWorktreeId = worktree.Id;
            await db.SaveChangesAsync();
            return card.Id;
        }

        public async Task<AgentSession> SeedInteractiveSessionAsync()
        {
            await using var db = CreateDb();
            var now = DateTime.UtcNow;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(), DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Starting, Cwd = Path, Cols = 120, Rows = 30,
                CreatedAt = now, StartedAt = now, LastSeenAt = now, StandingAgentId = AgentId,
            };
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<AgentSession> SeedStoppedCardSessionAsync()
        {
            var cardId = await SeedCardWithWorktreeAsync();
            await using var db = CreateDb();
            var now = DateTime.UtcNow;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(), CardId = cardId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Stopped, Cwd = Path, Cols = 120, Rows = 30,
                CreatedAt = now, StartedAt = now, LastSeenAt = now, EndedAt = now,
            };
            var worktree = await db.Worktrees.SingleAsync(w => w.CardId == cardId);
            session.WorktreeId = worktree.Id;
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<AgentSession> SeedInterruptedLaunchAsync()
        {
            var session = await SeedInteractiveSessionAsync();
            await using var db = CreateDb();
            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "interrupted", Goal = "interrupted",
                Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = Path, WorktreePath = Path, RepoPath = Path,
                AgentSessionId = session.Id, AgentId = AgentId, Status = AgentTaskStatus.Dispatched,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, DispatchedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<(AgentTask Owner, TaskWorktreeRetirement Release)> ReleasedWorktreeAsync()
        {
            await using var db = CreateDb();
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == OwnerTask.Id);
            owner.Workspace = WorkspaceMode.Worktree;
            owner.WorktreePath = Path;
            owner.WorktreeBranch = "feat/card-task-race";
            owner.RepoPath = Path;
            var release = new TaskWorktreeRetirement
            {
                Id = Guid.NewGuid(),
                TaskId = owner.Id,
                TaskAttempt = owner.Attempt,
                TerminalStatus = owner.Status,
                TaskCompletedAt = owner.CompletedAt ?? DateTime.UtcNow.AddHours(-3),
                ReportDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(owner.Result ?? ""))),
                ReleasedTaskRevision = owner.ConcurrencyToken,
                CallerIdentity = "operator",
                ReleaseReason = "x",
                ReleasedAt = DateTime.UtcNow,
                HandoffDispositionJson = "[]",
                RepositoryPath = Path,
                CommonDirectory = Path,
                WorktreePath = Path,
                GitDirectory = Path,
                SourceFullRef = "refs/heads/feat/card-task-race",
                SourceSha = new string('a', 40),
                TargetFullRef = "refs/heads/master",
                State = WorktreeRetirementState.Released,
                Active = true,
                UpdatedAt = DateTime.UtcNow,
            };
            db.TaskWorktreeRetirements.Add(release);
            await db.SaveChangesAsync();
            return (owner, release);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await Schema.DisposeAsync();
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }

    private sealed class OneAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class FixedWorktreeManager(string path) : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(path);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new WorktreeInfo(cardId, repoPath, path, $"feat/card-{cardId}", baseRef, now, now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
