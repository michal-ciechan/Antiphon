using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Read-only fleet pipeline projection (CARD-0304). Assembles DTOs in memory from bounded
/// no-tracking queries. Does not update rows, generate events, contact Git, or run dispatch.
/// </summary>
public sealed class AgentTaskPipelineStatusService
{
    internal const string QueueReasonSharedCheckoutLease = "sharedCheckoutLease";
    internal const string QueueReasonAwaitingDispatch = "awaitingDispatch";
    /// <summary>CARD-0305: waiting on its routing pin's <c>NotBefore</c>, not on a checkout.</summary>
    internal const string QueueReasonRoutingPinNotBefore = "routingPinNotBefore";
    /// <summary>
    /// CARD-0031: the fleet is at <see cref="DelegationSettings.MaxConcurrentTasks"/>. Same
    /// predicate as the dispatcher's active-task count (non-Check Dispatched/Working). Reported
    /// only after lease and pin, so a task behind a checkout still names the checkout.
    /// </summary>
    internal const string QueueReasonConcurrencyCap = "concurrencyCap";
    internal const string PlanDeliverablePrefix = "docs/superpowers/plans/";

    private static readonly AgentTaskRole[] VisibleRoles = Enum.GetValues<AgentTaskRole>()
        .Where(role => role != AgentTaskRole.Check)
        .OrderBy(role => (int)role)
        .ToArray();

    private readonly AppDbContext _db;
    private readonly DelegationSettings _settings;
    private readonly AreaMapLoader _areas;
    private readonly TimeProvider _time;

    public AgentTaskPipelineStatusService(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        AreaMapLoader areas,
        TimeProvider timeProvider)
    {
        _db = db;
        _settings = settings.Value;
        _areas = areas;
        _time = timeProvider;
    }

    public async Task<AgentTaskPipelineDto> GetAsync(CancellationToken ct)
    {
        var asOf = _time.GetUtcNow().UtcDateTime;

        var open = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Role != AgentTaskRole.Check
                && (t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working
                    || t.Status == AgentTaskStatus.Blocked))
            .Select(t => new TaskRow(
                t.Id, t.Title, t.Role, t.Status, t.CardId, t.AgentName, t.AgentKind, t.ModelLevel,
                t.CreatedAt, t.DispatchedAt, t.CompletedAt, t.AgentSessionId, t.WorkingDirectory,
                t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch, t.DeliverablePath,
                t.DeliverableRef, t.Complexity, t.FailureReason))
            .ToListAsync(ct);

