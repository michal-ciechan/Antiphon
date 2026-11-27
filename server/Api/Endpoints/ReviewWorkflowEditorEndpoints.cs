using Antiphon.Server.Application.Review;

namespace Antiphon.Server.Api.Endpoints;

public static class ReviewWorkflowEditorEndpoints
{
    public static void MapReviewWorkflowEditorEndpoints(this WebApplication app)
    {
        app.MapGet("/api/review-runs/{id:guid}/workflow-policy", async (
            Guid id, Guid? revisionId, ReviewWorkflowPolicyService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, revisionId, ct))).WithTags("Review engine");
    }
}
