using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// <see cref="InProcessGitReads"/> answers exactly what git answers for the shapes it handles, sees
/// changes another process makes while a handle stays open, and leaves everything else to git.
/// Real temp repositories (a canonical repo, a linked worktree, a bare remote).
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class InProcessGitReadsTests
{
    private static async Task<LandingGitResult> CliAsync(string repo, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repo, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new LandingGitResult(p.ExitCode, output, "");
    }

    private static async Task ShouldMatchGitAsync(InProcessGitReads.Repositories handles, string repo, params string[] args)
    {
        var git = await CliAsync(repo, args);
        var answer = InProcessGitReads.TryAnswer(repo, args, handles, allowRemoteConfig: true);
        var shape = string.Join(' ', args);
        answer.ShouldNotBeNull("answered in process: " + shape);
        answer.ExitCode.ShouldBe(git.ExitCode, shape);
        answer.Output.ShouldBe(git.Output, shape);
    }

    [Test]
    public async Task Answers_match_git_for_every_handled_shape()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        using var handles = new InProcessGitReads.Repositories();
        var head = (await CliAsync(fixture.Source, "rev-parse", "HEAD")).Output.Trim();

        foreach (var repo in new[] { fixture.Repository, fixture.Source })
        {
            await ShouldMatchGitAsync(handles, repo, "rev-parse", "HEAD");
            await ShouldMatchGitAsync(handles, repo, "rev-parse", "--verify", "HEAD^{commit}");
            await ShouldMatchGitAsync(handles, repo, "rev-parse", "--verify", fixture.TargetRef + "^{commit}");
            await ShouldMatchGitAsync(handles, repo, "rev-parse", "--verify", head + "^{commit}");
            await ShouldMatchGitAsync(handles, repo, "show-ref", "--verify", "--hash", fixture.SourceRef);
            await ShouldMatchGitAsync(handles, repo, "show-ref", "--verify", fixture.TargetRef, fixture.SourceRef);
            await ShouldMatchGitAsync(handles, repo, "show-ref", "--exists", fixture.SourceRef);
            await ShouldMatchGitAsync(handles, repo, "show-ref", "--exists", "refs/heads/no-such-branch");
            await ShouldMatchGitAsync(handles, repo, "symbolic-ref", "-q", "HEAD");
            await ShouldMatchGitAsync(handles, repo, "symbolic-ref", "-q", fixture.TargetRef);
            await ShouldMatchGitAsync(handles, repo, "merge-base", "--is-ancestor", fixture.SeedSha, head);
        }
        await ShouldMatchGitAsync(handles, fixture.Repository, "remote", "get-url", "--push", "--all", "origin");
        InProcessGitReads.TryAnswer(fixture.Source, ["remote", "get-url", "--push", "--all", "origin"], handles, true)
            .ShouldBeNull("a linked worktree's handle lacks the common config, so git answers");
    }

    [Test]
    public async Task Not_an_ancestor_matches_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "ahead");
        var ahead = (await CliAsync(fixture.Source, "rev-parse", "HEAD")).Output.Trim();
        using var handles = new InProcessGitReads.Repositories();

        await ShouldMatchGitAsync(handles, fixture.Repository, "merge-base", "--is-ancestor", ahead, fixture.SeedSha);
        (await CliAsync(fixture.Repository, "merge-base", "--is-ancestor", ahead, fixture.SeedSha)).ExitCode.ShouldBe(1);
    }

    [Test]
    public async Task Changes_made_by_another_process_are_seen_through_an_open_handle()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        using var handles = new InProcessGitReads.Repositories();
        await ShouldMatchGitAsync(handles, fixture.Repository, "remote", "get-url", "--push", "--all", "origin");
        await ShouldMatchGitAsync(handles, fixture.Repository, "show-ref", "--verify", "--hash", fixture.SourceRef);

        await fixture.RequiredAsync(fixture.Repository, "remote", "set-url", "--push", "origin", Path.Combine(fixture.Root, "elsewhere.git"));
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "moved");
        await fixture.RequiredAsync(fixture.Repository, "pack-refs", "--all");

        await ShouldMatchGitAsync(handles, fixture.Repository, "remote", "get-url", "--push", "--all", "origin");
        await ShouldMatchGitAsync(handles, fixture.Repository, "show-ref", "--verify", "--hash", fixture.SourceRef);
        await ShouldMatchGitAsync(handles, fixture.Source, "rev-parse", "--verify", "HEAD^{commit}");
    }

    [Test]
    public async Task Failures_and_unhandled_shapes_are_left_to_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        using var handles = new InProcessGitReads.Repositories();
        string[][] gits =
        [
            ["rev-parse", "--verify", "refs/heads/no-such^{commit}"],
            ["show-ref", "--verify", "--hash", "refs/heads/no-such"],
            ["symbolic-ref", "-q", "refs/heads/no-such"],
            ["rev-parse", "--absolute-git-dir"],
            ["rev-parse", "--path-format=absolute", "--git-common-dir"],
            ["status", "--porcelain=v1", "-z"],
            ["worktree", "list", "--porcelain", "-z"],
        ];
        foreach (var args in gits)
            InProcessGitReads.TryAnswer(fixture.Repository, args, handles, allowRemoteConfig: true).ShouldBeNull(string.Join(' ', args));
        InProcessGitReads.TryAnswer(Path.Combine(fixture.Root, "not-a-repo"), ["rev-parse", "HEAD"], handles, true).ShouldBeNull();
    }

    [Test]
    public async Task Url_rewrites_and_other_config_levels_leave_get_url_to_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        string[] getUrl = ["remote", "get-url", "--push", "--all", "origin"];
        using (var handles = new InProcessGitReads.Repositories())
            InProcessGitReads.TryAnswer(fixture.Repository, getUrl, handles, allowRemoteConfig: false).ShouldBeNull("not allowed for this instance");

        await fixture.RequiredAsync(fixture.Repository, "config", "url.https://example.invalid/.insteadOf", "x:");
        using (var handles = new InProcessGitReads.Repositories())
            InProcessGitReads.TryAnswer(fixture.Repository, getUrl, handles, allowRemoteConfig: true).ShouldBeNull("an insteadOf rewrite exists");
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
