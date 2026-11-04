using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-1704 S1: an in-process git backend for one repository-lease episode. A land's outer
/// <see cref="ILandingGit.BeginOperationScope"/> owns it and disposes it before the lease is released
/// (<c>LandRepositoryLeaseScope</c>). Each read answers with git's own result shape, or
/// <see cref="GitAnswer{T}.NotHandled"/> so the caller runs today's git command. In S1 production never
/// attaches a session for any backend value.
/// </summary>
public interface IGitSession : IDisposable
{
    string Backend { get; }
    GitAnswer<LandingGitResult> ReadCommit(string repository, string revision);
    GitAnswer<LandingGitResult> ReadAncestry(string repository, string ancestor, string descendant);
    GitAnswer<LandingGitResult> ReadRef(string repository, string fullRef);
    GitAnswer<LandingGitResult> ReadSymbolicRef(string repository, string name);
    GitAnswer<LandingGitResult> ReadGitDirectory(string repository);
    GitAnswer<LandingGitResult> ReadConfig(string repository, string key);
    GitAnswer<LandingGitResult> ReadTreeZ(string repository, string treeish);
    GitAnswer<LandingGitResult> ReadDiffTreeZ(string repository, string from, string to);
    GitAnswer<LandingGitResult> ReadWorktreeStatus(string repository, bool untracked);
}

/// <summary>Handled with git's result, or not handled (the reason is diagnostic only; the CLI runs).</summary>
public readonly record struct GitAnswer<T>(bool Handled, T? Value, string? Reason)
{
    public static GitAnswer<T> Answer(T value) => new(true, value, null);
    public static GitAnswer<T> NotHandled(string reason) => new(false, default, reason);
}

/// <summary>The land git backend names (<c>Landing:GitBackend</c>). Ordinal, untrimmed.</summary>
public static class LandGitBackendNames
{
    public const string Cli = "Cli";
    public const string LibGit2 = "LibGit2";
    public const string Compare = "Compare";

    public static bool IsKnown(string? value) => value is Cli or LibGit2 or Compare;
}

public enum GitOperationKind
{
    /// <summary>A git process with arguments only.</summary>
    Process,
    /// <summary>A git process fed through standard input (<c>check-attr --stdin</c>, <c>cat-file --batch</c>, <c>update-ref --stdin</c>).</summary>
    Stdin,
    /// <summary>Answered without a process (in-process read or a memoised verdict).</summary>
    InProcess,
}

/// <summary>One completed git call. <see cref="ExitCode"/> is null when the call threw (timeout, kill, start failure after the child existed).</summary>
public sealed record GitOperation(GitOperationKind Kind, string Repository, IReadOnlyList<string> Arguments,
    string Backend, TimeSpan Elapsed, int? ExitCode);

/// <summary>
/// CARD-1704 S1: told once per git call <see cref="ILandingGit"/> completes, after the child's journal record is
/// settled. It never changes git: an exception it throws is swallowed and counted.
/// </summary>
public interface IGitOperationObserver
{
    void Completed(GitOperation operation);
}

public sealed class NoGitOperationObserver : IGitOperationObserver
{
    public static NoGitOperationObserver Instance { get; } = new();
    private NoGitOperationObserver() { }
    public void Completed(GitOperation operation) { }
}

/// <summary>The exact argv of each typed read: today's call-site commands, shared by the defaults and <c>LandingGit</c>.</summary>
public static class GitReadArguments
{
    public static string[] Commit(string revision) => ["rev-parse", "--verify", revision + "^{commit}"];
    public static string[] Ancestry(string ancestor, string descendant) => ["merge-base", "--is-ancestor", ancestor, descendant];
    public static string[] Ref(string fullRef) => ["show-ref", "--exists", fullRef];
    public static string[] SymbolicRef(string name) => ["symbolic-ref", "-q", name];
    public static string[] GitDirectory() => ["rev-parse", "--absolute-git-dir"];
    public static string[] Config(string key) => ["config", "--get", key];
    public static string[] TreeZ(string treeish) => ["ls-tree", "-r", "-z", treeish];
    public static string[] DiffTreeZ(string from, string to) => ["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", from, to];
    public static string[] WorktreeStatus(bool untracked) =>
        ["status", "--porcelain=v1", "-z", untracked ? "--untracked-files=all" : "--untracked-files=no", "--ignore-submodules=none"];
}

public enum GitRefState { Exists, Missing, Error }

/// <summary><c>rev-parse --verify &lt;rev&gt;^{commit}</c>: the peeled commit id, or null.</summary>
public sealed record GitCommitRead(LandingGitResult Result)
{
    public string? Sha => Result.Succeeded && GitObjectId.IsFull(Result.Output.Trim()) ? Result.Output.Trim() : null;
}

/// <summary><c>merge-base --is-ancestor</c>: exit 0 true, 1 false, anything else null (an error is never false).</summary>
public sealed record GitAncestryRead(LandingGitResult Result)
{
    public bool? IsAncestor => Result.ExitCode switch { 0 => true, 1 => false, _ => null };
}

/// <summary><c>show-ref --exists</c>: exit 0 Exists, 2 Missing, anything else Error.</summary>
public sealed record GitRefRead(LandingGitResult Result)
{
    public GitRefState State => Result.ExitCode switch { 0 => GitRefState.Exists, 2 => GitRefState.Missing, _ => GitRefState.Error };
}

/// <summary><c>symbolic-ref -q</c>: exit 0 the target, 1 not symbolic (a detached HEAD), anything else an error.</summary>
public sealed record GitSymbolicRefRead(LandingGitResult Result)
{
    public string? Target => Result.Succeeded ? Result.Output.Trim() : null;
    public bool NotSymbolic => Result.ExitCode == 1;
}

/// <summary><c>rev-parse --absolute-git-dir</c>: git's spelling of the administrative directory, or null.</summary>
public sealed record GitDirectoryRead(LandingGitResult Result)
{
    public string? Path => Result.Succeeded && Result.Output.Trim() is { Length: > 0 } path ? path : null;
}

/// <summary><c>config --get</c>: exit 0 the value, 1 missing, anything else an error.</summary>
public sealed record GitConfigRead(LandingGitResult Result)
{
    public string? Value => Result.Succeeded ? Result.Output.TrimEnd('\n') : null;
    public bool Missing => Result.ExitCode == 1;
}

/// <summary>A <c>-z</c> listing (<c>ls-tree</c>, <c>diff-tree</c>, <c>status</c>): <see cref="LandingGitResult.Output"/> untrimmed.</summary>
public sealed record GitListingRead(LandingGitResult Result)
{
    public string? Output => Result.Succeeded ? Result.Output : null;
}
