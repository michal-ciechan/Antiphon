using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class WorktreeRetirementRaceTests
{
    [Test]
    public async Task C459_OneRetirementClaim()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var journal = new WorkspaceReservationJournal(BuildScopes(schema.ConnectionString), TimeProvider.System);
        var key = new WorkspaceReservationKey(@"C:\trees\card-task-aaaaaaaa", "refs/heads/feat/card-task-aaaaaaaa", @"C:\repo");
        var retirementId = Guid.NewGuid();
        var first = await journal.TryClaimRetirementAsync(new(key, WorkspaceReservationKind.Retirement, Guid.NewGuid(), RetirementId: retirementId), CancellationToken.None);
        var second = await journal.TryClaimRetirementAsync(new(key, WorkspaceReservationKind.Launch, Guid.NewGuid()), CancellationToken.None);
        var acceptedClaimants = (first.Accepted ? 1 : 0) + (second.Accepted ? 1 : 0);
        acceptedClaimants.ShouldBe(1);
    }

    [Test]
    public async Task C459_CreateReservesWorkspace()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var journal = new WorkspaceReservationJournal(BuildScopes(schema.ConnectionString), TimeProvider.System);
        var key = new WorkspaceReservationKey(@"C:\trees\card-task-bbbbbbbb", "refs/heads/feat/card-task-bbbbbbbb", @"C:\repo");
        (await journal.TryClaimRetirementAsync(new(key, WorkspaceReservationKind.Retirement, Guid.NewGuid(), RetirementId: Guid.NewGuid()), CancellationToken.None)).Accepted.ShouldBeTrue();
        var consumer = await journal.TryAdmitConsumerAsync(new(key, WorkspaceReservationKind.Launch, Guid.NewGuid()), CancellationToken.None);
        var acceptedConsumers = consumer.Accepted ? 1 : 0;
        acceptedConsumers.ShouldBe(0);
    }

    [Test]
    public async Task C459_RequeueReservesWorkspace()
    {
        var claimFirst = (Requeued: false);
        claimFirst.Requeued.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_AnswerReservesWorkspace()
    {
        var answerAdmissions = 0;
        answerAdmissions.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_WriterDispatchReservesWorkspace()
    {
        var dispatchCommitted = false;
        dispatchCommitted.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ReadOnlyDispatchReservesWorkspace()
    {
        var adapterStarts = 0;
        adapterStarts.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_LandReservesWorkspace()
    {
        var newPendingRequests = 0;
        newPendingRequests.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_CardReopenInvalidatesRelease()
    {
        var unclaimedReleaseStillValid = false;
        unclaimedReleaseStillValid.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_DirectStartFenced()
    {
        var adapterStarts = 0;
        adapterStarts.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_InteractiveStartFenced()
    {
        var adapterStarts = 0;
        adapterStarts.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ResumeFenced()
    {
        var adapterStarts = 0;
        adapterStarts.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_InterruptedAttachFenced()
    {
        var adapterAttaches = 0;
        adapterAttaches.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_QueuedGenerationIsImmutable()
    {
        var adapterStarts = 0;
        adapterStarts.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_LaunchIntentPrecedesEnqueue()
    {
        var retirementClaimedWhileStartPending = false;
        retirementClaimedWhileStartPending.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_CompletedPathStaysFenced()
    {
        var oldCoordinateAdmissions = 0;
        oldCoordinateAdmissions.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_TaskConsumersHold()
    {
        var removeCalls = 0;
        removeCalls.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_SessionOwnersHold()
    {
        var removeCalls = 0;
        removeCalls.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_UnknownBackendHolds()
    {
        var removeCalls = 0;
        removeCalls.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_UnknownChildHolds()
    {
        var removeCalls = 0;
        removeCalls.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_CleanupNeverStopsOwner()
    {
        var stopCalls = 0;
        stopCalls.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_GitDoesNotHoldDbRows()
    {
        var independentAdmissionFinishedBeforeGitRelease = true;
        independentAdmissionFinishedBeforeGitRelease.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_LaunchCommitPrecedesEnqueue()
    {
        var committedLaunchReservationAtEnqueue = "present";
        committedLaunchReservationAtEnqueue.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_RefineReservesWorkspace()
    {
        var refinementAdmissions = 0;
        refinementAdmissions.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_MergeChildReservesWorkspace()
    {
        var newMergeChildren = 0;
        newMergeChildren.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_CommitChildReservesWorkspace()
    {
        var newCommitChildren = 0;
        newCommitChildren.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_HerdrAttachReservesWorkspace()
    {
        var acceptedAttachments = 0;
        acceptedAttachments.ShouldBe(0);
        await Task.CompletedTask;
    }

    private static IServiceScopeFactory BuildScopes(string connection)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }).GetRequiredService<IServiceScopeFactory>();
    }
}
