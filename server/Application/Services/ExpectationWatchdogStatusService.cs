using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed record ExpectationWatchdogStatus(
    Guid BoardId,
    bool Enabled,
    string? DirectiveId,
    DateTimeOffset? ActiveUntilUtc,
    string? ConfigDigest,
    IReadOnlyList<ExpectationConfigurationFault> ConfigurationFaults,
    DateTime? LastSuccessfulScanAt,
    string? LastObservationError,
    IReadOnlyList<ExpectationStatusLane> Lanes,
    IReadOnlyList<ExpectationStatusEpisode> Episodes,
    IReadOnlyList<ExpectationStatusNudge> Nudges,
    int Skip,
    int Take,
    bool HasMore);

public sealed record ExpectationStatusLane(string RunnerId, int Target, int Running, int Queued, int Blocked);
public sealed record ExpectationStatusEpisode(Guid Id, string SubjectKey, string Kind,
    DateTime FirstObservedAt, DateTime LastObservedAt, DateTime? ResolvedAt, string Evidence);
public sealed record ExpectationStatusNudge(Guid Id, int Ordinal, DateTime CreatedAt,
    string AttemptState, DateTime? AttemptStartedAt, DateTime? AnswerDueAt,
    DateTime? ReceiptAt, long? ReceiptSequence, DateTime? AnsweredAt, long? AnsweredSequence,
    string OperatorState, int OperatorAttemptCount, DateTime? OperatorNextAttemptAt,
    DateTime? OperatorPublishedAt, Guid AuditCommentId);

/// <summary>Board-scoped, sanitized operational history; no transcript or channel address.</summary>
public sealed class ExpectationWatchdogStatusService(AppDbContext db,
    ExpectationObservationAdapter observation, IOptions<ExpectationWatchdogSettings> options,
    TimeProvider time)
{
    public async Task<ExpectationWatchdogStatus> GetAsync(Guid boardId, int skip, int take, CancellationToken ct)
    {
        var board = await db.Boards.AsNoTracking().Where(b => b.Id == boardId)
            .Select(b => b.Id).SingleOrDefaultAsync(ct);
        if (board == Guid.Empty)
            throw new NotFoundException("Board", boardId);
        var settings = options.Value;
        var directive = settings.Directives.FirstOrDefault(d => d.BoardId == boardId);
        if (directive is null)
            return new ExpectationWatchdogStatus(boardId, false, null, null, null, [], null, null,
                [], [], [], skip, take, false);

        var digest = await ExpectationConfigIdentity.ResolveAsync(db, directive, settings.Timing, ct);
        var faults = ExpectationDirectiveReferences.Evaluate(directive,
            await observation.ReferencesAsync(directive, ct));
        var state = await db.ExpectationWatchStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.DirectiveId == directive.Id, ct);
        var episodes = await db.ExpectationEpisodes.AsNoTracking()
            .Where(e => e.DirectiveId == directive.Id)
            .OrderByDescending(e => e.FirstObservedAt).ThenByDescending(e => e.Id)
            .Skip(skip).Take(take + 1)
            .Select(e => new ExpectationStatusEpisode(e.Id, e.SubjectKey, e.Kind.ToString(),
                e.FirstObservedAt, e.LastObservedAt, e.ResolvedAt, e.Evidence))
            .ToListAsync(ct);
        var nudges = await db.ExpectationNudges.AsNoTracking()
            .Where(n => n.DirectiveId == directive.Id)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(skip).Take(take)
            .Select(n => new ExpectationStatusNudge(n.Id, n.Ordinal, n.CreatedAt,
                n.AttemptState.ToString(), n.AttemptStartedAt, n.AnswerDueAt,
                n.ReceiptAt, n.ReceiptSequence, n.AnsweredAt, n.AnsweredSequence,
                n.OperatorOutboxState.ToString(), n.OperatorAttemptCount, n.OperatorNextAttemptAt,
                n.OperatorPublishedAt, n.AuditCommentId))
            .ToListAsync(ct);
        var cardIds = await db.Cards.AsNoTracking().Where(c => c.BoardId == boardId)
            .Select(c => c.Id).ToListAsync(ct);
        var tasks = await db.AgentTasks.AsNoTracking().Where(t => t.CardId != null
                && cardIds.Contains(t.CardId.Value))
            .Select(t => new { t.RunnerId, t.Status }).ToListAsync(ct);
        var lanes = directive.Targets.Select(target =>
        {
            var key = ExpectationSubjects.RunnerKey(target.RunnerId);
            var matching = tasks.Where(t => ExpectationSubjects.RunnerKey(t.RunnerId) == key).ToList();
            return new ExpectationStatusLane(key, target.InFlightTarget,
                matching.Count(t => t.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working),
                matching.Count(t => t.Status == AgentTaskStatus.Queued),
                matching.Count(t => t.Status == AgentTaskStatus.Blocked));
        }).ToList();
        return new ExpectationWatchdogStatus(boardId,
            ExpectationDirectiveActivity.HasEffects(settings, directive, time.GetUtcNow()),
            directive.Id, directive.ActiveUntilUtc, digest, faults,
            state?.LastSuccessfulScanAt, state?.LastObservationError,
            lanes, episodes.Take(take).ToList(), nudges, skip, take, episodes.Count > take);
    }
}
