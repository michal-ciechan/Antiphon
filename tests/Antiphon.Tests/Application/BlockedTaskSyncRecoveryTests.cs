using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedTaskSyncRecoveryTests
{
    [Test]
    public async Task C1065_LeaseBusyDoesNotRetainPublishedIdleSeat()
    {
        await using var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true);
        var sha = await w.CommitAsync("published.txt");
        await using var lease = await w.Leases.TryAcquireAsync(w.Repository, default);
        lease.ShouldNotBeNull();
        (await w.PrepareAsync()).Evidence.ShouldNotBeNull("independent runner publication needs no desktop lease");
        var park = await w.RowAsync();
        park.SyncState.ShouldBe(AgentTaskParkSyncState.Pending, "G-119: publication must persist desktop sync debt");
        park.SyncSourceSha.ShouldBe(sha);
    }

    [Test]
    public async Task C1065_SyncDebtRecoversWithoutMintingApproval()
    {
        await using var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true);
        var sha = await w.CommitAsync("recovery.txt");
        (await w.PrepareAsync()).Evidence.ShouldNotBeNull();
        var park = await w.RowAsync();
        park.SyncSourceSha.ShouldBe(sha, "G-120: recovery must retain the exact published source");
        park.SyncNextAttemptAt.ShouldNotBeNull("G-125: the due time must survive service recreation");
    }
}
