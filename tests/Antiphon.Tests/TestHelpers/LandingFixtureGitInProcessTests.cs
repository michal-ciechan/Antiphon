using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// LandingFixtureGitInProcess does the land fixtures' `rev-parse HEAD`, `add .` and `commit [--allow-empty] -m` in
// process. These pin that it leaves what git leaves (read back by git), records the commands as git's would be, and
// hands git every case git refuses or a test watches.
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandingFixtureGitInProcessTests
{
    [Test]
    public async Task In_process_setup_leaves_what_git_leaves()
    {
        await using var inProcess = new LandingGitFixture();
        await using var git = new LandingGitFixture();
        await inProcess.InitializeAsync();
        await git.InitializeAsync();
        string[][] commands =
        [
            ["add", "."], ["commit", "-m", "feature"], ["rev-parse", "HEAD"],
            ["commit", "--allow-empty", "-m", "  empty tip  "], ["rev-parse", "HEAD"],
        ];
        foreach (var fixture in new[] { inProcess, git })
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "feature.txt"), "valuable feature\r\nsecond line\n");
            Directory.CreateDirectory(Path.Combine(fixture.Source, "sub"));
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "sub", "nested.txt"), "nested\n");
            Directory.CreateDirectory(Path.Combine(fixture.Source, "obj"));
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "obj", "ignored.txt"), "ignored\n");
            File.Delete(Path.Combine(fixture.Source, "keep.txt"));
            fixture.Git.Trace.Clear();
            fixture.Git.Commands.Clear();
        }
        var heads = new List<string>[] { [], [] };
        foreach (var command in commands)
        {
            var handled = LandingFixtureGitInProcess.TryRun(inProcess.Git, inProcess.Source, command);
            handled.ShouldNotBeNull(string.Join(' ', command));
            var ran = await git.Git.RunAsync(git.Source, command, CancellationToken.None);
            ran.Succeeded.ShouldBeTrue(ran.Diagnostic);
            if (command[0] == "rev-parse")
            {
                handled.ShouldMatch("^[0-9a-f]{40}\n$");
                ran.Output.ShouldMatch("^[0-9a-f]{40}\n$");
                heads[0].Add(handled.Trim());
                heads[1].Add(ran.Output.Trim());
            }
        }

        // Recorded as git's own run records them.
        inProcess.Git.Trace.ShouldBe(git.Git.Trace);
        inProcess.Git.Commands.Select(c => Path.GetRelativePath(inProcess.Root, c.Directory) + "|" + string.Join(' ', c.Arguments))
            .ShouldBe(git.Git.Commands.Select(c => Path.GetRelativePath(git.Root, c.Directory) + "|" + string.Join(' ', c.Arguments)));

        // What git reads back is the same: tree (but the per-task owner file), messages, identity, a clean status
        // with nothing staged, and an empty commit whose tree is its parent's.
        string[] views = [];
        foreach (var fixture in new[] { inProcess, git })
        {
            var tree = (await GitAsync(fixture, "ls-tree", "-r", "HEAD"))
                .Split('\n').Where(l => !l.EndsWith("\tfixture-owner.txt", StringComparison.Ordinal));
            var log = await GitAsync(fixture, "log", "-3", "--format=%an <%ae>|%cn <%ce>|%P|%B");
            var parents = (await GitAsync(fixture, "rev-list", "--parents", "-n", "1", "HEAD")).Trim().Split(' ');
            var status = await GitAsync(fixture, "status", "--porcelain=v1", "--untracked-files=all");
            var staged = await fixture.Git.RunAsync(fixture.Source, ["diff", "--cached", "--quiet"], CancellationToken.None);
            var sameTree = (await GitAsync(fixture, "rev-parse", "HEAD^{tree}")).Trim()
                == (await GitAsync(fixture, "rev-parse", "HEAD~1^{tree}")).Trim();
            views = [.. views, string.Join('\n', tree), StripShas(log), parents.Length.ToString(), status,
                staged.ExitCode.ToString(), sameTree.ToString()];
        }
        views[..6].ShouldBe(views[6..]);
        views[3].ShouldBeEmpty();
        views[5].ShouldBe("True");
        heads[0].Distinct().Count().ShouldBe(2);
        heads[1].Distinct().Count().ShouldBe(2);
    }

    // The template every default fixture clones is built by BuildAsync, whose root commit (on an unborn HEAD, staging
    // the dotfile .gitignore) now runs in process: the same seed as one built wholly by git, reflogs and state included.
    [Test]
    public async Task A_seed_built_in_process_matches_one_built_by_git()
    {
        var task = Guid.NewGuid();
        await using var inProcess = new LandingGitFixture(taskId: task);
        await using var git = new LandingGitFixture(taskId: task);
        await inProcess.BuildAsync();
        git.Git.AfterCommand = (_, _, _) => Task.CompletedTask; // a watching test: every command starts git
        await git.BuildAsync();
        git.Git.AfterCommand = null;
        var views = new List<string>[] { [], [] };
        foreach (var (fixture, view) in new[] { (inProcess, views[0]), (git, views[1]) })
        {
            async Task<string> Read(string directory, params string[] arguments)
            {
                var result = await fixture.Git.RunAsync(directory, arguments, CancellationToken.None);
                result.Succeeded.ShouldBeTrue(string.Join(' ', arguments) + ": " + result.Diagnostic);
                return result.Output;
            }
            view.Add(await Read(fixture.Repository, "rev-parse", "HEAD^{tree}"));
            view.Add(await Read(fixture.Repository, "ls-tree", "-r", "HEAD"));
            view.Add(await Read(fixture.Repository, "log", "--format=%an <%ae>|%cn <%ce>|%P|%B"));
            view.Add(await Read(fixture.Repository, "reflog", "show", "--format=%gn <%ge> %gs", "HEAD"));
            view.Add(await Read(fixture.Repository, "reflog", "show", "--format=%gn <%ge> %gs", "refs/heads/master"));
            view.Add(await Read(fixture.Repository, "status", "--porcelain=v1", "--untracked-files=all"));
            view.Add(await Read(fixture.Source, "status", "--porcelain=v1", "--untracked-files=all"));
            // git alone writes COMMIT_EDITMSG (nothing reads it back); every other administrative entry is the same.
            view.Add(string.Join('\n', Directory.EnumerateFileSystemEntries(Path.Combine(fixture.Repository, ".git"))
                .Select(Path.GetFileName).Where(n => n != "COMMIT_EDITMSG").Order(StringComparer.Ordinal)));
        }
        views[0].ShouldBe(views[1]);
    }

    [Test]
    public async Task What_git_refuses_or_a_test_watches_goes_to_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        string[] head = ["rev-parse", "HEAD"];

        // Nothing to commit: git's refusal, not an in-process one.
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, ["commit", "-m", "nothing"]).ShouldBeNull();
        (await Should.ThrowAsync<InvalidOperationException>(() => fixture.RequiredAsync(fixture.Source, "commit", "-m", "nothing")))
            .Message.ShouldBe("fixture_git_failed:commit:1");
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, ["commit", "--allow-empty", "-m", "   "]).ShouldBeNull();
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, ["commit", "-a", "-m", "other shape"]).ShouldBeNull();

        // A sequencer (or any operation) in progress: libgit2's commit would clear it, git keeps it.
        var gitDir = (await fixture.Git.RunAsync(fixture.Source, ["rev-parse", "--absolute-git-dir"], CancellationToken.None)).Output.Trim();
        Directory.CreateDirectory(Path.Combine(gitDir, "sequencer"));
        await File.WriteAllTextAsync(Path.Combine(gitDir, "sequencer", "todo"), "pick 0000000 planted\n");
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, ["commit", "--allow-empty", "-m", "kept"]).ShouldBeNull();
        File.Exists(Path.Combine(gitDir, "sequencer", "todo")).ShouldBeTrue();
        Directory.Delete(Path.Combine(gitDir, "sequencer"), recursive: true);

        // Not a checkout root; a bare repository answers rev-parse HEAD in process but stages nothing.
        Directory.CreateDirectory(Path.Combine(fixture.Source, "sub"));
        LandingFixtureGitInProcess.TryRun(fixture.Git, Path.Combine(fixture.Source, "sub"), head).ShouldBeNull();
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Remote, ["add", "."]).ShouldBeNull();
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Remote, head)
            .ShouldBe((await fixture.Git.RunAsync(fixture.Remote, head, CancellationToken.None)).Output);

        // An unborn HEAD.
        var unborn = Path.Combine(fixture.Root, "unborn");
        Directory.CreateDirectory(unborn);
        (await fixture.Git.RunAsync(unborn, ["init", "-b", "master"], CancellationToken.None)).Succeeded.ShouldBeTrue();
        LandingFixtureGitInProcess.TryRun(fixture.Git, unborn, head).ShouldBeNull();
        LandingFixtureGitInProcess.TryRun(fixture.Git, unborn, ["commit", "-m", "first"]).ShouldBeNull();

        // A nested repository where `add .` reaches (git adds a gitlink); one under an ignored directory is not reached.
        var ignoredNested = Path.Combine(fixture.Source, "obj", "inner");
        Directory.CreateDirectory(ignoredNested);
        (await fixture.Git.RunAsync(ignoredNested, ["init", "-b", "master"], CancellationToken.None)).Succeeded.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "added.txt"), "added\n");
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, ["add", "."]).ShouldBe("");
        var nested = Path.Combine(fixture.Source, "vendored");
        Directory.CreateDirectory(nested);
        (await fixture.Git.RunAsync(nested, ["init", "-b", "master"], CancellationToken.None)).Succeeded.ShouldBeTrue();
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, ["add", "."]).ShouldBeNull();
        Directory.Delete(nested, recursive: true);

        // A test that watches or rewrites commands sees every one run as git; the harness's evidence writer (which
        // writes for no command run in process) does not count.
        fixture.Git.Evidence = _ => Task.CompletedTask;
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, head).ShouldNotBeNull();
        fixture.Git.AfterCommand = (_, _, _) => Task.CompletedTask;
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, head).ShouldBeNull();
        fixture.Git.AfterCommand = null;
        fixture.Git.HooksPathOverride = Path.Combine(fixture.Root, "hooks");
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, ["commit", "-m", "hooked"]).ShouldBeNull();
        fixture.Git.HooksPathOverride = null;
        fixture.Git.BeforeCommand = (_, _) => Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        LandingFixtureGitInProcess.TryRun(fixture.Git, fixture.Source, head).ShouldBeNull();
    }

    private static string StripShas(string log) =>
        System.Text.RegularExpressions.Regex.Replace(log, "[0-9a-f]{40}", "<sha>");

    // A git child, never the in-process path: what git itself reads back.
    private static async Task<string> GitAsync(LandingGitFixture fixture, params string[] arguments)
    {
        var result = await fixture.Git.RunAsync(fixture.Source, arguments, CancellationToken.None);
        result.Succeeded.ShouldBeTrue(string.Join(' ', arguments) + ": " + result.Diagnostic);
        return result.Output;
    }
}
