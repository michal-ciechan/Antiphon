using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// Two git process savings: the child journal reuses git's common-dir answer while a checkout's
/// .git layout holds (<see cref="GitCommonDirectory"/>), and <see cref="LandingGit"/> memoises
/// branch <c>check-ref-format</c> verdicts per instance. Real temp repositories.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandingGitProcessSavingTests
{
    [Test]
    public async Task Common_dir_is_gits_answer_and_is_reused_while_the_layout_holds()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();

        foreach (var path in new[] { fixture.Repository, fixture.Source })
        {
            var expected = await new LandingGit().CommonDirectoryAsync(path, CancellationToken.None);
            var asked = 0;
            Task<string> Ask() { asked++; return new LandingGit().CommonDirectoryAsync(path, CancellationToken.None); }

            // The fixture's own mutating commands may already have cached it (journal lookups).
            (await GitCommonDirectory.ResolveAsync(path, Ask)).ShouldBe(expected, path);
            asked.ShouldBeLessThanOrEqualTo(1);
            var afterFirst = asked;
            (await GitCommonDirectory.ResolveAsync(path, Ask)).ShouldBe(expected, "exactly git's spelling, " + path);
            (await GitCommonDirectory.ResolveAsync(path + Path.DirectorySeparatorChar, Ask)).ShouldBe(expected);
            asked.ShouldBe(afterFirst, "git is not asked again while the .git layout is unchanged: " + path);
        }
        File.Exists(Path.Combine(fixture.Source, ".git")).ShouldBeTrue("the source is a linked worktree");
    }

    [Test]
    public async Task A_repointed_worktree_asks_git_again()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var asked = 0;
        Task<string> Ask() { asked++; return new LandingGit().CommonDirectoryAsync(fixture.Source, CancellationToken.None); }
        await GitCommonDirectory.ResolveAsync(fixture.Source, Ask);
        var before = asked;

        var gitDir = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "--path-format=absolute", "--git-dir")).Trim();
        var pointer = Path.Combine(fixture.Source, ".git");
        var attributes = File.GetAttributes(pointer);
        File.SetAttributes(pointer, FileAttributes.Normal);
        await File.WriteAllTextAsync(pointer, "gitdir: " + Path.GetRelativePath(fixture.Source, gitDir) + "\n");
        File.SetAttributes(pointer, attributes);

        (await GitCommonDirectory.ResolveAsync(fixture.Source, Ask))
            .ShouldBe(await new LandingGit().CommonDirectoryAsync(fixture.Source, CancellationToken.None));
        asked.ShouldBe(before + 1, "a changed pointer is a new layout");
    }

    [Test]
    public async Task Paths_without_a_dot_git_entry_are_never_cached()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var sub = Path.Combine(fixture.Repository, "sub");
        Directory.CreateDirectory(sub);

        GitCommonDirectory.Stamp(fixture.Remote).ShouldBeNull("a bare repository is git's discovery");
        GitCommonDirectory.Stamp(sub).ShouldBeNull("so is a subdirectory");
        GitCommonDirectory.Stamp(Path.Combine(fixture.Root, "missing")).ShouldBeNull();
        var asked = 0;
        Task<string> Ask() { asked++; return Task.FromResult("x"); }
        await GitCommonDirectory.ResolveAsync(sub, Ask);
        await GitCommonDirectory.ResolveAsync(sub, Ask);
        asked.ShouldBe(2);
    }

    [Test]
    public async Task Ref_format_verdicts_are_memoised_per_instance_and_failures_are_not()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var git = new CountingGit();

        (await git.RunAsync(fixture.Repository, ["check-ref-format", "refs/heads/master"], CancellationToken.None)).ExitCode.ShouldBe(0);
        (await git.RunAsync(fixture.Repository, ["check-ref-format", "refs/heads/master"], CancellationToken.None)).ExitCode.ShouldBe(0);
        (await git.RunAsync(fixture.Repository, ["check-ref-format", "refs/heads/bad..name"], CancellationToken.None)).ExitCode.ShouldBe(1);
        (await git.RunAsync(fixture.Repository, ["check-ref-format", "refs/heads/bad..name"], CancellationToken.None)).ExitCode.ShouldBe(1);
        git.Started("check-ref-format").ShouldBe(2, "one process per distinct branch ref");

        var observation = $"refs/antiphon/land/{Guid.NewGuid():N}/remote-observed";
        await git.RunAsync(fixture.Repository, ["check-ref-format", observation], CancellationToken.None);
        await git.RunAsync(fixture.Repository, ["check-ref-format", observation], CancellationToken.None);
        git.Started("check-ref-format").ShouldBe(4, "per-land refs are not memoised");

        new CountingGit().RunAsync(fixture.Repository, ["check-ref-format", "refs/heads/master"], CancellationToken.None)
            .GetAwaiter().GetResult().ExitCode.ShouldBe(0);

        var missing = Path.Combine(fixture.Root, "no-such-dir");
        // git cannot start in a missing directory; that is never a verdict.
        await Should.ThrowAsync<Exception>(() => git.RunAsync(missing, ["check-ref-format", "refs/heads/elsewhere"], CancellationToken.None));
        await Should.ThrowAsync<Exception>(() => git.RunAsync(missing, ["check-ref-format", "refs/heads/elsewhere"], CancellationToken.None));
        git.Started("check-ref-format").ShouldBe(6, "a git that could not run is asked again, never memoised");
    }

    private sealed class CountingGit : LandingGit
    {
        private readonly List<string> _started = [];

        public int Started(string command)
        {
            lock (_started) return _started.Count(c => c == command);
        }

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            lock (_started) _started.Add(start.ArgumentList.FirstOrDefault() ?? "");
            return base.StartProcess(start);
        }
    }
}
