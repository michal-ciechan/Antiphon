using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>
/// The review engine API, first slice (docs/features/012-review-engine/api.md). Not the agent Files
/// review surface, which is <see cref="ReviewEndpoints"/>.
/// </summary>
public static class ReviewRunEndpoints
{
    public static void MapReviewRunEndpoints(this WebApplication app)
    {
        var runs = app.MapGroup("/api/review-runs").WithTags("Review engine");

        runs.MapGet("/", async (string? status, string? repo, string? stage, string? q, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.ListRunsAsync(ParseEnum<ReviewRunStatus>("status", status), repo, ParseEnum<ReviewStage>("stage", stage), q, ct)));

        runs.MapGet("/kpis", async (ReviewService service, CancellationToken ct) => Results.Ok(await service.GetKpisAsync(ct)));

        runs.MapPost("/", async (CreateReviewRunRequest request, ReviewService service, CancellationToken ct) =>
        {
            var run = await service.CreateRunAsync(request, ct);
            return Results.Created($"/api/review-runs/{run.Id}", run);
        });

        // Demo data for the UI's http mode; off unless Review:DemoSeedEnabled (on by default in Development).
        runs.MapPost("/demo", async (IOptions<ReviewSettings> settings, ReviewDemoSeeder seeder, ReviewService service, CancellationToken ct) =>
        {
            if (!settings.Value.DemoSeedEnabled)
                throw new NotFoundException("Review demo seeding", "demo", "Review demo seeding is disabled (Review:DemoSeedEnabled).");
            var ids = await seeder.SeedAsync(ct);
            return Results.Ok(await service.GetSummariesAsync(ids, ct));
        });

        runs.MapGet("/{id:guid}", async (Guid id, ReviewService service, CancellationToken ct) => Results.Ok(await service.GetRunAsync(id, ct)));

        runs.MapGet("/{id:guid}/findings", async (Guid id, string? status, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.ListFindingsAsync(id, ParseEnum<ReviewFindingStatus>("status", status), ct)));

        runs.MapPost("/{id:guid}/refresh", async (Guid id, ReviewService service, CancellationToken ct) =>
        {
            await service.RefreshAsync(id, ct);
            return Results.Accepted();
        });

        runs.MapPost("/{id:guid}/responses", async (Guid id, BatchFindingResponsesRequest request, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.RespondBatchAsync(id, request, ct)));

        app.MapPost("/api/review-findings/{id:guid}/responses", async (Guid id, FindingResponseRequest request, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.RespondAsync(id, request, ct))).WithTags("Review engine");

        app.MapGet("/api/review-queue", async (string? scope, string? login, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.GetQueueAsync(scope, login, ct))).WithTags("Review engine");

        var gates = app.MapGroup("/api/review-gates/{id:guid}").WithTags("Review engine");
        gates.MapPost("/assign", async (Guid id, GateAssignRequest? request, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.AssignAsync(id, request?.Login, ct)));
        gates.MapPost("/pass", async (Guid id, GatePassRequest request, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.PassAsync(id, request.Reason, ct)));
        gates.MapPost("/decision", async (Guid id, GateDecisionRequest request, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.DecideAsync(id, request, ct)));

        app.MapGet("/api/review-me", (ReviewService service) => Results.Ok(service.GetViewer())).WithTags("Review engine");

        var workflows = app.MapGroup("/api/review-workflows").WithTags("Review engine");
        workflows.MapGet("/default", (ReviewWorkflowService service) => Results.Ok(service.GetDefault()));
        workflows.MapGet("/revisions/{id:guid}", async (Guid id, ReviewService service, CancellationToken ct) =>
            Results.Ok(await service.GetWorkflowRevisionAsync(id, ct)));
        workflows.MapPost("/validate", (WorkflowGraphDto graph, ReviewWorkflowService service) => Results.Ok(service.Validate(graph)));
    }

    private static T? ParseEnum<T>(string name, string? value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _))
            return parsed;
        throw new ValidationException(name, $"{name} must be one of {string.Join(", ", Enum.GetNames<T>())}.");
    }
}
