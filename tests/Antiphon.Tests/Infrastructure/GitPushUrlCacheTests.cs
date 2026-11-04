using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// <see cref="GitPushUrlCache"/> through <see cref="LandingGit"/>: the push URL is git's own answer,
/// replayed only while the config files that decide it are unchanged, and asked again after any
/// change, including one that only a rewrite rule in the (isolated) global config makes.
/// Also covers the in-process <c>check-ref-format</c> answer for the land's own ref names.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class GitPushUrlCacheTests
{
    private static readonly string[] GetUrl = ["remote", "get-url", "--push", "--all", "origin"];

    /// <summary>The fixture's git, counting the git children it starts per subcommand.</summary>
    private sealed class CountingFixtureGit(string home, Guid taskId) : LandingGitFixture.FixtureGit(home, taskId)
    {
        private readonly List<string> _started = [];
        public int Started(params string[] prefix)
        {
            lock (_started) return _started.Count(s => s.StartsWith(string.Join(' ', prefix), StringComparison.Ordinal));
        }
        protected override Process? StartProcess(ProcessStartInfo start)
        {
            lock (_started) _started.Add(string.Join(' ', start.ArgumentList.SkipWhile(a => a == "-c" || a.Contains('='))));
            return base.StartProcess(start);
        }
    }

    private static async Task<(LandingGitFixture Fixture, CountingFixtureGit Git)> CreateAsync()
    {
        var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        return (fixture, new CountingFixtureGit(Path.Combine(fixture.Root, "home"), fixture.TaskId));
    }

    private static async Task<string> UrlAsync(LandingGit git, string repo) =>
        (await git.RunAsync(repo, GetUrl, CancellationToken.None)).Output;

    [Test]
    public async Task Repeated_reads_replay_gits_answer_without_starting_git()
    {
        var (fixture, git) = await CreateAsync();
        await using var _ = fixture;
        var first = await UrlAsync(git, fixture.Repository);
        for (var i = 0; i < 5; i++)
            (await UrlAsync(git, fixture.Repository)).ShouldBe(first);

        first.Trim().ShouldBe(fixture.Remote);
        git.Started("remote get-url").ShouldBe(1, "git answers once; the rest are replays");
    }

    [Test]
    public async Task A_changed_push_url_is_asked_again()
    {
        var (fixture, git) = await CreateAsync();
        await using var _ = fixture;
        await UrlAsync(git, fixture.Repository);
        var elsewhere = Path.Combine(fixture.Root, "elsewhere.git");
        await fixture.RequiredAsync(fixture.Repository, "remote", "set-url", "--push", "origin", elsewhere);

        (await UrlAsync(git, fixture.Repository)).Trim().ShouldBe(elsewhere);
        git.Started("remote get-url").ShouldBe(2);
    }

    [Test]
    public async Task A_same_size_rewrite_is_still_a_change()
    {
        var (fixture, git) = await CreateAsync();
        await using var _ = fixture;
        await fixture.RequiredAsync(fixture.Repository, "remote", "set-url", "--push", "origin", "C:/aaaa/remote.git");
        (await UrlAsync(git, fixture.Repository)).Trim().ShouldBe("C:/aaaa/remote.git");
        var config = Path.Combine(fixture.Repository, ".git", "config");
        var stamp = File.GetLastWriteTimeUtc(config);
        await File.WriteAllTextAsync(config, (await File.ReadAllTextAsync(config)).Replace("C:/aaaa/", "C:/bbbb/"));
        File.SetLastWriteTimeUtc(config, stamp);

        (await UrlAsync(git, fixture.Repository)).Trim().ShouldBe("C:/bbbb/remote.git");
    }

    [Test]
    public async Task A_rewrite_rule_added_to_the_childs_global_config_is_asked_again()
    {
        var (fixture, git) = await CreateAsync();
        await using var _ = fixture;
        var before = (await UrlAsync(git, fixture.Repository)).Trim();
        // FixtureGit's children read GIT_CONFIG_GLOBAL=<home>/empty-config, not the developer's ~/.gitconfig.
        var global = Path.Combine(fixture.Root, "home", "empty-config");
        await File.AppendAllTextAsync(global, $"[url \"C:/rewritten/\"]\n\tpushInsteadOf = {before[..3]}\n");

        var after = (await UrlAsync(git, fixture.Repository)).Trim();
        after.ShouldStartWith("C:/rewritten/");
        git.Started("remote get-url").ShouldBe(2);
    }

    [Test]
    public async Task An_include_is_never_cached()
    {
        var (fixture, git) = await CreateAsync();
        await using var _ = fixture;
        var included = Path.Combine(fixture.Root, "included.config");
        await File.WriteAllTextAsync(included, "[core]\n\tquotepath = false\n");
        await fixture.RequiredAsync(fixture.Repository, "config", "include.path", included);

        await UrlAsync(git, fixture.Repository);
        await UrlAsync(git, fixture.Repository);
        git.Started("remote get-url").ShouldBe(2, "an include's target and conditions are git's to evaluate");
    }

    [Test]
    public async Task Land_ref_names_are_valid_without_git_and_others_still_ask_it()
    {
        var (fixture, git) = await CreateAsync();
        await using var _ = fixture;
        var task = Guid.NewGuid().ToString("N");
        var op = Guid.NewGuid().ToString("N");
        string[] ours =
        [
            $"refs/antiphon/land/{task}/{op}",
            $"refs/antiphon/land/{task}/{op}/target-before",
            $"refs/antiphon/land/{task}/{op}/source-observed/probe",
            $"refs/antiphon/land/{task}/{op}/source-observed/{Guid.NewGuid():N}",
        ];
        foreach (var name in ours)
        {
            var answer = await git.RunAsync(fixture.Repository, ["check-ref-format", name], CancellationToken.None);
            answer.ExitCode.ShouldBe(0, name);
            (await fixture.RequiredAsync(fixture.Repository, "check-ref-format", name)).ShouldBe("", name);
        }
        git.Started("check-ref-format").ShouldBe(0, "the land's own names are answered in process");

        string[] others = [$"refs/antiphon/land/{task}/{op}/x..y", $"refs/antiphon/land/{task}/{op}/a.lock", "refs/antiphon/land/short/id"];
        foreach (var name in others)
            await git.RunAsync(fixture.Repository, ["check-ref-format", name], CancellationToken.None);
        git.Started("check-ref-format").ShouldBe(others.Length, "any other shape is git's to judge");
    }
}
