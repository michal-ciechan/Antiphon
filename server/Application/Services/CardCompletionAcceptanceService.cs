using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Antiphon.Server.Application.Services;

/// <summary>Append-only acceptance authority. Writers serialize with close and task admission.</summary>
public sealed class CardCompletionAcceptanceService(AppDbContext db, TimeProvider clock)
{
    public async Task<CardCompletionAcceptance> WriteAsync(Guid cardId, WriteCardCompletionAcceptanceRequest request,
        string actor, CancellationToken ct)
    {
        RequireActor(actor);
        if (request.ExpectedRevision < 0 || request.ExpectedCardToken == Guid.Empty
            || string.IsNullOrWhiteSpace(request.PolicyRevision) || request.PolicyRevision.Length > 200
            || string.IsNullOrWhiteSpace(request.DecisionRevision) || request.DecisionRevision.Length > 200
            || request.AcceptanceItems is null || request.DeploymentItems is null || request.Companion is null)
            throw new ValidationException("acceptance", "Explicit revision, policy, decision and obligation sets are required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockCardAsync(db, cardId, ct);
        var card = await db.Cards.AsNoTracking().Include(c => c.Board).SingleOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
        if (card.ConcurrencyToken != request.ExpectedCardToken) throw Refuse("card_changed");
        if (card.ArchivedAt is not null || card.Status is CardStatus.Done or CardStatus.Canceled) throw Refuse("card_ineligible");
        await RequireWorkflowAsync(db, card, request.WorkflowRunId, request.PipelineDefinitionRevisionId,
            request.CurrentStageId, request.WorkflowUpdatedAt, ct);
        var latest = await LatestAsync(db, cardId, ct);
        if ((latest?.Revision ?? 0) != request.ExpectedRevision) throw Refuse("acceptance_changed");
        var evidence = await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == request.ReviewEvidenceId, ct)
            ?? throw Refuse("review_missing");
        if (evidence.StageTaskId is not Guid reviewId) throw Refuse("review_missing");
        await ReviewEvidenceRecoveryService.LockTasksAsync(db, [request.ImplementationTaskId, reviewId], ct);
        var owner = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.ImplementationTaskId, ct);
        RequireOwner(card, owner, request.RepositoryPath, request.SourceRef);
        await LandApproval.LoadUsableEvidenceAsync(db, request.ReviewEvidenceId, request.ReviewedSourceSha, owner!, ct);
        if ((request.OperationId is null) != (request.LandedSha is null)) throw Refuse("publication_incomplete");
        if (request.OperationId is Guid operationId)
        {
            var operation = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, ct);
            RequirePublication(operation, owner!, request.ReviewEvidenceId, request.ReviewedSourceSha, request.LandedSha);
        }
        var instant = clock.GetUtcNow().UtcDateTime;
        // PostgreSQL timestamps have microsecond precision; JSON item stamps must round-trip identically.
        var now = new DateTime(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
        CompletionItem[] Stamp(IReadOnlyList<CompletionItem> items)
        {
            if (items.Count > 100 || items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Id) || i.Id.Length > 200
                    || !Enum.IsDefined(i.State) || i.State == CompletionItemState.Satisfied && !Digest(i.EvidenceDigest))
                || items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != items.Count)
                throw new ValidationException("items", "Bounded unique items, explicit states and SHA-256 evidence for satisfied items are required.");
            return items.Select(i => i with { Actor = actor, RecordedAt = now }).ToArray();
        }
        var empty = request.EmptyObligationsDecision;
        if (empty is not null && (string.IsNullOrWhiteSpace(empty.Category) || string.IsNullOrWhiteSpace(empty.Reason)
            || empty.Category.Length > 200 || empty.Reason.Length > 2000))
            throw new ValidationException("emptyObligationsDecision", "An explicit bounded category decision is required.");
        var row = new CardCompletionAcceptance
        {
            Id = Guid.NewGuid(), CardId = cardId, BoardId = card.BoardId, ProjectId = card.Board.ProjectId,
            Revision = request.ExpectedRevision + 1, ExpectedCardToken = request.ExpectedCardToken,
            WorkflowRunId = request.WorkflowRunId, PipelineDefinitionRevisionId = request.PipelineDefinitionRevisionId,
            CurrentStageId = request.CurrentStageId, WorkflowUpdatedAt = request.WorkflowUpdatedAt,
            ImplementationTaskId = request.ImplementationTaskId, RepositoryPath = request.RepositoryPath,
            SourceRef = request.SourceRef, ReviewedSourceSha = request.ReviewedSourceSha, ReviewEvidenceId = request.ReviewEvidenceId,
            OperationId = request.OperationId, LandedSha = request.LandedSha, PolicyRevision = request.PolicyRevision,
            DecisionRevision = request.DecisionRevision, AcceptanceItemsJson = JsonSerializer.Serialize(Stamp(request.AcceptanceItems)),
            DeploymentItemsJson = JsonSerializer.Serialize(Stamp(request.DeploymentItems)),
            EmptyObligationsDecisionJson = empty is null ? null : JsonSerializer.Serialize(empty with { Actor = actor, RecordedAt = now }),
            CompanionObligationJson = JsonSerializer.Serialize(request.Companion), ReadyForRelease = request.ReadyForRelease,
            Actor = actor, RecordedAt = now,
        };
        db.CardCompletionAcceptances.Add(row);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row;
    }

    public async Task<CardCompletionView> GetAsync(Guid cardId, CancellationToken ct)
    {
        var card = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
        var row = await LatestAsync(db, cardId, ct);
        var receipt = await db.CardCompletionReceipts.AsNoTracking().Where(r => r.CardId == cardId)
            .OrderByDescending(r => r.RecordedAt).FirstOrDefaultAsync(ct);
        return new(row, row is { ReadyForRelease: true } && card.Status == CardStatus.Review
            && card.ConcurrencyToken == row.ExpectedCardToken ? "ready for release" : null, receipt);
    }

    internal static Task LockCardAsync(AppDbContext db, Guid cardId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Cards\" WHERE \"Id\" = {cardId} FOR UPDATE", ct);
    internal static Task<CardCompletionAcceptance?> LatestAsync(AppDbContext db, Guid cardId, CancellationToken ct) =>
        db.CardCompletionAcceptances.AsNoTracking().Where(a => a.CardId == cardId).OrderByDescending(a => a.Revision).FirstOrDefaultAsync(ct);
    internal static async Task RequireWorkflowAsync(AppDbContext db, Card card, Guid? runId, Guid? definition,
        Guid? stage, DateTime? updated, CancellationToken ct)
    {
        if (card.ActiveWorkflowRunId != runId) throw Refuse("workflow_changed");
        if (runId is Guid id)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"CardWorkflowRuns\" WHERE \"Id\" = {id} FOR UPDATE", ct);
            var run = await db.CardWorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
            if (run is null || run.CardId != card.Id || run.PipelineDefinitionRevisionId != definition
                || run.CurrentStageId != stage || run.UpdatedAt != updated) throw Refuse("workflow_changed");
        }
        else if (definition is not null || stage is not null || updated is not null) throw Refuse("workflow_changed");
    }
    internal static void RequireOwner(Card card, AgentTask? owner, string repository, string sourceRef)
    {
        if (owner is null || owner.Role != AgentTaskRole.Code || owner.Workspace != WorkspaceMode.Worktree
            || owner.Status != AgentTaskStatus.Succeeded || owner.CardId != card.Id || owner.ProjectId != card.Board.ProjectId
            || owner.RepoPath != repository || ReviewEvidenceBindingService.FullRef(owner.WorktreeBranch) != sourceRef)
            throw Refuse("implementation_owner_mismatch");
    }
    internal static void RequirePublication(AgentTaskLanding? operation, AgentTask owner, Guid review, string source, string? landed)
    {
        if (operation is null || !new AgentTaskLandingState().HasPublication(operation)
            || operation.TaskId != owner.Id || operation.RepositoryPath != owner.RepoPath
            || operation.SourceFullRef != ReviewEvidenceBindingService.FullRef(owner.WorktreeBranch)
            || operation.OriginalSourceSha != source || operation.ReviewEvidenceId != review
            || operation.VerifiedSourceSha != landed) throw Refuse("publication_unconfirmed");
    }
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    internal static void RequireActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new ValidationException("actor", "Authenticated actor is required.");
    }
    internal static ConflictException Refuse(string code) => new("Published card completion refused: " + code, "card_completion_" + code);
}
