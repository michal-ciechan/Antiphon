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
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V30_MultiplePushDestinationsCannotBeSelectedImplicitly(bool multiple)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var other = Path.Combine(fixture.Root, "other-endpoint.git");
        await fixture.RequiredAsync(fixture.Root, "clone", "--bare", fixture.Remote, other);
        await fixture.RequiredAsync(fixture.Repository, "config", "--add", "remote.origin.pushurl", fixture.Remote);
        if (multiple) await fixture.RequiredAsync(fixture.Repository, "config", "--add", "remote.origin.pushurl", other);
        fixture.Git.Trace.Clear();
        Exception? refusal = null;
        try { await fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None); }
        catch (IOException ex) { refusal = ex; }
        if (multiple) refusal.ShouldNotBeNull("multiple push destinations must be refused, never selected implicitly");
        else refusal.ShouldBeNull();
        fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a[0] == "fetch" || a.Contains("remove"));
        (await fixture.RequiredAsync(other, "rev-parse", fixture.TargetRef)).Trim().ShouldBe(fixture.SeedSha);
        await fixture.AssertRemoteSourceAsync();
    }

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

    [Test]
    [Arguments("status", 0)]
    [Arguments("status", 1)]
    [Arguments("status", 128)]
    [Arguments("status", -1)]
    [Arguments("status", int.MinValue)]
    [Arguments("status", int.MaxValue)]
    [Arguments("ls-files", 128)]
    public async Task C498_InspectionCarriesCommandAndExit(string command, int exit)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        if (exit == 0 && command == "status")
        {
            var accepted = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
            accepted.Accepted.ShouldBeTrue();
            accepted.Diagnostic.ShouldBeNull();
            await fixture.AssertRemoteSourceAsync();
            return;
        }
        fixture.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(
            args[0] == command ? new LandingGitResult(exit, "", "synthetic-secret-marker") : null);
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe(command == "status" ? "status_error" : "ignored_status_error");
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic!.Command.ShouldBe(command == "status" ? "git status --porcelain=v1" : "git ls-files --others --ignored");
        result.Diagnostic.ExitCode.ShouldBe(exit);
        result.Diagnostic.Code.ShouldBe($"git_exit_{exit}");
        result.Diagnostic.ExceptionType.ShouldBeNull();
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("worktree list")]
    [Arguments("rev-parse --absolute-git-dir")]
    [Arguments("rev-parse --git-common-dir")]
    public async Task C498_RequiredCommandFailureKeepsCommandIdentity(string command)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        fixture.Git.BeforeCommand = (_, args) =>
        {
            var match = command == "worktree list" ? args[0] == "worktree"
                : command.Contains("absolute-git-dir", StringComparison.Ordinal) ? args.Contains("--absolute-git-dir")
                : args.Contains("--git-common-dir");
            return Task.FromResult<LandingGitResult?>(match ? new LandingGitResult(128, "", "synthetic-secret-marker") : null);
        };
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        result.Reason.ShouldBe("identity_io_error");
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic!.ExceptionType.ShouldBe("LandingGitCommandException");
        result.Diagnostic.Command.ShouldBe(command.StartsWith("worktree", StringComparison.Ordinal) ? "git worktree list" : "git rev-parse <identity>");
        result.Diagnostic.ExitCode.ShouldBe(128);
        result.Diagnostic.Code.ShouldBe("git_exit_128");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("missing-worktree")]
    [Arguments("unauthorized")]
    [Arguments("hostile-io")]
    [Arguments("start-failed")]
    public async Task C498_FilesystemFailureHasNoInventedExit(string kind)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        LandSourceCoordinates coordinates = fixture.Coordinates;
        if (kind == "missing-worktree")
            coordinates = fixture.Coordinates with { WorktreePath = Path.Combine(fixture.Root, "missing-tree") };
        else if (kind == "unauthorized")
            fixture.Git.BeforeCommand = (_, _) => throw new UnauthorizedAccessException("synthetic-secret-marker");
        else if (kind == "hostile-io")
            fixture.Git.BeforeCommand = (_, _) => throw new IOException("synthetic-secret-marker");
        else
            fixture.Git.BeforeCommand = (_, _) => throw new IOException("git_start_failed");
        var result = await fixture.Git.InspectAsync(coordinates, CancellationToken.None);
        if (kind == "missing-worktree")
        {
            result.Diagnostic!.ExceptionType.ShouldBe("IOException");
            result.Diagnostic.Command.ShouldBe("filesystem canonicalization");
            result.Diagnostic.ExitCode.ShouldBeNull();
            result.Diagnostic.Code.ShouldBe("path_missing_or_inaccessible");
        }
        else if (kind == "unauthorized")
        {
            result.Reason.ShouldBe("identity_inaccessible");
            result.Diagnostic!.ExceptionType.ShouldBe("UnauthorizedAccessException");
            result.Diagnostic.ExitCode.ShouldBeNull();
            result.Diagnostic.Command.ShouldBeNull();
            result.Diagnostic.Code.ShouldBeNull();
        }
        else if (kind == "hostile-io")
        {
            result.Reason.ShouldBe("identity_io_error");
            result.Diagnostic!.ExceptionType.ShouldBe("IOException");
            result.Diagnostic.ExitCode.ShouldBeNull();
        }
        else
        {
            result.Diagnostic!.Code.ShouldBe("git_start_failed");
            result.Diagnostic.ExitCode.ShouldBeNull();
        }
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C498_RealStderrStaysSuppressed()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var markerDir = Path.Combine(fixture.Root, "home", "synthetic-secret-marker");
        Directory.CreateDirectory(markerDir);
        var config = Path.Combine(markerDir, "config");
        await File.WriteAllTextAsync(config, "[broken \"synthetic-secret-marker://user:pw@host/?q=1\"\n");
        var git = new MarkerHomeGit(Path.Combine(fixture.Root, "home"), fixture.TaskId, config);
        var result = await git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        result.Reason.ShouldBe("identity_io_error");
        result.Diagnostic!.Code.ShouldBe("git_exit_128");
        result.Diagnostic.Command.ShouldBe("git rev-parse <identity>");
        (result.Diagnostic.Command + result.Diagnostic.Code + (await git.RunAsync(fixture.Repository, ["rev-parse", "--absolute-git-dir"], CancellationToken.None)).Diagnostic
            + (await git.RunAsync(fixture.Repository, ["rev-parse", "--absolute-git-dir"], CancellationToken.None)).Output)
            .ShouldNotContain("synthetic-secret-marker");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("source_dirty")]
    [Arguments("active_sequencer")]
    [Arguments("source_equals_target")]
    [Arguments("wrong_repository")]
    public async Task C498_SemanticRefusalsCarryNoDiagnostic(string reason)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        LandSourceInspection result;
        if (reason == "source_dirty")
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "keep.txt"), "dirty\n");
            result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        }
        else if (reason == "active_sequencer")
        {
            var gitDir = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "--absolute-git-dir")).Trim();
            Directory.CreateDirectory(Path.Combine(gitDir, "rebase-merge"));
            result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        }
        else if (reason == "source_equals_target")
            result = await fixture.Git.InspectAsync(fixture.Coordinates with { SourceFullRef = fixture.TargetRef }, CancellationToken.None);
        else
            result = await fixture.Git.InspectAsync(fixture.Coordinates with { RepositoryPath = fixture.Remote }, CancellationToken.None);
        result.Reason.ShouldBe(reason);
        result.Diagnostic.ShouldBeNull();
        await fixture.AssertRemoteSourceAsync();
    }

    private sealed class MarkerHomeGit(string home, Guid taskId, string configPath) : LandingGitFixture.FixtureGit(home, taskId)
    {
        protected override void ConfigureProcess(System.Diagnostics.ProcessStartInfo start)
        {
            base.ConfigureProcess(start);
            start.Environment["GIT_CONFIG_GLOBAL"] = configPath;
        }
    }
}
