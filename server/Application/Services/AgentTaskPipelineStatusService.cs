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
    /// <summary>
    /// CARD-0301 / CARD-0215: a same-card Worktree sibling has a pending land
    /// (<c>AgentTasks.LandRequestedAt</c>). Matches the dispatcher's hold without the Git
    /// probes. CARD-0331 moved this off LandRequested/Landed/LandRefused event rows onto the column.
    /// </summary>
    internal const string QueueReasonSiblingLandInFlight = "siblingLandInFlight";
    internal const string QueueReasonAwaitingDispatch = "awaitingDispatch";
    /// <summary>CARD-0305: waiting on its routing pin's <c>NotBefore</c>, not on a checkout.</summary>
    internal const string QueueReasonRoutingPinNotBefore = "routingPinNotBefore";
    /// <summary>
    /// CARD-0031: the fleet is at <see cref="DelegationSettings.MaxConcurrentTasks"/>. Same
    /// predicate as the dispatcher's active-task count (non-specialist Dispatched/Working). Reported
    /// only after lease and pin, so a task behind a checkout still names the checkout.
    /// </summary>
    internal const string QueueReasonConcurrencyCap = "concurrencyCap";
    internal const string PlanDeliverablePrefix = "docs/superpowers/plans/";

    private static readonly AgentTaskRole[] VisibleRoles = Enum.GetValues<AgentTaskRole>()
        .Where(role => !AgentTaskRoles.IsSpecialist(role))
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
            .Where(AgentTaskRoles.NotSpecialist)
            .Where(t => t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working
                    || t.Status == AgentTaskStatus.Blocked)
            .Select(t => new TaskRow(
                t.Id, t.Title, t.Role, t.Status, t.CardId, t.AgentName, t.AgentKind, t.ModelLevel,
                t.CreatedAt, t.DispatchedAt, t.CompletedAt, t.AgentSessionId, t.WorkingDirectory,
                t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch, t.DeliverablePath,
                t.DeliverableRef, t.Complexity, t.FailureReason, t.NextStage, t.NextHandoff, t.RoutingPinId))
            .ToListAsync(ct);

        var boundStages = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId != null)
            .Where(AgentTaskRoles.Stage)
            .Select(t => new TaskRow(
                t.Id, t.Title, t.Role, t.Status, t.CardId, t.AgentName, t.AgentKind, t.ModelLevel,
                t.CreatedAt, t.DispatchedAt, t.CompletedAt, t.AgentSessionId, t.WorkingDirectory,
                t.RepoPath, t.Scope, t.Workspace, t.WorktreeBranch, t.DeliverablePath,
                t.DeliverableRef, t.Complexity, t.FailureReason, t.NextStage, t.NextHandoff, t.RoutingPinId))
            .ToListAsync(ct);

        var cardIds = open.Select(t => t.CardId)
            .Concat(boundStages.Select(t => t.CardId))
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

        var ready = BuildReady(boundStages, cards, stagePins, cardPins);

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
        var siblingLands = await LoadSiblingLandHoldersAsync(queued, ct);

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
                .Select(t => ToQueued(t, cards, holders, stagePins, cardPins, asOf, inFlightRows.Count, siblingLands))
                .ToList();
            var roleBlocked = blocked
                .Where(t => t.Role == role)
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .Select(t => ToBlocked(t, cards))
                .ToList();
            var roleReady = ready.TryGetValue(role, out var rows) ? rows : [];

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

    internal static bool CodeConsumesReadiness(TaskRow code, DateTime planCompletedAt) =>
        RoleConsumesReadiness(code, planCompletedAt);

    internal static bool RoleConsumesReadiness(TaskRow task, DateTime sourceCompletedAt)
    {
        if (task.Status is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched
            or AgentTaskStatus.Working or AgentTaskStatus.Blocked)
        {
            return true;
        }

        return task.CreatedAt > sourceCompletedAt && task.DispatchedAt is not null;
    }

    /// <summary>
    /// CARD-0146 S4 / D7: a settled Succeeded stage-role task whose <c>NextStage</c> is a
    /// pipeline stage X, whose card has no open or newer task in role X, and whose card is not
    /// terminal / NeedsDecision / archived, is a ready row under X. A Plan with
    /// <c>NextStage == null</c> and a verified plan-doc deliverable still yields the legacy
    /// Code row. <c>land</c> / <c>decide</c> / <c>none</c> produce none.
    /// </summary>
    private Dictionary<AgentTaskRole, List<AgentTaskPipelineReadyDto>> BuildReady(
        List<TaskRow> boundStages,
        Dictionary<Guid, CardRow> cards,
        Dictionary<AgentTaskRole, RoutingPin> stagePins,
        Dictionary<(Guid CardId, AgentTaskRole Role), RoutingPin> cardPins)
    {
        var ready = new List<(AgentTaskRole Target, AgentTaskPipelineReadyDto Row)>();
        foreach (var group in boundStages
            .Where(t => t.CardId is not null)
            .GroupBy(t => t.CardId!.Value))
        {
            if (!cards.TryGetValue(group.Key, out var card))
                continue;
            if (card.ArchivedAt is not null)
                continue;
            if (card.Status is CardStatus.Done or CardStatus.Canceled or CardStatus.NeedsDecision)
                continue;

            var tasks = group.ToList();
            var sources = new List<(TaskRow Source, AgentTaskRole Target)>();
            foreach (var task in tasks)
            {
                if (task.Status != AgentTaskStatus.Succeeded || task.CompletedAt is null)
                    continue;

                if (task.NextStage is { } kind
                    && PipelineHandoff.TryToStageRole(kind, out var target))
                {
                    sources.Add((task, target));
                }
                else if (task.NextStage is null
                    && task.Role == AgentTaskRole.Plan
                    && IsVerifiedPlanDeliverable(task.DeliverablePath))
                {
                    sources.Add((task, AgentTaskRole.Code));
                }
            }

            foreach (var byTarget in sources.GroupBy(s => s.Target))
            {
                var picked = byTarget
                    .OrderByDescending(s => s.Source.CompletedAt)
                    .ThenByDescending(s => s.Source.Id)
                    .First();
                if (tasks.Any(t => t.Role == byTarget.Key
                    && RoleConsumesReadiness(t, picked.Source.CompletedAt!.Value)))
                {
                    continue;
                }

                var pin = EffectivePin(card.Id, byTarget.Key, stagePins, cardPins);
                ready.Add((byTarget.Key, new AgentTaskPipelineReadyDto(
                    new AgentTaskPipelineCardRefDto(card.Id, card.Identifier, card.Title),
                    picked.Source.Id,
                    DelegationReportFormatter.Short(picked.Source.Id),
                    picked.Source.CompletedAt!.Value,
                    picked.Source.DeliverablePath ?? "",
                    picked.Source.DeliverableRef,
                    picked.Source.Role,
                    picked.Source.NextHandoff,
                    pin is null ? null : RoutingPinService.ToRef(pin, card.Identifier))));
            }
        }

        return ready
            .GroupBy(r => r.Target)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Row)
                    .OrderBy(r => r.ReadySince)
                    .ThenBy(r => r.Card.Identifier, StringComparer.OrdinalIgnoreCase)
                    .ToList());
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
        if (AgentTaskRoles.IsSpecialist(role))
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
        int inFlightAgainstCap,
        Dictionary<Guid, SiblingLandRow> siblingLands)
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

        // CARD-0301 / CARD-0146 S4: lease → sibling land → pin → cap. The dispatcher holds a
        // card-bound Worktree task (any IsStage pair, and helpers for the same git reason)
        // while a same-card sibling's LandRequestedAt is set (CARD-0331); name that hold here
        // without the Git probes the dispatcher adds.
        if (queueReason == QueueReasonAwaitingDispatch
            && task.CardId is Guid cardId
            && task.Workspace == WorkspaceMode.Worktree
            && siblingLands.TryGetValue(cardId, out var sibling)
            && sibling.Id != task.Id)
        {
            queueReason = QueueReasonSiblingLandInFlight;
            heldBy = [new AgentTaskPipelineHolderDto(
                sibling.Id,
                DelegationReportFormatter.Short(sibling.Id),
                sibling.Title)];
        }

        // CARD-0305: the same precedence the dispatcher applies — the lease is checked first, so a
        // task waiting on BOTH reports the checkout it is behind, not the date it is before.
        if (queueReason == QueueReasonAwaitingDispatch
            && EffectivePin(task.CardId, task.Role, stagePins, cardPins) is { NotBefore: { } notBefore }
            && notBefore > asOf)
        {
            queueReason = QueueReasonRoutingPinNotBefore;
        }

        // Lease → sibling land → pin → cap, the reasons the rail can name. The dispatcher itself
        // checks the cap first and continues, but a task that is also behind a checkout still
        // reports the checkout.
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
            RoutingExhausted: (task.Complexity is not null || task.RoutingPinId is not null)
                && task.FailureReason is not null
                && task.FailureReason.StartsWith(
                    ComplexityRoutingService.RoutingExhaustedPrefix, StringComparison.Ordinal));

    private static AgentTaskPipelineCardRefDto? CardRef(
        Guid? cardId, Dictionary<Guid, CardRow> cards) =>
        cardId is Guid id && cards.TryGetValue(id, out var card)
            ? new AgentTaskPipelineCardRefDto(card.Id, card.Identifier, card.Title)
            : null;

    /// <summary>
    /// CARD-0331: pending lands are <c>LandRequestedAt != null</c> on Succeeded/Blocked Worktree
    /// siblings — the same column the dispatcher reads. One query keyed by the queued rows' card
    /// ids; never contacts Git.
    /// </summary>
    private async Task<Dictionary<Guid, SiblingLandRow>> LoadSiblingLandHoldersAsync(
        List<TaskRow> queued, CancellationToken ct)
    {
        var cardIds = queued
            .Where(t => t.CardId is not null && t.Workspace == WorkspaceMode.Worktree)
            .Select(t => t.CardId!.Value)
            .Distinct()
            .ToList();
        if (cardIds.Count == 0)
            return [];

        var rows = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId != null
                && cardIds.Contains(t.CardId.Value)
                && t.Workspace == WorkspaceMode.Worktree
                && t.WorktreeBranch != null
                && t.LandRequestedAt != null
                && (t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Blocked))
            .Select(t => new SiblingLandRow(t.Id, t.Title, t.CardId!.Value, t.LandRequestedAt!.Value))
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.CardId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.LandRequestedAt).ThenBy(r => r.Id).First());
    }

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
        string? FailureReason = null,
        PipelineHandoffKind? NextStage = null,
        string? NextHandoff = null,
        Guid? RoutingPinId = null);

    private sealed record SiblingLandRow(Guid Id, string Title, Guid CardId, DateTime LandRequestedAt);

    private sealed record CardRow(
        Guid Id, string Identifier, string Title, CardStatus Status, DateTime? ArchivedAt);
}
