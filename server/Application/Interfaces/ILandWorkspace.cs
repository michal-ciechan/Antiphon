using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0688 D-3/D-5: one persistent, locked, detached land worktree per repository. The rebase and
/// verification build of every land run there; it is reset before each use, so it is disposable.
/// </summary>
public interface ILandWorkspace
{
    /// <summary><c>&lt;worktree root&gt;/land/&lt;first 12 hex of SHA-256(canonical common dir)&gt;</c>.</summary>
    string PathFor(string commonDirectory);

    /// <summary>
    /// Creates (or heals) the land worktree detached at <paramref name="sha"/>, or aborts any sequencer and
    /// resets it there, then proves it clean, detached and locked. Every mutating git command goes through
    /// <paramref name="mutate"/> (the caller's owned-child journal); reads use the plain git runner.
    /// A refusal is returned in <see cref="LandWorkspaceState.Reason"/>, never thrown.
    /// </summary>
    Task<LandWorkspaceState> EnsureAsync(string repository, string path, string sha,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<LandingGitResult>>? mutate, CancellationToken ct);

    /// <summary>
    /// Plain file reads of <c>&lt;common&gt;/HEAD</c> and every <c>&lt;common&gt;/worktrees/*/HEAD</c>; no git
    /// process and no <c>worktree list</c>. The main checkout's row has <see cref="LandingHeadFile.IsMain"/>.
    /// </summary>
    IReadOnlyList<LandingHeadFile> ScanHeadFiles(string commonDirectory);
}

public sealed record LandWorkspaceState(string Path, string? HeadSha, bool Created, string? Reason, string? Detail = null)
{
    public bool Ready => Reason is null && HeadSha is not null;
}

/// <summary>One HEAD file: <paramref name="SymbolicRef"/> for <c>ref: &lt;name&gt;</c>, otherwise <paramref name="Sha"/>.
/// <paramref name="WorktreePath"/> is null when it cannot be derived (a bare or non-<c>.git</c> common directory).</summary>
public sealed record LandingHeadFile(string AdminDirectory, string? WorktreePath, string? SymbolicRef, string? Sha, bool IsMain);