        var boundPlans = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Role == AgentTaskRole.Plan && t.CardId != null)
            .Select(t => new TaskRow(
                t.Id, t.Title, t.Role, t.Status, t.CardId, t.AgentName, t.AgentKind, t.ModelLevel,
                t.CreatedAt, t.DispatchedAt, t.CompletedAt, t.AgentSessionId, t.WorkingDirectory,
                t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch, t.DeliverablePath,
                t.DeliverableRef, t.Complexity, t.FailureReason))
            .ToListAsync(ct);

        var latestPlans = boundPlans
            .GroupBy(t => t.CardId!.Value)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).First())
            .ToList();

        var candidatePlans = latestPlans
            .Where(p => p.Status == AgentTaskStatus.Succeeded
                && p.CompletedAt is not null
                && IsVerifiedPlanDeliverable(p.DeliverablePath))
            .ToList();

        var candidateCardIds = candidatePlans.Select(p => p.CardId!.Value).ToList();
        var codeByCard = candidateCardIds.Count == 0
            ? new Dictionary<Guid, List<TaskRow>>()
            : (await _db.AgentTasks.AsNoTracking()
                    .Where(t => t.Role == AgentTaskRole.Code && t.CardId != null
                        && candidateCardIds.Contains(t.CardId.Value))
                    .Select(t => new TaskRow(
                        t.Id, t.Title, t.Role, t.Status, t.CardId, t.AgentName, t.AgentKind,
                        t.ModelLevel, t.CreatedAt, t.DispatchedAt, t.CompletedAt, t.AgentSessionId,
                        t.WorkingDirectory, t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch,
                        t.DeliverablePath, t.DeliverableRef, t.Complexity, t.FailureReason))
                    .ToListAsync(ct))
                .GroupBy(t => t.CardId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

        var cardIds = open.Select(t => t.CardId)
            .Concat(candidatePlans.Select(p => p.CardId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var cards = cardIds.Count == 0
            ? new Dictionary<Guid, CardRow>()
            : await _db.Cards.AsNoTracking()
                .Where(c => cardIds.Contains(c.Id))
                .Select(c => new CardRow(c.Id, c.Identifier, c.Title, c.Status, c.ArchivedAt))
                .ToDictionaryAsync(c => c.Id, ct);

        // CARD-0305, read-only: the pins that the NEXT dispatch in each stage would resolve
        // through. Expiry is filtered rather than cleared — this projection never writes, so a
        // NotAfter that has passed is simply not returned here and the lazy clear happens the next
        // time a create or a dispatch tick reads the row.
        var activePins = await _db.RoutingPins.AsNoTracking()
            .Where(p => p.ClearedAt == null && (p.NotAfter == null || p.NotAfter > asOf))
            .ToListAsync(ct);
        var stagePins = activePins
            .Where(p => p.CardId == null)
            .ToDictionary(p => p.Role);
        var cardPins = activePins
            .Where(p => p.CardId != null)
            .ToDictionary(p => (p.CardId!.Value, p.Role));

        var ready = BuildReady(candidatePlans, codeByCard, cards, stagePins, cardPins);

        var inFlightRows = open
            .Where(t => t.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            .ToList();
        var lastActivity = await LoadLastActivityAsync(inFlightRows, ct);

        var holders = inFlightRows
            .Where(t => SharedWriterLeaseProjection.Participates(t.Workspace, t.Role))
            .Select(t => SharedWriterLeaseProjection.Holder.From(
                t.Id, t.Title, t.RepoPath, t.WorkingDirectory, t.Scope, t.Workspace, t.WorktreeBranch,
                _areas.Load(t.RepoPath)))
            .ToList();

        var queued = open.Where(t => t.Status == AgentTaskStatus.Queued).ToList();
        var blocked = open.Where(t => t.Status == AgentTaskStatus.Blocked).ToList();

        var stages = new List<AgentTaskPipelineStageDto>(VisibleRoles.Length);
        foreach (var role in VisibleRoles)
        {
            var recommended = _settings.RecommendedInFlightFor(role);
            var roleInFlight = inFlightRows
                .Where(t => t.Role == role)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => ToInFlight(t, cards, lastActivity))
                .ToList();
            var roleQueued = queued
                .Where(t => t.Role == role)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => ToQueued(t, cards, holders, stagePins, cardPins, asOf, inFlightRows.Count))
                .ToList();
            var roleBlocked = blocked
                .Where(t => t.Role == role)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => ToBlocked(t, cards))
                .ToList();
            var roleReady = role == AgentTaskRole.Code ? ready : [];

            stages.Add(new AgentTaskPipelineStageDto(
                role,
                recommended,
                roleInFlight.Count,
                recommended is int limit && roleInFlight.Count >= limit,
                roleInFlight,
                roleQueued,
                roleBlocked,
                roleReady,
                stagePins.TryGetValue(role, out var stagePin)
                    ? RoutingPinService.ToRef(stagePin, null)
                    : null));
        }

        return new AgentTaskPipelineDto(
            asOf,
            RecommendationsAreAdvisory: true,
            _settings.MaxConcurrentTasks,
            inFlightRows.Count,
            stages);
    }

    internal static bool IsVerifiedPlanDeliverable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith(PlanDeliverablePrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = normalized[PlanDeliverablePrefix.Length..^3];
        return name.Length > 0 && !normalized.Contains("..", StringComparison.Ordinal);
    }

    internal static bool CodeConsumesReadiness(TaskRow code, DateTime planCompletedAt)
    {
        if (code.Status is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched
            or AgentTaskStatus.Working or AgentTaskStatus.Blocked)
        {
            return true;
        }

        return code.CreatedAt > planCompletedAt && code.DispatchedAt is not null;
    }

    private IReadOnlyList<AgentTaskPipelineReadyDto> BuildReady(
        List<TaskRow> candidatePlans,
        Dictionary<Guid, List<TaskRow>> codeByCard,
        Dictionary<Guid, CardRow> cards,
        Dictionary<AgentTaskRole, RoutingPin> stagePins,
        Dictionary<(Guid CardId, AgentTaskRole Role), RoutingPin> cardPins)
    {
        var ready = new List<AgentTaskPipelineReadyDto>();
        foreach (var plan in candidatePlans)
        {
            if (!cards.TryGetValue(plan.CardId!.Value, out var card))
                continue;
            if (card.ArchivedAt is not null)
                continue;
            if (card.Status is CardStatus.Done or CardStatus.Canceled or CardStatus.NeedsDecision)
                continue;

            if (codeByCard.TryGetValue(card.Id, out var codes)
                && codes.Any(c => CodeConsumesReadiness(c, plan.CompletedAt!.Value)))
            {
                continue;
            }

            var codePin = EffectivePin(card.Id, AgentTaskRole.Code, stagePins, cardPins);
            ready.Add(new AgentTaskPipelineReadyDto(
                new AgentTaskPipelineCardRefDto(card.Id, card.Identifier, card.Title),
                plan.Id,
                DelegationReportFormatter.Short(plan.Id),
                plan.CompletedAt!.Value,
                plan.DeliverablePath!,
                plan.DeliverableRef,
                codePin is null ? null : RoutingPinService.ToRef(codePin, card.Identifier)));
        }

        return ready
            .OrderBy(r => r.ReadySince)
            .ThenBy(r => r.Card.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The pin a dispatch of this card+role would resolve through: the card's own pin outranks the
    /// stage-wide one AS A WHOLE ROW, exactly as <see cref="RoutingPinService.ResolveAsync"/> does.
    /// </summary>
    private static RoutingPin? EffectivePin(
        Guid? cardId,
        AgentTaskRole role,
        Dictionary<AgentTaskRole, RoutingPin> stagePins,
        Dictionary<(Guid CardId, AgentTaskRole Role), RoutingPin> cardPins)
    {
        if (role == AgentTaskRole.Check)
            return null;
        if (cardId is Guid id && cardPins.TryGetValue((id, role), out var cardPin))
            return cardPin;
        return stagePins.TryGetValue(role, out var stagePin) ? stagePin : null;
    }

    private AgentTaskPipelineQueuedDto ToQueued(
        TaskRow task,
        Dictionary<Guid, CardRow> cards,
        List<SharedWriterLeaseProjection.Holder> holders,
        Dictionary<AgentTaskRole, RoutingPin> stagePins,
        Dictionary<(Guid CardId, AgentTaskRole Role), RoutingPin> cardPins,
        DateTime asOf,
        int inFlightAgainstCap)
    {
        IReadOnlyList<AgentTaskPipelineHolderDto> heldBy = [];
        var queueReason = QueueReasonAwaitingDispatch;
        if (SharedWriterLeaseProjection.Participates(task.Workspace, task.Role))
        {
            var serialising = SharedWriterLeaseProjection.SerialisingHolders(
                holders,
                ScopeResolver.KeyFor(task.RepoPath, task.WorkingDirectory),
                task.Workspace,
                ScopeResolver.Resolve(task.Scope, _areas.Load(task.RepoPath)),
                _settings.SerialiseSharedWriters);
            if (serialising.Count > 0)
            {
                queueReason = QueueReasonSharedCheckoutLease;
                heldBy = serialising
                    .Select(o => new AgentTaskPipelineHolderDto(
                        o.Holder.TaskId,
                        DelegationReportFormatter.Short(o.Holder.TaskId),
                        o.Holder.Title))
                    .ToList();
            }
        }

        // CARD-0305: the same precedence the dispatcher applies — the lease is checked first, so a
        // task waiting on BOTH reports the checkout it is behind, not the date it is before.
        if (queueReason == QueueReasonAwaitingDispatch
            && EffectivePin(task.CardId, task.Role, stagePins, cardPins) is { NotBefore: { } notBefore }
            && notBefore > asOf)
        {
            queueReason = QueueReasonRoutingPinNotBefore;
        }

        // Lease → pin → cap, the reasons the rail can name. The dispatcher itself checks the cap
        // first and continues, but a task that is also behind a checkout still reports the checkout.
        if (queueReason == QueueReasonAwaitingDispatch
            && inFlightAgainstCap >= _settings.MaxConcurrentTasks)
        {
            queueReason = QueueReasonConcurrencyCap;
        }

        return new AgentTaskPipelineQueuedDto(
            task.Id,
            DelegationReportFormatter.Short(task.Id),
            task.Title,
            CardRef(task.CardId, cards),
            task.CreatedAt,
            queueReason,
            heldBy,
            task.AgentKind,
            task.ModelLevel,
            task.Workspace);
    }

    private static AgentTaskPipelineInFlightDto ToInFlight(
        TaskRow task,
        Dictionary<Guid, CardRow> cards,
        Dictionary<Guid, DateTime> lastActivity)
    {
        var dispatchedAt = task.DispatchedAt;
        var activity = dispatchedAt ?? task.CreatedAt;
        if (task.AgentSessionId is Guid sessionId
            && lastActivity.TryGetValue(sessionId, out var last)
            && (dispatchedAt is null || last > dispatchedAt))
        {
            activity = last;
        }

        return new AgentTaskPipelineInFlightDto(
            task.Id,
            DelegationReportFormatter.Short(task.Id),
            task.Title,
            task.Status,
            CardRef(task.CardId, cards),
            task.AgentName,
            dispatchedAt,
            activity,
            task.AgentKind,
            task.ModelLevel,
            task.Workspace);
    }

    private static AgentTaskPipelineBlockedDto ToBlocked(
        TaskRow task, Dictionary<Guid, CardRow> cards) =>
        new(
            task.Id,
            DelegationReportFormatter.Short(task.Id),
            task.Title,
            CardRef(task.CardId, cards),
            task.CreatedAt,
            task.AgentKind,
            task.ModelLevel,
            RoutingExhausted: task.Complexity is not null
                && task.FailureReason is not null
                && task.FailureReason.StartsWith(
                    ComplexityRoutingService.RoutingExhaustedPrefix, StringComparison.Ordinal));

    private static AgentTaskPipelineCardRefDto? CardRef(
        Guid? cardId, Dictionary<Guid, CardRow> cards) =>
        cardId is Guid id && cards.TryGetValue(id, out var card)
            ? new AgentTaskPipelineCardRefDto(card.Id, card.Identifier, card.Title)
            : null;

    private async Task<Dictionary<Guid, DateTime>> LoadLastActivityAsync(
        List<TaskRow> inFlight, CancellationToken ct)
    {
        var sessionIds = inFlight
            .Where(t => t.AgentSessionId is not null)
            .Select(t => t.AgentSessionId!.Value)
            .Distinct()
            .ToList();
        if (sessionIds.Count == 0)
            return [];

        var dispatchBySession = inFlight
            .Where(t => t.AgentSessionId is not null && t.DispatchedAt is not null)
            .GroupBy(t => t.AgentSessionId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(t => t.DispatchedAt));

        var rows = await _db.TranscriptEntries.AsNoTracking()
            .Where(e => sessionIds.Contains(e.AgentSessionId))
            .Select(e => new { e.AgentSessionId, e.Timestamp, e.CreatedAt })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, DateTime>();
        foreach (var group in rows.GroupBy(r => r.AgentSessionId))
        {
            dispatchBySession.TryGetValue(group.Key, out var dispatchedAt);
            var last = group
                .Select(r => r.Timestamp ?? r.CreatedAt)
                .Where(at => dispatchedAt is null || at > dispatchedAt)
                .DefaultIfEmpty()
                .Max();
            if (last != default)
                result[group.Key] = last;
        }

        return result;
    }

    internal sealed record TaskRow(
        Guid Id,
        string Title,
        AgentTaskRole Role,
        AgentTaskStatus Status,
        Guid? CardId,
        string? AgentName,
        AgentKind AgentKind,
        AgentModelLevel ModelLevel,
        DateTime CreatedAt,
        DateTime? DispatchedAt,
        DateTime? CompletedAt,
        Guid? AgentSessionId,
        string WorkingDirectory,
        string? RepoPath,
        string? Scope,
        WorkspaceMode Workspace,
        string? WorktreeBranch,
        string? DeliverablePath,
        string? DeliverableRef,
        TaskComplexity? Complexity = null,
        string? FailureReason = null);

    private sealed record CardRow(
        Guid Id, string Identifier, string Title, CardStatus Status, DateTime? ArchivedAt);
}
