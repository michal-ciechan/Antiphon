using Antiphon.Server.Domain.Enums;
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
public sealed class C0824RefOnlyProducerLifecycleTests
{
    [Test]
    public async Task Repair_from_ref_only_uses_exact_owned_source()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync();
        var ownerId = await f.SeedAsync();
        var owner = await f.LaunchAsync(ownerId);
        await using (var db = f.Rig.NewDb())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == ownerId);
            row.Status = AgentTaskStatus.Succeeded; row.CompletedAt = DateTime.UtcNow;
            var session = await db.AgentSessions.SingleAsync(s => s.Id == row.AgentSessionId);
            session.Status = SessionStatus.Stopped; session.EndedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
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
        repair.WorktreeBaseSha.ShouldBe(f.Git.Baseline);
        repair.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Repair);
        repair.WorktreeBaseTaskId.ShouldBe(ownerId);
        Path.Exists(repair.WorktreePath).ShouldBeFalse();
        Path.Exists(owner.WorktreePath).ShouldBeFalse();
        (await f.Git.RevParseAsync("refs/heads/" + owner.WorktreeBranch)).ShouldBe(f.Git.Baseline);
    }
}
