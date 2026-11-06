using Antiphon.GitInProcess;
using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// Differential tests for the commands answered in process (ls-remote of a local-path remote, for-each-ref,
/// diff-tree, ls-tree, recovery pins, worktree lock): each runs both in process and through git, and the exit code
/// and output (or the written file) must be identical, with no git process for the in-process side. This is the one
/// place real <c>git ls-remote</c> still runs in the land tests; every other test reads the remote in process.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class InProcessGitCommandsTests
{
    [Test]
    public async Task Ls_remote_of_a_local_remote_matches_git()
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        // A packed ref, a loose ref, a nested name sharing a tail, and the observer's symbolic origin/HEAD.
        await f.RequiredAsync(f.Remote, "update-ref", "refs/heads/other/master", f.SeedSha);
        await f.RequiredAsync(f.Remote, "pack-refs", "--all");
        await f.RequiredAsync(f.Remote, "update-ref", "refs/heads/loose", f.SeedSha);
        var (inProcess, git) = Runners(f);
        foreach (var (endpoint, patterns) in new[]
                 {
                     (f.Remote, new[] { f.TargetRef }),
                     (f.Remote, new[] { f.SourceRef, f.TargetRef }),
                     (f.Remote, new[] { "master" }),
                     (f.Remote, new[] { "heads/loose", "refs/heads/missing" }),
                     (f.Remote, new[] { "refs/heads/missing" }),
                     (f.Observer, new[] { "refs/remotes/origin/HEAD", "refs/heads/master" }),
                 })
        {
            string[] args = ["ls-remote", "--refs", "--exit-code", endpoint, .. patterns];
            await AssertSameAsync(f, inProcess, git, f.Repository, args);
        }
        // URL remotes and glob patterns are git's.
        await AssertSpawnsAsync(inProcess, f.Repository, ["ls-remote", "--refs", "--exit-code", f.Remote, "refs/heads/*"]);
        await AssertSpawnsAsync(inProcess, f.Repository,
            ["ls-remote", "--refs", "--exit-code", new Uri(f.Remote).AbsoluteUri, f.TargetRef]);
    }

    [Test]
    public async Task Park_proof_reads_match_git()
    {
        // LocalTaskParkPublisher's identity and remote reads: --show-toplevel, the absolute git dir, ls-remote with "--".
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var (inProcess, git) = Runners(f);
        foreach (var checkout in new[] { f.Repository, f.Source, f.Observer })
        {
            await AssertSameAsync(f, inProcess, git, checkout, ["rev-parse", "--show-toplevel"]);
            await AssertSameAsync(f, inProcess, git, checkout, ["rev-parse", "--path-format=absolute", "--git-dir"]);
            await AssertSameAsync(f, inProcess, git, checkout, ["ls-remote", "--refs", "--exit-code", "--", f.Remote, f.SourceRef]);
        }
        await AssertSameAsync(f, inProcess, git, f.Repository, ["ls-remote", "--refs", "--exit-code", "--", f.Remote, "refs/heads/missing"]);
        // Below a checkout root, git searches upward: git's answer.
        var nested = Path.Combine(f.Repository, "nested");
        Directory.CreateDirectory(nested);
        await AssertSpawnsAsync(inProcess, nested, ["rev-parse", "--show-toplevel"]);
    }

    [Test]
    public async Task For_each_ref_diff_tree_and_ls_tree_match_git()
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var land = $"refs/antiphon/land/{f.TaskId:N}/";
        var progress = $"refs/antiphon/progress/{f.TaskId:N}/";
        await f.RequiredAsync(f.Repository, "update-ref", land + Guid.NewGuid().ToString("N") + "/source", f.SeedSha);
        await f.RequiredAsync(f.Repository, "pack-refs", "--all");
        await f.RequiredAsync(f.Repository, "update-ref", land + Guid.NewGuid().ToString("N") + "/target-before", f.SeedSha);
        await f.RequiredAsync(f.Repository, "update-ref", progress + "a", f.SeedSha);
        var (inProcess, git) = Runners(f);
        await AssertSameAsync(f, inProcess, git, f.Repository, ["for-each-ref", "--format=%(refname) %(objectname)", land, progress]);
        await AssertSameAsync(f, inProcess, git, f.Repository, ["for-each-ref", "--format=%(objectname) %(refname)", "refs/heads"]);
        await AssertSameAsync(f, inProcess, git, f.Repository, ["for-each-ref", "--format=%(refname) %(objectname)", land + "none/"]);

        // A modification, an executable bit, a nested addition, then a deletion alone.
        var seed = f.SeedSha;
        var modified = await CommitAsync(f, seed, ("keep.txt", "changed\n", false), ("docs/a/b.md", "b\n", false),
            ("docs/a-b.md", "c\n", false), ("run.sh", "#!/bin/sh\n", true));
        var deleted = await CommitAsync(f, modified, ("keep.txt", null, false));
        foreach (var (from, to) in new[] { (seed, modified), (modified, deleted), (seed, seed) })
            await AssertSameAsync(f, inProcess, git, f.Repository, DiffTree(from, to));
        foreach (var rev in new[] { seed, modified, deleted })
            await AssertSameAsync(f, inProcess, git, f.Repository, ["ls-tree", "-r", "-z", rev]);

        // An addition and a deletion together could be a rename: git decides.
        var renamed = await CommitAsync(f, modified, ("keep.txt", null, false), ("kept.txt", "changed\n", false));
        await AssertSpawnsAsync(inProcess, f.Repository, DiffTree(modified, renamed));
    }

    [Test]
    public async Task Recovery_pin_and_worktree_lock_match_git()
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var (inProcess, git) = Runners(f);
        var prefix = $"refs/antiphon/land/{f.TaskId:N}/{Guid.NewGuid():N}";
        var zero = new string('0', 40);
        (await inProcess.RunAsync(f.Repository, ["update-ref", prefix + "/source", f.SeedSha, zero], default)).ExitCode.ShouldBe(0);
        inProcess.Spawned.ShouldBe(0);
        (await git.RunAsync(f.Repository, ["update-ref", prefix + "/target-before", f.SeedSha, zero], default)).ExitCode.ShouldBe(0);
        var refs = Path.Combine(f.Repository, ".git");
        (await File.ReadAllTextAsync(Path.Combine(refs, prefix + "/source")))
            .ShouldBe(await File.ReadAllTextAsync(Path.Combine(refs, prefix + "/target-before")));
        File.Exists(Path.Combine(refs, "logs", prefix + "/source")).ShouldBe(File.Exists(Path.Combine(refs, "logs", prefix + "/target-before")));
        // An existing pin is git's refusal.
        (await inProcess.RunAsync(f.Repository, ["update-ref", prefix + "/source", f.SeedSha, zero], default)).ExitCode.ShouldNotBe(0);
        inProcess.Spawned.ShouldBe(1);

        // Two more linked worktrees: one locked in process, one by git; same file.
        var a = Path.Combine(f.Root, "trees", "lock-a");
        var b = Path.Combine(f.Root, "trees", "lock-b");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", a, f.SeedSha);
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", b, f.SeedSha);
        var before = inProcess.Spawned;
        (await inProcess.RunAsync(f.Repository, ["worktree", "lock", "--reason", "antiphon-land", a], default)).ExitCode.ShouldBe(0);
        inProcess.Spawned.ShouldBe(before);
        (await git.RunAsync(f.Repository, ["worktree", "lock", "--reason", "antiphon-land", b], default)).ExitCode.ShouldBe(0);
        var admin = Path.Combine(refs, "worktrees");
        (await File.ReadAllBytesAsync(Path.Combine(admin, "lock-a", "locked")))
            .ShouldBe(await File.ReadAllBytesAsync(Path.Combine(admin, "lock-b", "locked")));
        Normalize(await f.RequiredAsync(f.Repository, "worktree", "list", "--porcelain"), a, b)
            .ShouldContain("locked antiphon-land");
        // Already locked, or the main worktree: git's errors.
        foreach (var path in new[] { a, f.Repository })
        {
            before = inProcess.Spawned;
            var locked = await inProcess.RunAsync(f.Repository, ["worktree", "lock", "--reason", "antiphon-land", path], default);
            var expected = await git.RunAsync(f.Repository, ["worktree", "lock", "--reason", "antiphon-land", path], default);
            locked.ExitCode.ShouldBe(expected.ExitCode);
            locked.ExitCode.ShouldNotBe(0);
            inProcess.Spawned.ShouldBe(before + 1);
        }
    }

    /// <summary>
    /// The worktree manager's and the workspace service's own git runners answer the same read shapes in process:
    /// creating a worktree reads the repository without a git process except for the add itself, and each answer
    /// is the one git gives (the created worktree is git's, its registration listed by git and in process alike).
    /// </summary>
    [Test]
    public async Task Worktree_manager_and_workspace_reads_are_answered_in_process()
    {
        using var repo = new ScratchGitRepo("antiphon-inproc-runner");
        await repo.CommitFileAsync("a.txt", "a\n");
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Antiphon.Server" or "Antiphon.Tests.InProcessRunners",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span => { lock (spans) spans.Add(span); },
        };
        ActivitySource.AddActivityListener(listener);
        using var traced = new ActivitySource("Antiphon.Tests.InProcessRunners").StartActivity("runner-test");
        traced.ShouldNotBeNull();
        var trace = traced.TraceId;

        var manager = new WorktreeManager(Options.Create(new GitSettings { WorktreeBasePath = repo.WorktreeRoot }),
            TimeProvider.System, NullLogger<WorktreeManager>.Instance);
        var info = await manager.CreateAsync(repo.Path, "CARD-1234", "master", CancellationToken.None);
        var workspace = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance);
        var inside = await workspace.IsRepositoryAsync(info.Path, CancellationToken.None);
        var head = await workspace.GetHeadShaAsync(info.Path, CancellationToken.None);

        List<Activity> git;
        lock (spans) git = spans.Where(s => s.TraceId == trace && s.Source.Name == "Antiphon.Server").ToList();
        List<string> Names(string runner, bool spawned) => git
            .Where(s => s.GetTagItem("git.runner") as string == runner && s.GetTagItem("git.spawned") is bool b && b == spawned)
            .Select(s => s.DisplayName).ToList();
        Names("worktree", spawned: true).ShouldBe(["git worktree add"], "only the add starts git");
        // Two work-tree checks, the base ref twice, the new checkout's git dir; the registration listing; the branch check.
        var reads = Names("worktree", spawned: false);
        reads.Count(n => n == "git rev-parse").ShouldBe(5, string.Join(", ", reads));
        reads.Count(n => n == "git worktree list").ShouldBe(1, string.Join(", ", reads));
        reads.Count(n => n == "git show-ref").ShouldBe(1, string.Join(", ", reads));
        Names("workspace", spawned: true).ShouldBeEmpty();
        Names("workspace", spawned: false).ShouldBe(["git rev-parse", "git rev-parse"]);

        inside.ShouldBeTrue();
        head.ShouldBe((await repo.GitReadAsync("rev-parse", "master")).Trim());
        (await ScratchGitRepo.GitInAsync(info.Path, "rev-parse", "--abbrev-ref", "HEAD")).StdOut.Trim().ShouldBe(info.Branch);
        (await ScratchGitRepo.GitInAsync(info.Path, "status", "--porcelain")).StdOut.ShouldBeEmpty();
        // The listing the manager now reads in process is git's own.
        var listed = await repo.GitReadAsync("worktree", "list", "--porcelain");
        InProcessGitReads.TryAnswer(repo.Path, ["worktree", "list", "--porcelain"]).ShouldNotBeNull().Output.ShouldBe(listed);
        (await manager.ListAsync(repo.Path, CancellationToken.None)).Select(w => w.Path)
            .ShouldContain(info.Path);
    }

    private static string[] DiffTree(string from, string to) =>
        ["diff-tree", "--no-commit-id", "--raw", "-r", "-z", "--no-abbrev", "--no-ext-diff", "--no-textconv", "--find-renames", from, to, "--"];

    private static string Normalize(string text, params string[] _) => text.Replace('\\', '/');

    /// <summary>A commit on <paramref name="parent"/> with the given files written (null deletes), made with plumbing
    /// in a scratch index so no checkout changes.</summary>
    private static async Task<string> CommitAsync(LandingGitFixture f, string parent, params (string Path, string? Content, bool Executable)[] files)
    {
        var index = Path.Combine(f.Root, "scratch-index-" + Guid.NewGuid().ToString("N"));
        var git = new LandingGitFixture.FixtureGit(Path.Combine(f.Root, "home"), f.TaskId);
        async Task<string> Run(params string[] args)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = f.Repository, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment["GIT_INDEX_FILE"] = index;
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(f.Root, "home", "empty-config");
            start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "C448 Fixture";
            start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid";
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.ShouldBe(0, string.Join(' ', args) + ": " + await process.StandardError.ReadToEndAsync());
            return output.Trim();
        }
        await Run("read-tree", parent);
        foreach (var (path, content, executable) in files)
        {
            if (content is null) { await Run("update-index", "--force-remove", path); continue; }
            var file = Path.Combine(f.Root, "scratch-" + Guid.NewGuid().ToString("N"));
            await File.WriteAllTextAsync(file, content);
            var blob = await Run("hash-object", "-w", file);
            await Run("update-index", "--add", "--cacheinfo", (executable ? "100755" : "100644") + "," + blob + "," + path);
        }
        var tree = await Run("write-tree");
        return await Run("commit-tree", tree, "-p", parent, "-m", "change");
    }

    private static async Task AssertSameAsync(LandingGitFixture f, CountingGit inProcess, CountingGit git, string directory, string[] args)
    {
        var before = inProcess.Spawned;
        var gitBefore = git.Spawned;
        var mine = await inProcess.RunAsync(directory, args, default);
        var theirs = await git.RunAsync(directory, args, default);
        var label = string.Join(' ', args.Select(a => a.Replace(f.Root, "<root>")));
        inProcess.Spawned.ShouldBe(before, "answered in process: " + label);
        git.Spawned.ShouldBe(gitBefore + 1, "the reference answer must come from git itself: " + label);
        mine.ExitCode.ShouldBe(theirs.ExitCode, label);
        mine.Output.ShouldBe(theirs.Output, label);
    }

    private static async Task AssertSpawnsAsync(CountingGit inProcess, string directory, string[] args)
    {
        var before = inProcess.Spawned;
        await inProcess.RunAsync(directory, args, default);
        inProcess.Spawned.ShouldBe(before + 1, "left to git: " + string.Join(' ', args));
    }

    private static (CountingGit InProcess, CountingGit Git) Runners(LandingGitFixture f) =>
        (new CountingGit(f, gitOnly: false), new CountingGit(f, gitOnly: true));

    /// <summary>The fixture's git, counting started processes. <c>gitOnly</c> sets a repository-discovery variable,
    /// which turns every in-process answer off for that instance (LandingGit.InProcessEligible).</summary>
    private sealed class CountingGit(LandingGitFixture f, bool gitOnly)
        : LandingGitFixture.FixtureGit(Path.Combine(f.Root, "home"), f.TaskId)
    {
        public int Spawned;

        protected override void ConfigureProcess(ProcessStartInfo start)
        {
            base.ConfigureProcess(start);
            if (gitOnly) start.Environment["GIT_CEILING_DIRECTORIES"] = Path.Combine(Path.GetPathRoot(f.Root)!, "no-such-ceiling");
        }

        protected override Process? StartProcess(ProcessStartInfo start)
        {
            Interlocked.Increment(ref Spawned);
            return base.StartProcess(start);
        }
    }
}
