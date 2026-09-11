using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Exceptions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Explicit rowless disposal with standing-owner synchronization through the runner verdict.
/// </summary>
public sealed class HerdrPaneDisposalService(ISessionRunnerClient runner, IHerdrPaneDisposalOwnership ownership)
{
    public async Task<HerdrPaneDisposalPreview> PreviewAsync(
        HerdrPaneDisposalPreviewRequest request, CancellationToken cancellationToken)
    {
        var preview = await runner.PreviewHerdrPaneDisposalAsync(request, cancellationToken);
        try { await using var lease = await ownership.AcquireAsync(preview, cancellationToken); }
        catch (ConflictException ex)
        { return preview with { PreviewId = Guid.Empty, Eligible = false, Blockers = preview.Blockers.Append(ex.Code!).Distinct().ToArray(), PlannedTerminationPids = [] }; }
        return preview;
    }

    public async Task<HerdrPaneDisposalReceipt> ExecuteAsync(
        HerdrPaneDisposalRequest request, CancellationToken cancellationToken)
    {
        if ((await runner.GetCapabilitiesAsync(cancellationToken))?.Features?.Contains(
            HerdrPaneDisposalCodes.BestEffortCapability, StringComparer.Ordinal) != true)
            throw new ConflictException("The runner cannot execute best-effort pane disposal.", HerdrPaneDisposalCodes.GuardUnavailable);
        request = request with { GuardMode = "antiphon-best-effort" };
        // An existing durable operation cannot close again. Preserve its retry/conflict semantics
        // even after preview expiry or a later owner starting on a replacement pane.
        var exists = false;
        try { await runner.GetHerdrPaneDisposalAsync(request.OperationId, cancellationToken); exists = true; }
        catch (HerdrPaneDisposalException ex) when (ex.StatusCode == 404) { }
        if (exists) return await runner.DisposeHerdrPaneAsync(request, cancellationToken);
        var preview = await runner.GetHerdrPaneDisposalPreviewAsync(request.PreviewId, cancellationToken);
        await using var lease = await ownership.AcquireAsync(preview, cancellationToken);
        return await runner.DisposeHerdrPaneAsync(request, cancellationToken);
    }

    public Task<HerdrPaneDisposalReceipt> GetAsync(Guid operationId, CancellationToken cancellationToken) =>
        runner.GetHerdrPaneDisposalAsync(operationId, cancellationToken);
}
