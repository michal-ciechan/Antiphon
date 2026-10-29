using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
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
        Console.WriteLine($"C448_PUSH expectedEndpoint={JsonSerializer.Serialize(fixture.Remote)} actual={JsonSerializer.Serialize(fixture.Git.Trace.Where(a => a[0] == "push"))}");
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
        result.Diagnostic.Command.ShouldBe("git check-ref-format <ref>");
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

    // CARD-0642 V-2..V-5: the lease-scoped registration cache and the inspection scope.
    private static int WorktreeLists(LandingGitFixture fixture) =>
        fixture.Git.Trace.Count(a => a.Length > 1 && a[0] == "worktree" && a[1] == "list");

    private static bool IsTree(LandingRegistration row, string name) =>
        row.Path.Replace('\\', '/').EndsWith("/trees/" + name, StringComparison.OrdinalIgnoreCase);

    [Test]
    public async Task C642_ScopeCachesRegistrationsUntilOwnMutation()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var extra = Path.Combine(fixture.Root, "trees", "extra");
        using (fixture.Git.BeginOperationScope())
        {
            fixture.Git.Trace.Clear();
            for (var i = 0; i < 3; i++)
                (await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None))
                    .ShouldContain(r => IsTree(r, "source"));
            WorktreeLists(fixture).ShouldBe(1, "three reads inside one operation scope list registrations once");
            (await fixture.Git.RunAsync(fixture.Repository, ["worktree", "add", "--detach", extra, "HEAD"], CancellationToken.None))
                .Succeeded.ShouldBeTrue();
            var rows = await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None);
            WorktreeLists(fixture).ShouldBe(2, "the scope's own worktree mutation must force a re-list");
            rows.ShouldContain(r => IsTree(r, "extra"));
            fixture.Git.BeginOperationScope().Dispose(); // a nested scope borrows the outer one; disposing it ends nothing
            await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None);
            WorktreeLists(fixture).ShouldBe(2);
        }
        await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None);
        WorktreeLists(fixture).ShouldBe(3, "no cache outlives its operation scope");
    }

    [Test]
    public async Task C642_ScopeReListsAfterOwnOptionPrefixedHeadMove()
    {
        // The protocol's rebase/ff-merge run as `-c key=value <command>`; the cached HEAD must not survive them.
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        using var scope = fixture.Git.BeginOperationScope();
        (await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None))
            .Single(r => IsTree(r, "source")).Head.ShouldBe(fixture.SeedSha);
        (await fixture.Git.RunAsync(fixture.Source, ["-c", "commit.gpgSign=false", "commit", "--allow-empty", "-m", "moved"],
            CancellationToken.None)).Succeeded.ShouldBeTrue();
        var moved = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).Trim();
        moved.ShouldNotBe(fixture.SeedSha);
        (await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None))
            .Single(r => IsTree(r, "source")).Head.ShouldBe(moved);
        (await fixture.Git.InspectAsync(fixture.Coordinates, LandInspectionScope.IdentityAndStatus, CancellationToken.None))
            .Snapshot!.HeadSha.ShouldBe(moved);
    }

    [Test]
    public async Task C975_InspectAsyncAlwaysReadsCurrentRegistrations()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        using var scope = fixture.Git.BeginOperationScope();
        fixture.Git.Trace.Clear();
        var first = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        var second = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        first.Accepted.ShouldBeTrue(first.Reason);
        second.Accepted.ShouldBeTrue(second.Reason);
        second.Snapshot.ShouldBe(first.Snapshot);
        WorktreeLists(fixture).ShouldBe(4, "each mutation-authorizing identity sample reads current registrations");
        scope.Profile.Inspections.ShouldBe(2);
        scope.Profile.WorktreeLists.ShouldBe(4);
        scope.Profile.RegistrationHits.ShouldBe(0);
        scope.Profile.CanonicalHits.ShouldBeGreaterThan(0);
        scope.Profile.Processes.ShouldBe(fixture.Git.Trace.Count);
    }

    [Test]
    public async Task C642_ScopeSeesExternalRegistrationChange()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var external = new LandingGitFixture.FixtureGit(Path.Combine(fixture.Root, "home"), fixture.TaskId);
        var extra = Path.Combine(fixture.Root, "trees", "janitor");
        using var scope = fixture.Git.BeginOperationScope();
        fixture.Git.Trace.Clear();
        (await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None)).ShouldNotContain(r => IsTree(r, "janitor"));
        (await external.RunAsync(fixture.Repository, ["worktree", "add", "--detach", extra, "HEAD"], CancellationToken.None))
            .Succeeded.ShouldBeTrue();
        fixture.Git.Trace.ShouldNotContain(a => a[0] == "worktree" && a[1] == "add", "the change must come from outside the scoped instance");
        var rows = await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None);
        rows.ShouldContain(r => IsTree(r, "janitor"), "a registration change by another process must invalidate the scope cache");
        WorktreeLists(fixture).ShouldBe(2);
        (await external.RunAsync(fixture.Repository, ["worktree", "remove", extra], CancellationToken.None)).Succeeded.ShouldBeTrue();
        (await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None)).ShouldNotContain(r => IsTree(r, "janitor"));
        WorktreeLists(fixture).ShouldBe(3);
    }

    [Test]
    public async Task C642_IdentityAndStatusScopeSkipsIgnoredListing()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        Directory.CreateDirectory(Path.Combine(fixture.Source, ".antiphon"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, ".antiphon", "ignored-note.md"), "ignored\n");
        fixture.Git.Trace.Clear();
        var light = await fixture.Git.InspectAsync(fixture.Coordinates, LandInspectionScope.IdentityAndStatus, CancellationToken.None);
        light.Accepted.ShouldBeTrue(light.Reason);
        light.Snapshot!.IgnoredPaths.ShouldBeEmpty();
        fixture.Git.Trace.ShouldNotContain(a => a[0] == "ls-files");
        fixture.Git.Trace.ShouldContain(a => a[0] == "status", "the dirty check is unchanged by the scope");
        fixture.Git.Trace.Clear();
        var full = await fixture.Git.InspectAsync(fixture.Coordinates, LandInspectionScope.Full, CancellationToken.None);
        full.Accepted.ShouldBeTrue(full.Reason);
        full.Snapshot!.IgnoredPaths.ShouldContain(".antiphon/ignored-note.md");
        fixture.Git.Trace.ShouldContain(a => a[0] == "ls-files");
        var legacy = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        legacy.Snapshot!.IgnoredPaths.ShouldBe(full.Snapshot.IgnoredPaths, "the two-argument call stays Full");
    }

    [Test]
    public async Task C688_HeadFileScanFindsTargetCheckouts()
    {
        // CARD-0688 V-2: "master only in the main checkout" is asserted from HEAD files, never a listing.
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var detached = Path.Combine(fixture.Root, "trees", "detached");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "add", "--detach", detached, fixture.SeedSha);
        var workspace = new LandWorkspace(fixture.Git, Microsoft.Extensions.Options.Options.Create(
            new Antiphon.Server.Application.Settings.GitSettings { WorktreeBasePath = Path.Combine(fixture.Root, "trees") }), TimeProvider.System);
        var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, CancellationToken.None);
        fixture.Git.Trace.Clear();

        var scan = workspace.ScanHeadFiles(common);

        fixture.Git.Trace.ShouldBeEmpty("the scan reads files; it runs no git and no worktree list");
        scan.Count.ShouldBe(3);
        var main = scan.Single(h => h.IsMain);
        main.SymbolicRef.ShouldBe(fixture.TargetRef);
        LandingGit.PathsEqual(main.WorktreePath!, fixture.Repository).ShouldBeTrue();
        scan.Single(h => h.WorktreePath is not null && LandingGit.PathsEqual(h.WorktreePath, fixture.Source))
            .SymbolicRef.ShouldBe(fixture.SourceRef);
        var loose = scan.Single(h => h.WorktreePath is not null && LandingGit.PathsEqual(h.WorktreePath, detached));
        loose.SymbolicRef.ShouldBeNull();
        loose.Sha.ShouldBe(fixture.SeedSha);
        scan.Where(h => h.SymbolicRef == fixture.TargetRef).ShouldHaveSingleItem().IsMain.ShouldBeTrue();

        // Construction proof: git itself refuses a second checkout of master without an override.
        (await fixture.Git.RunAsync(detached, ["checkout", "master"], CancellationToken.None)).Succeeded.ShouldBeFalse();
        await fixture.RequiredAsync(detached, "checkout", "--ignore-other-worktrees", "master");
        fixture.Git.Trace.Clear();

        var forced = workspace.ScanHeadFiles(common).Where(h => h.SymbolicRef == fixture.TargetRef).ToList();

        fixture.Git.Trace.ShouldBeEmpty();
        forced.Count.ShouldBe(2);
        forced.ShouldContain(h => !h.IsMain && h.WorktreePath != null && LandingGit.PathsEqual(h.WorktreePath, detached));
    }

    [Test]
    public async Task C642_RecheckSourceRemoteIsOneRoundTripWithoutPins()
    {
        // CARD-0642 V-14 (absorbed by CARD-0688 D-1 as V-3).
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var fingerprint = (await fixture.Git.DestinationAsync(fixture.Repository, fixture.TargetRef, CancellationToken.None)).Fingerprint;
        var before = await fixture.Git.RunAsync(fixture.Repository, ["for-each-ref", "refs/antiphon/"], CancellationToken.None);
        fixture.Git.Trace.Clear();

        var recheck = await ((Antiphon.Server.Application.Interfaces.ILandingGit)fixture.Git).RecheckSourceRemoteAsync(fixture.Repository, fixture.SourceRef, fixture.SeedSha,
            fingerprint, CancellationToken.None);

        recheck.Reason.ShouldBeNull();
        recheck.Sha.ShouldBe(fixture.SeedSha);
        recheck.Fingerprint.ShouldBe(fingerprint);
        fixture.Git.Trace.Count(a => a[0] == "ls-remote").ShouldBe(1);
        fixture.Git.Trace.ShouldNotContain(a => a[0] == "fetch" || a[0] == "update-ref");
        (await fixture.Git.RunAsync(fixture.Repository, ["for-each-ref", "refs/antiphon/"], CancellationToken.None))
            .Output.ShouldBe(before.Output, "a recheck writes no pin ref");

        (await ((Antiphon.Server.Application.Interfaces.ILandingGit)fixture.Git).RecheckSourceRemoteAsync(fixture.Repository, fixture.SourceRef, fixture.SeedSha,
            new string('0', 64), CancellationToken.None)).Reason.ShouldBe("source_remote_endpoint_changed");

        await fixture.RequiredAsync(fixture.Remote, "update-ref", "-d", fixture.SourceRef);
        (await ((Antiphon.Server.Application.Interfaces.ILandingGit)fixture.Git).RecheckSourceRemoteAsync(fixture.Repository, fixture.SourceRef, fixture.SeedSha,
            fingerprint, CancellationToken.None)).Reason.ShouldBe("source_remote_missing");
    }

    // CARD-1076 V-1. A caller budget kills a push stalled in pre-push; the three-argument overload keeps the five-minute default.
    [Test]
    [Timeout(60_000)]
    public async Task C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal(CancellationToken ct)
    {
        LandingGit.DefaultBudget.ShouldBe(TimeSpan.FromMinutes(5));
        using var repo = new ScratchGitRepo("c1076-budget");
        await repo.CommitFileAsync("README.md", "seed\n");
        await repo.AddBareOriginAsync();
        await repo.CommitFileAsync("more.md", "more\n");
        // The sleeper records its pid so the test can close the pipe. Tree kill does not reach a
        // reparented msys grandchild, so the journal clear follows the holder, not the return.
        var pidFile = Path.Combine(repo.Path, "holder.pid");
        var hook = await InstallExecutableHookAsync(repo.Path, "pre-push",
            "#!/bin/sh\necho $$ > \"" + pidFile.Replace('\\', '/') + "\"\nsleep 30\nexit 0\n", ct);
        var git = new LandingGit();

        var watch = Stopwatch.StartNew();
        try
        {
            var thrown = await Should.ThrowAsync<TimeoutException>(async () => await git.RunAsync(repo.Path,
                ["push", "origin", "master"], new LandingGitRunOptions(Budget: TimeSpan.FromSeconds(2)), ct));
            watch.Stop();

            Console.WriteLine($"C1076_BUDGET elapsed={watch.Elapsed} error={thrown}");

            thrown.Message.ShouldBe("git_timeout");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15) + LandingGit.KillDrainGrace);
            var common = await git.CommonDirectoryAsync(repo.Path, ct);
            var children = Path.Combine(common, "antiphon", "children");
            KillHolder(pidFile);
            var cleared = Stopwatch.StartNew();
            while (JournalFiles(children).Length > 0)
            {
                if (cleared.Elapsed > TimeSpan.FromSeconds(5)) break;
                await Task.Delay(50, ct);
            }
            JournalFiles(children).ShouldBeEmpty();
            File.Delete(hook);
            (await git.RunAsync(repo.Path, ["push", "origin", "master"], ct)).Succeeded.ShouldBeTrue();
        }
        finally
        {
            KillHolder(pidFile);
        }
    }

    // CARD-1076. Windows qualification 28bbb58e: the 2s push budget did not return until 30.5s,
    // the pre-push sleeper's own lifetime. Kill(entireProcessTree) does not reach a grandchild
    // that has been reparented, and ReadToEndAsync then waits until that grandchild closes the
    // redirected pipe. This stand-in is that shape on Linux.
    [Test]
    [Timeout(60_000)]
    public async Task C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("POSIX double-fork witness; the in-tree pre-push sleeper is C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal.");
            return;
        }

        using var repo = new ScratchGitRepo("c1076-pipe");
        await repo.CommitFileAsync("README.md", "seed\n");
        var pidFile = Path.Combine(repo.Path, "holder.pid");
        var script = Path.Combine(repo.Path, "holder.sh");
        await File.WriteAllTextAsync(script, DetachedPipeHolderGit.Script(pidFile), ct);
        var git = new DetachedPipeHolderGit(script);
        TimeoutException? thrown = null;
        var watch = Stopwatch.StartNew();
        try
        {
            thrown = await Should.ThrowAsync<TimeoutException>(async () => await git.RunAsync(repo.Path,
                ["push", "origin", "master"], new LandingGitRunOptions(Budget: TimeSpan.FromSeconds(2)), ct));
            watch.Stop();
            Console.WriteLine($"C1076_PIPE elapsed={watch.Elapsed} error={thrown}");
            thrown.Message.ShouldBe("git_timeout");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
            var holder = int.Parse(File.ReadAllText(pidFile).Trim());
            HolderAlive(holder).ShouldBeTrue(
                "the reparented grandchild still holds stdout; waiting for pipe EOF would run out its sleep");
            var common = await git.CommonDirectoryAsync(repo.Path, ct);
            var children = Path.Combine(common, "antiphon", "children");
            // The call has returned and the Process handle is done. The holder is still alive,
            // so the journal must still fence admission until both redirected pipes reach EOF.
            JournalFiles(children).ShouldNotBeEmpty(
                "a live pipe holder keeps the repository child journal until the abandoned streams close");
            var preview = await RunRecoveryAsync(repo.Path);
            preview.Exit.ShouldBe(3, preview.Output);
            preview.Output.ShouldContain("retained (");
            preview.Output.ShouldNotContain("recovered (");
            JournalFiles(children).ShouldNotBeEmpty("preview recovery leaves a live descendant's journal");
            var execute = await RunRecoveryAsync(repo.Path, "-Execute");
            execute.Exit.ShouldBe(3, execute.Output);
            execute.Output.ShouldNotContain("recovered (");
            JournalFiles(children).ShouldNotBeEmpty(
                "-Execute without -ConfirmDescendantsExited still treats the descendant as owned");
            KillHolder(pidFile);
            var cleared = Stopwatch.StartNew();
            while (JournalFiles(children).Length > 0)
            {
                if (cleared.Elapsed > TimeSpan.FromSeconds(5)) break;
                await Task.Delay(50, ct);
            }
            JournalFiles(children).ShouldBeEmpty(
                "once the holder exits, the abandon continuation removes the journal");
        }
        finally
        {
            watch.Stop();
            KillHolder(pidFile);
        }
    }

    // CARD-1076 D-2. A root that exits 0 must not become git_timeout because a reparented
    // grandchild keeps stdout open past the remaining budget. The journal stays until that close.
    [Test]
    [Timeout(60_000)]
    public async Task C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("POSIX double-fork witness for a normal exit whose grandchild keeps stdout.");
            return;
        }

        using var repo = new ScratchGitRepo("c1076-normal");
        await repo.CommitFileAsync("README.md", "seed\n");
        var pidFile = Path.Combine(repo.Path, "holder.pid");
        var script = Path.Combine(repo.Path, "holder.sh");
        await File.WriteAllTextAsync(script, NormalExitPipeHolderGit.Script(pidFile), ct);
        var git = new NormalExitPipeHolderGit(script);
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var children = Path.Combine(common, "antiphon", "children");
        var call = git.RunAsync(repo.Path, ["push", "origin", "master"],
            new LandingGitRunOptions(Budget: TimeSpan.FromSeconds(2)), ct);
        var watch = Stopwatch.StartNew();
        try
        {
            var pastKillWindow = false;
            while (watch.Elapsed < TimeSpan.FromSeconds(12))
            {
                if (call.IsCompleted) break;
                if (File.Exists(pidFile)
                    && int.TryParse((await File.ReadAllTextAsync(pidFile, ct)).Trim(), out var holder)
                    && HolderAlive(holder)
                    && watch.Elapsed > TimeSpan.FromSeconds(2) + LandingGit.KillDrainGrace + TimeSpan.FromMilliseconds(500))
                {
                    JournalFiles(children).ShouldNotBeEmpty(
                        "a successful exit keeps the journal until the detached holder closes stdout");
                    pastKillWindow = true;
                    break;
                }
                await Task.Delay(50, ct);
            }

            pastKillWindow.ShouldBeTrue("the call stays in flight past the budget and the kill grace");
            var result = await call;
            watch.Stop();
            Console.WriteLine($"C1076_NORMAL elapsed={watch.Elapsed} exit={result.ExitCode} error={result.Diagnostic}");
            result.Succeeded.ShouldBeTrue(result.Diagnostic);
            watch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(2));
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
            JournalFiles(children).ShouldBeEmpty();
        }
        finally
        {
            watch.Stop();
            KillHolder(pidFile);
            try { await call; } catch { /* observe a red timeout so it is not unobserved */ }
        }
    }

    private static bool HolderAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    private static void KillHolder(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile)) return;
            if (!int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) || pid <= 0) return;
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { }
    }

    private static string[] JournalFiles(string children) =>
        Directory.Exists(children) ? Directory.EnumerateFiles(children).ToArray() : [];

    private static async Task<(int Exit, string Output)> RunRecoveryAsync(string repository, params string[] options)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Antiphon.sln"))) root = root.Parent;
        root.ShouldNotBeNull();
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root.FullName, "scripts", "recover-repository-children.ps1"));
        start.ArgumentList.Add("-Repository");
        start.ArgumentList.Add(repository);
        foreach (var option in options) start.ArgumentList.Add(option);
        using var process = Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = stdout + stderr;
        Console.WriteLine($"C1076_RECOVERY exit={process.ExitCode} output={output}");
        return (process.ExitCode, output);
    }

    // CARD-1076 V-2. The live push journal names the task and purpose, then the owner deletes it on exit.
    [Test]
    [Timeout(30_000)]
    public async Task C1076_TaggedPushJournalNamesTaskAndPurposeWhileAlive(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c1076-tag");
        await repo.CommitFileAsync("README.md", "seed\n");
        await repo.AddBareOriginAsync();
        await repo.CommitFileAsync("more.md", "more\n");
        await InstallExecutableHookAsync(repo.Path, "pre-push", "#!/bin/sh\nsleep 8\nexit 0\n", ct);
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var children = Path.Combine(common, "antiphon", "children");
        var taskId = Guid.NewGuid();
        var pushing = git.RunAsync(repo.Path, ["push", "origin", "master"],
            new LandingGitRunOptions(Child: new RepositoryChildTag(taskId, RepositoryChildPurposes.RemotePrepPush)), ct);

        string? path = null;
        using (var poll = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            poll.CancelAfter(TimeSpan.FromSeconds(8));
            while (path is null)
            {
                if (Directory.Exists(children))
                    path = Directory.EnumerateFiles(children, "*.json").FirstOrDefault();
                if (path is not null) break;
                if (pushing.IsCompleted) await pushing;
                await Task.Delay(50, poll.Token);
            }
        }

        var record = JsonSerializer.Deserialize<RepositoryChildJournal.ChildRecord>(await File.ReadAllTextAsync(path!, ct));
        record.ShouldNotBeNull();
        record.Purpose.ShouldBe(RepositoryChildPurposes.RemotePrepPush);
        record.TaskId.ShouldBe(taskId);
        (await pushing).Succeeded.ShouldBeTrue();
        File.Exists(path).ShouldBeFalse();
    }

    // CARD-1432 D2 (Review 9e126982). The update-ref --stdin deletion batch is a mutating child: a standing
    // journal record names its process while it runs and the owner deletes it once the exact handle exited.
    [Test]
    [Timeout(30_000)]
    public async Task C1432_UpdateRefsBatchRunsUnderTheChildJournal(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c1432-journal");
        await repo.CommitFileAsync("README.md", "seed\n");
        var git = new LandingGit();
        var sha = (await git.RunAsync(repo.Path, ["rev-parse", "HEAD"], ct)).Output.Trim();
        var pin = $"refs/antiphon/land/{Guid.NewGuid():N}/{Guid.NewGuid():N}/source";
        (await git.RunAsync(repo.Path, ["update-ref", pin, sha], ct)).Succeeded.ShouldBeTrue();
        // The hook runs inside the ref transaction, so the batch stays alive while it sleeps.
        await InstallExecutableHookAsync(repo.Path, "reference-transaction", "#!/bin/sh\n[ \"$1\" = prepared ] && sleep 8\nexit 0\n", ct);
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var children = Path.Combine(common, "antiphon", "children");
        var deleting = git.UpdateRefsAsync(repo.Path, [(pin, sha)], ct);

        string? path = null;
        RepositoryChildJournal.ChildRecord? record = null;
        using (var poll = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            poll.CancelAfter(TimeSpan.FromSeconds(8));
            while (record?.ProcessId is null)
            {
                if (Directory.Exists(children))
                    path = Directory.EnumerateFiles(children, "*.json").FirstOrDefault();
                if (path is not null)
                    record = JsonSerializer.Deserialize<RepositoryChildJournal.ChildRecord>(await File.ReadAllTextAsync(path, ct));
                if (record?.ProcessId is not null) break;
                if (deleting.IsCompleted) await deleting;
                await Task.Delay(50, poll.Token);
            }
        }

        record.ShouldNotBeNull("a fencing record exists while the batch runs");
        Console.WriteLine($"C1432_JOURNAL expectedCommon={JsonSerializer.Serialize(common)} record={JsonSerializer.Serialize(record)}");
        record.ProcessId.ShouldNotBeNull("the started child is recorded");
        record.CommonDirectory.ShouldBe(common);
        record.Purpose.ShouldBeNull("the batch is the untagged journal, like every other update-ref");
        var result = await deleting;
        result.Succeeded.ShouldBeTrue(result.Diagnostic);
        File.Exists(path!).ShouldBeFalse("the owner deletes its record after the exact handle exited");
        Directory.EnumerateFiles(children).ShouldBeEmpty();
        (await git.RunAsync(repo.Path, ["show-ref", "--verify", pin], ct)).Succeeded.ShouldBeFalse("the batch applied");
    }

    // Review 22c9afa6 D-1. update-ref --stdin that exits before reading faults the write with
    // EPIPE. That fault is not a descendant. The base cleared the journal; the round-2 drain
    // left it, and a dead root then fences every later land.
    [Test]
    [Timeout(60_000)]
    public async Task C1076_UpdateRefsStdinBreakClearsTheJournal(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("POSIX stand-in that closes stdin so the batch write gets EPIPE.");
            return;
        }

        using var repo = new ScratchGitRepo("c1076-epipe");
        await repo.CommitFileAsync("README.md", "seed\n");
        var git = new StdinBreakGit();
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var children = Path.Combine(common, "antiphon", "children");
        // Larger than the pipe buffer, so the write cannot succeed before the stand-in closes stdin.
        var oid = new string('b', 40);
        var deletes = new (string Ref, string Oid)[2048];
        for (var i = 0; i < deletes.Length; i++)
            deletes[i] = ($"refs/antiphon/land/{Guid.NewGuid():N}/source", oid);

        Exception? thrown = null;
        LandingGitResult? result = null;
        try { result = await git.UpdateRefsAsync(repo.Path, deletes, ct); }
        catch (Exception ex) { thrown = ex; }

        Console.WriteLine($"C1076_EPIPE thrown={thrown?.GetType().Name}: {thrown?.Message} resultExit={result?.ExitCode} resultError={result?.Diagnostic}");
        thrown.ShouldBeOfType<IOException>("the base surfaces the broken-pipe write, not a swallowed success");
        result.ShouldBeNull();
        JournalFiles(children).ShouldBeEmpty(
            "an EPIPE write with both reads at EOF and the root exited must drop the journal, as the base did");
    }

    // Review 22c9afa6 A-2. The batch path's budget is the fixed five minutes, so cancellation
    // is the kill. A detached stdout holder keeps the journal until that pipe reaches EOF.
    [Test]
    [Timeout(60_000)]
    public async Task C1076_UpdateRefsCancellationKeepsJournalUntilStdoutHolderExits(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("POSIX double-fork witness for the update-ref --stdin abandon path.");
            return;
        }

        using var repo = new ScratchGitRepo("c1076-batch-pipe");
        await repo.CommitFileAsync("README.md", "seed\n");
        var pidFile = Path.Combine(repo.Path, "holder.pid");
        var script = Path.Combine(repo.Path, "holder.sh");
        await File.WriteAllTextAsync(script, BatchStdoutHolderGit.Script(pidFile), ct);
        var git = new BatchStdoutHolderGit(script);
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var children = Path.Combine(common, "antiphon", "children");
        var pin = $"refs/antiphon/land/{Guid.NewGuid():N}/source";
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var call = git.UpdateRefsAsync(repo.Path, [(pin, new string('c', 40))], cancel.Token);
        var watch = Stopwatch.StartNew();
        try
        {
            var ready = false;
            while (watch.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (File.Exists(pidFile) && JournalFiles(children).Length > 0)
                {
                    ready = true;
                    break;
                }
                if (call.IsCompleted) break;
                await Task.Delay(50, ct);
            }

            ready.ShouldBeTrue("the batch journal is recorded while the child is still running");
            cancel.Cancel();
            var thrown = await Should.ThrowAsync<OperationCanceledException>(async () => await call);
            watch.Stop();
            Console.WriteLine($"C1076_BATCH_PIPE elapsed={watch.Elapsed} error={thrown.GetType().Name}");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15),
                "cancellation returns inside the kill grace, not the stand-in's own sleep");
            var holder = int.Parse((await File.ReadAllTextAsync(pidFile, ct)).Trim());
            HolderAlive(holder).ShouldBeTrue(
                "the reparented grandchild still holds stdout after the batch call has returned");
            JournalFiles(children).ShouldNotBeEmpty(
                "a live pipe holder keeps the batch journal until the abandoned reads close");
            KillHolder(pidFile);
            var cleared = Stopwatch.StartNew();
            while (JournalFiles(children).Length > 0)
            {
                if (cleared.Elapsed > TimeSpan.FromSeconds(5)) break;
                await Task.Delay(50, ct);
            }
            JournalFiles(children).ShouldBeEmpty(
                "once the holder exits, the abandon continuation removes the batch journal");
        }
        finally
        {
            watch.Stop();
            cancel.Cancel();
            KillHolder(pidFile);
            try { await call; } catch { /* observe a red cancellation so it is not unobserved */ }
        }
    }

    private static async Task<string> InstallExecutableHookAsync(string repository, string name, string body, CancellationToken ct)
    {
        var hooks = Path.Combine(repository, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, name);
        await File.WriteAllTextAsync(hook, body, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return hook;
    }

    /// <summary>A push stand-in whose grandchild is reparented to init and keeps stdout open.</summary>
    private sealed class DetachedPipeHolderGit(string scriptPath) : LandingGit
    {
        public static string Script(string pidFile) =>
            "#!/bin/sh\n" +
            "(\n" +
            "trap '' HUP\n" +
            "sh -c 'trap \"\" HUP; echo $$ > \"" + pidFile + "\"; exec sleep 20' </dev/null &\n" +
            ") &\n" +
            "sleep 20\n" +
            "exit 0\n";

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            if (start.ArgumentList.Count == 0 || start.ArgumentList[0] != "push")
                return base.StartProcess(start);
            return StartHolder(start, scriptPath);
        }

        internal static Process? StartHolder(ProcessStartInfo start, string scriptPath)
        {
            var standIn = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = start.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            standIn.ArgumentList.Add(scriptPath);
            return Process.Start(standIn);
        }
    }

    /// <summary>A push stand-in that exits 0 at once while a reparented grandchild keeps stdout.</summary>
    private sealed class NormalExitPipeHolderGit(string scriptPath) : LandingGit
    {
        public static string Script(string pidFile) =>
            "#!/bin/sh\n" +
            "trap '' HUP\n" +
            "(\n" +
            "trap '' HUP\n" +
            "sh -c 'trap \"\" HUP; echo $$ > \"" + pidFile + "\"; exec sleep 8' </dev/null &\n" +
            ") &\n" +
            "i=0\n" +
            "while [ ! -s \"" + pidFile + "\" ] && [ \"$i\" -lt 100 ]; do\n" +
            "  i=$((i+1))\n" +
            "  sleep 0.05\n" +
            "done\n" +
            "exit 0\n";

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            if (start.ArgumentList.Count == 0 || start.ArgumentList[0] != "push")
                return base.StartProcess(start);
            return DetachedPipeHolderGit.StartHolder(start, scriptPath);
        }
    }

    /// <summary>An update-ref --stdin stand-in that closes stdin and exits 3 without reading.</summary>
    private sealed class StdinBreakGit : LandingGit
    {
        protected override Process? StartProcess(ProcessStartInfo start)
        {
            if (!IsUpdateRefStdin(start)) return base.StartProcess(start);
            var standIn = RedirectedShell(start);
            standIn.ArgumentList.Add("-c");
            standIn.ArgumentList.Add("exec 0<&-; sleep 0.2; exit 3");
            return Process.Start(standIn);
        }
    }

    /// <summary>An update-ref --stdin stand-in whose reparented grandchild keeps stdout open.</summary>
    private sealed class BatchStdoutHolderGit(string scriptPath) : LandingGit
    {
        public static string Script(string pidFile) =>
            "#!/bin/sh\n" +
            "(\n" +
            "trap '' HUP\n" +
            "sh -c 'trap \"\" HUP; echo $$ > \"" + pidFile + "\"; exec sleep 20' </dev/null &\n" +
            ") &\n" +
            "sleep 30\n" +
            "exit 0\n";

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            if (!IsUpdateRefStdin(start)) return base.StartProcess(start);
            var standIn = RedirectedShell(start);
            standIn.ArgumentList.Add(scriptPath);
            return Process.Start(standIn);
        }
    }

    private static bool IsUpdateRefStdin(ProcessStartInfo start) =>
        start.ArgumentList.Count >= 2 && start.ArgumentList[0] == "update-ref" && start.ArgumentList[1] == "--stdin";

    private static ProcessStartInfo RedirectedShell(ProcessStartInfo start) => new("/bin/sh")
    {
        WorkingDirectory = start.WorkingDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    private sealed class MarkerHomeGit(string home, Guid taskId, string configPath) : LandingGitFixture.FixtureGit(home, taskId)
    {
        protected override void ConfigureProcess(System.Diagnostics.ProcessStartInfo start)
        {
            base.ConfigureProcess(start);
            start.Environment["GIT_CONFIG_GLOBAL"] = configPath;
        }
    }
}
