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

    private static async Task<LandingGitResult> GitAsync(string cwd, params string[] args) =>
        (await GitWithErrorAsync(cwd, args)).Result;

    private static async Task<(LandingGitResult Result, string Error)> GitWithErrorAsync(string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (new LandingGitResult(p.ExitCode, await output, ""), await error);
    }

    private static async Task CompareAsync(string cwd, Expect expect, params string[] args)
    {
        var shape = string.Join(' ', args) + " @ " + cwd;
        var answer = InProcessGitReads.TryAnswer(cwd, args);
        if (expect == Expect.Fallback) { answer.ShouldBeNull("must fall back to git: " + shape); return; }
        if (answer is null) { expect.ShouldNotBe(Expect.Answered, "must be answered in process: " + shape); return; }
        var (git, error) = await GitWithErrorAsync(cwd, args);
        answer.ExitCode.ShouldBe(git.ExitCode, shape);
        answer.Output.ShouldBe(git.Output, shape);
        if (answer.ExitCode != 0) answer.Diagnostic.ShouldBe($"git_exit_{answer.ExitCode}", shape);
        // An answer the stderr-forwarding runners take (TryAnswerSilently) is only one git gives without a word.
        if (InProcessGitReads.TryAnswerSilently(cwd, args) is not null) error.ShouldBeEmpty("git wrote to stderr: " + shape);
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
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--path-format=absolute", "--git-path", "logs/HEAD");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--path-format=absolute", "--git-path", "logs/" + fixture.SourceRef);
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--path-format=absolute", "--git-path", "logs/refs/heads/no-such-branch");
        await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--path-format=absolute", "--git-path", "logs/refs/bisect/bad");
        await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--path-format=absolute", "--git-path", "logs/refs/worktree/x");
        foreach (var perWorktree in new[] { "logs/refs/bisect", "logs/refs/rewritten", "logs/refs/worktree", "logs/refs/rewritten/x" })
            await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--path-format=absolute", "--git-path", perWorktree);
        // An existing log spelled otherwise than on disk: git prints the on-disk spelling where the file system
        // ignores case, so git answers there.
        var branch = fixture.SourceRef["refs/heads/".Length..];
        var ignoresCase = cwd != cwd.ToUpperInvariant() && Directory.Exists(cwd.ToUpperInvariant());
        await CompareAsync(cwd, ignoresCase ? Expect.Fallback : Expect.Answered,
            "rev-parse", "--path-format=absolute", "--git-path", "logs/refs/heads/" + branch.ToUpperInvariant());
        await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--path-format=absolute", "--git-path", "logs/" + fixture.SourceRef + ".");
        // Through an existing reflog file: git's "Not a directory" (128).
        await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--path-format=absolute", "--git-path", "logs/" + fixture.SourceRef + "/x");
        // A missing name with a describe tail (-g<hex>) is a commit to git.
        await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--verify", "--quiet", "refs/heads/nope-1-g" + head[..7]);
        await CompareAsync(cwd, Expect.Answered, "rev-parse", fixture.TargetRef);
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", fixture.SourceRef);
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "--quiet", fixture.SourceRef);
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "--quiet", "refs/heads/no-such-branch");
        await CompareAsync(cwd, Expect.Fallback, "rev-parse", "refs/heads/no-such-branch");
        await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--verify", "refs/heads/no-such-branch");
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
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--show-prefix");
        await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "--quiet", "HEAD");
        await CompareAsync(cwd, Expect.Answered, "symbolic-ref", "--quiet", "HEAD");
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
            // Unpeeled: a tag ref names its tag object, packed or loose. Another reading of the name (here
            // refs/heads/refs/tags/light) makes git warn or resolve it otherwise: git answers.
            await CompareAsync(cwd, Expect.Answered, "rev-parse", "--verify", "refs/tags/annotated");
            await CompareAsync(cwd, Expect.Answered, "rev-parse", "refs/tags/light");
        }
        await fixture.RequiredAsync(fixture.Repository, "update-ref", "refs/heads/refs/tags/light", fixture.SeedSha);
        await fixture.RequiredAsync(fixture.Repository, "update-ref", "refs/heads/refs/heads/ghost", fixture.SeedSha);
        foreach (var cwd in new[] { fixture.Repository, fixture.Source })
        {
            await CompareAsync(cwd, Expect.Fallback, "rev-parse", "refs/tags/light");
            await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--verify", "refs/tags/light");
            await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--verify", "--quiet", "refs/heads/ghost");
            await CompareAsync(cwd, Expect.Fallback, "rev-parse", "--verify", "refs/tags/light^{commit}");
            await CompareAsync(cwd, Expect.Answered, "show-ref", "--verify", "--hash", fixture.TargetRef);
        }
        await CompareAsync(fixture.Source, Expect.Answered, "symbolic-ref", "-q", "HEAD");
        await CompareAsync(fixture.Source, Expect.Answered, "rev-parse", "HEAD");
        await CompareAsync(fixture.Source, Expect.Answered, "rev-parse", "--absolute-git-dir");
    }

    // for-each-ref's formats (with a pattern, a bare prefix, or none), a direct and a symbolic ref, a tag; a symref chain
    // or a dangling symref is git's for %(symref).
    [Test]
    public async Task For_each_ref_formats_match_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Repository, "update-ref", "refs/antiphon/salvage/direct", fixture.SeedSha);
        await fixture.RequiredAsync(fixture.Repository, "symbolic-ref", "refs/antiphon/salvage/sym", fixture.TargetRef);
        await fixture.RequiredAsync(fixture.Repository, "tag", "-a", "annotated", "-m", "annotated", fixture.SeedSha);
        string[] formats = ["--format=%(refname) %(objectname)", "--format=%(objectname) %(refname)", "--format=%(refname)",
            "--format=%(refname) %(objectname) %(symref)"];
        foreach (var cwd in new[] { fixture.Repository, fixture.Source })
        foreach (var format in formats)
        {
            await CompareAsync(cwd, Expect.Answered, "for-each-ref", format);
            await CompareAsync(cwd, Expect.Answered, "for-each-ref", format, "refs/antiphon/salvage/");
            await CompareAsync(cwd, Expect.Answered, "for-each-ref", format, "refs/antiphon/salvage");
            await CompareAsync(cwd, Expect.Answered, "for-each-ref", format, "refs/heads/", "refs/tags/");
            await CompareAsync(cwd, Expect.Answered, "for-each-ref", format, "refs/antiphon/none/");
        }
        // The slot pool's per-worktree listing in a linked worktree: empty is answered, any such ref is git's.
        string[] perWorktree = ["for-each-ref", "--format=%(refname)", "refs/worktree/", "refs/bisect/"];
        await CompareAsync(fixture.Source, Expect.Answered, perWorktree);
        await fixture.RequiredAsync(fixture.Source, "update-ref", "refs/bisect/bad", fixture.SeedSha);
        await CompareAsync(fixture.Source, Expect.Fallback, perWorktree);
        await CompareAsync(fixture.Source, Expect.Fallback, "for-each-ref", "--format=%(refname)");
        await fixture.RequiredAsync(fixture.Source, "update-ref", "-d", "refs/bisect/bad");
        await fixture.RequiredAsync(fixture.Repository, "symbolic-ref", "refs/antiphon/salvage/chain", "refs/antiphon/salvage/sym");
        await CompareAsync(fixture.Repository, Expect.Fallback, "for-each-ref", "--format=%(refname) %(objectname) %(symref)",
            "refs/antiphon/salvage/");
        await fixture.RequiredAsync(fixture.Repository, "update-ref", "-d", "--no-deref", "refs/antiphon/salvage/chain");
        await fixture.RequiredAsync(fixture.Repository, "symbolic-ref", "refs/antiphon/salvage/dangling", "refs/heads/no-such-branch");
        foreach (var format in formats)
            await CompareAsync(fixture.Repository, Expect.Fallback, "for-each-ref", format, "refs/antiphon/salvage/");

        // Last, as it packs every ref: a loose ref spelled otherwise beside a packed one where the file system ignores
        // case. git lists both values, libgit2 would read the loose file for both: git's. A by-name read is the
        // loose file's value in git too, so it stays answered (and equal).
        // Gated as the production check is: on the refs folder itself (a case-insensitive mount may sit under a
        // case-sensitive root).
        var common = (await fixture.RequiredAsync(fixture.Repository, "rev-parse", "--path-format=absolute", "--git-common-dir")).Trim();
        if (Directory.Exists(Path.Combine(common, "REFS")))
        {
            await fixture.RequiredAsync(fixture.Repository, "update-ref", "refs/antiphon/case/foo", fixture.SeedSha);
            await fixture.RequiredAsync(fixture.Repository, "pack-refs", "--all");
            Directory.CreateDirectory(Path.Combine(common, "refs", "antiphon", "case"));
            await File.WriteAllTextAsync(Path.Combine(common, "refs", "antiphon", "case", "Foo"),
                (await fixture.RequiredAsync(fixture.Repository, "rev-parse", fixture.TargetRef)).Trim() + "\n");
            await CompareAsync(fixture.Repository, Expect.Fallback, "for-each-ref", "--format=%(refname) %(objectname)", "refs/antiphon/case/");
            await CompareAsync(fixture.Repository, Expect.Fallback, "for-each-ref", "--format=%(refname)", "refs/heads/");
            await CompareAsync(fixture.Repository, Expect.Answered, "show-ref", "--verify", "--hash", "refs/antiphon/case/foo");
            await CompareAsync(fixture.Repository, Expect.Answered, "rev-parse", "--verify", "refs/antiphon/case/foo");
        }
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
        // Below the root the prefix is a path, git's to spell.
        await CompareAsync(Path.Combine(fixture.Repository, "sub"), Expect.Fallback, "rev-parse", "--show-prefix");
    }

    [Test]
    public async Task Symlinked_refs_are_git_s_to_read()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var repo = fixture.Repository;
        var gitDir = Path.Combine(repo, ".git");
        var branch = (await fixture.RequiredAsync(repo, "symbolic-ref", "HEAD")).Trim();
        await fixture.RequiredAsync(repo, "branch", "via-link");
        // HEAD a symref to refs/heads/via-link, a link whose text names the checked-out branch (the shape
        // core.preferSymlinkRefs writes: git reads that text as a symref; libgit2 reads the file it reaches).
        // Relative to its own directory the link reaches nothing: where libgit2 then reads the ref as missing, the
        // target's RefFileIsLink check is what makes this fall back; on Windows libgit2 fails on it either way.
        var link = Path.Combine(gitDir, "refs", "heads", "via-link");
        File.Delete(link);
        try { File.CreateSymbolicLink(link, branch.Replace('/', Path.DirectorySeparatorChar)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Skip.Test("symbolic links cannot be created here");
        }
        await fixture.RequiredAsync(repo, "symbolic-ref", "HEAD", "refs/heads/via-link");
        await CompareAsync(repo, Expect.Fallback, "symbolic-ref", "--quiet", "HEAD");
        await CompareAsync(repo, Expect.Fallback, "symbolic-ref", "-q", "HEAD");
        // HEAD itself a link (core.preferSymlinkRefs' shape).
        await fixture.RequiredAsync(repo, "symbolic-ref", "HEAD", branch);
        File.Delete(Path.Combine(gitDir, "HEAD"));
        File.CreateSymbolicLink(Path.Combine(gitDir, "HEAD"), branch.Replace('/', Path.DirectorySeparatorChar));
        await CompareAsync(repo, Expect.Fallback, "symbolic-ref", "--quiet", "HEAD");
    }

    [Test]
    public async Task Head_reads_match_git_when_unborn_detached_or_read_otherwise()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var repo = fixture.Repository;
        var head = (await fixture.RequiredAsync(repo, "rev-parse", "HEAD")).Trim();
        string[][] reads =
        [
            ["rev-parse", "HEAD"], ["rev-parse", "--verify", "HEAD^{commit}"], ["--no-optional-locks", "rev-parse", "HEAD"],
            ["rev-parse", "--verify", "--quiet", "HEAD"], ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"],
        ];
        // Another rule's ref named HEAD, loose or packed, broken or dangling: git warns on stderr (of ambiguity
        // unless --quiet, of a broken or dangling ref even under --quiet), so every HEAD read is git's.
        var tagHead = Path.Combine(repo, ".git", "refs", "tags", "HEAD");
        foreach (var other in new[] { "refs/HEAD", "refs/tags/HEAD", "refs/heads/HEAD", "refs/remotes/HEAD", "refs/remotes/HEAD/HEAD" })
        {
            await fixture.RequiredAsync(repo, "update-ref", other, head);
            foreach (var args in reads) await CompareAsync(repo, Expect.Fallback, args);
            await fixture.RequiredAsync(repo, "update-ref", "-d", other);
        }
        await fixture.RequiredAsync(repo, "update-ref", "refs/tags/HEAD", head);
        await fixture.RequiredAsync(repo, "pack-refs", "--all");
        foreach (var args in reads) await CompareAsync(repo, Expect.Fallback, args);
        await fixture.RequiredAsync(repo, "update-ref", "-d", "refs/tags/HEAD");
        await File.WriteAllTextAsync(tagHead, "garbage\n");
        foreach (var args in reads) await CompareAsync(repo, Expect.Fallback, args);
        (await GitWithErrorAsync(repo, "rev-parse", "--verify", "--quiet", "HEAD")).Error.ShouldContain("ignoring broken ref");
        File.Delete(tagHead);
        await fixture.RequiredAsync(repo, "symbolic-ref", "refs/heads/HEAD", "refs/heads/deleted-branch");
        foreach (var args in reads) await CompareAsync(repo, Expect.Fallback, args);
        (await GitWithErrorAsync(repo, "rev-parse", "--verify", "--quiet", "HEAD")).Error.ShouldContain("ignoring dangling symref");
        File.Delete(Path.Combine(repo, ".git", "refs", "heads", "HEAD"));
        foreach (var args in reads) await CompareAsync(repo, Expect.Answered, args);
        // The silent path itself answers (CompareAsync checks stderr only where it does).
        InProcessGitReads.TryAnswerSilently(repo, ["rev-parse", "--verify", "--quiet", "HEAD"]).ShouldNotBeNull();
        InProcessGitReads.TryAnswerSilently(repo, ["rev-parse", "--show-prefix"]).ShouldNotBeNull();

        // Detached: symbolic-ref's silent 1, HEAD still a commit.
        await fixture.RequiredAsync(repo, "checkout", "--quiet", "--detach");
        await CompareAsync(repo, Expect.Answered, "symbolic-ref", "--quiet", "HEAD");
        InProcessGitReads.TryAnswerSilently(repo, ["symbolic-ref", "--quiet", "HEAD"]).ShouldBe((1, ""));
        await CompareAsync(repo, Expect.Answered, "rev-parse", "--verify", "--quiet", "HEAD");
        await CompareAsync(repo, Expect.Answered, "rev-parse", "--show-prefix");

        // Unborn: HEAD names a branch no ref holds, a silent 1; a loose file of that name, even broken, is git's.
        var unborn = Path.Combine(fixture.Root, "unborn");
        Directory.CreateDirectory(unborn);
        await fixture.RequiredAsync(unborn, "init", "--quiet", "-b", "trunk");
        await CompareAsync(unborn, Expect.Answered, "rev-parse", "--verify", "--quiet", "HEAD");
        InProcessGitReads.TryAnswerSilently(unborn, ["rev-parse", "--verify", "--quiet", "HEAD"]).ShouldBe((1, ""));
        await CompareAsync(unborn, Expect.Answered, "symbolic-ref", "--quiet", "HEAD");
        await CompareAsync(unborn, Expect.Answered, "rev-parse", "--show-prefix");
        // After an unborn HEAD git reads the other rules: an annotated tag named HEAD is its answer.
        await fixture.RequiredAsync(repo, "tag", "--annotate", "--message", "to be named HEAD", "head-tag", head);
        await fixture.RequiredAsync(unborn, "fetch", "--quiet", repo, "refs/tags/head-tag:refs/tags/HEAD");
        await CompareAsync(unborn, Expect.Fallback, "rev-parse", "--verify", "--quiet", "HEAD");
        var tagObject = (await fixture.RequiredAsync(repo, "rev-parse", "refs/tags/head-tag")).Trim();
        (await GitAsync(unborn, "rev-parse", "--verify", "--quiet", "HEAD")).ShouldBe(new LandingGitResult(0, tagObject + "\n", ""));
        File.Delete(Path.Combine(unborn, ".git", "refs", "tags", "HEAD"));
        await File.WriteAllTextAsync(Path.Combine(unborn, ".git", "refs", "heads", "trunk"), "not a ref\n");
        await CompareAsync(unborn, Expect.Fallback, "rev-parse", "--verify", "--quiet", "HEAD");
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

        // Inside the git directory, in a bare repository, or in a work tree moved by config, the prefix is git's.
        await CompareAsync(Path.Combine(repo, ".git"), Expect.Fallback, "rev-parse", "--show-prefix");
        await CompareAsync(fixture.Remote, Expect.Fallback, "rev-parse", "--show-prefix");
        // A work tree moved by config is git's.
        await fixture.RequiredAsync(repo, "config", "core.worktree", repo);
        await CompareAsync(repo, Expect.Fallback, "rev-parse", "--is-inside-work-tree");
        await CompareAsync(repo, Expect.Fallback, "rev-parse", "--show-prefix");
        await CompareAsync(repo, Expect.Fallback, "worktree", "list", "--porcelain");
    }

    /// <summary>
    /// A git child's whole environment for the remote get-url cases: this process's, with no system config, an empty
    /// global file and an empty XDG directory (what the libgit2 isolation reads), then <paramref name="changes"/>.
    /// </summary>
    private static Dictionary<string, string?> RemoteChild(LandingGitFixture fixture, params (string Name, string? Value)[] changes)
    {
        var env = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string?)e.Value;
        var emptyGlobal = Path.Combine(fixture.Root, "home", "remote-url-empty-global");
        if (!File.Exists(emptyGlobal)) File.WriteAllText(emptyGlobal, "");
        env["GIT_CONFIG_NOSYSTEM"] = "1";
        env["GIT_CONFIG_GLOBAL"] = emptyGlobal;
        env["XDG_CONFIG_HOME"] = LibGit2ConfigIsolation.EmptyDirectory;
        foreach (var (name, value) in changes) env[name] = value;
        return env;
    }

    /// <summary>git run with exactly <paramref name="env"/>: its result and stderr.</summary>
    private static async Task<(LandingGitResult Result, string Error)> GitInAsync(string cwd, IReadOnlyDictionary<string, string?> env,
        params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Clear();
        foreach (var (name, value) in env) if (value is not null) start.Environment[name] = value;
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (new LandingGitResult(p.ExitCode, await output, ""), await error);
    }

    private static async Task ConfigAsync(string cwd, IReadOnlyDictionary<string, string?> env, params string[] args)
    {
        var (result, error) = await GitInAsync(cwd, env, args);
        result.ExitCode.ShouldBe(0, string.Join(' ', args) + ": " + error);
    }

    /// <summary>
    /// <c>remote get-url &lt;name&gt;</c> in process against git under the same child environment: answered means
    /// git's exit 0, byte-identical stdout and an empty stderr. Returns git's own result (for cases that show git's
    /// answer would differ from the plain local value). Never answered without a child environment.
    /// </summary>
    private static async Task<(LandingGitResult Result, string Error)> CompareRemoteUrlAsync(string cwd, Expect expect,
        IReadOnlyDictionary<string, string?> env, string name)
    {
        string[] args = ["remote", "get-url", name];
        var shape = "remote get-url " + name + " @ " + cwd;
        var answer = InProcessGitReads.TryAnswerRemoteUrl(cwd, args, env);
        var silent = InProcessGitReads.TryAnswerSilently(cwd, args, env);
        (silent is null).ShouldBe(answer is null, shape);
        InProcessGitReads.TryAnswer(cwd, args).ShouldBeNull("never without the child's environment: " + shape);
        InProcessGitReads.TryAnswerSilently(cwd, args).ShouldBeNull("never without the child's environment: " + shape);
        (InProcessGitReads.TryAnswerRemoteUrl(cwd, ["--no-optional-locks", .. args], env)?.Output).ShouldBe(answer?.Output, shape);
        var git = await GitInAsync(cwd, env, args);
        if (expect == Expect.Fallback) { answer.ShouldBeNull("must fall back to git: " + shape); return git; }
        answer.ShouldNotBeNull("must be answered in process: " + shape);
        answer.ExitCode.ShouldBe(git.Result.ExitCode, shape);
        answer.Output.ShouldBe(git.Result.Output, shape);
        silent!.Value.Output.ShouldBe(git.Result.Output, shape);
        git.Error.ShouldBeEmpty("git wrote to stderr: " + shape);
        return git;
    }

    [Test]
    public async Task Remote_get_url_of_one_local_url_matches_git()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var env = RemoteChild(fixture);
        // origin is the bare remote's Windows path: backslashes escaped in the file, printed as written.
        var (origin, _) = await CompareRemoteUrlAsync(fixture.Repository, Expect.Answered, env, "origin");
        origin.Output.ShouldBe(fixture.Remote + "\n");
        // A linked worktree reads the common directory's config.
        await CompareRemoteUrlAsync(fixture.Source, Expect.Answered, env, "origin");
        await CompareRemoteUrlAsync(fixture.Observer, Expect.Answered, env, "origin");
        // Spaces, quotes, comment characters and backslashes survive the file's quoting and escaping.
        await ConfigAsync(fixture.Repository, env, "remote", "add", "odd", "https://example.invalid/a b;c#d\"e\\f'g");
        var (odd, _) = await CompareRemoteUrlAsync(fixture.Repository, Expect.Answered, env, "odd");
        odd.Output.ShouldBe("https://example.invalid/a b;c#d\"e\\f'g\n");
        await File.AppendAllTextAsync(Path.Combine(fixture.Repository, ".git", "config"),
            "[remote \"hand\"]\n\turl = \"ssh://h/x y\"   ; trailing comment\n\tfetch = +refs/heads/*:refs/remotes/hand/*\n");
        await CompareRemoteUrlAsync(fixture.Repository, Expect.Answered, env, "hand");
        // Auto-maintenance keys injected through GIT_CONFIG_COUNT (as the runners add them) decide no URL.
        var maintenance = RemoteChild(fixture, ("GIT_CONFIG_COUNT", "2"), ("GIT_CONFIG_KEY_0", "gc.auto"), ("GIT_CONFIG_VALUE_0", "0"),
            ("GIT_CONFIG_KEY_1", "maintenance.auto"), ("GIT_CONFIG_VALUE_1", "false"));
        await CompareRemoteUrlAsync(fixture.Repository, Expect.Answered, maintenance, "origin");
    }

    [Test]
    public async Task Remote_get_url_falls_back_for_repository_config_git_reads_otherwise()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var repo = fixture.Repository;
        var gitDir = Path.Combine(repo, ".git");
        var config = Path.Combine(gitDir, "config");
        var env = RemoteChild(fixture);
        await ConfigAsync(repo, env, "remote", "add", "plain", "https://example.invalid/plain.git");
        await CompareRemoteUrlAsync(repo, Expect.Answered, env, "plain");

        // Names: missing (git's "No such remote", exit 2, on stderr), another case, options and patterns.
        var (missing, missingError) = await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "nope");
        missing.ExitCode.ShouldBe(2);
        missingError.ShouldNotBeEmpty();
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "PLAIN");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "-plain");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "pl*");
        InProcessGitReads.TryAnswerRemoteUrl(repo, ["remote", "get-url", "--push", "plain"], env).ShouldBeNull();
        InProcessGitReads.TryAnswerRemoteUrl(repo, ["remote", "get-url", "--all", "plain"], env).ShouldBeNull();

        // Two values: git prints the first.
        await ConfigAsync(repo, env, "config", "--add", "remote.plain.url", "https://example.invalid/second.git");
        var (two, _) = await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "plain");
        two.Output.ShouldBe("https://example.invalid/plain.git\n");
        // An empty value resets git's list: git then prints the name itself.
        await ConfigAsync(repo, env, "remote", "add", "emptied", "https://example.invalid/emptied.git");
        await File.AppendAllTextAsync(config, "[remote \"emptied\"]\n\turl =\n");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "emptied");
        await File.AppendAllTextAsync(config, "[remote \"blank\"]\n\turl =\n");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "blank");

        // insteadOf rewrites the URL git prints.
        await ConfigAsync(repo, env, "remote", "add", "rewritten", "https://example.invalid/r.git");
        await CompareRemoteUrlAsync(repo, Expect.Answered, env, "rewritten");
        await ConfigAsync(repo, env, "config", "url.https://mirror.invalid/.insteadOf", "https://example.invalid/");
        var (rewritten, _) = await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "rewritten");
        rewritten.Output.ShouldBe("https://mirror.invalid/r.git\n");
        await ConfigAsync(repo, env, "config", "--remove-section", "url.https://mirror.invalid/");
        await CompareRemoteUrlAsync(repo, Expect.Answered, env, "rewritten");

        // include / includeIf of a file only git finds (~ is the child's HOME, which libgit2 does not read): git's
        // first URL comes from the included file, ahead of the local one.
        var home = Path.Combine(fixture.Root, "child-home");
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "remotes.inc"), "[remote \"rewritten\"]\n\turl = https://included.invalid/r.git\n");
        var withHome = RemoteChild(fixture, ("HOME", home));
        var original = await File.ReadAllTextAsync(config);
        await File.WriteAllTextAsync(config, "[include]\n\tpath = ~/remotes.inc\n" + original);
        var (included, _) = await CompareRemoteUrlAsync(repo, Expect.Fallback, withHome, "rewritten");
        included.Output.ShouldBe("https://included.invalid/r.git\n");
        var gitDirPattern = gitDir.Replace('\\', '/'); // no trailing slash: that would mean <gitdir>/**
        await File.WriteAllTextAsync(config, $"[includeIf \"gitdir/i:{gitDirPattern}\"]\n\tpath = ~/remotes.inc\n" + original);
        var (conditional, _) = await CompareRemoteUrlAsync(repo, Expect.Fallback, withHome, "rewritten");
        conditional.Output.ShouldBe("https://included.invalid/r.git\n");
        await File.WriteAllTextAsync(config, original);
        await CompareRemoteUrlAsync(repo, Expect.Answered, withHome, "rewritten");

        // Legacy remotes/<name> and branches/<name> files, in the git directory or (from a linked worktree) the common one.
        foreach (var legacy in new[] { "remotes", "branches" })
        {
            var file = Path.Combine(gitDir, legacy, "rewritten");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, legacy == "remotes" ? "URL: https://legacy.invalid/r.git\n" : "https://legacy.invalid/r.git\n");
            await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "rewritten");
            await CompareRemoteUrlAsync(fixture.Source, Expect.Fallback, env, "rewritten");
            File.Delete(file);
        }
        await CompareRemoteUrlAsync(repo, Expect.Answered, env, "rewritten");

        // Per-worktree config: a worktree-level remote.<name>.url comes after the local one, and config.worktree
        // (with extensions.worktreeConfig) is git's to merge, for the main and a linked worktree alike.
        await ConfigAsync(repo, env, "config", "extensions.worktreeConfig", "true");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "rewritten");
        await ConfigAsync(repo, env, "config", "--worktree", "remote.wt.url", "https://worktree.invalid/wt.git");
        File.Exists(Path.Combine(gitDir, "config.worktree")).ShouldBeTrue();
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "wt");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "rewritten");
        await CompareRemoteUrlAsync(fixture.Source, Expect.Fallback, env, "rewritten");
        await ConfigAsync(repo, env, "config", "--unset", "extensions.worktreeConfig");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, env, "rewritten"); // config.worktree still there
        File.Delete(Path.Combine(gitDir, "config.worktree"));
        await CompareRemoteUrlAsync(repo, Expect.Answered, env, "rewritten");
    }

    [Test]
    public async Task Remote_get_url_falls_back_for_config_only_the_child_reads()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var repo = fixture.Repository;
        var env = RemoteChild(fixture);
        await CompareRemoteUrlAsync(repo, Expect.Answered, env, "origin");

        // A global file the child sees through GIT_CONFIG_GLOBAL: its remote comes first in git's order.
        var global = Path.Combine(fixture.Root, "home", "remote-url-global");
        await File.WriteAllTextAsync(global, "[remote \"origin\"]\n\turl = https://global.invalid/origin.git\n");
        var globalEnv = RemoteChild(fixture, ("GIT_CONFIG_GLOBAL", global));
        var (fromGlobal, _) = await CompareRemoteUrlAsync(repo, Expect.Fallback, globalEnv, "origin");
        fromGlobal.Output.ShouldBe("https://global.invalid/origin.git\n");
        // ... and a remote defined only there: no local URL at all (git: not configured in the repository, exit 2).
        await File.WriteAllTextAsync(global, "[remote \"onlyglobal\"]\n\turl = https://global.invalid/only.git\n");
        var (onlyGlobal, onlyGlobalError) = await CompareRemoteUrlAsync(repo, Expect.Fallback, globalEnv, "onlyglobal");
        onlyGlobal.ExitCode.ShouldBe(2);
        onlyGlobalError.ShouldNotBeEmpty();
        // The same file rewriting URLs; edited after the child's view was cached, so the stamp must catch it.
        await File.WriteAllTextAsync(global, "[url \"https://mirror.invalid/\"]\n\tinsteadOf = " + fixture.Remote[..2] + "\n");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, globalEnv, "origin");
        await File.WriteAllTextAsync(global, "[include]\n\tpath = nowhere.inc\n");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, globalEnv, "origin");
        await File.WriteAllTextAsync(global, "[core]\n\tautocrlf = false\n");
        await CompareRemoteUrlAsync(repo, Expect.Answered, globalEnv, "origin");

        // Config injected through the environment.
        var injected = RemoteChild(fixture, ("GIT_CONFIG_COUNT", "1"), ("GIT_CONFIG_KEY_0", "remote.origin.url"),
            ("GIT_CONFIG_VALUE_0", "https://command.invalid/origin.git"));
        await CompareRemoteUrlAsync(repo, Expect.Fallback, injected, "origin");
        await CompareRemoteUrlAsync(repo, Expect.Fallback,
            RemoteChild(fixture, ("GIT_CONFIG_PARAMETERS", "'url.https://p.invalid/.insteadof'='x'")), "origin");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, RemoteChild(fixture, ("GIT_CONFIG", global)), "origin");
        await CompareRemoteUrlAsync(repo, Expect.Fallback, RemoteChild(fixture, ("GIT_CONFIG_COUNT", "x")), "origin");
        // A system file the child reads (GIT_CONFIG_SYSTEM, NOSYSTEM cleared) with a rewrite.
        var systemFile = Path.Combine(fixture.Root, "home", "remote-url-system");
        await File.WriteAllTextAsync(systemFile, "[url \"https://mirror.invalid/\"]\n\tinsteadOf = x\n");
        await CompareRemoteUrlAsync(repo, Expect.Fallback,
            RemoteChild(fixture, ("GIT_CONFIG_NOSYSTEM", null), ("GIT_CONFIG_SYSTEM", systemFile)), "origin");
        // A child pointed at another repository.
        await CompareRemoteUrlAsync(repo, Expect.Fallback, RemoteChild(fixture, ("GIT_DIR", Path.Combine(fixture.Observer, ".git"))), "origin");
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
