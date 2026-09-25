using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0633 D-4..D-8: a runner-bound task's branch push and runner mirror run as an owned
/// background operation, never inside the claim transaction or on the serial tick. The runner is
/// a real phone-home connection on a fake clock, so a silent mirror is advanced, never slept.
/// </summary>
[Category("Integration")]
public sealed class RemoteWorkspacePreparerTests
{
    private static readonly TimeSpan TickBound = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MirrorBudget =
        PhoneHomeLiveConnection.RequestTimeoutFor(PhoneHomeOperation.WorkspaceMirror);

    [Test]
    public async Task Mirror_runs_outside_the_claim_and_the_task_stays_queued_with_an_in_flight_hold()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();

        await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.RemoteWorktreePath.ShouldBeNull();
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldStartWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
        await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);

        await rig.TickAsync().WaitAsync(TickBound);

        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
        (await rig.EventsAsync(taskId, AgentTaskEventType.Held)).Count.ShouldBe(1);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Mirror_success_is_recorded_and_the_next_tick_launches_into_it()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        await rig.TickAsync().WaitAsync(TickBound);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);

        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        await rig.Peer.EmitAsync(MirrorResult(request, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);

        var prepared = await rig.ReadTaskAsync(taskId);
        prepared.RemoteWorktreePath.ShouldBe(mirror);
        prepared.RemotePrepFailures.ShouldBe(0);
        prepared.DispatchNotBeforeAt.ShouldBeNull();

        await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        await using var db = rig.NewDb();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.RunnerCwd.ShouldBe(mirror);
        session.Cwd.ShouldBe(rig.WorkspacePath);
        rig.Sink.Specs.Single().Cwd.ShouldBe(mirror);
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
    }

    [Test]
    public async Task Mirror_failure_backs_off_exponentially_and_resets_on_success()
    {
        await using var rig = await Rig.StartAsync(s =>
        {
            s.RemotePrepBackoffBaseSeconds = 30;
            s.RemotePrepBackoffMaxSeconds = 120;
        });
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        // Attempt 1 times out; the retry waits 30 s.
        await rig.TickAsync().WaitAsync(TickBound);
        await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        await rig.AdvanceAsync(MirrorBudget);
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        var failed = await rig.ReadTaskAsync(taskId);
        failed.RemotePrepFailures.ShouldBe(1);
        failed.DispatchNotBeforeAt.ShouldBe((DateTime?)(rig.Now + TimeSpan.FromSeconds(30)));
        var warnings = await rig.EventsAsync(taskId, AgentTaskEventType.Warning);
        warnings.Count.ShouldBe(1);
        warnings[0].Detail.ShouldContain(PhoneHomeProblemTypes.RequestTimeout);

        await rig.TickAsync().WaitAsync(TickBound);
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1, "the not-before gate must hold the task");
        var backoffHold = (await rig.EventsAsync(taskId, AgentTaskEventType.Held)).Last();
        backoffHold.Detail.ShouldBe(DispatchHoldDetails.RemotePrepBackoff(
            rig.RunnerId, 1, failed.DispatchNotBeforeAt!.Value));

        // Attempts 2..4: 60 s, 120 s, then the 120 s cap.
        var expected = new[] { 60, 120, 120 };
        var previousDelay = 30;
        for (var attempt = 2; attempt <= 4; attempt++)
        {
            await rig.AdvanceAsync(TimeSpan.FromSeconds(previousDelay));
            await rig.TickAsync().WaitAsync(TickBound);
            await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, attempt);
            await rig.AdvanceAsync(MirrorBudget);
            await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
            var row = await rig.ReadTaskAsync(taskId);
            row.RemotePrepFailures.ShouldBe(attempt);
            row.DispatchNotBeforeAt.ShouldBe((DateTime?)(rig.Now + TimeSpan.FromSeconds(expected[attempt - 2])));
            previousDelay = expected[attempt - 2];
        }

        // Attempt 5 succeeds and resets both columns.
        await rig.AdvanceAsync(TimeSpan.FromSeconds(previousDelay));
        await rig.TickAsync().WaitAsync(TickBound);
        var last = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 5);
        await rig.Peer.EmitAsync(MirrorResult(last, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        var reset = await rig.ReadTaskAsync(taskId);
        reset.RemoteWorktreePath.ShouldBe(mirror);
        reset.RemotePrepFailures.ShouldBe(0);
        reset.DispatchNotBeforeAt.ShouldBeNull();
        (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).Count.ShouldBe(4);
    }

    [Test]
    public async Task Full_runner_holds_before_remote_prep_and_the_trace_is_deduped()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        await rig.SeedOccupantAsync(SessionStatus.Running);
        var taskId = await rig.SeedAsync();

        await rig.TickAsync().WaitAsync(TickBound);
        await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.RemoteWorktreePath.ShouldBeNull();
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldContain($"runner '{rig.RunnerId}' at capacity 1/1");
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        (await rig.EventsAsync(taskId, AgentTaskEventType.Dispatched)).ShouldBeEmpty();
    }

    [Test]
    public async Task Failed_and_stopped_runner_sessions_do_not_reserve_a_slot()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        await rig.SeedOccupantAsync(SessionStatus.Failed);
        await rig.SeedOccupantAsync(SessionStatus.Stopped);
        var taskId = await rig.SeedAsync();

        await rig.TickAsync().WaitAsync(TickBound);

        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.ShouldNotContain(e => e.Detail.Contains("at capacity", StringComparison.Ordinal));
        await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
    }

    [Test]
    public async Task Runner_not_eligible_is_one_held_trace_not_a_warning_per_tick()
    {
        await using var rig = await Rig.StartAsync(recovered: false);
        var taskId = await rig.SeedAsync();

        for (var i = 0; i < 3; i++)
            await rig.TickAsync().WaitAsync(TickBound);

        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldContain("RunnerUnavailable");
        held[0].Detail.ShouldContain("has not completed recovery");
        (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).ShouldBeEmpty();
        (await rig.EventsAsync(taskId, AgentTaskEventType.Dispatched)).ShouldBeEmpty();
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task A_process_restart_re_arms_the_mirror_and_the_row_is_written_once()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        await rig.TickAsync().WaitAsync(TickBound);
        var first = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);

        // Process death, in-process: the provider (and with it the preparer's registry) is gone.
        await rig.RestartAsync();
        rig.Preparer.IsInFlight(taskId, out _).ShouldBeFalse();

        await rig.TickAsync().WaitAsync(TickBound);
        var second = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 2);
        var a = first.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var b = second.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        b.ShouldBe(a);

        // The first request's waiter died with provider A; only the re-armed one is answered.
        await rig.Peer.EmitAsync(MirrorResult(second, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.RemoteWorktreePath.ShouldBe(mirror);
        task.RemotePrepFailures.ShouldBe(0);
        task.DispatchNotBeforeAt.ShouldBeNull();
        (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).ShouldBeEmpty(
            "the cancelled operation on the dead provider must write nothing");
    }

    [Test]
    public async Task Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        var tick = rig.TickAsync();
        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);

        // CARD-0629: this cancel used to block on the claim's FOR UPDATE lock for the whole mirror.
        using (var scope = rig.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CancelAsync(taskId, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        await tick.WaitAsync(TickBound);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Canceled);

        await rig.Peer.EmitAsync(MirrorResult(request, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Canceled);
        task.RemoteWorktreePath.ShouldBe(mirror, "a late mirror must stay known to retirement");
    }

    [Test]
    [Arguments("prepared")]
    [Arguments("unprepared")]
    [Arguments("repair-source")]
    [Arguments("snapshot")]
    [Arguments("interim")]
    public async Task C672_prepared_task_launches_while_a_land_holds_the_lease(string arm)
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var mirror = "/work/worktrees/c672-" + arm;
        Guid? repairOwner = arm == "repair-source" ? await rig.SeedOwnerAsync() : null;
        Guid? snapshotOperation = arm == "snapshot" ? await rig.SeedLandingAsync(await rig.SeedOwnerAsync()) : null;
        var taskId = await rig.SeedAsync(t =>
        {
            t.RemoteWorktreePath = arm == "unprepared" ? null : mirror;
            t.RepairSourceTaskId = repairOwner;
            t.SourceLandingOperationId = snapshotOperation;
            if (arm == "interim")
                t.VerificationRound = VerificationRound.Interim;
        });
        rig.Lease.Held = true;

        await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        if (arm == "prepared")
        {
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            rig.Sink.Specs.ShouldHaveSingleItem().Cwd.ShouldBe(mirror);
            held.ShouldNotContain(e => e.Detail.Contains("repository mutation lease", StringComparison.Ordinal));
            rig.Waiters.IsEmpty.ShouldBeTrue();
        }
        else
        {
            task.Status.ShouldBe(AgentTaskStatus.Queued);
            held.ShouldHaveSingleItem().Detail.ShouldBe(DispatchHoldDetails.LeaseHeldByOwner(
                rig.Lease.HolderTaskId, RepositoryLeasePurposes.Land, rig.Lease.AcquiredAt));
            rig.Sink.Specs.ShouldBeEmpty();
            rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        }
    }

    [Test]
    public async Task C672_three_queued_runner_tasks_cross_the_lease_once_and_launch_behind_the_next_land()
    {
        await using var rig = await Rig.StartAsync(capacity: 3);
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            // Each its own desktop worktree and branch, as three real tasks would have.
            var worktree = Directory.CreateDirectory(Path.Combine(rig.WorkspacePath, $"wt-{i}")).FullName;
            ids.Add(await rig.SeedAsync(t =>
            {
                t.WorktreePath = worktree;
                t.WorktreeBranch = $"feat/test-remote-prep-{i}";
            }));
        }
        var common = Path.GetFullPath(rig.WorkspacePath);

        // (1) A land holds the lease: every first crossing is refused and waits for it.
        rig.Lease.Held = true;
        (await rig.TickAsync().WaitAsync(TickBound)).HeldOnLease.ShouldBe(3);
        foreach (var id in ids)
        {
            (await rig.EventsAsync(id, AgentTaskEventType.Held)).ShouldHaveSingleItem().Detail
                .ShouldContain("repository mutation lease");
        }
        var waiting = rig.Waiters.Snapshot(common);
        waiting.Select(w => w.TaskId).ShouldBe(ids, ignoreOrder: true);
        waiting.ShouldAllBe(w => w.Purpose == RepositoryLeasePurposes.Dispatch);

        // (2) The gap: each crosses once, the preparer mirrors all three off the tick.
        rig.Lease.Held = false;
        await rig.TickAsync().WaitAsync(TickBound);
        rig.Waiters.IsEmpty.ShouldBeTrue();
        foreach (var id in ids)
        {
            (await rig.EventsAsync(id, AgentTaskEventType.Held)).Last().Detail
                .ShouldStartWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
        }
        for (var n = 1; n <= 3; n++)
        {
            var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, n);
            await rig.Peer.EmitAsync(MirrorResult(request, "/work/worktrees/c672-mirror-" + n));
        }
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        foreach (var id in ids)
            (await rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldNotBeNull();
        var heldBefore = new Dictionary<Guid, int>();
        foreach (var id in ids)
            heldBefore[id] = (await rig.EventsAsync(id, AgentTaskEventType.Held)).Count;

        // (3) The next land holds the lease again: the prepared launches do not need it.
        rig.Lease.Held = true;
        var launched = await rig.TickAsync().WaitAsync(TickBound);
        launched.Dispatched.ShouldBe(3);
        launched.HeldOnLease.ShouldBe(0);
        foreach (var id in ids)
        {
            var task = await rig.ReadTaskAsync(id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            (await rig.EventsAsync(id, AgentTaskEventType.Held)).Count.ShouldBe(heldBefore[id]);
        }
        rig.Sink.Specs.Count.ShouldBe(3);
        rig.Waiters.IsEmpty.ShouldBeTrue();
    }

    private static PhoneHomeFrame MirrorResult(PhoneHomeFrame request, string path) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceMirrorResponse(path), PhoneHomeFraming.Json));

    private sealed class Rig : IAsyncDisposable
    {
        private readonly List<IServiceScope> _scopes = [];
        private Action<DelegationSettings>? _tweak;
        private bool _recovered;

        public IsolatedTestSchema Schema { get; private set; } = null!;
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeScriptedPeer Peer { get; private set; } = null!;
        public PhoneHomeLiveConnection Live { get; private set; } = null!;
        public FakeTimeProvider Clock { get; } =
            new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        public ServiceProvider Provider { get; private set; } = null!;
        public RecordingLaunchSink Sink { get; } = new();
        public string WorkspacePath { get; } =
            Directory.CreateTempSubdirectory("antiphon-remote-prep-ws").FullName;
        public string RunnerId => Host.AllowedRunnerId;
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public RemoteWorkspacePreparer Preparer => Provider.GetRequiredService<RemoteWorkspacePreparer>();

        /// <summary>CARD-0672: the repository mutation lease every dispatch in this rig sees.</summary>
        public HoldableLease Lease { get; } = new();
        public RepositoryLeaseWaiters Waiters => Provider.GetRequiredService<RepositoryLeaseWaiters>();

        public static async Task<Rig> StartAsync(
            Action<DelegationSettings>? tweak = null, bool recovered = true, int capacity = 1)
        {
            var rig = new Rig { _tweak = tweak, _recovered = recovered };
            rig.Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            rig.Host = await PhoneHomeTestHost.StartAsync(rig.Clock);
            rig.Host.Capacity = capacity;
            rig.Peer = await rig.Host.ConnectPeerAsync();
            rig.Live = await rig.Host.WaitLiveAsync();
            if (recovered)
                rig.Host.Directory.MarkRecovered(rig.Live);
            rig.Provider = rig.BuildProvider();
            return rig;
        }

        public AppDbContext NewDb() =>
            new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        /// <summary>One tick on a fresh scope, as the hosted service resolves one per tick.</summary>
        public Task<AgentTaskDispatcher.TickResult> TickAsync()
        {
            var scope = Provider.CreateScope();
            _scopes.Add(scope);
            return scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        }

        /// <summary>
        /// Moves the shared fake clock. The runner's lease is on the same clock, so the heartbeat
        /// is renewed with it: this test is about the mirror budget, not lease expiry.
        /// </summary>
        public async Task AdvanceAsync(TimeSpan by)
        {
            // The request's budget timer is armed right after its frame is written; let the server
            // reach it before moving the clock (as PhoneHomeConnectionTests does).
            await Task.Delay(100);
            Clock.Advance(by);
            Live.NoteHeartbeat(Clock.GetUtcNow());
            if (_recovered)
                Host.Directory.MarkRecovered(Live);
        }

        public async Task<PhoneHomeFrame> WaitForRequestsAsync(PhoneHomeOperation operation, int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (Peer.RequestCount(operation) >= count)
                {
                    return Peer.Incoming.ToArray()
                        .Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == operation)
                        .ElementAt(count - 1);
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Expected {count} {operation} request(s); saw {Peer.RequestCount(operation)}.");
        }

        public async Task SeedOccupantAsync(SessionStatus status)
        {
            var now = Now;
            await using var db = NewDb();
            db.AgentSessions.Add(new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = status,
                Cwd = WorkspacePath,
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                EndedAt = status is SessionStatus.Failed or SessionStatus.Stopped ? now : null,
                RunnerId = RunnerId,
                RunnerStoreId = Host.StoreId,
                RunnerCwd = "/work",
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedAsync(Action<AgentTask>? tweak = null)
        {
            var now = Now;
            var id = Guid.NewGuid();
            await using var db = NewDb();
            // CARD-0672: RepoPath makes the claim consult the rig's lease, as production does.
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "remote task", Goal = "reply",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = WorkspacePath, RepoPath = WorkspacePath, WorktreePath = WorkspacePath,
                WorktreeBranch = "feat/test-remote-prep",
                RunnerId = RunnerId, Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
            };
            tweak?.Invoke(task);
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return id;
        }

        /// <summary>A settled Worktree task a repair or a verification snapshot can name.</summary>
        public async Task<Guid> SeedOwnerAsync()
        {
            var id = Guid.NewGuid();
            await using var db = NewDb();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "owner", Goal = "owner", Role = AgentTaskRole.Code,
                Workspace = WorkspaceMode.Worktree, WorkingDirectory = WorkspacePath, RepoPath = WorkspacePath,
                WorktreeBranch = "feat/test-remote-owner", Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = Now, CompletedAt = Now,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> SeedLandingAsync(Guid ownerId)
        {
            var id = Guid.NewGuid();
            await using var db = NewDb();
            db.AgentTaskLandings.Add(new AgentTaskLanding
            {
                Id = id, TaskId = ownerId, CreatedAt = Now, UpdatedAt = Now,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<AgentTask> ReadTaskAsync(Guid id)
        {
            await using var db = NewDb();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(Guid id, AgentTaskEventType type)
        {
            await using var db = NewDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == id && e.Type == type)
                .OrderBy(e => e.At).ThenBy(e => e.Id)
                .ToListAsync();
        }

        /// <summary>Drops the whole provider (registry included) and builds a new one.</summary>
        public async Task RestartAsync()
        {
            foreach (var scope in _scopes)
                scope.Dispose();
            _scopes.Clear();
            await Provider.DisposeAsync();
            Provider = BuildProvider();
        }

        private ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(Schema.ConnectionString, n =>
            {
                n.MigrationsAssembly("Antiphon.Server");
                n.SetPostgresVersion(16, 0);
            }));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            var delegation = new DelegationSettings { MaxConcurrentTasks = 512, AllowedRoots = ["C:\\", "/"] };
            _tweak?.Invoke(delegation);
            services.AddSingleton(Options.Create(delegation));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "grok";
                s.GrokCredentialProbeEnabled = false;
                s.Definitions["grok"] = new AgentDefinition
                {
                    Kind = "Grok", Exe = "grok.exe", ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-remote-prep-{Guid.NewGuid():N}"),
            });
            services.AddSingleton<IRepositoryMutationLease>(Lease);
            services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true, AllowedRunnerId = Host.AllowedRunnerId, AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\src\Antiphon", CallbackOrigin = "https://antiphon.desktop.codeperf.net",
                SharedSecret = "x", ClaudeAuthProbeEnabled = false,
            }));
            services.AddSingleton<PhoneHomeLaunchPolicy>();
            services.AddSingleton<ISessionRunnerDirectory>(Host.Directory);
            services.AddSingleton<ILandingGit, PushOnlyGit>();
            services.AddSingleton<RemoteWorkspaceService>();
            services.AddSingleton<RemoteWorkspacePreparer>();
            services.AddSingleton<IAgentTaskLaunchSink>(Sink);
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            return services.BuildServiceProvider();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
                scope.Dispose();
            await Provider.DisposeAsync();
            await Peer.DisposeAsync();
            await Host.DisposeAsync();
            await Schema.DisposeAsync();
            try { Directory.Delete(WorkspacePath, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Specs.Add(spec);
    }

    /// <summary>
    /// CARD-0672: a lease a test holds on behalf of a running land. While held, every acquire is
    /// refused and the owner reads as a Known <c>land</c> holder, as the real lease reports one.
    /// </summary>
    internal sealed class HoldableLease : IRepositoryMutationLease
    {
        public bool Held { get; set; }
        public Guid HolderTaskId { get; } = Guid.NewGuid();
        public DateTimeOffset AcquiredAt { get; } = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);

        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            Task.FromResult(Held ? null : (RepositoryLease?)new TrivialLease(repository));

        public bool Owns(RepositoryLease lease, string commonDirectory) => lease is TrivialLease;

        public Task<RepositoryLeaseOwner?> FindOwnerAsync(string repository, CancellationToken ct) =>
            Task.FromResult<RepositoryLeaseOwner?>(Held
                ? new RepositoryLeaseOwner(RepositoryLeaseOwnerState.Known, HolderTaskId, RepositoryLeasePurposes.Land,
                    Guid.NewGuid(), AcquiredAt)
                : RepositoryLeaseOwner.Unknown);

        private sealed class TrivialLease(string repository) : RepositoryLease
        {
            public override string CommonDirectory { get; } = Path.GetFullPath(repository);
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class PushOnlyGit : ILandingGit
    {
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            Task.FromResult(arguments[0] switch
            {
                "rev-parse" => new LandingGitResult(0, new string('1', 40), ""),
                "push" => new LandingGitResult(0, "", ""),
                _ => throw new NotSupportedException(arguments[0]),
            });
        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) =>
            Task.FromResult(Path.GetFullPath(repository));
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
}
