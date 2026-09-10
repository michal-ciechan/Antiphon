using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public static class HerdrPaneDisposalRoutes
{
    public static void MapHerdrPaneDisposalRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/herdr/pane-disposals");
        group.MapPost("/preview", (HerdrPaneDisposalPreviewRequest request,
            HerdrPaneDisposalService service, CancellationToken ct) =>
            RespondAsync(async () => Results.Ok(await service.PreviewAsync(request, ct))));
        group.MapPost("", (HerdrPaneDisposalRequest request,
            HerdrPaneDisposalService service, CancellationToken ct) => RespondAsync(async () =>
        {
            var receipt = await service.ExecuteAsync(request, ct);
            return Results.Problem(statusCode: 503, type: receipt.Code,
                title: "Guarded Herdr disposal is unavailable",
                detail: "No teardown was dispatched. The guarded backend prerequisite is not implemented.",
                extensions: new Dictionary<string, object?>
                {
                    ["operationId"] = receipt.OperationId, ["receipt"] = receipt,
                });
        }));
        group.MapGet("/{operationId:guid}", (Guid operationId,
            HerdrPaneDisposalService service, CancellationToken ct) => RespondAsync(async () =>
            await service.GetAsync(operationId, ct) is { } receipt ? Results.Ok(receipt)
                : Results.Problem(statusCode: 404, type: "herdr_disposal_operation_not_found",
                    title: "Disposal operation not found")));
    }

    private static async Task<IResult> RespondAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ArgumentException)
        { return Results.Problem(statusCode: 400, type: "herdr_disposal_invalid_request", title: "Invalid disposal request"); }
        catch (HerdrLaunchException ex) { return HerdrProblemMapper.MapLaunch(ex); }
        catch (HerdrBackendUnavailableException)
        { return Results.Problem(statusCode: 503, type: HerdrProblemTypes.Unreachable, title: "Herdr unavailable"); }
        catch (HerdrProtocolException)
        { return Results.Problem(statusCode: 503, type: HerdrProblemTypes.Unreachable, title: "Herdr protocol could not be verified"); }
        catch (HerdrApiException)
        { return Results.Problem(statusCode: 503, type: HerdrProblemTypes.Unreachable, title: "Herdr inspection failed"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { return Results.Problem(statusCode: 503, type: "herdr_disposal_store_unavailable", title: "Disposal receipt store unavailable"); }
    }
}
