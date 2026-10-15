using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public static class ReviewEvidenceCorrectionEndpoints
{
    public static IEndpointRouteBuilder MapReviewEvidenceCorrectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/agent-tasks/{reviewId:guid}/review-evidence/corrections", async (
            Guid reviewId, ReviewEvidenceCorrectionRequest request, HttpContext http,
            AppDbContext db, ReviewEvidenceCorrectionService corrections,
            IOptions<PhoneHomeRunnerSettings> settings, CancellationToken ct) =>
        {
            string actor;
            if (OperatorCredential.HeaderMatches(http, settings.Value)) actor = "operator";
            else
            {
                var service = http.RequestServices.GetRequiredService<AgentTaskService>();
                var caller = await service.AuthenticateAsync(http.Request.Headers["X-Antiphon-Task-Token"].FirstOrDefault(), ct);
                if (!caller.MayDelegate) throw Denied("principal");
                var review = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == reviewId, ct)
                    ?? throw new NotFoundException(nameof(AgentTask), reviewId);
                var project = caller.Task?.ProjectId ?? await service.DeriveCallerProjectAsync(caller, ct);
                if (review.ProjectId is null || project != review.ProjectId) throw Denied("project");
                var roots = new[] { caller.Task?.RepoPath ?? caller.WorkingDirectory }.Concat(caller.ExtraAllowedRoots ?? []);
                if (review.RepoPath is null || !roots.Any(root => !string.IsNullOrWhiteSpace(root)
                    && DelegationWorkspaceResolver.IsWithinRoot(review.RepoPath, root))) throw Denied("repository");
                Guid? board = caller.BoardId;
                if (caller.Task?.CardId is Guid callerCard)
                {
                    if (review.CardId != callerCard) throw Denied("card");
                    board ??= await db.Cards.Where(c => c.Id == callerCard).Select(c => (Guid?)c.BoardId).SingleOrDefaultAsync(ct);
                }
                if (caller.SessionId is Guid session)
                    board ??= await db.Agents.Where(a => a.PersistentSessionId == session.ToString("D"))
                        .Select(a => a.BoardId).FirstOrDefaultAsync(ct);
                if (board is Guid authorizedBoard && (review.CardId is not Guid targetCard
                    || !await db.Cards.AnyAsync(c => c.Id == targetCard && c.BoardId == authorizedBoard, ct))) throw Denied("board");
                actor = caller.Task is { } task ? $"task:{task.Id:D}"
                    : caller.CapabilityId is Guid capability ? $"capability:{capability:D}" : $"session:{caller.SessionId:D}";
            }
            return Results.Ok(await corrections.CorrectAsync(reviewId, request, actor, ct));
        });
        return app;
    }
    private static ForbiddenException Denied(string scope) => new("Correction requires authorized " + scope + " scope.",
        "review_correction_" + scope + "_denied");
}
