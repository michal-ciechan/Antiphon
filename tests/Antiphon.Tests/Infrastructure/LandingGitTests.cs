using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandingGitTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task C448_V12_RemoteMovementNeedsCorrelatedContainment(int movements)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var fetches = 0;
        fixture.Git.BeforeCommand = async (_, arguments) =>
        {
            if (arguments[0] == "fetch" && ++fetches <= movements)
            {
                await fixture.RequiredAsync(fixture.Repository, "commit", "--allow-empty", "-m", "remote movement");
                await fixture.RequiredAsync(fixture.Repository, "push", "origin", fixture.TargetRef);
            }
            return null;
        };
        var destination = await fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None);
        var observed = await fixture.Git.ObserveAsync(fixture.Repository, destination,
            fixture.SeedSha, fixture.ObservationRef, CancellationToken.None);
        fetches.ShouldBe(Math.Min(movements + 1, 3));
        observed.ContainsSource.ShouldBe(movements < 3);
        observed.Reason.ShouldBe(movements == 3 ? "remote_changed_during_confirmation" : null);
        if (movements < 3)
            observed.Sha.ShouldBe((await fixture.RequiredAsync(fixture.Remote, "rev-parse", fixture.TargetRef)).Trim());
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V30_RecoveryPinCannotOverwriteAnotherCommit()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var pin = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source";
        (await fixture.Git.PinAsync(fixture.Repository, pin, fixture.SeedSha, CancellationToken.None)).Succeeded.ShouldBeTrue();
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "new source");
        var newer = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).Trim();
        (await fixture.Git.PinAsync(fixture.Repository, pin, newer, CancellationToken.None)).Diagnostic.ShouldBe("recovery_ref_collision");
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", pin)).Trim().ShouldBe(fixture.SeedSha);
        (await fixture.Git.PinAsync(fixture.Repository, pin, fixture.SeedSha, CancellationToken.None)).Succeeded.ShouldBeTrue();
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V30_PushUsesPinnedCommitAndExplicitDestination()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "intended");
        var intended = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).Trim();
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "later writer");
        var destination = await fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None);
        fixture.Git.Trace.Clear();
        (await fixture.Git.PushAsync(fixture.Repository, destination, intended, CancellationToken.None)).Succeeded.ShouldBeTrue();
        fixture.Git.Trace.Single(a => a[0] == "push").ShouldBe(["push", fixture.Remote, $"{intended}:{fixture.TargetRef}"]);
        (await fixture.RequiredAsync(fixture.Remote, "rev-parse", fixture.TargetRef)).Trim().ShouldBe(intended);
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public void C448_V30_NulRegistrationPreservesPathSpelling()
    {
        var oid = new string('a', 40);
        var records = LandingGit.ParseRegistrations($"worktree C:/trees/space ü\nquote\"\0HEAD {oid}\0branch refs/heads/Case\0locked owner\0\0");
        records.ShouldHaveSingleItem();
        records[0].Path.ShouldBe("C:/trees/space ü\nquote\"");
        records[0].Branch.ShouldBe("refs/heads/Case");
        records[0].Locked.ShouldBeTrue();
    }

    [Test]
    [Arguments("duplicate-path")]
    [Arguments("duplicate-head")]
    [Arguments("duplicate-branch")]
    [Arguments("missing-head")]
    [Arguments("branch-and-detached")]
    [Arguments("orphan-field")]
    [Arguments("truncated")]
    public void C448_V30_MalformedRegistrationCannotHideAmbiguousIdentity(string variant)
    {
        var oid = new string('a', 40);
        var row = $"worktree C:/fixture\0HEAD {oid}\0branch refs/heads/source\0";
        var malformed = variant switch
        {
            "duplicate-path" => row + "worktree C:/other\0\0",
            "duplicate-head" => row + $"HEAD {oid}\0\0",
            "duplicate-branch" => row + "branch refs/heads/other\0\0",
            "missing-head" => "worktree C:/fixture\0branch refs/heads/source\0\0",
            "branch-and-detached" => row + "detached\0\0",
            "orphan-field" => $"HEAD {oid}\0\0",
            _ => row.TrimEnd('\0'),
        };
        Should.Throw<IOException>(() => LandingGit.ParseRegistrations(malformed));
        LandingGit.ParseRegistrations(row + "\0").ShouldHaveSingleItem();
    }

    [Test]
    [Arguments("--all")]
    [Arguments("HEAD~1")]
    [Arguments("refs/tags/tag")]
    [Arguments("refs/heads/../invalid")]
    public async Task C448_V30_RefsParsingAndDestinationsFailClosed(string invalid)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var result = await fixture.Git.InspectAsync(fixture.Coordinates with { SourceFullRef = invalid }, CancellationToken.None);
        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe("invalid_identity");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(128)]
    public async Task C448_V05_RemoteErrorsAreUnknown(int exit)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var destination = await fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None);
        fixture.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(args[0] == "ls-remote"
            ? new(exit, "", "synthetic-secret-marker") : null);
        var observation = await fixture.Git.ObserveAsync(fixture.Repository, destination,
            fixture.SeedSha, fixture.ObservationRef, CancellationToken.None);
        observation.ContainsSource.ShouldBeFalse();
        observation.Sha.ShouldBeNull();
        observation.Reason.ShouldBe(exit == 0 ? "remote_response_invalid" : "remote_read_failed");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V09_ConfirmationUsesPushEndpoint(bool containing)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var other = Path.Combine(fixture.Root, "push.git");
        Directory.CreateDirectory(other);
        await fixture.RequiredAsync(other, "init", "--bare");
        if (containing) await fixture.RequiredAsync(fixture.Repository, "push", other, fixture.TargetRef);
        await fixture.RequiredAsync(fixture.Repository, "remote", "set-url", "--push", "origin", other);
        var destination = await fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None);
        fixture.Git.Trace.Clear();
        var observation = await fixture.Git.ObserveAsync(fixture.Repository, destination,
            fixture.SeedSha, fixture.ObservationRef, CancellationToken.None);
        observation.ContainsSource.ShouldBe(containing);
        fixture.Git.Trace.Where(a => a[0] == "ls-remote").ShouldAllBe(a => a.Contains(other));
        fixture.Git.Trace.Where(a => a[0] == "fetch").ShouldAllBe(a => a.Contains(other));
        if (containing) observation.Sha.ShouldBe(fixture.SeedSha);
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(128)]
    public async Task C448_V05_AncestryExitIsTriState(int ancestryExit)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var destination = await fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None);
        fixture.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(args[0] == "merge-base"
            ? new(ancestryExit, "", "synthetic-secret-marker") : null);
        var observation = await fixture.Git.ObserveAsync(fixture.Repository, destination,
            fixture.SeedSha, fixture.ObservationRef, CancellationToken.None);
        observation.ContainsSource.ShouldBe(ancestryExit == 0);
        observation.Reason.ShouldBe(ancestryExit == 128 ? "remote_ancestry_error" : null);
        await fixture.AssertRemoteSourceAsync();
    }
}
