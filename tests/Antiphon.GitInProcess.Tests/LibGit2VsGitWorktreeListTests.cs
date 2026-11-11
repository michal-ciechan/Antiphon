using Antiphon.GitInProcess;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// <c>git worktree list --porcelain -z</c> answered in process (<see cref="InProcessGitReads"/>)
/// against the real git CLI over the registration shapes a land and its cleanup meet: the answer is
/// byte-identical to git's, or the shape falls back to git. "Answered" shapes must not fall back.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LibGit2VsGitWorktreeListTests
{
    private static readonly string[] List = ["worktree", "list", "--porcelain", "-z"];

    private enum Expect { Answered, Fallback }

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

    private static async Task CompareAsync(string cwd, Expect expect, string label)
    {
        var answer = InProcessGitReads.TryAnswer(cwd, List);
        if (expect == Expect.Fallback) { answer.ShouldBeNull("must fall back to git: " + label); return; }
        answer.ShouldNotBeNull("must be answered in process: " + label);
        var git = await GitAsync(cwd, List);
        git.ExitCode.ShouldBe(0, label);
        answer.ExitCode.ShouldBe(0, label);
        answer.Output.Replace('\0', '|').ShouldBe(git.Output.Replace('\0', '|'), label);
        // The land's parser reads both the same way.
        LandingGit.ParseRegistrations(answer.Output).ShouldBe(LandingGit.ParseRegistrations(git.Output), label);
    }

    private static async Task<string> AddAsync(LandingGitFixture fixture, string relative, params string[] extra)
    {
        var path = Path.Combine(fixture.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await fixture.RequiredAsync(fixture.Repository, ["worktree", "add", .. extra, path, fixture.SeedSha]);
        return path;
    }

    [Test]
    public async Task Main_and_linked_worktree_match_git_from_either_side()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await CompareAsync(fixture.Repository, Expect.Answered, "main");
        await CompareAsync(fixture.Source, Expect.Answered, "from linked");
        await CompareAsync(Path.Combine(fixture.Repository, ".git"), Expect.Answered, "from .git");
    }

    [Test]
    public async Task Locked_detached_prunable_and_branch_worktrees_match_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Repository, "worktree", "lock", "--reason", "antiphon-land", fixture.Source);
        var bare = await AddAsync(fixture, Path.Combine("wt", "locked-bare"), "--detach");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "lock", bare);
        var spaced = await AddAsync(fixture, Path.Combine("wt", "spaced reason"), "--detach");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "lock", "--reason", "  two words  ", spaced);
        await AddAsync(fixture, Path.Combine("wt", "detached"), "--detach");
        await AddAsync(fixture, Path.Combine("wt", "branched"), "-b", "feature/listed");
        var gone = await AddAsync(fixture, Path.Combine("wt", "gone"), "--detach");
        Directory.Delete(gone, recursive: true);

        await CompareAsync(fixture.Repository, Expect.Answered, "mixed registrations");
        await CompareAsync(fixture.Source, Expect.Answered, "mixed registrations from linked");
    }

    [Test]
    public async Task Registration_order_differs_from_path_order_and_git_sorts_by_path()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        // Ids follow the directory names (alpha < beta); paths sort the other way (a-dir < z-dir).
        await AddAsync(fixture, Path.Combine("z-dir", "alpha"), "--detach");
        await AddAsync(fixture, Path.Combine("a-dir", "beta"), "--detach");
        await CompareAsync(fixture.Repository, Expect.Answered, "path order");
    }

    [Test]
    public async Task Removal_by_another_process_is_seen()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var extra = await AddAsync(fixture, Path.Combine("wt", "short-lived"), "--detach");
        await CompareAsync(fixture.Repository, Expect.Answered, "before removal");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "remove", "--force", extra);
        await CompareAsync(fixture.Repository, Expect.Answered, "after removal");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "prune");
        await CompareAsync(fixture.Repository, Expect.Answered, "after prune");
    }

    [Test]
    public async Task Shapes_whose_listing_depends_on_rules_not_copied_fall_back()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        // A bare repository lists itself as bare.
        await CompareAsync(fixture.Remote, Expect.Fallback, "bare");

        // An order that depends on core.ignorecase: "B-dir" < "a-dir" by bytes, the other way folded.
        await AddAsync(fixture, Path.Combine("B-dir", "one"), "--detach");
        await AddAsync(fixture, Path.Combine("a-dir", "two"), "--detach");
        await CompareAsync(fixture.Repository, Expect.Fallback, "case-dependent order");
    }

    [Test]
    public async Task Unborn_heads_and_moved_or_included_config_fall_back()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Repository, "worktree", "add", "--orphan", "-b", "unborn", Path.Combine(fixture.Root, "orphan"));
        await CompareAsync(fixture.Repository, Expect.Fallback, "unborn linked HEAD");
        await fixture.RequiredAsync(fixture.Repository, "worktree", "remove", "--force", Path.Combine(fixture.Root, "orphan"));
        await CompareAsync(fixture.Repository, Expect.Answered, "orphan removed");

        await fixture.RequiredAsync(fixture.Repository, "config", "include.path", "missing.inc");
        await CompareAsync(fixture.Repository, Expect.Fallback, "include");
        await fixture.RequiredAsync(fixture.Repository, "config", "--unset", "include.path");
        await fixture.RequiredAsync(fixture.Repository, "config", "core.worktree", fixture.Repository.Replace('\\', '/'));
        await CompareAsync(fixture.Repository, Expect.Fallback, "core.worktree");
    }
}
