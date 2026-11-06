using Antiphon.GitInProcess;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// <see cref="InProcessGitReads"/> (libgit2) against the real git CLI, command by command, over the
/// repository shapes a land meets. For every case the in-process answer must be byte-identical to
/// git's (exit code and output), or the case must fall back to git. "Answered" cases must not fall
/// back, so a silent loss of coverage fails too. Real temp repositories, git on PATH.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LibGit2VsGitDifferentialTests
{
    private enum Expect { Answered, Fallback, Either }

    private static async Task<LandingGitResult> GitAsync(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new LandingGitResult(p.ExitCode, output, "");
    }

    private static async Task CompareAsync(string cwd, Expect expect, params string[] args)
    {
        var shape = string.Join(' ', args) + " @ " + cwd;
        var answer = InProcessGitReads.TryAnswer(cwd, args);
        if (expect == Expect.Fallback) { answer.ShouldBeNull("must fall back to git: " + shape); return; }
        if (answer is null) { expect.ShouldNotBe(Expect.Answered, "must be answered in process: " + shape); return; }
        var git = await GitAsync(cwd, args);
        answer.ExitCode.ShouldBe(git.ExitCode, shape);
        answer.Output.ShouldBe(git.Output, shape);
        if (answer.ExitCode != 0) answer.Diagnostic.ShouldBe($"git_exit_{answer.ExitCode}", shape);
    }

    /// <summary>Every handled shape, answered and equal to git, for a checkout root.</summary>
    private static async Task AllShapesAsync(LandingGitFixture fixture, string cwd)
    {
        var head = (await GitAsync(cwd, "rev-parse", "HEAD")).Output.Trim();
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "HEAD");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "HEAD^{commit}");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", fixture.TargetRef + "^{commit}");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", head + "^{commit}");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--absolute-git-dir");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--path-format=absolute", "--git-common-dir");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--path-format=absolute", "--git-path", "index.lock");
        await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", "--hash", fixture.SourceRef);
        await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", fixture.TargetRef, fixture.SourceRef);
        await CompareAsync(cwd, Expect.Answered, "show-ref", "--exists", fixture.SourceRef);
        await CompareAsync(cwd, Expect.Answered, "show-ref", "--exists", "refs/heads/no-such-branch");
        await CompareAsync(cwd, Expect.Answered, "symbolic-ref", "-q", "HEAD");
        await CompareAsync(cwd, Expect.Answered, "symbolic-ref", "-q", fixture.TargetRef);
        await CompareAsync(cwd, Expect.Answered, "merge-base", "--is-ancestor", fixture.SeedSha, head);
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "--quiet", "HEAD^{commit}");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "--quiet", fixture.TargetRef + "^{commit}");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "master^{commit}");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "--quiet", "master^{commit}");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--is-inside-work-tree");
        await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", "--quiet", fixture.SourceRef);
        await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", "--quiet", "refs/heads/no-such-branch");
        await CompareAsync(cwd, Expect.Answered, "worktree", "list", "--porcelain");
        // GitWorkspaceService.RunReadOnlyAsync's prefix: the read is the same without index refresh.
        await CompareAsync(cwd, Expect.Answered, "--no-optional-locks", "rev-parse", "--path-format=absolute", "--git-common-dir");
        await CompareAsync(cwd, Expect.Answered, "--no-optional-locks", "rev-parse", "HEAD");
    }

    [Test]
    public async Task Main_repository_and_linked_worktree_match_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await AllShapesAsync(fixture, fixture.Repository);
        await AllShapesAsync(fixture, fixture.Source);
    }

    [Test]
    public async Task Paths_reached_by_a_differently_cased_spelling_print_the_on_disk_spelling()
    {
        if (!OperatingSystem.IsWindows()) return; // case-insensitive paths are a Windows shape
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        foreach (var path in new[] { fixture.Repository, fixture.Source })
        {
            var shouted = path.ToUpperInvariant();
            Directory.Exists(shouted).ShouldBeTrue();
            await CompareAsync(shouted, Expect.Answered, "rev-parse", "--absolute-git-dir");
            await CompareAsync(shouted, Expect.Answered, "rev-parse", "--path-format=absolute", "--git-common-dir");
            await CompareAsync(shouted, Expect.Answered, "rev-parse", "--path-format=absolute", "--git-path", "index.lock");
            var lower = char.ToLowerInvariant(path[0]) + path[1..];
            await CompareAsync(lower, Expect.Answered, "rev-parse", "--absolute-git-dir");
        }
    }

    [Test]
    public async Task Detached_head_packed_refs_and_tags_match_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Repository, "tag", "light", fixture.SeedSha);
        await fixture.RequiredAsync(fixture.Repository, "tag", "-a", "annotated", "-m", "annotated", fixture.SeedSha);
        await fixture.RequiredAsync(fixture.Repository, "pack-refs", "--all");
        await fixture.RequiredAsync(fixture.Source, "checkout", "--detach");

        foreach (var cwd in new[] { fixture.Repository, fixture.Source })
        {
            await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", "refs/tags/light", "refs/tags/annotated");
            await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", "--hash", "refs/tags/annotated");
            await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "refs/tags/annotated^{commit}");
            await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", "--hash", fixture.TargetRef);
        }
        await CompareAsync(fixture.Source, Expect.Answered, "symbolic-ref", "-q", "HEAD");
        await CompareAsync(fixture.Source, Expect.Answered, "rev-parse", "HEAD");
        await CompareAsync(fixture.Source, Expect.Answered, "rev-parse", "--absolute-git-dir");
    }

    [Test]
    public async Task Ancestry_matches_git_both_ways()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "ahead");
        var ahead = (await GitAsync(fixture.Source, "rev-parse", "HEAD")).Output.Trim();

        await CompareAsync(fixture.Repository, Expect.Answered, "merge-base", "--is-ancestor", fixture.SeedSha, ahead);
        await CompareAsync(fixture.Repository, Expect.Answered, "merge-base", "--is-ancestor", ahead, fixture.SeedSha);
        await CompareAsync(fixture.Repository, Expect.Answered, "merge-base", "--is-ancestor", ahead, ahead);
    }

    [Test]
    public async Task Changes_by_another_git_process_are_seen()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await CompareAsync(fixture.Repository, Expect.Answered, "show-ref", "--verify", "--hash", fixture.SourceRef);
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "moved");
        await CompareAsync(fixture.Repository, Expect.Answered, "show-ref", "--verify", "--hash", fixture.SourceRef);
        await fixture.RequiredAsync(fixture.Repository, "pack-refs", "--all");
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "moved again");
        await CompareAsync(fixture.Repository, Expect.Answered, "show-ref", "--verify", "--hash", fixture.SourceRef);
        await CompareAsync(fixture.Source, Expect.Answered, "rev-parse", "--verify", "HEAD^{commit}");
    }

    [Test]
    public async Task Symbolic_chains_are_left_to_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Repository, "symbolic-ref", "refs/heads/alias", fixture.TargetRef);
        await fixture.RequiredAsync(fixture.Repository, "symbolic-ref", "refs/heads/chain", "refs/heads/alias");
        await CompareAsync(fixture.Repository, Expect.Answered, "symbolic-ref", "-q", "refs/heads/alias");
        await CompareAsync(fixture.Repository, Expect.Fallback, "symbolic-ref", "-q", "refs/heads/chain");
        await CompareAsync(fixture.Repository, Expect.Fallback, "show-ref", "--verify", "--hash", "refs/heads/alias");
    }

    [Test]
    public async Task Repositories_libgit2_reads_differently_fall_back_for_every_command()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var shallow = Path.Combine(fixture.Root, "shallow");
        await fixture.RequiredAsync(fixture.Root, "clone", "--depth", "1", "--no-local", "--", fixture.Remote, shallow);
        await CompareAsync(shallow, Expect.Fallback, "rev-parse", "HEAD");
        await CompareAsync(shallow, Expect.Fallback, "rev-parse", "--absolute-git-dir");

        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "graft me");
        var tip = (await GitAsync(fixture.Source, "rev-parse", "HEAD")).Output.Trim();
        await fixture.RequiredAsync(fixture.Repository, "replace", "--graft", tip);
        await CompareAsync(fixture.Repository, Expect.Fallback, "merge-base", "--is-ancestor", fixture.SeedSha, tip);
        await CompareAsync(fixture.Source, Expect.Fallback, "rev-parse", "HEAD");
        await fixture.RequiredAsync(fixture.Repository, "pack-refs", "--all");
        await CompareAsync(fixture.Repository, Expect.Fallback, "show-ref", "--verify", "--hash", fixture.SourceRef);
    }

    [Test]
    public async Task Failures_names_git_would_read_differently_and_unhandled_commands_fall_back()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        string[][] fallbacks =
        [
            ["rev-parse", "--verify", "refs/heads/no-such^{commit}"],
            ["rev-parse", "--verify", "no-such^{commit}"],                // a short name nothing resolves: git's failure
            ["rev-parse", "--verify", "--quiet", "no-such^{commit}"],
            ["show-ref", "--verify", "--hash", "refs/heads/no-such"],
            ["show-ref", "--verify", "--hash", "refs/heads//master"],    // git refuses, libgit2 would normalise
            ["symbolic-ref", "-q", "refs/heads/no-such"],
            ["remote", "get-url", "--push", "--all", "origin"],          // config is never read in process
            ["status", "--porcelain=v1", "-z"],
            ["worktree", "list"],                                         // only the porcelain listings are answered
        ];
        foreach (var args in fallbacks)
            await CompareAsync(fixture.Repository, Expect.Fallback, args);
        await CompareAsync(Path.Combine(fixture.Repository, "sub-does-not-exist"), Expect.Fallback, "rev-parse", "HEAD");
        Directory.CreateDirectory(Path.Combine(fixture.Repository, "sub"));
        await CompareAsync(Path.Combine(fixture.Repository, "sub"), Expect.Fallback, "rev-parse", "HEAD");
    }

    [Test]
    public async Task Short_names_resolve_as_git_resolves_them_or_fall_back()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var repo = fixture.Repository;
        var source = fixture.SourceRef["refs/heads/".Length..];
        foreach (var cwd in new[] { repo, fixture.Source })
        {
            await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", source + "^{commit}");
            await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "--quiet", source + "^{commit}");
        }
        await fixture.RequiredAsync(repo, "tag", "light", fixture.SeedSha);
        await fixture.RequiredAsync(repo, "tag", "-a", "annotated", "-m", "annotated", fixture.SeedSha);
        await CompareAsync(repo, Expect.Answered, "rev-parse", "--verify", "light^{commit}");
        await CompareAsync(repo, Expect.Answered, "rev-parse", "--verify", "--quiet", "annotated^{commit}");
        await fixture.RequiredAsync(repo, "pack-refs", "--all");
        await CompareAsync(repo, Expect.Answered, "rev-parse", "--verify", "light^{commit}");
        await CompareAsync(repo, Expect.Answered, "rev-parse", "--verify", "master^{commit}");
        // A remote-tracking branch, by its short name, in the observer clone.
        await CompareAsync(fixture.Observer, Expect.Answered, "rev-parse", "--verify", "origin/master^{commit}");

        // Names git would read another way, or more than one way, are git's.
        await fixture.RequiredAsync(repo, "tag", "dup", fixture.SeedSha);
        await fixture.RequiredAsync(repo, "branch", "dup", fixture.SeedSha);             // ambiguous: git warns
        await fixture.RequiredAsync(repo, "branch", "cafe", fixture.SeedSha);            // hex: an abbreviated id?
        await fixture.RequiredAsync(repo, "branch", "SHOUT", fixture.SeedSha);           // capitals: a pseudo-ref name
        await fixture.RequiredAsync(repo, "branch", "beside", fixture.SeedSha);
        await File.WriteAllTextAsync(Path.Combine(repo, ".git", "beside"), fixture.SeedSha + "\n"); // $GIT_DIR/<name>
        await fixture.RequiredAsync(repo, "branch", "broken", fixture.SeedSha);
        await File.WriteAllTextAsync(Path.Combine(repo, ".git", "refs", "tags", "broken"), "not a ref\n"); // git warns
        await fixture.RequiredAsync(repo, "symbolic-ref", "refs/heads/alias", fixture.TargetRef);    // a symref to follow
        foreach (var name in new[] { "dup", "cafe", "SHOUT", "beside", "broken", "alias", "no-such", "origin", "master@{0}", "bisect/x" })
        {
            await CompareAsync(repo, Expect.Fallback, "rev-parse", "--verify", name + "^{commit}");
            await CompareAsync(repo, Expect.Fallback, "rev-parse", "--verify", "--quiet", name + "^{commit}");
        }
        await CompareAsync(fixture.Observer, Expect.Fallback, "rev-parse", "--verify", "origin^{commit}"); // origin/HEAD is a symref
    }

    [Test]
    public async Task Work_tree_checks_quiet_ref_checks_and_the_plain_worktree_listing_match_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var repo = fixture.Repository;
        // Inside the git directory or in a bare repository, git's.
        await CompareAsync(Path.Combine(repo, ".git"), Expect.Fallback, "rev-parse", "--is-inside-work-tree");
        await CompareAsync(fixture.Remote, Expect.Fallback, "rev-parse", "--is-inside-work-tree");

        // A symref or a broken loose ref is git's to judge; a missing one is a silent 1.
        await fixture.RequiredAsync(repo, "symbolic-ref", "refs/heads/alias", fixture.TargetRef);
        await CompareAsync(repo, Expect.Fallback, "show-ref", "--verify", "--quiet", "refs/heads/alias");
        await File.WriteAllTextAsync(Path.Combine(repo, ".git", "refs", "heads", "broken"), "not a ref\n");
        await CompareAsync(repo, Expect.Fallback, "show-ref", "--verify", "--quiet", "refs/heads/broken");
        File.Delete(Path.Combine(repo, ".git", "refs", "heads", "broken"));
        await fixture.RequiredAsync(repo, "pack-refs", "--all");
        await CompareAsync(repo, Expect.Answered, "show-ref", "--verify", "--quiet", fixture.SourceRef);
        await CompareAsync(repo, Expect.Answered, "show-ref", "--verify", "--quiet", "refs/heads/gone");

        // Lock reasons: printed as written without -z unless git would quote them.
        await fixture.RequiredAsync(repo, "worktree", "lock", "--reason", "held by a land", fixture.Source);
        await CompareAsync(repo, Expect.Answered, "worktree", "list", "--porcelain");
        await CompareAsync(repo, Expect.Answered, "worktree", "list", "--porcelain", "-z");
        await fixture.RequiredAsync(repo, "worktree", "unlock", fixture.Source);
        await fixture.RequiredAsync(repo, "worktree", "lock", "--reason", "say \"hi\"", fixture.Source);
        await CompareAsync(repo, Expect.Fallback, "worktree", "list", "--porcelain");
        await CompareAsync(repo, Expect.Answered, "worktree", "list", "--porcelain", "-z");
        await fixture.RequiredAsync(repo, "worktree", "unlock", fixture.Source);
        await fixture.RequiredAsync(repo, "worktree", "lock", fixture.Source);
        await CompareAsync(repo, Expect.Answered, "worktree", "list", "--porcelain");

        // A work tree moved by config is git's.
        await fixture.RequiredAsync(repo, "config", "core.worktree", repo);
        await CompareAsync(repo, Expect.Fallback, "rev-parse", "--is-inside-work-tree");
        await CompareAsync(repo, Expect.Fallback, "worktree", "list", "--porcelain");
    }

    [Test]
    public async Task A_land_scope_answers_reads_in_process_and_counts_them()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        using var scope = fixture.Git.BeginOperationScope();
        fixture.Git.Trace.Clear();

        var inspection = await fixture.Git.InspectAsync(fixture.Coordinates, LandInspectionScope.Full, CancellationToken.None);

        inspection.Accepted.ShouldBeTrue(inspection.Reason);
        scope.Profile.InProcessHits.ShouldBeGreaterThan(0, "the fixture's isolated config still allows reads in process");
        scope.Profile.Processes.ShouldBe(fixture.Git.Trace.Count - scope.Profile.RefFormatHits - scope.Profile.InProcessHits);
    }
}
