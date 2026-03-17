using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

public static class GateEndpoints
{
    public static void MapGateEndpoints(this WebApplication app)
    {
        var gates = app.MapGroup("/api/workflows/{id:guid}/gates")
            .WithTags("Gates");

        gates.MapPost("/approve", async (
            Guid id,
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
                    $"Workflow must be in GateWaiting state to approve. Current: {workflow.Status}");
            }

            // Find the current stage (most recently completed, which triggered the gate)
            var currentStage = workflow.Stages
                .OrderByDescending(s => s.StageOrder)
                .FirstOrDefault(s => s.Status == StageStatus.Completed)
                ?? throw new ConflictException("No completed stage found for gate approval.");

            // Record the gate decision
            var decision = new GateDecision
            {
                Id = Guid.NewGuid(),
                StageId = currentStage.Id,
                WorkflowId = id,
                Action = GateAction.Approved,
                DecidedBy = currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };
            db.GateDecisions.Add(decision);
            await db.SaveChangesAsync(cancellationToken);

            await eventBus.PublishToGroupAsync(
                $"workflow-{id}",
                "GateActioned",
                new { workflowId = id, action = "approved", stageId = currentStage.Id },
                cancellationToken);

            // Resume workflow execution
            await engine.ResumeAfterGateAsync(id, cancellationToken);

            return Results.NoContent();
        });

        gates.MapPost("/reject", async (
            Guid id,
            GateFeedbackRequest request,
            AppDbContext db,
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
                    $"Workflow must be in GateWaiting state to reject. Current: {workflow.Status}");
            }

            var currentStage = workflow.Stages
                .OrderByDescending(s => s.StageOrder)
                .FirstOrDefault(s => s.Status == StageStatus.Completed)
                ?? throw new ConflictException("No completed stage found for gate rejection.");

            // Record the gate decision
            var decision = new GateDecision
            {
                Id = Guid.NewGuid(),
                StageId = currentStage.Id,
                WorkflowId = id,
                Action = GateAction.RejectedWithFeedback,
                Feedback = request.Feedback,
                DecidedBy = currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };
            db.GateDecisions.Add(decision);

            // Reset the stage status to pending so it can be re-executed with feedback
            currentStage.Status = StageStatus.Pending;
            currentStage.CurrentVersion++;

            await db.SaveChangesAsync(cancellationToken);

            await eventBus.PublishToGroupAsync(
                $"workflow-{id}",
                "GateActioned",
                new { workflowId = id, action = "rejected", stageId = currentStage.Id },
                cancellationToken);

            return Results.NoContent();
        });

        gates.MapPost("/prompt", async (
            Guid id,
            GateFeedbackRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            IEventBus eventBus,
            CancellationToken cancellationToken) =>
        {
            var workflow = await db.Workflows
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Workflow), id);

            // Prompt can be sent in either Running or GateWaiting state
            if (workflow.Status != WorkflowStatus.GateWaiting && workflow.Status != WorkflowStatus.Running)
            {
                throw new ConflictException(
                    $"Workflow must be in Running or GateWaiting state to send a prompt. Current: {workflow.Status}");
            }

            // Store as a system event (feedback to agent)
            // In a full implementation, this would be passed to the agent executor.
            await eventBus.PublishToGroupAsync(
                $"workflow-{id}",
                "UserPromptSent",
                new { workflowId = id, feedback = request.Feedback, userId = currentUser.UserId },
                cancellationToken);

            return Results.NoContent();
        });

        gates.MapPost("/comment", async (
            Guid id,
            GateCommentRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            IEventBus eventBus,
            CancellationToken cancellationToken) =>
        {
            // Comments do NOT trigger the agent -- human-to-human only (UX-DR21)
            var workflow = await db.Workflows
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(Workflow), id);

            await eventBus.PublishToGroupAsync(
                $"workflow-{id}",
                "UserCommentAdded",
                new { workflowId = id, content = request.Content, userId = currentUser.UserId },
                cancellationToken);

            return Results.NoContent();
        });
    }
}

public record GateFeedbackRequest(string Feedback);
public record GateCommentRequest(string Content);
