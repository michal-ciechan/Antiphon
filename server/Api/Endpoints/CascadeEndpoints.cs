using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

public static class CascadeEndpoints
{
    public static void MapCascadeEndpoints(this WebApplication app)
    {
        var gates = app.MapGroup("/api/workflows/{id:guid}/gates")
            .WithTags("Gates");

        var cascade = app.MapGroup("/api/workflows/{id:guid}/cascade")
            .WithTags("Cascade");

        // POST /api/workflows/{id}/gates/go-back — Initiate go-back course correction (FR24)
        gates.MapPost("/go-back", async (
            Guid id,
            GoBackRequest request,
            AppDbContext db,
            WorkflowEngine engine,
            ICurrentUser currentUser,
            IEventBus eventBus,
            CancellationToken cancellationToken) =>
        {
            var workflow = await db.Workflows
                .Include(w => w.Stages.OrderBy(s => s.StageOrder))
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Workflow), id);

            if (workflow.Status != WorkflowStatus.GateWaiting)
            {
                throw new ConflictException(
                    $"Workflow must be in GateWaiting state to go back. Current: {workflow.Status}");
            }

            // Find the current gate stage for audit
            var currentStage = workflow.Stages
                .OrderByDescending(s => s.StageOrder)
                .FirstOrDefault(s => s.Status == StageStatus.Completed)
                ?? throw new ConflictException("No completed stage found for go-back.");

            // Record the gate decision (FR42, FR53)
            var decision = new GateDecision
            {
                Id = Guid.NewGuid(),
                StageId = currentStage.Id,
                WorkflowId = id,
                Action = GateAction.GoBack,
                Feedback = $"Go-back to stage: {request.TargetStageId}",
                DecidedBy = currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };
            db.GateDecisions.Add(decision);
            await db.SaveChangesAsync(cancellationToken);

            // Delegate to engine: detect affected stages, transition to CascadeWaiting
            var result = await engine.GoBackAsync(id, request.TargetStageId, cancellationToken);

            return Results.Ok(new GoBackResponse(result.AffectedStages));
        });

        // POST /api/workflows/{id}/cascade — Submit cascade decisions (FR26)
        cascade.MapPost("/", async (
            Guid id,
            CascadeRequest request,
            AppDbContext db,
            CascadeService cascadeService,
            WorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            var workflow = await db.Workflows
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Workflow), id);

            if (workflow.Status != WorkflowStatus.CascadeWaiting)
            {
                throw new ConflictException(
                    $"Workflow must be in CascadeWaiting state to submit cascade decisions. Current: {workflow.Status}");
            }

            // Execute cascade decisions
            var decisions = request.Decisions
                .Select(d => new CascadeDecisionDto(d.StageId, d.Action))
                .ToList();

            await cascadeService.ExecuteCascadeAsync(id, decisions, cancellationToken);

            // Resume workflow execution after cascade
            await engine.ResumeAfterCascadeAsync(id, cancellationToken);

            return Results.NoContent();
        });

        // GET /api/workflows/{id}/cascade/affected — Get affected stages for current cascade
        cascade.MapGet("/affected", async (
            Guid id,
            AppDbContext db,
            CascadeService cascadeService,
            CancellationToken cancellationToken) =>
        {
            var workflow = await db.Workflows
                .Include(w => w.Stages.OrderBy(s => s.StageOrder))
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Workflow), id);

            if (workflow.Status != WorkflowStatus.CascadeWaiting)
            {
                throw new ConflictException(
                    $"Workflow must be in CascadeWaiting state to view affected stages. Current: {workflow.Status}");
            }

            // The target stage is the one currently set as Pending (the go-back target)
            var targetStage = workflow.Stages
                .OrderBy(s => s.StageOrder)
                .FirstOrDefault(s => s.Status == StageStatus.Pending);

            if (targetStage is null)
            {
                return Results.Ok(Array.Empty<AffectedStageDto>());
            }

            var affected = await cascadeService.DetectAffectedStagesAsync(
                id, targetStage.Id, cancellationToken);

            return Results.Ok(affected);
        });
    }
}

public record GoBackRequest(Guid TargetStageId);

public record GoBackResponse(IReadOnlyList<AffectedStageDto> AffectedStages);

public record CascadeRequest(IReadOnlyList<CascadeDecisionRequest> Decisions);

public record CascadeDecisionRequest(Guid StageId, CascadeAction Action);
