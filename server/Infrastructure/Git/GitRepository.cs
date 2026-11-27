using Antiphon.GitInProcess;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// <see cref="IGitRepository"/> answered by libgit2 in process wherever it can answer correctly
/// (<see cref="InProcessGitInspection"/>), and by git through the runner (<see cref="GitCommandRepository"/>) wherever it
/// cannot. <see cref="LandingGit.Open"/> hands one out only when its git children read the same repository libgit2 opens.
/// </summary>
public sealed class GitRepository(ILandingGit git, string path, Action? inProcessHit = null) : GitCommandRepository(git, path)
{
    public override Task<DelegationWorktreeService.CommitContainment> ContainsUnmergedAsync(string target, string source,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DelegationWorktreeService.CommitContainment? answer = InProcessGitInspection.Containment(Path, target, source, ct) switch
        {
            PatchContainment.Contained => DelegationWorktreeService.CommitContainment.Contained,
            PatchContainment.NotContained => DelegationWorktreeService.CommitContainment.NotContained,
            PatchContainment.HasMerge => DelegationWorktreeService.CommitContainment.Unknown,
            _ => null,
        };
        if (answer is not { } known) return base.ContainsUnmergedAsync(target, source, ct);
        inProcessHit?.Invoke();
        return Task.FromResult(known);
    }

    public override Task<bool> OperationInProgressAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (InProcessGitInspection.OperationInProgress(Path) is not { } busy) return base.OperationInProgressAsync(ct);
        inProcessHit?.Invoke();
        return Task.FromResult(busy);
    }
}
