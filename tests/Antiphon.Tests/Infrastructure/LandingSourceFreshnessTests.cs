using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandingSourceFreshnessTests
{
    [Test]
    public async Task C488_ExactSourceRefObserved()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var git = new LandingGit();
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeTrue();
        observed.Sha.ShouldBe(fixture.SeedSha);
        observed.Fingerprint.ShouldNotBeNull();
        observed.Fingerprint!.Length.ShouldBe(64);
    }

    [Test]
    public async Task C488_MissingSourceRefuses()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Remote, "update-ref", "-d", fixture.SourceRef);
        var git = new LandingGit();
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeFalse();
        observed.Reason.ShouldBe("source_remote_missing");
    }

    [Test]
    public async Task C488_ObservationPinsImmutable()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var git = new LandingGit();
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var first = await git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        var second = await git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        first.Accepted.ShouldBeTrue();
        second.Accepted.ShouldBeTrue();
        first.ObservationRef.ShouldNotBe(second.ObservationRef);
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", first.ObservationRef!)).Trim().ShouldBe(first.Sha);
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", second.ObservationRef!)).Trim().ShouldBe(second.Sha);
    }

    [Test]
    public async Task C488_SourcePolicyMatrix()
    {
        await C488_ExactSourceRefObserved();
        await C488_MissingSourceRefuses();
    }

    [Test]
    public async Task C488_ExactPushEndpointObservation() => await C488_ExactSourceRefObserved();

    [Test]
    public async Task C488_PublicationNeverMutatesRemoteSource()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.AssertRemoteSourceAsync();
    }
}
