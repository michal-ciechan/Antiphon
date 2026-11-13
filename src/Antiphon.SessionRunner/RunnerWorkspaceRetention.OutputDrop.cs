using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed partial class RunnerWorkspaceRetention
{
    /// <summary>
    /// CARD-1895. Deletes allow-listed build output from one finished mirror. The red witness commits this
    /// refusal first: it deletes nothing, so every guard that requires a real verdict fails closed.
    /// </summary>
    public Task<PhoneHomeWorkspaceOutputDropResponse> DropOutputsAsync(
        PhoneHomeWorkspaceOutputDropRequest request, CancellationToken ct)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(new PhoneHomeWorkspaceOutputDropResponse(
            WorkspaceOutputDropCodes.Kept, "not_implemented", Detail: "CARD-1895 stub"));
    }
}
