using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        var audit = app.MapGroup("/api/audit").WithTags("Audit");

        // GET /api/audit — Query audit records with filters (FR52, NFR18)
        audit.MapGet("/", async (
            Guid? workflowId,
            Guid? stageId,
            DateTime? from,
            DateTime? to,
            decimal? minCost,
            decimal? maxCost,
            int? skip,
            int? take,
            AuditService auditService,
            CancellationToken cancellationToken) =>
        {
            var result = await auditService.QueryAsync(
                workflowId,
                stageId,
                from,
                to,
                minCost,
                maxCost,
                skip ?? 0,
                Math.Min(take ?? 50, 200),
                cancellationToken);

            return Results.Ok(result);
        });

        // GET /api/audit/cost-summary — Get cost summary for a workflow or stage
        audit.MapGet("/cost-summary", async (
            Guid? workflowId,
            Guid? stageId,
            CostTrackingService costService,
            CancellationToken cancellationToken) =>
        {
            if (workflowId.HasValue)
            {
                var summary = await costService.GetWorkflowCostSummaryAsync(workflowId.Value, cancellationToken);
                return Results.Ok(summary);
            }

            if (stageId.HasValue)
            {
                var summary = await costService.GetStageCostSummaryAsync(stageId.Value, cancellationToken);
                return Results.Ok(summary);
            }

            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["query"] = ["Either workflowId or stageId must be provided."]
            });
        });

        // GET /api/audit/cost-ledger — Query permanent cost ledger entries
        audit.MapGet("/cost-ledger", async (
            Guid? workflowId,
            Guid? stageId,
            DateTime? from,
            DateTime? to,
            int? skip,
            int? take,
            CostTrackingService costService,
            CancellationToken cancellationToken) =>
        {
            var entries = await costService.QueryAsync(
                workflowId,
                stageId,
                from,
                to,
                skip ?? 0,
                Math.Min(take ?? 50, 200),
                cancellationToken);

            return Results.Ok(entries);
        });

        // DELETE /api/audit/archive — Archive (clean up) full audit content older than specified days (NFR24)
        audit.MapDelete("/archive", async (
            int? olderThanDays,
            ICurrentUser currentUser,
            AuditService auditService,
            CancellationToken cancellationToken) =>
        {
            // Admin-only operation
            // Note: In MVP, all users are admin. Future: check currentUser.IsAdmin
            var days = olderThanDays ?? 90;

            if (days < 1)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["olderThanDays"] = ["Must be at least 1 day."]
                });
            }

            var result = await auditService.ArchiveFullContentAsync(days, cancellationToken);
            return Results.Ok(result);
        });
    }
}
