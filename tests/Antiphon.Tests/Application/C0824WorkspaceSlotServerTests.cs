using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
public sealed class C0824WorkspaceSlotServerTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    private static RunnerCapabilitiesDto SlotRunner() =>
        new("InboxConhost", "inbox", "test", false, Features: [RunnerCapabilityFeatures.WorkspaceSlotV1], VerificationCustodyBackend: null);

    private static Task<Rig> SlotRigAsync(Action<IServiceCollection>? configure = null) =>
        Rig.StartAsync(settings => settings.WorkspaceSlotsEnabled = true, configure: configure, capabilities: SlotRunner());

    private static HashSet<string> PropertyNames(PhoneHomeFrame request) =>
        request.Payload!.Value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

    [Test]
    [Arguments("flag-off")]
    [Arguments("runner-lacks-WorkspaceSlotV1")]
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
