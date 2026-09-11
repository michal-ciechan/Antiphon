using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
        await C488_LocalAheadRetained();
        await C488_DivergedSourceRefuses();
        await C488_UnreadableSourceRefuses();
    }

    [Test]
    public async Task C488_ExactPushEndpointObservation() => await C488_ExactSourceRefObserved();

    [Test]
    public async Task C488_PublicationNeverMutatesRemoteSource()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var git = fixture.Git;
        var destination = await git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None);
        git.Trace.Clear();
        (await git.PushAsync(fixture.Repository, destination, fixture.SeedSha, CancellationToken.None)).Succeeded.ShouldBeTrue();
        git.Trace.ShouldContain(a => a[0] == "push" && a[a.Length - 1] == $"{fixture.SeedSha}:{fixture.TargetRef}");
        git.Trace.ShouldNotContain(a => a[0] == "push" && a[a.Length - 1].Contains(fixture.SourceRef));
        git.Trace.ShouldNotContain(a => a.Contains("--force") || a.Contains("+"));
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C488_AmbiguousPushEndpointRefuses()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var other = Path.Combine(fixture.Root, "other-endpoint.git");
        await fixture.RequiredAsync(fixture.Root, "clone", "--bare", fixture.Remote, other);
        await fixture.RequiredAsync(fixture.Repository, "config", "--add", "remote.origin.pushurl", fixture.Remote);
        await fixture.RequiredAsync(fixture.Repository, "config", "--add", "remote.origin.pushurl", other);
        await Should.ThrowAsync<IOException>(() => fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None));
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeFalse();
        observed.Reason.ShouldBe("source_remote_endpoint_ambiguous");
        (await fixture.RequiredAsync(other, "rev-parse", fixture.SourceRef)).Trim().ShouldBe(fixture.SeedSha);
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C488_PushEndpointIsSourceAuthority()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var fetchOnly = Path.Combine(fixture.Root, "fetch-only.git");
        var pushOnly = Path.Combine(fixture.Root, "push-only.git");
        await fixture.RequiredAsync(fixture.Root, "clone", "--bare", fixture.Remote, fetchOnly);
        await fixture.RequiredAsync(fixture.Root, "clone", "--bare", fixture.Remote, pushOnly);
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "push-only B");
        var b = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).Trim();
        await fixture.RequiredAsync(pushOnly, "fetch", fixture.Source, $"HEAD:{fixture.SourceRef}");
        await fixture.RequiredAsync(fixture.Repository, "remote", "set-url", "origin", fetchOnly);
        await fixture.RequiredAsync(fixture.Repository, "remote", "set-url", "--push", "origin", pushOnly);
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeTrue();
        observed.Sha.ShouldBe(b);
        (await fixture.RequiredAsync(fetchOnly, "rev-parse", fixture.SourceRef)).Trim().ShouldBe(fixture.SeedSha);
    }

    [Test]
    public async Task C488_AdvertisementValidated()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "ls-remote")
                return Task.FromResult<LandingGitResult?>(new(0, "not-an-oid\t" + fixture.SourceRef + "\n", ""));
            return Task.FromResult<LandingGitResult?>(null);
        };
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeFalse();
        observed.Reason.ShouldBe("source_remote_response_invalid");
        observed.ObservationRef.ShouldBeNull();
    }

    [Test]
    public async Task C488_FetchedCommitValidated()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "rev-parse" && args.Any(a => a.Contains("^{commit}")) && args.Any(a => a.Contains("source-observed")))
                return Task.FromResult<LandingGitResult?>(new(128, "", "not a commit"));
            return Task.FromResult<LandingGitResult?>(null);
        };
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeFalse();
        observed.Reason.ShouldBe("source_remote_commit_invalid");
    }

    [Test]
    public async Task C488_ReadFetchRaceCorrelated()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var fetches = 0;
        fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] == "fetch" && ++fetches == 1)
            {
                await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "race B");
                await fixture.RequiredAsync(fixture.Source, "push", "origin", $"HEAD:{fixture.SourceRef}");
            }
            return null;
        };
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        fetches.ShouldBeGreaterThanOrEqualTo(2);
        observed.Accepted.ShouldBeTrue();
        observed.Sha.ShouldBe((await fixture.RequiredAsync(fixture.Remote, "rev-parse", fixture.SourceRef)).Trim());
    }

    [Test]
    public async Task C488_ObservationRetryBounded()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var fetches = 0;
        fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] == "fetch")
            {
                fetches++;
                await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "move " + fetches);
                await fixture.RequiredAsync(fixture.Source, "push", "origin", $"HEAD:{fixture.SourceRef}");
            }
            return null;
        };
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        fetches.ShouldBe(3);
        observed.Accepted.ShouldBeFalse();
        observed.Reason.ShouldBe("source_remote_changed_during_confirmation");
    }

    [Test]
    public async Task C488_ObservationRaceMatrix()
    {
        await C488_ReadFetchRaceCorrelated();
        await C488_ObservationRetryBounded();
        await C488_ObservationEndpointRechecked();
    }

    [Test]
    public async Task C488_ObservationEndpointRechecked()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var other = Path.Combine(fixture.Root, "moved-endpoint.git");
        await fixture.RequiredAsync(fixture.Root, "clone", "--bare", fixture.Remote, other);
        var fetches = 0;
        fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] == "fetch" && result.Succeeded && ++fetches == 1)
                await fixture.RequiredAsync(fixture.Repository, "remote", "set-url", "--push", "origin", other);
        };
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeFalse();
        observed.Reason.ShouldBe("source_remote_endpoint_changed");
    }

    [Test]
    public async Task C488_UnreadableSourceRefuses()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        fixture.Git.BeforeCommand = (_, args) =>
            args[0] == "ls-remote"
                ? Task.FromResult<LandingGitResult?>(new(128, "", "network"))
                : Task.FromResult<LandingGitResult?>(null);
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var observed = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        observed.Accepted.ShouldBeFalse();
        observed.Reason.ShouldBe("source_remote_unreadable");
        observed.Sha.ShouldBeNull();
    }

    [Test]
    public async Task C488_FailedFetchCannotReusePin()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source-observed";
        var first = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        first.Accepted.ShouldBeTrue();
        fixture.Git.BeforeCommand = (_, args) =>
            args[0] == "fetch"
                ? Task.FromResult<LandingGitResult?>(new(128, "", "fetch failed"))
                : Task.FromResult<LandingGitResult?>(null);
        var second = await fixture.Git.ObserveSourceAsync(fixture.Repository, fixture.SourceRef, prefix, CancellationToken.None);
        second.Accepted.ShouldBeFalse();
        second.Reason.ShouldBe("source_remote_fetch_failed");
        second.ObservationRef.ShouldBeNull();
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", first.ObservationRef!)).Trim().ShouldBe(first.Sha);
    }

    [Test]
    public async Task C488_LocalAheadRetained()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        local.ShouldNotBe(h.Git.SeedSha);
        h.Git.RemoteSource.ShouldBe(h.Git.SeedSha);
        var queued = await h.RequestAsync(expectedSourceSha: local);
        var run = await h.RunQueuedAsync();
        run.ShouldBe(LandRunResult.Complete);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRelationship.ShouldBe(LandSourceRelationship.LocalAhead);
        request.CandidateSourceSha.ShouldBe(local);
        request.RemoteSourceSha.ShouldBe(h.Git.SeedSha);
        request.ResolvedSourceSha.ShouldBe(local);
        h.Git.RemoteSource.ShouldBe(h.Git.SeedSha);
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge") && a.Contains("--ff-only") && a.Contains(local));
    }

    [Test]
    public async Task C488_DivergedSourceRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        var remote = h.Git.DivergeRemoteSource();
        remote.ShouldNotBe(local);
        var queued = await h.RequestAsync(expectedSourceSha: local);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("source_remote_diverged");
        request.LocalBeforeSha.ShouldBe(local);
        request.RemoteSourceSha.ShouldBe(remote);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));

        await using var other = new LandingProtocolHarness();
        await other.InitializeAsync();
        await other.AddSourceAsync();
        var otherRemote = other.Git.DivergeRemoteSource();
        var second = await other.RequestAsync(expectedSourceSha: otherRemote);
        await other.RunQueuedAsync();
        await using var observer = other.CreateContext();
        (await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == second.RequestId))
            .SourceRefusalReason.ShouldBe("source_remote_diverged");
    }

    [Test]
    public async Task C488_AncestryErrorsRefuse()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.BeforeCommand = (_, args) =>
            args[0] == "merge-base"
                ? Task.FromResult<LandingGitResult?>(new(128, "", "io"))
                : Task.FromResult<LandingGitResult?>(null);
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("source_remote_ancestry_error");
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_FastForwardUsesExactOperand()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        var merge = h.Git.OwnedTrace.Where(a => a.Contains("merge") && a.Contains("--ff-only") && a.Contains(b)).ToArray();
        merge.ShouldNotBeEmpty();
        merge[0].ShouldContain("merge.autoStash=false");
    }

    [Test]
    public async Task C488_FastForwardDisablesAutostash() => await C488_FastForwardUsesExactOperand();
}
