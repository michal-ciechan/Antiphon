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

/// <summary>Explicit authorized completion actions. Registered by the separate S6 integration slice.</summary>
public static class CardCompletionEndpoints
{
    public static void MapCardCompletionEndpoints(this WebApplication app)
    {
        var cards = app.MapGroup("/api/cards");
        cards.MapPut("/{cardId:guid}/completion-acceptance", async (Guid cardId, WriteCardCompletionAcceptanceRequest request,
            HttpContext http, AgentTaskService tasks, AppDbContext db, IOptions<PhoneHomeRunnerSettings> settings,
            CardCompletionAcceptanceService acceptance, CancellationToken ct) =>
        {
            var actor = await AuthorizeAsync(cardId, http, tasks, db, settings.Value, ct);
            return Results.Ok(await acceptance.WriteAsync(cardId, request, actor, ct));
        });
        cards.MapGet("/{cardId:guid}/completion-acceptance", async (Guid cardId, HttpContext http, AgentTaskService tasks,
            AppDbContext db, IOptions<PhoneHomeRunnerSettings> settings, CardCompletionAcceptanceService acceptance, CancellationToken ct) =>
        {
            await AuthorizeAsync(cardId, http, tasks, db, settings.Value, ct);
            return Results.Ok(await acceptance.GetAsync(cardId, ct));
        });
        cards.MapPost("/{cardId:guid}/complete-published", async (Guid cardId, PublishedCardCompletionProposal proposal,
            HttpContext http, AgentTaskService tasks, AppDbContext db, IOptions<PhoneHomeRunnerSettings> settings,
            CardService cardsService, PublishedCardCompletion completion, CancellationToken ct) =>
        {
            var actor = await AuthorizeAsync(cardId, http, tasks, db, settings.Value, ct);
            if (cardId != proposal.CardId) throw new ValidationException("cardId", "Proposal must name the path card.");
            return Results.Ok(await cardsService.CompletePublishedAsync(proposal, completion, actor, ct));
        });
    }

    private static async Task<string> AuthorizeAsync(Guid cardId, HttpContext http, AgentTaskService tasks,
        AppDbContext db, PhoneHomeRunnerSettings settings, CancellationToken ct)
    {
        if (OperatorCredential.HeaderMatches(http, settings)) return "operator";
        var caller = await tasks.AuthenticateAsync(http.Request.Headers["X-Antiphon-Task-Token"].FirstOrDefault(), ct);
        if (!caller.MayDelegate) throw new ForbiddenException("Completion requires an orchestrator credential.", "card_completion_principal_denied");
        var card = await db.Cards.AsNoTracking().Include(c => c.Board).ThenInclude(b => b.Project)
            .SingleOrDefaultAsync(c => c.Id == cardId, ct) ?? throw new NotFoundException(nameof(Card), cardId);
        var project = caller.Task?.ProjectId ?? await tasks.DeriveCallerProjectAsync(caller, ct);
        if (project != card.Board.ProjectId)
            throw new ForbiddenException("Completion requires the card's project scope.", "card_completion_project_denied");
        var roots = new[] { caller.Task?.RepoPath ?? caller.WorkingDirectory }.Concat(caller.ExtraAllowedRoots ?? []);
        var repository = card.Board.Project?.LocalRepositoryPath;
        if (string.IsNullOrWhiteSpace(repository) || !roots.Any(root => !string.IsNullOrWhiteSpace(root)
            && DelegationWorkspaceResolver.IsWithinRoot(repository, root)))
            throw new ForbiddenException("Completion requires the card's repository scope.", "card_completion_repository_denied");
        Guid? boundCard = caller.Task?.CardId;
        Guid? boundBoard = caller.BoardId;
        if (caller.SessionId is Guid sessionId)
        {
            boundCard ??= await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId).Select(s => s.CardId).SingleOrDefaultAsync(ct);
            boundBoard ??= await db.Agents.AsNoTracking().Where(a => a.PersistentSessionId == sessionId.ToString())
                .Select(a => (Guid?)a.BoardId).FirstOrDefaultAsync(ct);
        }
        if (boundCard is Guid boundId && boundId != cardId || boundBoard is Guid boardId && boardId != card.BoardId)
            throw new ForbiddenException("Completion is outside the caller's card/board binding.", "card_completion_card_denied");
        return caller.Task is { } task ? $"task:{task.Id:D}" : caller.CapabilityId is { } cap
            ? $"capability:{cap:D}" : $"session:{caller.SessionId:D}";
    }
}
