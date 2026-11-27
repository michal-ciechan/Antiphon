using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Questions about one repository or checkout, asked by what the caller wants to know rather than by git command. The
/// implementation answers each the fastest correct way it has (libgit2 in process where it can, git otherwise); the
/// caller never sees which. Obtain one from <see cref="ILandingGit.Open"/>, so a git answer runs through that runner
/// (its environment, journal and test seams).
/// </summary>
public interface IGitRepository
{
    /// <summary>The repository or checkout directory this answers for.</summary>
    string Path { get; }

    /// <summary>
    /// Whether every commit of <paramref name="source"/> beyond <paramref name="target"/> already has a patch-equivalent
    /// commit on the target. Unknown when there is a merge beyond the target or the answer cannot be read.
    /// </summary>
    Task<DelegationWorktreeService.CommitContainment> ContainsUnmergedAsync(string target, string source, CancellationToken ct);

    /// <summary>Whether a merge, rebase or am is in progress in this checkout. Throws when it cannot be read.</summary>
    Task<bool> OperationInProgressAsync(CancellationToken ct);
}
