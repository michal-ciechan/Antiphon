using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1704 S1 (K-1..K-14): the typed reads answer exactly as git does over real repository shapes. git run
/// directly on the same repository is the oracle. S1 runs <c>Cli</c> only; S2 adds <c>LibGit2</c> cases.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class C1704ContractTests
{
    private static C1704Corpus _corpus = null!;

    [Before(Class)]
    public static async Task BuildCorpus() => _corpus = await C1704Corpus.CreateAsync();

    [After(Class)]
    public static async Task DropCorpus() => await _corpus.DisposeAsync();

    private static ILandingGit Git(string backend) => backend == LandGitBackendNames.Cli
        ? _corpus.Landing()
        : throw new NotSupportedException("backend " + backend + " is not built in S1");

    private static string Repo(string name) => _corpus.Path_(name);

    private static void SameAsGit(LandingGitResult typed, (int Exit, byte[] Stdout, string Text, string Error) git, string what)
    {
        typed.ExitCode.ShouldBe(git.Exit, what);
        Encoding.UTF8.GetBytes(typed.Output).ShouldBe(git.Stdout, what + " bytes");
    }

    /// <summary>K-1 (G-27, G-30): commits peel tags, missing is Missing, names are never trimmed.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli", "plain", "HEAD", "Found")]
    [Arguments("Cli", "plain", "v1", "Found")]
    [Arguments("Cli", "plain", "l1", "Found")]
    [Arguments("Cli", "detached", "HEAD", "Found")]
    [Arguments("Cli", "empty", "HEAD", "Missing")]
    [Arguments("Cli", "shallow", "HEAD", "Found")]
    [Arguments("Cli", "plain", "feat/x\n", "Missing")]
    public async Task Commit_resolution(string backend, string repo, string rev, string expected, CancellationToken ct)
    {
        var read = await Git(backend).ReadCommitAsync(Repo(repo), rev, ct);
        var oracle = _corpus.Git(Repo(repo), "rev-parse", "--verify", rev + "^{commit}");
        SameAsGit(read.Result, oracle, rev);
        if (expected == "Missing")
        {
            read.Sha.ShouldBeNull();
            if (rev.EndsWith('\n'))
                _corpus.Git(Repo(repo), "rev-parse", "--verify", rev.TrimEnd() + "^{commit}").Exit.ShouldBe(0, "the trimmed name exists, so trimming would answer");
            return;
        }
        read.Sha.ShouldBe(oracle.Text.Trim());
        if (rev == "v1")
            read.Sha.ShouldNotBe(_corpus.Git(Repo(repo), "rev-parse", "v1").Text.Trim(), "an annotated tag peels to its commit");
    }

    /// <summary>K-2 (G-26): Exists / Missing / Error equal git's verdict; an error is never absence.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli", "plain", "refs/heads/master", "Exists")]
    [Arguments("Cli", "plain", "refs/heads/c1704-missing", "Missing")]
    [Arguments("Cli", "packed", "refs/heads/packed-only", "Exists")]
    [Arguments("Cli", "packed", "refs/heads/feat/x", "Exists")]
    [Arguments("Cli", "ref-lock", "refs/heads/feat/x", "Oracle")]
    [Arguments("Cli", "corrupt", "refs/heads/broken", "Oracle")]
    public async Task Ref_tristate(string backend, string repo, string fullRef, string expected, CancellationToken ct)
    {
        var git = Git(backend);
        var read = await git.ReadRefAsync(Repo(repo), fullRef, ct);
        var oracle = _corpus.Git(Repo(repo), "show-ref", "--exists", fullRef);
        SameAsGit(read.Result, oracle, fullRef);
        var verdict = oracle.Exit switch { 0 => GitRefState.Exists, 2 => GitRefState.Missing, _ => GitRefState.Error };
        read.State.ShouldBe(verdict);
        if (expected != "Oracle") read.State.ToString().ShouldBe(expected);
        if (oracle.Exit != 2) read.State.ShouldNotBe(GitRefState.Missing, "an error is never absence");
        if (repo == "packed" && fullRef == "refs/heads/feat/x")
        {
            var loose = _corpus.Git(Repo(repo), "rev-parse", "HEAD").Text.Trim();
            (await git.ReadRefsAsync(Repo(repo), [fullRef], ct))[fullRef].ShouldBe(loose, "the loose ref wins over packed, as in git");
        }
    }

    /// <summary>K-3 (G-28): HEAD is symbolic, detached (exit 1, not an error) or unborn-symbolic.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli", "plain", "refs/heads/master")]
    [Arguments("Cli", "detached", "Detached")]
    [Arguments("Cli", "empty", "refs/heads/master")]
    public async Task Symbolic_head(string backend, string repo, string expected, CancellationToken ct)
    {
        var read = await Git(backend).ReadSymbolicRefAsync(Repo(repo), "HEAD", ct);
        SameAsGit(read.Result, _corpus.Git(Repo(repo), "symbolic-ref", "-q", "HEAD"), repo);
        if (expected == "Detached")
        {
            read.NotSymbolic.ShouldBeTrue();
            read.Target.ShouldBeNull();
        }
        else read.Target.ShouldBe(expected);
    }

    /// <summary>K-4 (G-31): ancestry true, false, unrelated, and a missing object is an error, never false.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli", "true")]
    [Arguments("Cli", "false")]
    [Arguments("Cli", "unrelated")]
    [Arguments("Cli", "missing")]
    public async Task Ancestry(string backend, string shape, CancellationToken ct)
    {
        var repo = Repo("plain");
        string Rev(string r) => _corpus.Git(repo, "rev-parse", r).Text.Trim();
        var (ancestor, descendant) = shape switch
        {
            "true" => (Rev("HEAD~2"), Rev("HEAD")),
            "false" => (Rev("HEAD"), Rev("HEAD~2")),
            "unrelated" => (Rev("orphan"), Rev("HEAD")),
            _ => ("1234567890123456789012345678901234567890", Rev("HEAD")),
        };
        var read = await Git(backend).ReadAncestryAsync(repo, ancestor, descendant, ct);
        SameAsGit(read.Result, _corpus.Git(repo, "merge-base", "--is-ancestor", ancestor, descendant), shape);
        read.IsAncestor.ShouldBe(shape switch { "true" => true, "missing" => (bool?)null, _ => false });
    }

    /// <summary>K-5: the administrative directory is git's own spelling, byte for byte.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli", "main")]
    [Arguments("Cli", "linked")]
    [Arguments("Cli", "relative")]
    [Arguments("Cli", "symlink")]
    public async Task Git_directory(string backend, string shape, CancellationToken ct)
    {
        var path = shape switch
        {
            "main" => Repo("plain"),
            "linked" => Repo("wt-live"),
            "relative" => Repo("wt-relative"),
            _ => Link(Repo("plain")),
        };
        var read = await Git(backend).ReadGitDirectoryAsync(path, ct);
        var oracle = _corpus.Git(path, "rev-parse", "--absolute-git-dir");
        oracle.Exit.ShouldBe(0, shape);
        SameAsGit(read.Result, oracle, shape);
        read.Path.ShouldBe(oracle.Text.Trim());
    }

    private static string Link(string target)
    {
        var link = _corpus.Path_("link-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateSymbolicLink(link, target);
        return link;
    }

    /// <summary>K-6 (G-29): ls-tree -z bytes are never trimmed (names with a trailing newline and a tab).</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Tree_bytes_are_untrimmed(string backend, CancellationToken ct)
    {
        var repo = Repo("odd-names");
        var head = _corpus.Git(repo, "rev-parse", "HEAD").Text.Trim();
        var read = await Git(backend).ReadTreeZAsync(repo, head, ct);
        var oracle = _corpus.Git(repo, "ls-tree", "-r", "-z", head);
        SameAsGit(read.Result, oracle, "ls-tree");
        read.Output!.ShouldContain("\ttrailing\n\0");
        read.Output.ShouldContain("\ttab\tname\0");
    }

    /// <summary>K-7 (G-29): diff-tree -z bytes are never trimmed.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Diff_tree_bytes_are_untrimmed(string backend, CancellationToken ct)
    {
        var repo = Repo("odd-names");
        var from = _corpus.Git(repo, "rev-parse", "HEAD~1").Text.Trim();
        var to = _corpus.Git(repo, "rev-parse", "HEAD").Text.Trim();
        var read = await Git(backend).ReadDiffTreeZAsync(repo, from, to, ct);
        var oracle = _corpus.Git(repo, "diff-tree", "--no-commit-id", "--name-only", "-r", "-z", from, to);
        SameAsGit(read.Result, oracle, "diff-tree");
        read.Output!.ShouldEndWith("\0");
        read.Output.ShouldContain("trailing\n\0");
    }

    /// <summary>K-8: config --get: a value, a missing key (exit 1), a multi-valued key (git's own exit).</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Config_get(string backend, CancellationToken ct)
    {
        var repo = Repo("plain");
        var git = Git(backend);
        foreach (var key in new[] { "c1704.key", "c1704.unset", "c1704.multi" })
        {
            var read = await git.ReadConfigAsync(repo, key, ct);
            var oracle = _corpus.Git(repo, "config", "--get", key);
            SameAsGit(read.Result, oracle, key);
            read.Missing.ShouldBe(oracle.Exit == 1, key);
        }
        (await git.ReadConfigAsync(repo, "c1704.key", ct)).Value.ShouldBe("value one");
    }

    /// <summary>K-9: status porcelain bytes equal git's, with text=auto and eol=lf files dirty.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Status_bytes(string backend, CancellationToken ct)
    {
        var repo = _corpus.CloneShared("plain");
        await File.WriteAllTextAsync(Path.Combine(repo, ".gitattributes"), "*.txt text=auto\n*.lf eol=lf\n", ct);
        await File.WriteAllTextAsync(Path.Combine(repo, "b.lf"), "one\r\ntwo\r\n", ct);
        _corpus.Required(repo, "add", ".gitattributes", "b.lf");
        _corpus.Required(repo, "commit", "-m", "attributes");
        await File.WriteAllTextAsync(Path.Combine(repo, "a.txt"), "dirty\r\n", ct);
        await File.WriteAllTextAsync(Path.Combine(repo, "b.lf"), "changed\r\n", ct);
        await File.WriteAllTextAsync(Path.Combine(repo, "new.txt"), "untracked\n", ct);
        var git = Git(backend);
        foreach (var untracked in new[] { true, false })
        {
            var read = await git.ReadWorktreeStatusAsync(repo, untracked, ct);
            var oracle = _corpus.Git(repo, "status", "--porcelain=v1", "-z",
                untracked ? "--untracked-files=all" : "--untracked-files=no", "--ignore-submodules=none");
            SameAsGit(read.Result, oracle, "status untracked=" + untracked);
            read.Output!.Contains("new.txt").ShouldBe(untracked);
        }
    }

    /// <summary>K-10: ReadRefsAsync over 5,000 refs keeps request order and stops at the first missing ref.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Refs5k_read_refs_in_order(string backend, CancellationToken ct)
    {
        var repo = Repo("refs5k");
        var refs = Enumerable.Range(0, 200).Select(i => i == 149 ? "refs/heads/bulk/missing" : $"refs/heads/bulk/r{i * 25:D4}").ToList();
        var read = await Git(backend).ReadRefsAsync(repo, refs, ct);
        read.Keys.ShouldBe(refs.Take(149));
        var head = _corpus.Git(repo, "rev-parse", "HEAD").Text.Trim();
        read.Values.ShouldAllBe(v => v == head);
        (await Git(backend).ReadRefAsync(repo, "refs/heads/bulk/r4999", ct)).State.ShouldBe(GitRefState.Exists);
    }

    /// <summary>K-11: no value cache: an external update between two reads is seen.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Concurrent_external_update_is_seen(string backend, CancellationToken ct)
    {
        var repo = _corpus.CloneShared("plain");
        var git = Git(backend);
        using var scope = git.BeginOperationScope();
        var before = await git.ReadCommitAsync(repo, "refs/heads/master", ct);
        (await git.ReadRefAsync(repo, "refs/heads/c1704-new", ct)).State.ShouldBe(GitRefState.Missing);
        var other = _corpus.Git(repo, "rev-parse", "HEAD~2").Text.Trim();
        _corpus.Required(repo, "update-ref", "refs/heads/master", other);
        _corpus.Required(repo, "update-ref", "refs/heads/c1704-new", other);
        var after = await git.ReadCommitAsync(repo, "refs/heads/master", ct);
        before.Sha.ShouldNotBe(after.Sha);
        after.Sha.ShouldBe(other);
        (await git.ReadRefAsync(repo, "refs/heads/c1704-new", ct)).State.ShouldBe(GitRefState.Exists);
    }

    /// <summary>K-12: a repository git refuses (unknown extension) and a partial clone answer exactly like git.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Unknown_extension_and_partial_clone_answer_like_git(string backend, CancellationToken ct)
    {
        var git = Git(backend);
        foreach (var name in new[] { "ext", "partial" })
        {
            var repo = Repo(name);
            var head = _corpus.Git(Repo("plain"), "rev-parse", "HEAD").Text.Trim();
            var older = _corpus.Git(Repo("plain"), "rev-parse", "HEAD~2").Text.Trim();
            SameAsGit((await git.ReadCommitAsync(repo, "HEAD", ct)).Result, _corpus.Git(repo, "rev-parse", "--verify", "HEAD^{commit}"), name + " commit");
            SameAsGit((await git.ReadRefAsync(repo, "refs/heads/master", ct)).Result, _corpus.Git(repo, "show-ref", "--exists", "refs/heads/master"), name + " ref");
            SameAsGit((await git.ReadSymbolicRefAsync(repo, "HEAD", ct)).Result, _corpus.Git(repo, "symbolic-ref", "-q", "HEAD"), name + " symbolic");
            SameAsGit((await git.ReadGitDirectoryAsync(repo, ct)).Result, _corpus.Git(repo, "rev-parse", "--absolute-git-dir"), name + " git-dir");
            SameAsGit((await git.ReadConfigAsync(repo, "core.bare", ct)).Result, _corpus.Git(repo, "config", "--get", "core.bare"), name + " config");
            SameAsGit((await git.ReadAncestryAsync(repo, older, head, ct)).Result, _corpus.Git(repo, "merge-base", "--is-ancestor", older, head), name + " ancestry");
            SameAsGit((await git.ReadTreeZAsync(repo, older, ct)).Result, _corpus.Git(repo, "ls-tree", "-r", "-z", older), name + " tree");
            SameAsGit((await git.ReadDiffTreeZAsync(repo, older, head, ct)).Result,
                _corpus.Git(repo, "diff-tree", "--no-commit-id", "--name-only", "-r", "-z", older, head), name + " diff-tree");
            SameAsGit((await git.ReadWorktreeStatusAsync(repo, false, ct)).Result,
                _corpus.Git(repo, "status", "--porcelain=v1", "-z", "--untracked-files=no", "--ignore-submodules=none"), name + " status");
        }
        _corpus.Git(Repo("ext"), "rev-parse", "HEAD").Exit.ShouldNotBe(0, "git refuses the unknown extension");
    }

    /// <summary>K-13: unicode and punctuation branch names answer as git does.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Unicode_branch_names(string backend, CancellationToken ct)
    {
        var repo = Repo("odd-names");
        var git = Git(backend);
        foreach (var name in new[] { "refs/heads/ü/ñ", "refs/heads/x/@", "refs/heads/a-b_c.d" })
        {
            var read = await git.ReadRefAsync(repo, name, ct);
            SameAsGit(read.Result, _corpus.Git(repo, "show-ref", "--exists", name), name);
            read.State.ShouldBe(GitRefState.Exists, name);
            SameAsGit((await git.ReadSymbolicRefAsync(repo, name, ct)).Result, _corpus.Git(repo, "symbolic-ref", "-q", name), name + " symbolic");
            SameAsGit((await git.ReadCommitAsync(repo, name, ct)).Result, _corpus.Git(repo, "rev-parse", "--verify", name + "^{commit}"), name + " commit");
        }
    }

    /// <summary>K-14: a locked worktree answers as git; a prunable one (directory gone) fails as git does.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("Cli")]
    public async Task Prunable_and_locked_worktrees(string backend, CancellationToken ct)
    {
        var git = Git(backend);
        var locked = Repo("wt-locked");
        SameAsGit((await git.ReadGitDirectoryAsync(locked, ct)).Result, _corpus.Git(locked, "rev-parse", "--absolute-git-dir"), "locked git-dir");
        foreach (var untracked in new[] { true, false })
            SameAsGit((await git.ReadWorktreeStatusAsync(locked, untracked, ct)).Result,
                _corpus.Git(locked, "status", "--porcelain=v1", "-z", untracked ? "--untracked-files=all" : "--untracked-files=no",
                    "--ignore-submodules=none"), "locked status");
        var prunable = Repo("wt-prunable");
        Directory.Exists(prunable).ShouldBeFalse();
        var gitFailure = Record.Exception(() => _corpus.Git(prunable, "rev-parse", "--absolute-git-dir"));
        gitFailure.ShouldNotBeNull("git cannot start in a deleted directory");
        var typedFailure = await Record.ExceptionAsync(() => git.ReadGitDirectoryAsync(prunable, ct));
        typedFailure.ShouldNotBeNull("the typed read fails exactly where git cannot run");
        typedFailure.GetType().ShouldBe(gitFailure.GetType());
    }

    private static class Record
    {
        public static Exception? Exception(Action action)
        {
            try { action(); return null; }
            catch (Exception ex) { return ex; }
        }

        public static async Task<Exception?> ExceptionAsync(Func<Task> action)
        {
            try { await action(); return null; }
            catch (Exception ex) { return ex; }
        }
    }
}
