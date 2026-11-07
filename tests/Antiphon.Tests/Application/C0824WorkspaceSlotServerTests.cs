using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using Rig = Antiphon.Tests.Application.RemoteWorkspacePreparerTests.Rig;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S1 CP-4. The server half of the runner workspace slot pool, against a scripted phone-home
/// peer: the flag-off mirror request is unchanged, a slot lease carries Cold, a pool bound holds the task
/// without a fallback mirror, legacy removal treats slot_managed as removed, ownership compares
/// (slot, generation), and the release reconciler needs every D-10 condition.
/// </summary>
[Category("Integration")]
// CARD-1660 (R5): each test builds its own rig, and the class runs its 24 cases in parallel. Measured on
// server2 on 2026-11-02 at 30cb5279b: per-test p95 12.6-15.8 s alone (load 17-28), 17.2 s with 16 busy
// threads beside it (load 37). The budget is 2x that loaded p95; it was 20 s and went red under load.
[Timeout(35_000)]
public sealed class C0824WorkspaceSlotServerTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    private static RunnerCapabilitiesDto SlotRunner() =>
        new("InboxConhost", "inbox", "test", false, Features: [RunnerCapabilityFeatures.WorkspaceSlotV1], VerificationCustodyBackend: null);

    private static Task<Rig> SlotRigAsync(Action<IServiceCollection>? configure = null) =>
        Rig.StartAsync(settings => settings.WorkspaceSlotsEnabled = true, configure: configure, capabilities: SlotRunner());

    private static HashSet<string> PropertyNames(PhoneHomeFrame request) =>
        request.Payload!.Value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The handoff digest of a default request from before CARD-0824 S1: <c>new CreateAgentTaskRequest("goal")</c>
    /// through <c>AgentTaskService.HandoffRequestDigest</c> of a master build without <c>WorkspaceColdReset</c>
    /// (143449423; the record's 41 parameters end at <c>FreshWorktree</c> and are unchanged since ead93feb0).
    /// </summary>
    private const string PreSlotDefaultHandoffDigest = "5DCDE154371E04DE1AD48AB39D1A7214CAF4E1D9677A6672B56CB45A85460023";

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public void C0824_DefaultHandoffDigestIsThePreSlotDigest()
    {
        // V-12 (review f4c293bc D-2). A handoff action stored before the land keeps its digest: a default request omits
        // WorkspaceColdReset and hashes byte-identically to the pre-field record; a cold request still differs.
        var plain = new CreateAgentTaskRequest("goal");
        JsonSerializer.SerializeToElement(plain).TryGetProperty(nameof(CreateAgentTaskRequest.WorkspaceColdReset), out _)
            .ShouldBeFalse("the default is omitted");
        AgentTaskService.HandoffRequestDigest(plain).ShouldBe(PreSlotDefaultHandoffDigest);
        var cold = plain with { WorkspaceColdReset = true };
        JsonSerializer.SerializeToElement(cold).GetProperty(nameof(CreateAgentTaskRequest.WorkspaceColdReset)).GetBoolean().ShouldBeTrue();
        AgentTaskService.HandoffRequestDigest(cold).ShouldNotBe(PreSlotDefaultHandoffDigest);
    }

    [Test]
    [Arguments("flag-off")]
    [Arguments("runner-lacks-WorkspaceSlotV1")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_FlagOffMirrorRequestUnchanged(string mode)
    {
        // V-12. Either half missing keeps the legacy task-<id> request byte-identical and stores the runner's path.
        new DelegationSettings().WorkspaceSlotsEnabled.ShouldBeFalse();
        await using var rig = mode == "flag-off"
            ? await Rig.StartAsync(capabilities: SlotRunner())
            : await Rig.StartAsync(settings => settings.WorkspaceSlotsEnabled = true);
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        await rig.TickAsync().WaitAsync(Bound);
        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        PropertyNames(request).SetEquals(["branch", "sha", "name", "repository", "binding"]).ShouldBeTrue(string.Join(",", PropertyNames(request)));
        var mirror = request.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        mirror.Name.ShouldBe(RemoteWorkspaceService.MirrorName(taskId));
        var path = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);
        await rig.Peer.EmitAsync(RemoteWorkspacePreparerTests.MirrorResult(request, path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var task = await rig.ReadTaskAsync(taskId);
        task.RemoteWorktreePath.ShouldBe(path);
        task.RemoteWorkspaceSlotId.ShouldBeNull();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_ColdWorkspacePersistsAndLeaseCarriesCold()
    {
        // V-37a. A slot lease carries slot=true and the task's WorkspaceColdReset as cold; the reply's slot is stored.
        await using var rig = await SlotRigAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var cold = await rig.SeedAsync(task => task.WorkspaceColdReset = true);
        await rig.TickAsync().WaitAsync(Bound);
        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var sent = request.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        sent.Slot.ShouldBe(true);
        sent.Cold.ShouldBe(true, "cold: true");
        await rig.Peer.EmitAsync(SlotResult(request, "/work/worktrees/ws-03", "ws-03", 7));
        await rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var stored = await rig.ReadTaskAsync(cold);
        stored.RemoteWorktreePath.ShouldBe("/work/worktrees/ws-03");
        stored.RemoteWorkspaceSlotId.ShouldBe("ws-03");
        stored.RemoteWorkspaceSlotGeneration.ShouldBe(7);

        await using var warmRig = await SlotRigAsync();
        warmRig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        await warmRig.SeedAsync();
        await warmRig.TickAsync().WaitAsync(Bound);
        var warm = (await warmRig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1))
            .Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        warm.Slot.ShouldBe(true);
        warm.Cold.ShouldBe(false, "cold: false");
    }

    [Test]
    [Arguments("overflow_cap")]
    [Arguments("disk_budget")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_SlotUnavailableHoldsWithoutFallback(string bound)
    {
        // V-13. The runner's pool bound holds the task Queued with that reason and never falls back to task-<id>.
        await using var rig = await SlotRigAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        for (var round = 1; round <= 3; round++)
        {
            await rig.TickAsync().WaitAsync(Bound);
            var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, round);
            request.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!.Slot.ShouldBe(true, "no fallback mirror");
            await rig.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId, request.Operation,
                ErrorCode: PhoneHomeProblemTypes.UnsupportedTarget, ErrorDetail: WorkspaceSlotCodes.UnavailableDetail(bound), StatusCode: 409));
            await rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
            await rig.AdvanceAsync(TimeSpan.FromHours(1));
        }

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.RemoteWorktreePath.ShouldBeNull();
        var held = (await rig.EventsAsync(taskId, AgentTaskEventType.Held))
            .Where(item => item.Detail?.Contains(WorkspaceSlotCodes.Unavailable, StringComparison.Ordinal) == true).ToList();
        held.Count.ShouldBe(3);
        held[0].Detail.ShouldContain(bound);
        (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_ReleasedTaskCoordinatesNotAmbiguous()
    {
        // V-10a. Ownership of a reused slot path compares (slot, generation), never the path string.
        await using var rig = await Rig.StartAsync();
        const string path = "/work/worktrees/ws-01";
        var a = await rig.SeedAsync(task => Slot(task, AgentTaskStatus.Succeeded, null, "ws-01", 1));
        var b = await rig.SeedAsync(task => Slot(task, AgentTaskStatus.Working, path, "ws-01", 2));
        await using (var db = rig.NewDb())
            (await TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync(db, await db.AgentTasks.SingleAsync(t => t.Id == b), default)).ShouldBeFalse("released predecessor");
        await using (var db = rig.NewDb())
        {
            await db.AgentTasks.Where(t => t.Id == a).ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Status, AgentTaskStatus.Blocked).SetProperty(t => t.RemoteWorktreePath, path));
            (await TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync(db, await db.AgentTasks.SingleAsync(t => t.Id == b), default)).ShouldBeFalse("uncleared path, generation 1 != 2");
        }

        await rig.SeedAsync(task => Slot(task, AgentTaskStatus.Working, path, "ws-01", 2));
        await using (var db = rig.NewDb())
            (await TerminalRunnerSeatReleaseService.AmbiguousOwnershipAsync(db, await db.AgentTasks.SingleAsync(t => t.Id == b), default)).ShouldBeTrue("same slot and generation");
    }

    [Test]
    [Arguments("slot-managed-refusal")]
    [Arguments("slot-bound-no-request")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_LegacyRemovalTreatsSlotManagedAsRemoved(string mode)
    {
        // V-10b. The shared removal primitive (retirement and drain re-arm both call it) returns no residue for a slot.
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRemove);
        var taskId = await rig.SeedAsync(task =>
        {
            task.Status = AgentTaskStatus.Succeeded;
            task.RemoteWorktreePath = "/work/worktrees/ws-01";
            if (mode == "slot-bound-no-request")
            {
                task.RemoteWorkspaceSlotId = "ws-01";
                task.RemoteWorkspaceSlotGeneration = 4;
            }
        });
        using var scope = rig.Provider.CreateScope();
        var remote = scope.ServiceProvider.GetRequiredService<RemoteWorkspaceService>();
        var task = await rig.ReadTaskAsync(taskId);
        var removal = remote.RemoveMirrorAsync(task, CancellationToken.None);
        if (mode == "slot-managed-refusal")
        {
            var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRemove, 1);
            await rig.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId, request.Operation,
                ErrorCode: PhoneHomeProblemTypes.UnsupportedTarget, ErrorDetail: WorkspaceSlotCodes.SlotManaged, StatusCode: 409));
        }

        (await removal.WaitAsync(Bound)).ShouldBeNull("no residue: the slot is the pool's");
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRemove).ShouldBe(mode == "slot-managed-refusal" ? 1 : 0);
    }

    [Test]
    [Arguments("not-terminal")]
    [Arguments("seat-occupied")]
    [Arguments("sync-debt-pending")]
    [Arguments("sync-debt-held")]
    [Arguments("live-follow-up")]
    [Arguments("all-hold")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_ReleaseReconcilerRequiresAllConditions(string mode)
    {
        // V-55 (D-10). Only a terminal task with a free seat, no Pending/Held sync debt and no live follow-up is released.
        await using var rig = await SlotRigAsync(services => services.AddScoped<WorkspaceSlotReleaseService>());
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceSlotRelease);
        var sessionId = Guid.NewGuid();
        var taskId = await rig.SeedAsync(task =>
        {
            Slot(task, mode == "not-terminal" ? AgentTaskStatus.Blocked : AgentTaskStatus.Succeeded, "/work/worktrees/ws-02", "ws-02", 3);
            task.AgentSessionId = sessionId;
        });
        await using (var db = rig.NewDb())
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "grok", AgentKind = AgentKind.Grok,
                Status = mode == "seat-occupied" ? SessionStatus.Running : SessionStatus.Stopped,
                Cwd = rig.WorkspacePath, Cols = 80, Rows = 24, CreatedAt = rig.Now, StartedAt = rig.Now, LastSeenAt = rig.Now,
                EndedAt = mode == "seat-occupied" ? null : rig.Now, RunnerId = rig.RunnerId, RunnerStoreId = rig.Host.StoreId,
                RunnerCwd = "/work/worktrees/ws-02",
            });
            if (mode is "sync-debt-pending" or "sync-debt-held")
                db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
                {
                    Id = Guid.NewGuid(), TaskId = taskId, Attempt = 1, SettlementEventId = Guid.NewGuid(),
                    State = mode == "sync-debt-held" ? AgentTaskSyncDebtState.Held : AgentTaskSyncDebtState.Pending,
                    CreatedAt = rig.Now, UpdatedAt = rig.Now,
                });
            await db.SaveChangesAsync();
        }

        if (mode == "live-follow-up")
            await rig.SeedAsync(task =>
            {
                task.FollowUpOfTaskId = taskId;
                task.RunnerId = null;
                task.WorktreePath = Path.Combine(rig.WorkspacePath, "follow-up");
            });

        using var scope = rig.Provider.CreateScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>();
        var pass = reconciler.ReconcileAsync(CancellationToken.None);
        if (mode != "all-hold")
        {
            await pass.WaitAsync(Bound);
            rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceSlotRelease).ShouldBe(0, "release count 0");
            (await rig.ReadTaskAsync(taskId)).RemoteWorktreePath.ShouldBe("/work/worktrees/ws-02");
            return;
        }

        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceSlotRelease, 1);
        var sent = request.Payload!.Value.Deserialize<PhoneHomeWorkspaceSlotReleaseRequest>(PhoneHomeFraming.Json)!;
        sent.ShouldBe(new PhoneHomeWorkspaceSlotReleaseRequest("ws-02", 3, taskId));
        (await rig.ReadTaskAsync(taskId)).RemoteWorktreePath.ShouldNotBeNull("the path is cleared only after the reply");
        await rig.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceSlotReleaseResponse("released", "ws-02", 3), PhoneHomeFraming.Json)));
        (await pass.WaitAsync(Bound)).ShouldBe(1);
        var released = await rig.ReadTaskAsync(taskId);
        released.RemoteWorktreePath.ShouldBeNull();
        released.RemoteWorkspaceSlotReleasedAt.ShouldNotBeNull();
        released.RemoteWorkspaceSlotId.ShouldBe("ws-02", "slot history kept");
        released.RemoteWorkspaceSlotGeneration.ShouldBe(3);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_ReconcilerBoundsSlotUnknownRetries()
    {
        // D-3 (review 3a354688). A runner that answers slot_unknown (the slot is on another runner) is asked at most
        // three times for that slot and generation; then the reconciler stops, records workspace_slot_release_stuck
        // for an operator and never stamps a release it did not get.
        await using var rig = await SlotRigAsync(services => services.AddScoped<WorkspaceSlotReleaseService>());
        rig.Peer.Reply = frame => frame.Operation != PhoneHomeOperation.WorkspaceSlotRelease
            ? null
            : new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: PhoneHomeProblemTypes.UnsupportedTarget, ErrorDetail: WorkspaceSlotCodes.UnknownSlot, StatusCode: 409);
        var taskId = await rig.SeedAsync(task => Slot(task, AgentTaskStatus.Succeeded, "/work/worktrees/ws-02", "ws-02", 3));

        for (var pass = 0; pass < 5; pass++)
        {
            using var scope = rig.Provider.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>().ReconcileAsync(CancellationToken.None).WaitAsync(Bound))
                .ShouldBe(0);
        }

        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceSlotRelease).ShouldBe(WorkspaceSlotReleaseService.UnknownSlotAttempts, "bounded, not forever");
        var task = await rig.ReadTaskAsync(taskId);
        task.RemoteWorkspaceSlotReleasedAt.ShouldBeNull("never a guessed release");
        task.RemoteWorktreePath.ShouldBe("/work/worktrees/ws-02");
        var warnings = (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).Select(e => e.Detail ?? "").ToList();
        warnings.Count(text => text.StartsWith(WorkspaceSlotCodes.ReleaseUnknown + ": slot ws-02 generation 3 ", StringComparison.Ordinal)).ShouldBe(3);
        warnings.Count(text => text.StartsWith(WorkspaceSlotCodes.ReleaseStuck + ": slot ws-02 generation 3 ", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public void C0824_ContinueResumeRefusedForSlotCwd()
    {
        // V-39 (Q-1 default). --continue resumes the newest conversation in the cwd: in a shared slot that is another task's.
        var id = Guid.NewGuid();
        var refused = Should.Throw<ConflictException>(() =>
            AgentSessionService.BuildSessionIdentityArgs(["--model", "x"], id, AgentSessionResumeMode.Continue, sharedWorkspace: true));
        refused.Code.ShouldBe(WorkspaceSlotCodes.ContinueInSharedWorkspace);
        AgentSessionService.BuildSessionIdentityArgs(["--model", "x"], id, AgentSessionResumeMode.Resume, sharedWorkspace: true)
            .ShouldBe(["--model", "x", "--resume", id.ToString("D")]);
        AgentSessionService.BuildSessionIdentityArgs(["--model", "x"], id, AgentSessionResumeMode.Continue, sharedWorkspace: false)
            .ShouldContain("--continue");
        WorkspaceSlotCodes.IsSlotPath("/work/worktrees/ws-03-d2").ShouldBeTrue();
        WorkspaceSlotCodes.IsSlotPath("/work/worktrees/task-deadbeef").ShouldBeFalse();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C0824_OperatorColdResetRouteRecordsAndForwards()
    {
        // V-37b (pre-rc cold reset). The operator route needs the token and forwards the slot list (order and set)
        // or all, with the requester and reason, to the runner's cold-reset operation.
        await using var host = await PhoneHomeTestHost.StartAsync(configureServices: services =>
            services.AddSingleton<ISessionRunnerDirectory>(provider => provider.GetRequiredService<PhoneHomeRunnerDirectory>()));
        await using var peer = await host.ConnectPeerAsync(capabilities: SlotRunner());
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.SilentFor(PhoneHomeOperation.WorkspaceSlotColdReset);
        var route = "/api/session-runners/" + host.AllowedRunnerId + "/workspace-slots/cold-reset";
        using (var refused = await host.PostOperatorAsync(route, new WorkspaceSlotColdResetRouteRequest(["ws-01"], false, "no token"), token: null))
            ((int)refused.StatusCode).ShouldBeInRange(400, 403);
        peer.RequestCount(PhoneHomeOperation.WorkspaceSlotColdReset).ShouldBe(0);
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        var cases = new (WorkspaceSlotColdResetRouteRequest Body, IReadOnlyList<string>? Slots, bool All)[]
        {
            (new(["ws-03", "ws-01"], false, "stale dlls", "operator-x"), ["ws-03", "ws-01"], false),
            (new(null, true, "rc qualification rc/2026-11-01 0123456789ab", "orchestrator"), null, true),
        };
        for (var i = 0; i < cases.Length; i++)
        {
            var post = host.PostOperatorAsync(route, cases[i].Body, token);
            var frame = await WaitForAsync(peer, PhoneHomeOperation.WorkspaceSlotColdReset, i + 1);
            var sent = frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceSlotColdResetRequest>(PhoneHomeFraming.Json)!;
            if (cases[i].Slots is { } slots)
                sent.Slots.ShouldBe(slots, "order and set unchanged");
            else
                sent.Slots.ShouldBeNull();
            sent.All.ShouldBe(cases[i].All);
            sent.Requester.ShouldBe(cases[i].Body.Requester);
            sent.Reason.ShouldBe(cases[i].Body.Reason);
            sent.RequestId.ShouldNotBe(Guid.Empty);
            await peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceSlotColdResetResponse(sent.RequestId,
                    [new PhoneHomeWorkspaceSlotColdResetOutcome("ws-01", "pending")]), PhoneHomeFraming.Json)));
            using var response = await post.WaitAsync(Bound);
            response.EnsureSuccessStatusCode();
            var body = (await response.Content.ReadFromJsonAsync<PhoneHomeWorkspaceSlotColdResetResponse>(PhoneHomeFraming.Json))!;
            body.RequestId.ShouldBe(sent.RequestId);
        }
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task C0824_LeaseRoundTripRealRunner()
    {
        // V-40 (D-A). Server tick -> preparer -> socket -> real runner pool: the runner ledger and the tree agree with the row.
        await using var world = await C0824SlotTransportWorld.StartAsync();
        var taskId = await world.SeedAsync();
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var (task, slot) = await LeasedAsync(world, taskId);
        Git(slot.Path, "rev-parse", "HEAD").Trim().ShouldBe(world.Base);
        Git(slot.Path, "symbolic-ref", "HEAD").Trim().ShouldBe("refs/heads/" + RemoteWorkspaceService.OwnedBranch(taskId));
        task.RemoteWorktreePath.ShouldBe(slot.Path);
        task.RemoteWorkspaceSlotId.ShouldBe(slot.Id);
        task.RemoteWorkspaceSlotGeneration.ShouldBe(slot.LeaseGeneration);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task C0824_LeasedTreeReadSpawnsNoScratchGit()
    {
        // The 1 s scratch-git deadline is per process start. A leased tree read has to
        // use the file form, not start rev-parse and symbolic-ref once per case.
        C0824SlotTransportWorld.EnsureSharedScratchRepository();
        await using var world = await C0824SlotTransportWorld.StartAsync();
        var taskId = await world.SeedAsync();
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var (_, slot) = await LeasedAsync(world, taskId);
        using var counted = C0824SlotTransportWorld.BeginScratchGitCount();
        Git(slot.Path, "rev-parse", "HEAD").Trim().ShouldBe(world.Base);
        Git(slot.Path, "symbolic-ref", "HEAD").Trim().ShouldBe("refs/heads/" + RemoteWorkspaceService.OwnedBranch(taskId));
        counted.Starts.ShouldBe(0, "leased tree reads start no scratch git");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task C0824_BusyRunnerLeaseSerialisedNotDuplicated()
    {
        // V-41 (D-A). A slow recipient: ticks while the lease is outstanding send no second request and grow nothing.
        await using var world = await C0824SlotTransportWorld.StartAsync();
        world.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await world.SeedAsync();
        await world.Rig.TickAsync().WaitAsync(Bound);
        var request = await world.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.TickAsync().WaitAsync(Bound);
        world.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
        await world.Rig.Peer.EmitAsync(await Task.Run(() => world.Deliver(request)));
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        await LeasedAsync(world, taskId);
        world.Pool.Ledger().Slots.Count.ShouldBe(1, "no overflow slot");
    }

    [Test]
    [Arguments("reply-lost")]
    [Arguments("runner-crash-after-leasing")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task C0824_LeaseRecoveryAtEachHandoff(string handoff)
    {
        // V-42 (D-A). A lost reply or a runner crash mid-lease: the retry resends the same preparation and ends with one lease.
        await using var world = await C0824SlotTransportWorld.StartAsync();
        var taskId = await world.SeedAsync();
        var first = true;
        world.Intercept = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.WorkspaceMirror || !first)
                return null;
            first = false;
            if (handoff == "reply-lost")
            {
                world.Deliver(frame);
                return new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                    JsonSerializer.SerializeToElement(new { }, PhoneHomeFraming.Json));
            }

            world.Pool.PhaseCut = phase => { if (phase == "leasing") throw new InvalidOperationException("runner crashed"); };
            return world.Deliver(frame);
        };
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        (await world.Rig.ReadTaskAsync(taskId)).RemoteWorktreePath.ShouldBeNull("the first handoff failed");
        if (handoff == "runner-crash-after-leasing")
            world.StartRunner();
        await world.Rig.AdvanceAsync(TimeSpan.FromHours(1));
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var requests = world.Rig.Peer.Incoming.ToArray()
            .Where(frame => frame.Kind == PhoneHomeFrameKind.Request && frame.Operation == PhoneHomeOperation.WorkspaceMirror)
            .Select(frame => frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!.Binding!.PreparationId)
            .ToList();
        requests.Count.ShouldBe(2);
        requests.Distinct().Count().ShouldBe(1, "the retry resends the same preparation");
        var (task, slot) = await LeasedAsync(world, taskId);
        slot.Id.ShouldBe("ws-01", "no second slot touched");
        world.Pool.Ledger().Slots.Count.ShouldBe(1);
        task.RemoteWorkspaceSlotGeneration.ShouldBe(slot.LeaseGeneration);
    }

    [Test]
    [Arguments("direct")]
    [Arguments("reply-lost")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task C0824_ReleaseRoundTripRealRunner(string mode)
    {
        // V-43 (D-B). Reconciler -> socket -> real runner: the slot is Free and detached; the row keeps the slot
        // history and clears the path only after a success reply. A lost reply is resent as an idempotent success.
        await using var world = await C0824SlotTransportWorld.StartAsync();
        var taskId = await world.SeedAsync();
        await world.Rig.TickAsync().WaitAsync(Bound);
        await world.Rig.Preparer.WhenIdleAsync().WaitAsync(Bound);
        var (_, leased) = await LeasedAsync(world, taskId);
        await using (var db = world.Rig.NewDb())
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Status, AgentTaskStatus.Succeeded).SetProperty(t => t.CompletedAt, DateTime.UtcNow));
        if (mode == "reply-lost")
        {
            var lost = true;
            world.Intercept = frame =>
            {
                if (frame.Operation != PhoneHomeOperation.WorkspaceSlotRelease || !lost)
                    return null;
                lost = false;
                world.Deliver(frame);
                return new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                    JsonSerializer.SerializeToElement(new { }, PhoneHomeFraming.Json));
            };
            (await ReconcileAsync(world)).ShouldBe(0);
            (await world.Rig.ReadTaskAsync(taskId)).RemoteWorktreePath.ShouldBe(leased.Path, "the path is kept until a reply succeeds");
            world.Pool.Ledger().Slot(leased.Id)!.State.ShouldBe(WorkspaceSlotStates.Free, "the runner did release");
        }

        var idle = world.Pool.Ledger().Slot(leased.Id)!.IdleSince;
        (await ReconcileAsync(world)).ShouldBe(1);
        var free = world.Pool.Ledger().Slot(leased.Id)!;
        free.State.ShouldBe(WorkspaceSlotStates.Free);
        free.LastTaskId.ShouldBe(taskId);
        if (mode == "reply-lost")
            free.IdleSince.ShouldBe(idle, "the resend did not reset the slot a second time");
        C0824SlotTransportWorld.Git(free.Path, "rev-parse", "HEAD").Trim().ShouldBe(world.Base);
        var task = await world.Rig.ReadTaskAsync(taskId);
        task.RemoteWorktreePath.ShouldBeNull();
        task.RemoteWorkspaceSlotReleasedAt.ShouldNotBeNull();
        task.RemoteWorkspaceSlotId.ShouldBe(leased.Id);
        task.RemoteWorkspaceSlotGeneration.ShouldBe(leased.LeaseGeneration);
    }

    private static async Task<int> ReconcileAsync(C0824SlotTransportWorld world)
    {
        using var scope = world.Rig.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WorkspaceSlotReleaseService>().ReconcileAsync(CancellationToken.None).WaitAsync(Bound);
    }

    private static async Task<(AgentTask Task, Antiphon.SessionRunner.WorkspaceSlotRecord Slot)> LeasedAsync(
        C0824SlotTransportWorld world, Guid taskId)
    {
        var slot = world.Pool.Ledger().Slots.Where(item => item.TaskId == taskId).ShouldHaveSingleItem("one ledger lease");
        slot.State.ShouldBe(WorkspaceSlotStates.Leased);
        await using var db = world.Rig.NewDb();
        var preparation = (await db.TaskWorktreeMirrorBindings.AsNoTracking().Where(row => row.TaskId == taskId)
            .Select(row => row.PreparationId).ToListAsync()).ShouldHaveSingleItem();
        slot.PreparationId.ShouldBe(preparation);
        return (await world.Rig.ReadTaskAsync(taskId), slot);
    }

    private static string Git(string directory, params string[] args) => C0824SlotTransportWorld.Git(directory, args);

    private static async Task<PhoneHomeFrame> WaitForAsync(PhoneHomeScriptedPeer peer, PhoneHomeOperation operation, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (peer.RequestCount(operation) >= count)
                return peer.Incoming.ToArray().Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == operation).ElementAt(count - 1);
            await Task.Delay(20);
        }

        throw new TimeoutException($"Expected {count} {operation} request(s); saw {peer.RequestCount(operation)}.");
    }

    private static void Slot(AgentTask task, AgentTaskStatus status, string? path, string slot, int generation)
    {
        task.Status = status;
        task.RemoteWorktreePath = path;
        task.RemoteWorkspaceSlotId = slot;
        task.RemoteWorkspaceSlotGeneration = generation;
        task.WorktreePath = Path.Combine(Path.GetTempPath(), "c0824-desktop-" + task.Id.ToString("N"));
        if (status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled)
            task.CompletedAt = DateTime.UtcNow;
    }

    private static PhoneHomeFrame SlotResult(PhoneHomeFrame request, string path, string slot, int generation) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceMirrorResponse(path, null, slot, generation), PhoneHomeFraming.Json));
}
