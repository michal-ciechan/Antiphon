using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var workflows = app.MapGroup("/api/workflows")
            .WithTags("Workflows");

        workflows.MapGet("/", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var items = await db.Workflows
                .Include(w => w.Template)
                .Include(w => w.Project)
                .Include(w => w.CurrentStage)
                .Include(w => w.Stages)
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync(cancellationToken);

            var dtos = items.Select(w => new WorkflowDto(
                w.Id,
                w.Name,
                w.Description,
                w.Status,
                w.CurrentStage?.Name,
                w.TemplateId,
                w.Template.Name,
                w.ProjectId,
                w.Project.Name,
                w.Stages.Count,
                w.Stages.Count(s => s.Status == StageStatus.Completed),
                WorkflowStateMachine.GetAvailableTransitions(w.Status),
                w.CreatedAt,
                w.UpdatedAt)).ToList();

            return Results.Ok(dtos);
        });

        workflows.MapGet("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var w = await db.Workflows
                .Include(w => w.Template)
                .Include(w => w.Project)
                .Include(w => w.CurrentStage)
                .Include(w => w.Stages.OrderBy(s => s.StageOrder))
                .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

            if (w is null)
            {
                return Results.NotFound();
            }

            var dto = new WorkflowDetailDto(
                w.Id,
                w.Name,
                w.Description,
                w.Status,
                w.CurrentStage?.Name,
                w.TemplateId,
                w.Template.Name,
                w.ProjectId,
                w.Project.Name,
                w.Stages.Count,
                w.Stages.Count(s => s.Status == StageStatus.Completed),
                WorkflowStateMachine.GetAvailableTransitions(w.Status),
                w.Stages.Select(s => new StageDto(
                    s.Id,
                    s.Name,
                    s.StageOrder,
                    s.Status,
                    s.GateRequired,
                    s.CurrentVersion)).ToList(),
                w.CreatedAt,
                w.UpdatedAt);

            return Results.Ok(dto);
        });

        workflows.MapPost("/", async (
            CreateWorkflowRequest request,
            WorkflowEngine engine,
            CancellationToken cancellationToken) =>
        {
            var workflow = await engine.CreateWorkflowAsync(
                request.TemplateId,
                request.ProjectId,
                request.InitialContext ?? string.Empty,
                cancellationToken);

            return Results.Created($"/api/workflows/{workflow.Id}", new { id = workflow.Id });
        });
    }
}
