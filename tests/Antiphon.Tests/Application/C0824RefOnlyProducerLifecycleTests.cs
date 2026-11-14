using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Services;
using System.Text;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional production-caller lifecycle budget.
public sealed partial class C0824RefOnlyProducerLifecycleTests
{
    [Test]
    public async Task Repair_from_ref_only_uses_exact_owned_source()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync();
        var ownerId = await f.SeedAsync();
        var owner = await f.LaunchAsync(ownerId);
        var source = await f.CommitAsync(owner, new Dictionary<string, byte[]> { ["owner.txt"] = Encoding.UTF8.GetBytes("owned repair S\n") });
        await using (var settlement = await RunnerSettlementWorld.AttachProducedAsync(f, owner))
        {
            await settlement.SettleAsync(RunnerSettlementWorld.Report("Published. " + settlement.Claim(source)));
            settlement.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, settlement.Task.CompletionProgressEvidenceJson);
            settlement.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(source);
            await settlement.EndDelegateSessionAsync();
        }
        await f.Git.RunAsync(f.Git.Desktop, "commit", "--allow-empty", "-m", "main differs from repair source");
        (await f.Git.RevParseAsync("HEAD")).ShouldNotBe(f.Git.Baseline);
        var repairId = await f.SeedAsync(t => t.RepairSourceTaskId = ownerId);
        var next = f.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror) + 1;
        await f.Rig.TickAsync();
        var preparing = await f.Rig.ReadTaskAsync(repairId);
        preparing.Status.ShouldBe(AgentTaskStatus.Queued,
            "a published owned ref is a repair source without a registered checkout: " + preparing.FailureReason);
        var request = await f.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, next);
        await f.Rig.Peer.EmitAsync(RemoteWorkspacePreparerTests.MirrorResult(request, "/runner/repair/" + repairId.ToString("N")));
        await f.Rig.Preparer.WhenIdleAsync();
        await f.Rig.TickAsync();
        var repair = await f.Rig.ReadTaskAsync(repairId);
        repair.Status.ShouldBe(AgentTaskStatus.Dispatched, repair.FailureReason);
        repair.WorktreeBaseSha.ShouldBe(source);
        repair.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Repair);
        repair.WorktreeBaseTaskId.ShouldBe(ownerId);
        Path.Exists(repair.WorktreePath).ShouldBeFalse();
        Path.Exists(owner.WorktreePath).ShouldBeFalse();
        (await f.Git.RevParseAsync("refs/heads/" + owner.WorktreeBranch)).ShouldBe(source);
        await using (var db = f.Rig.NewDb())
        {
            var done = await db.AgentTasks.SingleAsync(t => t.Id == repairId);
            done.Status = AgentTaskStatus.Succeeded; done.CompletedAt = DateTime.UtcNow;
            var session = await db.AgentSessions.SingleAsync(t => t.Id == done.AgentSessionId);
            session.Status = SessionStatus.Stopped; session.EndedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await f.CommitAsync(owner, new Dictionary<string, byte[]> { ["ahead.txt"] = Encoding.UTF8.GetBytes("remote advanced\n") });
        var aheadId = await f.SeedAsync(t => t.RepairSourceTaskId = ownerId);
        await f.Rig.TickAsync();
        var ahead = await f.Rig.ReadTaskAsync(aheadId);
        ahead.Status.ShouldBe(AgentTaskStatus.Failed);
        ahead.FailureReason.ShouldContain("repair_source_owner_remote_ahead");
        (await f.Git.RevParseAsync("refs/heads/" + owner.WorktreeBranch)).ShouldBe(source);
        var divergent = await f.PublishForeignBranchAsync("feat/diverged-" + ownerId.ToString("N"), "diverged.txt");
        await f.Git.RunAsync(f.Git.Desktop, "push", "--force", "origin", divergent + ":refs/heads/" + owner.WorktreeBranch);
        var divergedId = await f.SeedAsync(t => t.RepairSourceTaskId = ownerId);
        await f.Rig.TickAsync();
        var diverged = await f.Rig.ReadTaskAsync(divergedId);
        diverged.Status.ShouldBe(AgentTaskStatus.Failed);
        diverged.FailureReason.ShouldContain("repair_source_owner_diverged");
        (await f.Git.RevParseAsync("refs/heads/" + owner.WorktreeBranch)).ShouldBe(source);
    }
}
