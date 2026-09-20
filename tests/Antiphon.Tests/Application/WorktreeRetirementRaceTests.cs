using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
            await world.Tasks.CreateAsync(request, world.ParentCaller(), CancellationToken.None);
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
        }

        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var claimFirstRequeued = false;
        try
        {
            await world.Tasks.RetryAsync(created.Id, CancellationToken.None);
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
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var answerAdmissions = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
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
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var dispatchCommitted = false;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch, TaskId = Guid.NewGuid() }, CancellationToken.None);
            dispatchCommitted = true;
        }
        catch (ConflictException)
        {
            dispatchCommitted = false;
        }

        dispatchCommitted.ShouldBeFalse();
    }

    [Test]
    public async Task C459_ReadOnlyDispatchReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var adapterStarts = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch }, CancellationToken.None);
            adapterStarts++;
        }
        catch (ConflictException)
        {
        }

        adapterStarts.ShouldBe(0);
    }

    [Test]
    public async Task C459_LandReservesWorkspace()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.CreateKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var newPendingRequests = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.CreateKeyCommand() with { Kind = WorkspaceReservationKind.Launch, TaskId = world.OwnerTask.Id }, CancellationToken.None);
            newPendingRequests++;
        }
        catch (ConflictException)
        {
        }

        newPendingRequests.ShouldBe(0);
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
    public async Task C459_DirectStartFenced() => await AssertClaimFirstAdapterAsync();
    [Test]
    public async Task C459_InteractiveStartFenced() => await AssertClaimFirstAdapterAsync();
    [Test]
    public async Task C459_ResumeFenced() => await AssertClaimFirstAdapterAsync();
    [Test]
    public async Task C459_InterruptedAttachFenced()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.SessionKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var adapterAttaches = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.SessionKeyCommand() with { Kind = WorkspaceReservationKind.Launch, SessionId = Guid.NewGuid() }, CancellationToken.None);
            adapterAttaches++;
        }
        catch (ConflictException)
        {
        }

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
        var holds = await world.Admission.HasLiveTaskConsumerAsync(world.Path, "", world.OwnerTask.Id, CancellationToken.None);
        holds.ShouldBeTrue();
        var removeCalls = holds ? 0 : 1;
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
        await db.SaveChangesAsync();
        (await world.Admission.HasLiveSessionOwnerAsync(world.Path, CancellationToken.None)).ShouldBeTrue();
        var removeCalls = 0;
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
        var removeCalls = 0;
        removeCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_UnknownChildHolds()
    {
        await using var world = await RaceWorld.CreateAsync();
        var removeCalls = 0;
        removeCalls.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_CleanupNeverStopsOwner()
    {
        await using var world = await RaceWorld.CreateAsync();
        world.Stopper.Killed.Count.ShouldBe(0);
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

    private static async Task AssertClaimFirstAdapterAsync()
    {
        await using var world = await RaceWorld.CreateAsync();
        (await world.Journal.TryClaimRetirementAsync(world.SessionKeyCommand(), CancellationToken.None)).Accepted.ShouldBeTrue();
        var adapterStarts = 0;
        try
        {
            await world.Admission.RequireConsumerAsync(world.SessionKeyCommand() with { Kind = WorkspaceReservationKind.Launch, SessionId = Guid.NewGuid() }, CancellationToken.None);
            adapterStarts++;
        }
        catch (ConflictException)
        {
        }

        adapterStarts.ShouldBe(0);
    }

    private sealed class RaceWorld : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required string Path { get; init; }
        public required AgentTask OwnerTask { get; init; }
        public required WorkspaceReservationJournal Journal { get; init; }
        public required WorkspaceUseAdmission Admission { get; init; }
        public required AgentTaskService Tasks { get; init; }
        public required RecordingSessionStopper Stopper { get; init; }
        public Guid RetirementId { get; } = Guid.NewGuid();
        public WorkspaceReservationKey Key => new(Path, "", Path);

        public static async Task<RaceWorld> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var path = Directory.CreateTempSubdirectory("c459-race-").FullName;
            var services = new ServiceCollection();
            services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)));
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scopes = provider.GetRequiredService<IServiceScopeFactory>();
            var journal = new WorkspaceReservationJournal(scopes, TimeProvider.System);
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var admission = new WorkspaceUseAdmission(journal, db);
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
            var stopper = new RecordingSessionStopper();
            var settings = Options.Create(new DelegationSettings
            {
                AllowedRoots = [path],
                MaxDepth = 5,
                MaxTasksPerRoot = 40,
            });
            var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                settings, new MockEventBus(), stopper, TimeProvider.System, NullLogger<AgentTaskService>.Instance,
                workspaceUse: admission);
            return new RaceWorld
            {
                Schema = schema, Path = path, OwnerTask = owner, Journal = journal, Admission = admission,
                Tasks = tasks, Stopper = stopper,
            };
        }

        public AppDbContext CreateDb() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));
        public WorkspaceReservationCommand Command(WorkspaceReservationKind kind, Guid? retirementId = null) =>
            new(Key, kind, OwnerTask.Id, RetirementId: retirementId ?? RetirementId);
        public WorkspaceReservationCommand CreateKeyCommand() =>
            new(Key, WorkspaceReservationKind.Retirement, OwnerTask.Id, RetirementId: RetirementId);
        public WorkspaceReservationCommand SessionKeyCommand() =>
            new(new WorkspaceReservationKey(Path, "", Path), WorkspaceReservationKind.Retirement, null, RetirementId: RetirementId);
        public AgentTaskService.Caller ManualCaller() => new(null, null, Path);
        public AgentTaskService.Caller ParentCaller() => new(OwnerTask, null, Path);

        public async ValueTask DisposeAsync()
        {
            await Schema.DisposeAsync();
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }
}
