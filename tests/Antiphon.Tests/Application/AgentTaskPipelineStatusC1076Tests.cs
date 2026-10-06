using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskPipelineStatusTests
{
    /// <summary>
    /// CARD-1076 D-6. The latest Held row of the current stint (after the last Dispatched
    /// event) names a repository lease. The owner sentence supplies heldBy; a fence does not;
    /// a running land on the same repository does when the sentence names no owner; a lease
    /// row from the previous stint does not.
    /// </summary>
    [Test]
    [Arguments("owner-text")]
    [Arguments("fenced-text")]
    [Arguments("running-land")]
    [Arguments("previous-stint")]
    public async Task C1076_a_dispatcher_lease_hold_is_the_queued_reason_and_names_the_owner(string arm)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var when = DateTime.UtcNow.AddHours(-2);
        var owner = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            title: "lease owner", workspace: WorkspaceMode.Worktree, repoPath: workspace.Path,
            completedAt: when);
        var waiting = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Queued,
            title: "waiting on the lease", workspace: WorkspaceMode.Worktree, repoPath: workspace.Path);

        var detail = arm switch
        {
            "owner-text" => DispatchHoldDetails.LeaseHeldByOwner(owner.Id, "land", when),
            "fenced-text" => DispatchHoldDetails.LeaseFenced("unfinished repository child journal"),
            "running-land" => DispatchHoldDetails.LeaseHeldByLand(
                DelegationReportFormatter.Short(owner.Id), owner.Title, "abcd1234", when),
            "previous-stint" => DispatchHoldDetails.LeaseHeldByOwner(owner.Id, "land", when),
            _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, "unknown arm"),
        };
        AddEvent(db, waiting.Id, AgentTaskEventType.Held, detail, when);
        if (arm == "previous-stint")
            AddEvent(db, waiting.Id, AgentTaskEventType.Dispatched, "dispatched", when.AddHours(1));
        if (arm == "running-land")
        {
            db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(),
                TaskId = owner.Id,
                RequestedAt = when,
                LastEvaluatedAt = when,
                LastProgressAt = when,
                StartedAt = when,
                State = LandRequestState.Running,
                IsPending = true,
                ExpectedSourceSha = new string('a', 40),
            });
        }

        await db.SaveChangesAsync();

        var row = await QueuedRowAsync(db, waiting.Id);
        if (arm == "previous-stint")
        {
            row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonAwaitingDispatch);
            row.HeldBy.ShouldBeEmpty();
            return;
        }

        row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonRepositoryLease);
        if (arm == "fenced-text")
        {
            row.HeldBy.ShouldBeEmpty();
            return;
        }

        row.HeldBy.Select(h => h.TaskId).ShouldBe([owner.Id]);
        row.HeldBy.Single().Title.ShouldBe("lease owner");
    }

    /// <summary>
    /// CARD-1076 D-6. An in-flight or backing-off remote preparation is remotePrep.
    /// A dated card pin still outranks it.
    /// </summary>
    [Test]
    [Arguments("in-flight")]
    [Arguments("backoff")]
    [Arguments("pin-outranks")]
    public async Task C1076_remote_preparation_in_flight_or_backing_off_is_the_queued_reason(string arm)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var card = arm == "pin-outranks"
            ? await SeedCardAsync(db, CardStatus.InProgress, "CARD-1076")
            : null;
        var waiting = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Queued,
            title: "waiting on preparation", cardId: card?.Id, workspace: WorkspaceMode.Worktree,
            repoPath: workspace.Path);
        var when = DateTime.UtcNow.AddMinutes(-5);
        var detail = arm == "backoff"
            ? DispatchHoldDetails.RemotePrepBackoff("server2", 2, when.AddMinutes(30))
            : DispatchHoldDetails.RemoteMirrorRequested("server2", when);
        AddEvent(db, waiting.Id, AgentTaskEventType.Held, detail, when);
        if (card is not null)
        {
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                Role = AgentTaskRole.Code,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.Grok,
                NotBefore = DateTime.UtcNow.AddHours(6),
                Reason = "dated pin outranks remote preparation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        var row = await QueuedRowAsync(db, waiting.Id);
        if (arm == "pin-outranks")
        {
            row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonRoutingPinNotBefore);
            row.HeldBy.ShouldBeEmpty();
            return;
        }

        row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonRemotePrep);
        row.HeldBy.ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-1076 D-4/D-6. heldBy is the preparer's enqueue-time BehindTaskId. With two tasks
    /// that snapshot is the task holding the push gate. CARD-1093: a third waiter can name a
    /// pusher that has already finished, or null. This test does not treat the field as a
    /// live reading of the current holder.
    /// </summary>
    [Test]
    public async Task C1076_a_task_waiting_for_its_push_turn_names_the_pushing_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var pushing = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Queued,
            title: "pushing the branch", workspace: WorkspaceMode.Worktree, repoPath: workspace.Path);
        var waiting = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Queued,
            title: "waiting for the push", workspace: WorkspaceMode.Worktree, repoPath: workspace.Path);
        pushing.WorktreePath = Path.Combine(workspace.Path, "pushing");
        pushing.WorktreeBranch = "feat/card-1076-pushing";
        waiting.WorktreePath = Path.Combine(workspace.Path, "waiting");
        waiting.WorktreeBranch = "feat/card-1076-waiting";
        var when = DateTime.UtcNow.AddMinutes(-1);
        AddEvent(db, waiting.Id, AgentTaskEventType.Held,
            DispatchHoldDetails.RemoteMirrorRequested("server2", when), when);
        await db.SaveChangesAsync();

        var git = new BlockingPushGit();
        var services = new ServiceCollection();
        services.AddSingleton<ILandingGit>(git);
        services.AddSingleton<ISessionRunnerDirectory>(new UnusedRunnerDirectory());
        services.AddSingleton<ILogger<RemoteWorkspaceService>>(NullLogger<RemoteWorkspaceService>.Instance);
        services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)));
        services.AddScoped<RemoteWorkspaceService>();
        await using var provider = services.BuildServiceProvider();
        await using var preparer = new RemoteWorkspacePreparer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<RemoteWorkspacePreparer>.Instance);
        try
        {
            preparer.TryBegin(pushing.Id, "server2").ShouldBeTrue();
            // InFlight.Phase defaults to Pushing before the gate is acquired. The blocked push
            // call is the signal that this task holds the turn.
            await git.PushEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            preparer.Progress(pushing.Id)?.Phase.ShouldBe(RemotePrepPhase.Pushing);

            preparer.TryBegin(waiting.Id, "server2").ShouldBeTrue();
            var waiterSeen = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < waiterSeen
                && preparer.Progress(waiting.Id)?.BehindTaskId != pushing.Id)
                await Task.Delay(20);
            // Two tasks: the snapshot recorded when the waiter entered the gate is the pusher.
            preparer.Progress(waiting.Id)?.BehindTaskId.ShouldBe(pushing.Id);

            var row = await QueuedRowAsync(db, waiting.Id, preparer);
            row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonRemotePrep);
            row.HeldBy.Select(h => h.TaskId).ShouldBe([pushing.Id]);
            row.HeldBy.Single().Title.ShouldBe("pushing the branch");
        }
        finally
        {
            git.PushHold.TrySetResult();
            await preparer.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private static async Task<AgentTaskPipelineQueuedDto> QueuedRowAsync(
        AppDbContext db, Guid taskId, RemoteWorkspacePreparer? preparer = null)
    {
        var dto = await CreateService(db, remotePrep: preparer).GetAsync(CancellationToken.None);
        return dto.Stages.SelectMany(s => s.Queued).Single(t => t.TaskId == taskId);
    }

    private static void AddEvent(
        AppDbContext db, Guid taskId, AgentTaskEventType type, string detail, DateTime at)
    {
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            Detail = detail,
            At = at,
        });
    }

    /// <summary>Push blocks until <see cref="PushHold"/> completes. The reads before it succeed.</summary>
    private sealed class BlockingPushGit : ILandingGit
    {
        public TaskCompletionSource PushEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PushHold { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<LandingGitResult> RunAsync(
            string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (arguments.Count > 0 && arguments[0] == "push")
            {
                PushEntered.TrySetResult();
                await PushHold.Task.WaitAsync(ct);
                return new LandingGitResult(1, "", "held");
            }

            return arguments[0] switch
            {
                "rev-parse" => new LandingGitResult(0, new string('a', 40), ""),
                "remote" => new LandingGitResult(0, "https://github.com/example/antiphon.git", ""),
                _ => new LandingGitResult(1, "", "unused"),
            };
        }

        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => Task.FromResult(Path.GetFullPath(repository));
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class UnusedRunnerDirectory : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => throw new NotSupportedException();
        public ISessionRunnerClient Resolve(string? runnerId) => throw new NotSupportedException();
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<string> KnownRunnerIds => [];
        public Guid? GetLiveStoreId(string? runnerId) => null;
    }
}
