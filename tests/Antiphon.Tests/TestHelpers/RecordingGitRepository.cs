using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// An <see cref="IGitRepository"/> that reports each question (its name, then its arguments) before the real one answers
/// it, in process or by git. A recording <c>LandingGit</c> returns it from <c>Open</c>.
/// </summary>
internal sealed class RecordingGitRepository(IGitRepository inner, Action<string[]> record) : IGitRepository
{
    public string Path => inner.Path;

    public Task<DelegationWorktreeService.CommitContainment> ContainsUnmergedAsync(string target, string source,
        CancellationToken ct)
    {
        record(["contains-unmerged", target, source]);
        return inner.ContainsUnmergedAsync(target, source, ct);
    }

    public Task<bool> OperationInProgressAsync(CancellationToken ct)
    {
        record(["operation-in-progress"]);
        return inner.OperationInProgressAsync(ct);
    }
}
