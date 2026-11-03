using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0824 S2 V-40 (CP-16). The runner deletes its local <c>refs/heads/&lt;b&gt;</c> and
/// <c>refs/remotes/origin/&lt;b&gt;</c> only at or behind the leased SHA, never for a checked-out, unpublished,
/// moved or non-delegate branch, and never reads an unknown object as absence. Real git in a scratch repository.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C0824RunnerBranchRefsTests
{
    private const string Branch = "feat/card-task-0824beef";
    private static string Local => "refs/heads/" + Branch;
    private static string Tracking => "refs/remotes/origin/" + Branch;

    [Test]
    [Timeout(30_000)]
    public async Task C0824_deletes_local_and_tracking_refs_at_expected_sha(CancellationToken ct)
    {
        using var s = Scratch.Create();
        var result = await s.Service().DeleteBranchRefsAsync(new(Branch, s.Sha), ct);
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Deleted, result.Code);
        result.Deleted.Order(StringComparer.Ordinal).ShouldBe([Local, Tracking]);
        s.Ref(Local).ShouldBeNull("local-gone");
        s.Ref(Tracking).ShouldBeNull("tracking-gone");
        s.Ref("refs/heads/main").ShouldNotBeNull("other refs untouched");
    }

    [Test]
    [Timeout(30_000)]
    public async Task C0824_absent_refs_are_idempotent_success(CancellationToken ct)
    {
        using var s = Scratch.Create();
        var service = s.Service();
        (await service.DeleteBranchRefsAsync(new(Branch, s.Sha), ct)).Succeeded.ShouldBeTrue();
        var again = await service.DeleteBranchRefsAsync(new(Branch, s.Sha), ct);
        again.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Deleted, again.Code);
        again.Deleted.ShouldBeEmpty();
        again.Absent.Order(StringComparer.Ordinal).ShouldBe([Local, Tracking]);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C0824_checked_out_branch_refuses_and_keeps_refs(CancellationToken ct)
    {
        using var s = Scratch.Create();
        Scratch.Git(s.Repo, "branch", "-D", Branch);
        Scratch.Git(s.Repo, "worktree", "add", "-B", Branch, Path.Combine(s.Work, "worktrees", "task-0824beef"), s.Sha);
        var result = await s.Service().DeleteBranchRefsAsync(new(Branch, s.Sha), ct);
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Refused);
        result.Code.ShouldBe(WorkspaceBranchRefsCodes.BranchCheckedOut);
        s.Ref(Local).ShouldBe(s.Sha, "refs-kept");
        s.Ref(Tracking).ShouldBe(s.Sha, "refs-kept");
    }

    [Test]
    [Timeout(30_000)]
    [Arguments("local-branch-ahead")]
    [Arguments("tracking-moved")]
    public async Task C0824_unpublished_local_tip_refuses(string family, CancellationToken ct)
    {
        using var s = Scratch.Create();
        var ahead = s.Child(s.Sha, family);
        var moved = family == "local-branch-ahead" ? Local : Tracking;
        Scratch.Git(s.Repo, "update-ref", moved, ahead);
        var result = await s.Service().DeleteBranchRefsAsync(new(Branch, s.Sha), ct);
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Refused, family);
        result.Code.ShouldBe(WorkspaceBranchRefsCodes.UnpublishedLocalRef, family);
        s.Ref(moved).ShouldBe(ahead, "unpublished-kept");
        s.Ref(family == "local-branch-ahead" ? Tracking : Local).ShouldBe(s.Sha, "nothing deleted on refusal");
    }

    [Test]
    [Timeout(30_000)]
    public async Task C0824_ancestor_tip_is_deleted(CancellationToken ct)
    {
        using var s = Scratch.Create();
        var child = s.Child(s.Sha, "landed-later");
        var result = await s.Service().DeleteBranchRefsAsync(new(Branch, child), ct);
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Deleted, result.Code);
        s.Ref(Local).ShouldBeNull();
        s.Ref(Tracking).ShouldBeNull();
    }

    [Test]
    [Timeout(30_000)]
    [Arguments("master")]
    [Arguments("rc/2026-11-01")]
    [Arguments("feat/other")]
    public async Task C0824_non_delegate_branch_refused(string branch, CancellationToken ct)
    {
        using var s = Scratch.Create();
        Scratch.Git(s.Repo, "update-ref", "refs/heads/" + branch, s.Sha);
        Scratch.Git(s.Repo, "update-ref", "refs/remotes/origin/" + branch, s.Sha);
        var result = await s.Service().DeleteBranchRefsAsync(new(branch, s.Sha), ct);
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Refused, branch);
        result.Code.ShouldBe(WorkspaceBranchRefsCodes.ProtectedBranch, branch);
        s.Ref("refs/heads/" + branch).ShouldBe(s.Sha, "master-kept");
        s.Ref("refs/remotes/origin/" + branch).ShouldBe(s.Sha, "master-kept");
    }

    [Test]
    [Timeout(30_000)]
    public async Task C0824_ref_moved_between_read_and_delete_refuses(CancellationToken ct)
    {
        using var s = Scratch.Create();
        var service = s.Service();
        var elsewhere = s.Child(s.Sha, "moved-in-race");
        var fired = false;
        service.BeforeBranchRefDelete = name =>
        {
            if (!fired && name == Local)
            {
                fired = true;
                Scratch.Git(s.Repo, "update-ref", Local, elsewhere);
            }
            return Task.CompletedTask;
        };
        var result = await service.DeleteBranchRefsAsync(new(Branch, s.Sha), ct);
        fired.ShouldBeTrue("the race seam ran before the delete");
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Refused);
        result.Code.ShouldBe(WorkspaceBranchRefsCodes.RefMoved);
        s.Ref(Local).ShouldBe(elsewhere, "moved-ref-kept");
    }

    [Test]
    [Timeout(30_000)]
    public async Task C0824_missing_expected_object_refuses(CancellationToken ct)
    {
        using var s = Scratch.Create();
        var unknown = new string('7', 40);
        var result = await s.Service().DeleteBranchRefsAsync(new(Branch, unknown), ct);
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Refused);
        result.Code.ShouldBe(WorkspaceBranchRefsCodes.ExpectedObjectMissing);
        result.Absent.ShouldBeEmpty("unknown is never absent");
        s.Ref(Local).ShouldBe(s.Sha);
        s.Ref(Tracking).ShouldBe(s.Sha);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C0824_missing_repository_refuses_unavailable(CancellationToken ct)
    {
        // Review 370083f1 A-4: an absent primary (an unmounted volume) is unknown, never "every ref is absent".
        using var s = Scratch.Create();
        var missing = Path.Combine(s.Root, "unmounted", "repo");
        var service = new RunnerWorkspaceService(missing, s.Work, s.Origin, psi => Process.Start(psi));
        var result = await service.DeleteBranchRefsAsync(new(Branch, s.Sha), ct);
        result.Outcome.ShouldBe(WorkspaceBranchRefsCodes.Refused, "missing-repository-refused");
        result.Code.ShouldBe(WorkspaceBranchRefsCodes.RepositoryUnavailable, "missing-repository-refused");
        result.Succeeded.ShouldBeFalse();
        result.Absent.ShouldBeEmpty("unknown is never absent");
        Directory.Exists(missing).ShouldBeFalse("the op never creates the repository");
    }

    /// <summary>Bare origin with the task branch, a runner clone with both refs at the branch tip.</summary>
    private sealed class Scratch : IDisposable
    {
        public required string Root { get; init; }
        public required string Repo { get; init; }
        public required string Origin { get; init; }
        public required string Work { get; init; }
        public required string Sha { get; init; }

        public static Scratch Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "c0824-refs-" + Guid.NewGuid().ToString("N"));
            var seed = Path.Combine(root, "seed");
            var origin = Path.Combine(root, "origin.git");
            var repo = Path.Combine(root, "repo");
            var work = Path.Combine(root, "work");
            Directory.CreateDirectory(seed);
            Directory.CreateDirectory(work);
            Git(seed, "init", "--initial-branch=main");
            File.WriteAllText(Path.Combine(seed, "README.md"), "c0824");
            Git(seed, "add", "README.md");
            Git(seed, "commit", "-m", "base");
            Git(seed, "checkout", "-b", Branch);
            File.WriteAllText(Path.Combine(seed, "task.txt"), "task work");
            Git(seed, "add", "task.txt");
            Git(seed, "commit", "-m", "task");
            var sha = Git(seed, "rev-parse", "HEAD").Trim();
            Git(seed, "checkout", "main");
            Git(root, "clone", "--bare", seed, origin);
            Git(root, "clone", origin, repo);
            Git(repo, "branch", Branch, "origin/" + Branch);
            return new Scratch { Root = root, Repo = repo, Origin = origin, Work = work.Replace('\\', '/'), Sha = sha };
        }

        public RunnerWorkspaceService Service() =>
            new(Repo, Work, Origin, psi =>
            {
                Configure(psi, Root);
                return Process.Start(psi);
            });

        public string? Ref(string name)
        {
            var (code, output) = TryGit(Repo, "rev-parse", "--verify", "-q", name);
            return code == 0 ? output.Trim() : null;
        }

        /// <summary>A new commit whose parent is <paramref name="parent"/>, reachable from no ref.</summary>
        public string Child(string parent, string message) =>
            Git(Repo, "commit-tree", parent + "^{tree}", "-p", parent, "-m", message).Trim();

        private static void Configure(ProcessStartInfo psi, string root)
        {
            var empty = Path.Combine(root, "empty.gitconfig");
            if (!File.Exists(empty)) File.WriteAllText(empty, "");
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_CONFIG_GLOBAL"] = empty;
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GIT_AUTHOR_NAME"] = psi.Environment["GIT_COMMITTER_NAME"] = "c0824";
            psi.Environment["GIT_AUTHOR_EMAIL"] = psi.Environment["GIT_COMMITTER_EMAIL"] = "c0824@localhost";
        }

        public static string Git(string cwd, params string[] args)
        {
            var (code, output) = TryGit(cwd, args);
            if (code != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {output}");
            return output;
        }

        private static (int Code, string Output) TryGit(string cwd, params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            var root = cwd;
            while (Path.GetFileName(root) is not { } leaf || !leaf.StartsWith("c0824-refs-", StringComparison.Ordinal))
                root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("scratch root not found");
            Configure(psi, root);
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout.Result + stderr);
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
