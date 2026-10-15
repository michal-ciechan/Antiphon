using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Antiphon.Server.Application.Services.CardCompletionAcceptanceService;

namespace Antiphon.Server.Application.Services;

/// <summary>Proposes immutable coordinates; CardService owns the transaction that can act on them.</summary>
public sealed class PublishedCardCompletion(AppDbContext db, IOptions<CardWorkTransitionSettings> settings,
    LandDeliveryBoundary boundary, TimeProvider clock)
{
    internal AppDbContext Database => db;
    internal LandDeliveryBoundary Boundary => boundary;

    public async Task<PublishedCardCompletionProposal> ProposeAsync(Guid cardId, CancellationToken ct)
    {
        var row = await LatestAsync(db, cardId, ct) ?? throw Refuse("acceptance_missing");
        var proposal = new PublishedCardCompletionProposal(cardId, row.ExpectedCardToken, row.Revision, row.Id,
            row.WorkflowRunId, row.PipelineDefinitionRevisionId, row.CurrentStageId, row.WorkflowUpdatedAt,
            row.PolicyRevision, row.DecisionRevision);
        await boundary.ReachedAsync("completion-proposed", cardId, row.Id, ct);
        return proposal;
    }

    internal void RequirePolicy(PublishedCardCompletionProposal proposal)
    {
        if (proposal.PolicyRevision != settings.Value.PublishedClosePolicyRevision
            || proposal.DecisionRevision != settings.Value.PublishedCloseDecisionRevision)
            throw Refuse("policy_changed");
    }

    internal async Task<PublishedCloseMode> ModeAsync(CancellationToken ct)
    {
        var mode = settings.Value.PublishedClose;
        if (!Enum.IsDefined(mode)) throw Refuse("mode_invalid");
        if (mode != PublishedCloseMode.Enabled) return mode;
        var observations = settings.Value.PublishedCloseShadowObservations.ToArray();
        if (observations.Length < 30 || observations.Any(o => o.ProposedClose != o.ExpectedClose
                || string.IsNullOrWhiteSpace(o.Adjudicator) || o.AdjudicatedAt == default
                || o.AdjudicatedAt > clock.GetUtcNow().UtcDateTime)
            || observations.Select(o => o.OperationId).Distinct().Count() != observations.Length)
            throw Refuse("shadow_unqualified");
        // Count actual retained acceptance/publication joins, never a supplied numeric count.
        foreach (var observation in observations)
        {
            var acceptance = await db.CardCompletionAcceptances.AsNoTracking().SingleOrDefaultAsync(a => a.Id == observation.AcceptanceId, ct);
            var operation = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == observation.OperationId, ct);
            if (acceptance is null || operation is null || acceptance.OperationId != operation.Id
                || acceptance.ImplementationTaskId != operation.TaskId || acceptance.LandedSha != operation.VerifiedSourceSha
                || acceptance.PolicyRevision != settings.Value.PublishedClosePolicyRevision
                || acceptance.DecisionRevision != settings.Value.PublishedCloseDecisionRevision
                || acceptance.RecordedAt > observation.AdjudicatedAt || !new AgentTaskLandingState().HasPublication(operation))
                throw Refuse("shadow_evidence_missing");
        }
        return mode;
    }

    internal async Task<CardCompletionAcceptance> ValidateAsync(Card card, PublishedCardCompletionProposal proposal, CancellationToken ct)
    {
        RequirePolicy(proposal);
        var acceptance = await LatestAsync(db, card.Id, ct) ?? throw Refuse("acceptance_missing");
        if (acceptance.Id != proposal.AcceptanceId || acceptance.Revision != proposal.AcceptanceRevision)
            throw Refuse("acceptance_changed");
        if (card.ConcurrencyToken != proposal.CardToken || acceptance.ExpectedCardToken != proposal.CardToken)
            throw Refuse("card_changed");
        if (card.Status != CardStatus.Review || card.ArchivedAt is not null || card.OwnerSessionId is not null)
            throw Refuse("card_ineligible");
        if (acceptance.BoardId != card.BoardId || acceptance.ProjectId != card.Board.ProjectId
            || acceptance.PolicyRevision != proposal.PolicyRevision || acceptance.DecisionRevision != proposal.DecisionRevision)
            throw Refuse("acceptance_identity_changed");
        if (acceptance.WorkflowRunId != proposal.WorkflowRunId || acceptance.PipelineDefinitionRevisionId != proposal.PipelineDefinitionRevisionId
            || acceptance.CurrentStageId != proposal.CurrentStageId || acceptance.WorkflowUpdatedAt != proposal.WorkflowUpdatedAt)
            throw Refuse("workflow_changed");
        await RequireWorkflowAsync(db, card, proposal.WorkflowRunId, proposal.PipelineDefinitionRevisionId,
            proposal.CurrentStageId, proposal.WorkflowUpdatedAt, ct);
        var evidence = await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == acceptance.ReviewEvidenceId, ct);
        if (evidence?.StageTaskId is not Guid reviewId) throw Refuse("review_missing");
        // Shared order: card, workflow, then sorted task/evidence locks. Finding/recovery writers use these same task locks.
        await ReviewEvidenceRecoveryService.LockTasksAsync(db, [acceptance.ImplementationTaskId, reviewId], ct);
        await boundary.ReachedAsync("completion-review-locked", card.Id, reviewId, ct);
        var owner = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == acceptance.ImplementationTaskId, ct);
        RequireOwner(card, owner, acceptance.RepositoryPath, acceptance.SourceRef);
        var approval = await LandApproval.LoadUsableEvidenceAsync(db, acceptance.ReviewEvidenceId, acceptance.ReviewedSourceSha, owner!, ct);
        if (approval.CommissionedRound != VerificationRound.Final || approval.OrdinaryScopeCompleted != VerificationScope.Full)
            throw Refuse("review_not_final_full");
        var operation = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == acceptance.OperationId, ct);
        RequirePublication(operation, owner!, acceptance.ReviewEvidenceId, acceptance.ReviewedSourceSha, acceptance.LandedSha);
        // Includes Queued. Refusing all open bound work also prevents terminal settlement from silently canceling it.
        if (await db.AgentTasks.AsNoTracking().AnyAsync(t => t.CardId == card.Id
            && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked), ct))
            throw Refuse("open_task");
        CompletionItem[] items, deployments;
        EmptyCompletionDecision? empty;
        CompletionCompanion? companion;
        try
        {
            items = JsonSerializer.Deserialize<CompletionItem[]>(acceptance.AcceptanceItemsJson) ?? throw Refuse("acceptance_invalid");
            deployments = JsonSerializer.Deserialize<CompletionItem[]>(acceptance.DeploymentItemsJson) ?? throw Refuse("deployment_invalid");
            empty = acceptance.EmptyObligationsDecisionJson is null ? null : JsonSerializer.Deserialize<EmptyCompletionDecision>(acceptance.EmptyObligationsDecisionJson);
            companion = JsonSerializer.Deserialize<CompletionCompanion>(acceptance.CompanionObligationJson);
        }
        catch (JsonException) { throw Refuse("obligations_invalid"); }
        if (items.Length + deployments.Length == 0 && (empty is null || string.IsNullOrWhiteSpace(empty.Category)
            || string.IsNullOrWhiteSpace(empty.Reason) || empty.Actor != acceptance.Actor || empty.RecordedAt != acceptance.RecordedAt))
            throw Refuse("empty_decision_missing");
        if (items.Concat(deployments).Any(i => i.State != CompletionItemState.Satisfied || !Digest(i.EvidenceDigest)
            || i.Actor != acceptance.Actor || i.RecordedAt != acceptance.RecordedAt)) throw Refuse("obligations_pending");
        if (deployments.Any(i => i.RequiredBuild != acceptance.LandedSha || i.ObservedBuild != i.RequiredBuild
            || string.IsNullOrWhiteSpace(i.RequiredVersion) || i.ObservedVersion != i.RequiredVersion))
            throw Refuse("deployment_mismatch");
        if (companion is null || companion.ImplementationTaskId != owner!.Id || companion.OperationId != operation!.Id)
            throw Refuse("companion_mismatch");
        if (companion.Required && (companion.CardId is not Guid companionId || companionId == card.Id
            || !await db.Cards.AsNoTracking().AnyAsync(c => c.Id == companionId && c.BoardId == card.BoardId && c.ArchivedAt == null
                && c.Status != CardStatus.Canceled && c.Status != CardStatus.Done, ct))) throw Refuse("companion_missing");
        return acceptance;
    }
}
