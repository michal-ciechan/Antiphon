using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Api.Endpoints;

public static class HerdrPaneDisposalEndpoints
{
    public static void MapHerdrPaneDisposalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/herdr/pane-disposals").WithTags("HerdrPaneDisposals");
        group.MapPost("/preview", async (HerdrPaneDisposalPreviewRequest request,
            HerdrPaneDisposalService service, CancellationToken ct) =>
            Results.Ok(await service.PreviewAsync(request, ct)));
        group.MapPost("", async (HerdrPaneDisposalRequest request,
            HerdrPaneDisposalService service, CancellationToken ct) =>
            Results.Ok(await service.ExecuteAsync(request, ct)));
        group.MapGet("/{operationId:guid}", async (Guid operationId,
            HerdrPaneDisposalService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(operationId, ct)));
    }
}
