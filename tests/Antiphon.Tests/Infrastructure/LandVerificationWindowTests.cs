using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandVerificationWindowTests
{
    [Test]
    public async Task C1379_LiveVerifierAdmitsOnlyScopedPreflight()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var child = new LandVerificationWindowHarness();
        var leases = new RepositoryMutationLease(fixture.Git);
        await using var execution = await new LandExecutionLease(fixture.Git)
            .TryAcquireAsync(fixture.Repository, CancellationToken.None);
        execution.ShouldNotBeNull();
        await using var mutation = await leases.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        mutation.ShouldNotBeNull();
        var identity = new RepositoryVerificationIdentity(fixture.TaskId, Guid.NewGuid(), 1, Guid.NewGuid(),
            fixture.SeedSha, fixture.Source, fixture.Repository, fixture.SourceRef, fixture.TargetRef);
        await using var window = await leases.OpenVerificationWindowAsync(mutation, execution, identity, CancellationToken.None);
        window.ShouldNotBeNull();
        var common = mutation.CommonDirectory;
        await mutation.DisposeAsync();
        var verify = child.Verifier.VerifyAsync(fixture.Source, null,
            new(fixture.TaskId, identity.OperationId, identity.RequestId, CommonDirectory: common, WindowId: window.Id),
            CancellationToken.None);
        try
        {
            await child.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using (var compatible = await leases.TryAcquirePreflightAsync(fixture.Repository,
                             RepositoryPreflight.Fetch(Guid.NewGuid(), fixture.SeedSha),
                             new(null, RepositoryLeasePurposes.StartRefFetch), CancellationToken.None))
                compatible.ShouldNotBeNull("c1379-live-preflight: a matching live verifier must admit the typed fetch");
            await using var ordinary = await leases.TryAcquireAsync(fixture.Repository,
                new(null, RepositoryLeasePurposes.StartRefFetch), CancellationToken.None);
            ordinary.ShouldBeNull("a purpose tag is not a capability");
            await using var foreign = await new RepositoryMutationLease(fixture.Git).TryAcquirePreflightAsync(
                fixture.Repository, RepositoryPreflight.Fetch(Guid.NewGuid(), fixture.SeedSha),
                new(null, RepositoryLeasePurposes.StartRefFetch), CancellationToken.None);
            foreign.ShouldBeNull("another provider has no live window capability");
            await using var secondLand = await new LandExecutionLease(fixture.Git)
                .TryAcquireAsync(fixture.Repository, CancellationToken.None);
            secondLand.ShouldBeNull();
        }
        finally
        {
            child.Release();
            (await verify).Passed.ShouldBeTrue();
        }
        await window.DisposeAsync();
        await using var after = await leases.TryAcquireAsync(fixture.Repository, CancellationToken.None);
        after.ShouldNotBeNull("the joined child and exact closed generation release admission");
    }
}
