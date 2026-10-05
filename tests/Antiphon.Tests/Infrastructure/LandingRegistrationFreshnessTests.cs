using System.Security.Cryptography;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandingRegistrationFreshnessTests
{
    [Test]
    [Arguments(LandInspectionScope.IdentityOnly)]
    [Arguments(LandInspectionScope.IdentityAndStatus)]
    [Arguments(LandInspectionScope.Full)]
    public async Task C975_CachedBacklinkChangeRefusesIdentity(LandInspectionScope inspectionScope)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var corruption = await BacklinkCorruption.CreateAsync(fixture);
        using var scope = fixture.Git.BeginOperationScope();
        await WarmCacheAsync(fixture);
        await corruption.RedirectAsync();
        fixture.Git.Trace.Clear();

        var result = await fixture.Git.InspectAsync(fixture.Coordinates, inspectionScope, CancellationToken.None);

        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe("registration_mismatch");
        result.Snapshot.ShouldBeNull();
        WorktreeLists(fixture).ShouldBe(1, "corruption must be followed by a real registration list");
        await corruption.AssertPreservedAsync();
    }

    [Test]
    [Arguments(LandInspectionScope.IdentityAndStatus)]
    [Arguments(LandInspectionScope.Full)]
    public async Task C975_SecondIdentityReadSeesBacklinkChange(LandInspectionScope inspectionScope)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var corruption = await BacklinkCorruption.CreateAsync(fixture);
        using var scope = fixture.Git.BeginOperationScope();
        await WarmCacheAsync(fixture);
        var injections = 0;
        fixture.Git.AfterCommand = async (directory, arguments, result) =>
        {
            if (LandingGit.PathsEqual(directory, fixture.Source) && arguments[0] == "status" && injections == 0)
            {
                result.Succeeded.ShouldBeTrue();
                WorktreeLists(fixture).ShouldBe(1, "the first identity sample succeeded before status");
                injections++;
                await corruption.RedirectAsync();
            }
        };
        fixture.Git.Trace.Clear();

        var result = await fixture.Git.InspectAsync(fixture.Coordinates, inspectionScope, CancellationToken.None);

        injections.ShouldBe(1);
        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe("source_changed");
        result.Snapshot.ShouldBeNull();
        WorktreeLists(fixture).ShouldBe(2, "the second identity sample must list after the backlink write");
        await corruption.AssertPreservedAsync();
    }

    [Test]
    public async Task C975_RecoveryProofSeesBacklinkChange()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var local = fixture.SeedSha;
        var reviewedTree = Path.Combine(fixture.Root, "trees", "reviewed");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "add", "--detach", reviewedTree, local);
        await File.WriteAllTextAsync(Path.Combine(reviewedTree, "keep.txt"), "reviewed\n");
        await fixture.RequiredAsync(reviewedTree, "add", ".");
        await fixture.RequiredAsync(reviewedTree, "commit", "-m", "reviewed tip");
        var reviewed = (await fixture.RequiredAsync(reviewedTree, "rev-parse", "HEAD")).Trim();
        reviewed.ShouldNotBe(local);
        await fixture.RequiredAsync(fixture.Repository, "update-ref", "--no-deref", fixture.SourceRef, reviewed, local);
        (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(reviewed);
        (await File.ReadAllTextAsync(Path.Combine(fixture.Source, "keep.txt"))).ShouldBe("seed\n");
        // Establish that this actual half-reset reaches a successful proof before injecting corruption.
        (await fixture.Git.InspectRecoveryCheckoutAsync(fixture.Coordinates, local, reviewed, CancellationToken.None))
            .Accepted.ShouldBeTrue();
        await using var corruption = await BacklinkCorruption.CreateAsync(fixture);
        using var scope = fixture.Git.BeginOperationScope();
        await WarmCacheAsync(fixture);
        var injections = 0;
        fixture.Git.AfterCommand = async (directory, arguments, result) =>
        {
            if (LandingGit.PathsEqual(directory, fixture.Source) && arguments[0] == "ls-tree"
                && arguments[^1] == reviewed && injections == 0)
            {
                result.Succeeded.ShouldBeTrue();
                WorktreeLists(fixture).ShouldBe(1, "the entry identity sample preceded the reviewed tree read");
                injections++;
                await corruption.RedirectAsync();
            }
        };
        fixture.Git.Trace.Clear();

        var result = await fixture.Git.InspectRecoveryCheckoutAsync(fixture.Coordinates, local, reviewed, CancellationToken.None);

        injections.ShouldBe(1);
        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe("adopt_local_changed");
        WorktreeLists(fixture).ShouldBe(2, "strict proof must sample registration again at exit");
        await corruption.AssertPreservedAsync();
    }

    [Test]
    public async Task C975_RegistrationReadFailureDoesNotReuseCache()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var preservation = await BacklinkCorruption.CreateAsync(fixture);
        using var scope = fixture.Git.BeginOperationScope();
        await WarmCacheAsync(fixture);
        var failures = 0;
        fixture.Git.BeforeCommand = (_, arguments) =>
        {
            if (!IsWorktreeList(arguments) || failures != 0) return Task.FromResult<LandingGitResult?>(null);
            failures++;
            return Task.FromResult<LandingGitResult?>(new(128, "", "private fixture stderr must not escape"));
        };
        fixture.Git.Trace.Clear();

        var result = await fixture.Git.InspectAsync(fixture.Coordinates, LandInspectionScope.IdentityOnly, CancellationToken.None);

        failures.ShouldBe(1);
        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe("identity_io_error");
        result.Snapshot.ShouldBeNull();
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Command.ShouldBe("git worktree list");
        result.Diagnostic.ExitCode.ShouldBe(128);
        result.Diagnostic.Code.ShouldNotBeNullOrWhiteSpace();
        result.Diagnostic.ToString().ShouldNotContain("private fixture stderr");
        WorktreeLists(fixture).ShouldBe(1);
        scope.Profile.RegistrationHits.ShouldBe(0);
        await preservation.AssertPreservedAsync();
    }

    private static async Task WarmCacheAsync(LandingGitFixture fixture)
    {
        var rows = await fixture.Git.RegistrationsAsync(fixture.Repository, CancellationToken.None);
        rows.Count(row => LandingGit.PathsEqual(row.Path, fixture.Source)).ShouldBe(1);
    }

    private static bool IsWorktreeList(IReadOnlyList<string> arguments) =>
        arguments.Count >= 2 && arguments[0] == "worktree" && arguments[1] == "list";

    private static int WorktreeLists(LandingGitFixture fixture) => fixture.Git.Trace.Count(IsWorktreeList);

    /// <summary>Corrupt only owned metadata; retain it until after every outcome/preservation assertion.</summary>
    private sealed class BacklinkCorruption(
        LandingGitFixture fixture, LandingGitFixture.FixtureGit reader, string backlink, byte[] original,
        string worktrees, DateTime stamp, int entryCount, string image) : IAsyncDisposable
    {
        private bool redirected;

        public static async Task<BacklinkCorruption> CreateAsync(LandingGitFixture fixture)
        {
            var reader = new LandingGitFixture.FixtureGit(Path.Combine(fixture.Root, "home"), fixture.TaskId);
            var admin = (await RequiredAsync(reader, fixture.Source, "rev-parse", "--absolute-git-dir")).Trim();
            var common = (await RequiredAsync(reader, fixture.Repository, "rev-parse", "--git-common-dir")).Trim();
            common = Path.GetFullPath(common, fixture.Repository);
            var worktrees = Path.Combine(common, "worktrees");
            Path.GetDirectoryName(admin).ShouldBe(worktrees, "derive the linked admin path from Git inside the fixture");
            var backlink = Path.Combine(admin, "gitdir");
            return new(fixture, reader, backlink, await File.ReadAllBytesAsync(backlink), worktrees,
                Directory.GetLastWriteTimeUtc(worktrees), Directory.EnumerateFileSystemEntries(worktrees).Count(),
                await CaptureAsync(fixture, reader));
        }

        public async Task RedirectAsync()
        {
            redirected.ShouldBeFalse("each scenario injects one backlink change");
            redirected = true;
            await File.WriteAllTextAsync(backlink, Path.Combine(fixture.Observer, ".git") + "\n");
            AssertStamp();
            var listed = await RequiredAsync(reader, fixture.Repository, "worktree", "list", "--porcelain", "-z");
            var rows = LandingGit.ParseRegistrations(listed);
            rows.Any(row => LandingGit.PathsEqual(row.Path, fixture.Source)).ShouldBeFalse();
            rows.Any(row => LandingGit.PathsEqual(row.Path, fixture.Observer)).ShouldBeTrue();
        }

        public async Task AssertPreservedAsync()
        {
            Directory.Exists(fixture.Source).ShouldBeTrue();
            AssertStamp();
            (await CaptureAsync(fixture, reader)).ShouldBe(image,
                "source/observer HEAD, index, tracked bytes, .git, local and remote refs must remain unchanged");
            fixture.Git.Trace.Where(arguments => arguments[0] is "reset" or "update-ref" or "push" or "clean")
                .ShouldBeEmpty("a refusal must not mutate either checkout or refs");
        }

        private void AssertStamp()
        {
            Directory.GetLastWriteTimeUtc(worktrees).ShouldBe(stamp, "nested corruption must reproduce the unchanged cache stamp");
            Directory.EnumerateFileSystemEntries(worktrees).Count().ShouldBe(entryCount);
        }

        private static async Task<string> CaptureAsync(LandingGitFixture fixture, LandingGitFixture.FixtureGit reader)
        {
            var facts = new List<string>();
            foreach (var checkout in new[] { fixture.Source, fixture.Observer })
            {
                facts.Add(checkout);
                facts.Add(await RequiredAsync(reader, checkout, "rev-parse", "HEAD"));
                facts.Add(await RequiredAsync(reader, checkout, "symbolic-ref", "HEAD"));
                var admin = (await RequiredAsync(reader, checkout, "rev-parse", "--absolute-git-dir")).Trim();
                facts.Add(await DigestAsync(Path.Combine(admin, "index")));
                var dotGit = Path.Combine(checkout, ".git");
                if (File.Exists(dotGit)) facts.Add(await DigestAsync(dotGit));
                foreach (var path in (await RequiredAsync(reader, checkout, "ls-files", "-z"))
                             .Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
                {
                    facts.Add(path);
                    facts.Add(await DigestAsync(Path.Combine(checkout, path)));
                }
            }
            foreach (var repository in new[] { fixture.Repository, fixture.Observer, fixture.Remote })
                facts.Add(await RequiredAsync(reader, repository, "show-ref"));
            return string.Join('\0', facts);
        }

        private static async Task<string> DigestAsync(string path) =>
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));

        private static async Task<string> RequiredAsync(LandingGitFixture.FixtureGit reader, string repository, params string[] arguments)
        {
            var result = await reader.RunAsync(repository, arguments, CancellationToken.None);
            result.Succeeded.ShouldBeTrue(result.Diagnostic);
            return result.Output;
        }

        public async ValueTask DisposeAsync()
        {
            fixture.Git.AfterCommand = null;
            fixture.Git.BeforeCommand = null;
            if (redirected) await File.WriteAllBytesAsync(backlink, original);
        }
    }
}
