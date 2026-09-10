using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Independent, rowless inspection/refusal surface. No session lookup, Stop, synthetic row or
/// standing-owner mutation. The runner currently issues only ineligible previews and refusals;
/// enabling execution requires the planned standing launch synchronization and backend guard.
/// </summary>
public sealed class HerdrPaneDisposalService(ISessionRunnerClient runner)
{
    public Task<HerdrPaneDisposalPreview> PreviewAsync(
        HerdrPaneDisposalPreviewRequest request, CancellationToken cancellationToken) =>
        runner.PreviewHerdrPaneDisposalAsync(request, cancellationToken);

    public Task<HerdrPaneDisposalReceipt> ExecuteAsync(
        HerdrPaneDisposalRequest request, CancellationToken cancellationToken) =>
        runner.DisposeHerdrPaneAsync(request, cancellationToken);

    public Task<HerdrPaneDisposalReceipt> GetAsync(Guid operationId, CancellationToken cancellationToken) =>
        runner.GetHerdrPaneDisposalAsync(operationId, cancellationToken);
}
