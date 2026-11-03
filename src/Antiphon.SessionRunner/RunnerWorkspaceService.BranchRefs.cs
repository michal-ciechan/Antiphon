using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed partial class RunnerWorkspaceService
{
    /// <summary>CARD-0824 S2 test seam: runs after every check and before the first <c>update-ref -d</c>, with the ref name.</summary>
    internal Func<string, Task>? BeforeBranchRefDelete { get; set; }

    /// <summary>CARD-0824 S2 (V-40). Stub: refuses everything.</summary>
    public Task<PhoneHomeWorkspaceBranchRefsDeleteResponse> DeleteBranchRefsAsync(
        PhoneHomeWorkspaceBranchRefsDeleteRequest request, CancellationToken ct) =>
        Task.FromResult(new PhoneHomeWorkspaceBranchRefsDeleteResponse(
            WorkspaceBranchRefsCodes.Refused, WorkspaceBranchRefsCodes.NotImplemented, [], []));
}
