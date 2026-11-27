using System.Diagnostics;
using Antiphon.GitInProcess;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// The worktree-base inspection's questions answered by libgit2 (<see cref="InProcessGitInspection"/>) against the git
/// commands they replace: <c>rev-list --min-parents=2</c> + <c>git cherry</c> for containment, <c>rev-parse --git-path</c>
/// for an operation in progress, and <c>for-each-ref --merged</c>. The answers must agree (not the bytes), or libgit2
/// must hand the question back (null). Real temp repositories, git on PATH.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LibGit2VsGitInspectionTests
{
    private static async Task<string> GitAsync(string cwd, params string[] args) => (await GitRunAsync(cwd, args)).Output;

    private static async Task<(string Output, int Exit)> GitRunAsync(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var (key, value) in new[] { ("GIT_AUTHOR_NAME", "t"), ("GIT_AUTHOR_EMAIL", "t@example.invalid"),
                     ("GIT_COMMITTER_NAME", "t"), ("GIT_COMMITTER_EMAIL", "t@example.invalid") })
            start.Environment[key] = value;
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        _ = await error;
        return (await output, p.ExitCode);
    }

    private sealed class Repo : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("inspect-").FullName;
        public void Dispose() { try { SyncDelete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        private static void SyncDelete(string path)
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }

        public static async Task<Repo> CreateAsync()
        {
            var repo = new Repo();
            await GitAsync(repo.Path, "init", "-q", "-b", "master");
            await repo.CommitAsync("base.txt", "one\ntwo\nthree\n", "base");
            return repo;
        }

        public async Task<string> CommitAsync(string file, string content, string message)
        {
            var full = System.IO.Path.Combine(Path, file);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content);
            await GitAsync(Path, "add", "--", file);
            await GitAsync(Path, "commit", "-q", "-m", message);
            return (await GitAsync(Path, "rev-parse", "HEAD")).Trim();
        }

        public async Task<string> Sha(string reference) => (await GitAsync(Path, "rev-parse", reference)).Trim();
    }

    /// <summary>The production git-command answer (<see cref="GitCommandRepository"/>), through a real runner.</summary>
    private static GitCommandRepository GitAnswers(string path) => new(new LandingGit(), path);

    private static async Task<PatchContainment> GitContainmentAsync(string repo, string target, string source) =>
        await GitAnswers(repo).ContainsUnmergedAsync(target, source, CancellationToken.None) switch
        {
            DelegationWorktreeService.CommitContainment.Contained => PatchContainment.Contained,
            DelegationWorktreeService.CommitContainment.NotContained => PatchContainment.NotContained,
            _ => PatchContainment.HasMerge,
        };

    private static async Task SameContainmentAsync(Repo repo, string target, string source, PatchContainment expected)
    {
        var git = await GitContainmentAsync(repo.Path, target, source);
        git.ShouldBe(expected, "git's own answer");
        InProcessGitInspection.Containment(repo.Path, target, source).ShouldBe(expected, "libgit2's answer");
    }

    [Test]
    public async Task Containment_agrees_with_rev_list_and_cherry()
    {
        using var repo = await Repo.CreateAsync();
        var master = await repo.Sha("master");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "topic");
        var first = await repo.CommitAsync("a.txt", "a\n", "a");
        var second = await repo.CommitAsync("b.txt", "b\n", "b");

        // Source beyond the target, nothing applied there.
        await SameContainmentAsync(repo, master, second, PatchContainment.NotContained);
        // Source already in the target's history.
        await SameContainmentAsync(repo, second, first, PatchContainment.Contained);
        // Same commit.
        await SameContainmentAsync(repo, second, second, PatchContainment.Contained);

        // Both commits cherry-picked onto the target (new ids, same patches): contained.
        await GitAsync(repo.Path, "checkout", "-q", "master");
        await repo.CommitAsync("other.txt", "other\n", "other");
        await GitAsync(repo.Path, "cherry-pick", first, second);
        var picked = await repo.Sha("master");
        await SameContainmentAsync(repo, picked, second, PatchContainment.Contained);

        // Only the first picked: the second is not contained.
        await GitAsync(repo.Path, "checkout", "-q", "-b", "half", master);
        await GitAsync(repo.Path, "cherry-pick", first);
        await SameContainmentAsync(repo, await repo.Sha("half"), second, PatchContainment.NotContained);
    }

    [Test]
    public async Task Containment_sees_an_edit_picked_with_other_whitespace_and_a_different_edit()
    {
        using var repo = await Repo.CreateAsync();
        var master = await repo.Sha("master");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "topic");
        var edit = await repo.CommitAsync("base.txt", "one\nTWO\nthree\n", "edit");
        // The same change with different whitespace (git patch-id ignores whitespace): contained.
        await GitAsync(repo.Path, "checkout", "-q", "-b", "spaced", master);
        await repo.CommitAsync("base.txt", "one\nT W O\nthree\n", "edit spaced");
        await SameContainmentAsync(repo, await repo.Sha("spaced"), edit, PatchContainment.Contained);
        // A different change to the same line: not contained.
        await GitAsync(repo.Path, "checkout", "-q", "-b", "different", master);
        await repo.CommitAsync("base.txt", "one\n2\nthree\n", "edit differently");
        await SameContainmentAsync(repo, await repo.Sha("different"), edit, PatchContainment.NotContained);
    }

    [Test]
    public async Task Containment_reports_a_merge_beyond_the_target()
    {
        using var repo = await Repo.CreateAsync();
        var master = await repo.Sha("master");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "side");
        await repo.CommitAsync("side.txt", "side\n", "side");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "topic", master);
        await repo.CommitAsync("topic.txt", "topic\n", "topic");
        await GitAsync(repo.Path, "merge", "-q", "--no-ff", "--no-edit", "side");
        await SameContainmentAsync(repo, master, await repo.Sha("topic"), PatchContainment.HasMerge);
    }

    [Test]
    public async Task Containment_hands_back_a_binary_change_or_an_unknown_commit()
    {
        using var repo = await Repo.CreateAsync();
        var master = await repo.Sha("master");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "topic");
        await File.WriteAllBytesAsync(Path.Combine(repo.Path, "blob.bin"), [0, 1, 2, 0, 255, 0]);
        await GitAsync(repo.Path, "add", "blob.bin");
        await GitAsync(repo.Path, "commit", "-q", "-m", "binary");
        InProcessGitInspection.Containment(repo.Path, master, await repo.Sha("topic")).ShouldBeNull();
        InProcessGitInspection.Containment(repo.Path, master, new string('a', 40)).ShouldBeNull();
    }

    [Test]
    public async Task Containment_agrees_for_renames_mode_changes_empty_commits_and_target_merges()
    {
        using var repo = await Repo.CreateAsync();
        var master = await repo.Sha("master");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "topic");
        await GitAsync(repo.Path, "mv", "base.txt", "moved.txt");
        await GitAsync(repo.Path, "commit", "-q", "-m", "rename");
        var renamed = await repo.Sha("HEAD");
        await GitAsync(repo.Path, "update-index", "--chmod=+x", "moved.txt");
        await GitAsync(repo.Path, "commit", "-q", "-m", "mode");
        var moded = await repo.Sha("HEAD");
        await GitAsync(repo.Path, "commit", "-q", "--allow-empty", "-m", "empty");
        var empty = await repo.Sha("HEAD");
        await SameContainmentAsync(repo, master, empty, PatchContainment.NotContained);

        // The same three picked onto a target that also merged an unrelated branch (a target-side merge is skipped).
        await GitAsync(repo.Path, "checkout", "-q", "-b", "side", master);
        await repo.CommitAsync("side.txt", "side\n", "side");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "landed", master);
        await repo.CommitAsync("landed.txt", "landed\n", "landed");
        await GitAsync(repo.Path, "merge", "-q", "--no-ff", "--no-edit", "side");
        await GitAsync(repo.Path, "cherry-pick", "--allow-empty", "--keep-redundant-commits", renamed, moded, empty);
        var landed = await repo.Sha("landed");
        await SameContainmentAsync(repo, landed, empty, PatchContainment.Contained);
        // The rename alone picked: the mode change is not contained.
        await GitAsync(repo.Path, "checkout", "-q", "-b", "renamed-only", master);
        await GitAsync(repo.Path, "cherry-pick", renamed);
        await SameContainmentAsync(repo, await repo.Sha("renamed-only"), moded, PatchContainment.NotContained);
    }

    [Test]
    public async Task Repository_falls_back_to_git_for_a_binary_change()
    {
        using var repo = await Repo.CreateAsync();
        var master = await repo.Sha("master");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "topic");
        await File.WriteAllBytesAsync(Path.Combine(repo.Path, "blob.bin"), [0, 1, 2, 0, 255, 0]);
        await GitAsync(repo.Path, "add", "blob.bin");
        await GitAsync(repo.Path, "commit", "-q", "-m", "binary");
        var topic = await repo.Sha("topic");
        var hits = 0;
        var answer = await new GitRepository(new LandingGit(), repo.Path, () => hits++)
            .ContainsUnmergedAsync(master, topic, CancellationToken.None);
        answer.ShouldBe(DelegationWorktreeService.CommitContainment.NotContained);
        hits.ShouldBe(0, "libgit2 handed the binary change to git");
    }

    /// <summary>
    /// The production git-command answer: <c>rev-parse --git-path</c> of the three states, found on disk. In a main
    /// checkout git prints a relative path, read against the checkout (the merge case below pins it).
    /// </summary>
    private static Task<bool> GitOperationInProgressAsync(string checkout) =>
        GitAnswers(checkout).OperationInProgressAsync(CancellationToken.None);

    [Test]
    public async Task Operation_in_progress_agrees_with_git_path_in_main_and_linked_checkouts()
    {
        using var repo = await Repo.CreateAsync();
        var linked = repo.Path + "-linked";
        await GitAsync(repo.Path, "worktree", "add", "-q", "-b", "linked", linked);
        try
        {
            foreach (var checkout in new[] { repo.Path, linked })
            {
                (await GitOperationInProgressAsync(checkout)).ShouldBeFalse(checkout);
                InProcessGitInspection.OperationInProgress(checkout).ShouldBe(false, checkout);
            }

            // A conflicted merge in the main checkout leaves MERGE_HEAD there, not in the linked one.
            await GitAsync(repo.Path, "checkout", "-q", "-b", "left");
            await repo.CommitAsync("base.txt", "left\n", "left");
            await GitAsync(repo.Path, "checkout", "-q", "master");
            await repo.CommitAsync("base.txt", "right\n", "right");
            (await GitRunAsync(repo.Path, "merge", "left")).Exit.ShouldNotBe(0, "the merge conflicts");
            (await GitOperationInProgressAsync(repo.Path)).ShouldBeTrue();
            InProcessGitInspection.OperationInProgress(repo.Path).ShouldBe(true);
            (await GitOperationInProgressAsync(linked)).ShouldBeFalse();
            InProcessGitInspection.OperationInProgress(linked).ShouldBe(false);

            // A rebase state directory in the linked checkout's own git directory.
            var linkedGitDir = (await GitAsync(linked, "rev-parse", "--absolute-git-dir")).Trim();
            Directory.CreateDirectory(Path.Combine(linkedGitDir, "rebase-merge"));
            (await GitOperationInProgressAsync(linked)).ShouldBeTrue();
            InProcessGitInspection.OperationInProgress(linked).ShouldBe(true);
        }
        finally
        {
            await GitAsync(repo.Path, "worktree", "remove", "--force", linked);
        }
    }

    [Test]
    public async Task For_each_ref_merged_matches_git()
    {
        using var repo = await Repo.CreateAsync();
        var master = await repo.Sha("master");
        await GitAsync(repo.Path, "branch", "at-base");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "ahead");
        await repo.CommitAsync("a.txt", "a\n", "a");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "further");
        var further = await repo.CommitAsync("b.txt", "b\n", "b");
        await GitAsync(repo.Path, "checkout", "-q", "-b", "elsewhere", master);
        await repo.CommitAsync("c.txt", "c\n", "c");

        foreach (var into in new[] { master, further, await repo.Sha("ahead"), await repo.Sha("elsewhere") })
        {
            string[] args = ["for-each-ref", "--format=%(objectname) %(refname)", "--merged=" + into,
                "refs/heads/ahead", "refs/heads/at-base", "refs/heads/elsewhere", "refs/heads/further", "refs/heads/master"];
            var answer = InProcessGitReads.TryAnswer(repo.Path, args);
            answer.ShouldNotBeNull("answered in process: --merged=" + into);
            answer.ExitCode.ShouldBe(0);
            answer.Output.ShouldBe(await GitAsync(repo.Path, args), "--merged=" + into);
        }
        // A short or unknown id is git's.
        InProcessGitReads.TryAnswer(repo.Path, ["for-each-ref", "--format=%(objectname) %(refname)", "--merged=" + master[..8]])
            .ShouldBeNull();
        InProcessGitReads.TryAnswer(repo.Path, ["for-each-ref", "--format=%(objectname) %(refname)", "--merged=" + new string('b', 40)])
            .ShouldBeNull();
    }
}
