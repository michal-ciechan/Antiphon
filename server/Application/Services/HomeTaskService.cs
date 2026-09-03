using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The fleet-global read projection behind <c>GET /api/home/tasks</c> (CARD-0002): one list of
/// board cards and unbound delegations, grouped for the home rail.
///
/// <para><b>No storage, no stuckness.</b> This is a status switch plus joins. It does not move
/// cards, settle tasks, or decide that anything is stuck — that stays <c>AttentionService</c>.
/// There is deliberately no question field on the DTO; the rail reads the matching
/// <c>CardNeedsDecision</c> / <c>BlockedQuestion</c> evidence from <c>GET /api/attention</c>.</para>
///
/// <para><b>A bound task is not a second item.</b> Every non-Check task with <c>CardId</c> set
/// becomes the card's <see cref="HomeTaskItemDto.Worker"/>, never its own tile. Check rows are
/// excluded from both the item list and the worker join — they are about a task, not a card.</para>
/// </summary>
public sealed class HomeTaskService
{
    private static readonly TimeSpan DoneWindow = TimeSpan.FromDays(7);
    private const int DoneCap = 60;

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    public HomeTaskService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _time = timeProvider;
    }

    public async Task<HomeTasksDto> GetAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var doneSince = now - DoneWindow;

        var cards = await LoadCardsAsync(doneSince, ct);
        var boundByCard = await LoadBoundTasksAsync(ct);
        var ownerAgents = await LoadOwnerAgentsAsync(cards, ct);
        var unbound = await LoadUnboundTasksAsync(doneSince, ct);

        var ranked = new List<RankedItem>(cards.Count + unbound.Count);
        foreach (var card in cards)
        {
            boundByCard.TryGetValue(card.Id, out var bound);
            ranked.Add(RankCard(card, bound, ownerAgents, now));
        }

        foreach (var task in unbound)
            ranked.Add(RankTask(task));

        ranked.Sort(Compare);
        var items = CapDone(ranked);
        return new HomeTasksDto(now, items);
    }

    private async Task<List<CardRow>> LoadCardsAsync(DateTime doneSince, CancellationToken ct) =>
        await _db.Cards.AsNoTracking()
            .Where(c => c.ArchivedAt == null)
            .Where(c => (c.Status != CardStatus.Done && c.Status != CardStatus.Canceled)
                        || c.CompletedAt >= doneSince)
            .Select(c => new CardRow(
                c.Id,
                c.Identifier,
                c.Title,
                c.TerminalReason,
                c.Status,
                c.Importance,
                c.Urgency,
                c.DueAt,
                c.UrgentSince,
                c.Position,
                c.BoardId,
                c.OwnerSessionId,
                c.ActiveWorkflowRun != null ? c.ActiveWorkflowRun.Status : (CardWorkflowRunStatus?)null,
                c.ActiveWorkflowRun != null && c.ActiveWorkflowRun.CurrentStage != null
                    ? c.ActiveWorkflowRun.CurrentStage.Name
                    : null,
                c.Board.Project.LocalRepositoryPath,
                c.AssignedAgent != null ? c.AssignedAgent.WorkingDirectory : null,
                c.CurrentWorktree != null ? c.CurrentWorktree.Path : null,
                c.CreatedAt,
                c.StartedAt,
                c.UpdatedAt,
                c.CompletedAt))
            .ToListAsync(ct);

    private async Task<Dictionary<Guid, List<BoundRow>>> LoadBoundTasksAsync(CancellationToken ct)
    {
        var rows = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId != null)
            .Where(AgentTaskRoles.NotSpecialist)
            .Select(t => new BoundRow(
                t.CardId!.Value,
                t.Id,
                t.Role,
                t.Status,
                t.AgentKind,
                t.ModelLevel,
                t.AgentId,
                t.AgentName,
                t.AgentSessionId,
                t.CostUsd,
                t.DispatchedAt,
                t.CompletedAt,
                t.CreatedAt,
                t.RepoPath,
                t.WorkingDirectory))
            .ToListAsync(ct);

        return rows
            .GroupBy(t => t.CardId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.DispatchedAt ?? t.CreatedAt)
                    .ThenByDescending(t => t.TaskId)
                    .ToList());
    }

    private async Task<Dictionary<Guid, Guid>> LoadOwnerAgentsAsync(
        List<CardRow> cards, CancellationToken ct)
    {
        var sessionIds = cards
            .Where(c => c.OwnerSessionId is not null)
            .Select(c => c.OwnerSessionId!.Value)
            .Distinct()
            .ToArray();
        if (sessionIds.Length == 0)
            return new Dictionary<Guid, Guid>();

        var keys = sessionIds.Select(id => id.ToString("D")).ToArray();
        var agents = await _db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null && keys.Contains(a.PersistentSessionId))
            .Select(a => new { a.Id, a.PersistentSessionId })
            .ToListAsync(ct);

        var map = new Dictionary<Guid, Guid>();
        foreach (var agent in agents)
        {
            if (Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                map[sessionId] = agent.Id;
        }

        return map;
    }

    private async Task<List<TaskRow>> LoadUnboundTasksAsync(DateTime doneSince, CancellationToken ct) =>
        await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId == null)
            .Where(AgentTaskRoles.NotSpecialist)
            .Where(t => (t.Status != AgentTaskStatus.Succeeded
                         && t.Status != AgentTaskStatus.Failed
                         && t.Status != AgentTaskStatus.Canceled)
                        || t.CompletedAt >= doneSince)
            .Select(t => new TaskRow(
                t.Id,
                t.Title,
                t.Status,
                t.Role,
                t.AgentKind,
                t.ModelLevel,
                t.EscalatedFrom,
                t.CostUsd,
                t.AgentId,
                t.AgentName,
                t.AgentSessionId,
                t.ReadAt,
                t.DeliverablePath,
                t.DeliverableRef,
                t.WorkingDirectory,
                t.RepoPath,
                t.WorktreePath,
                t.CreatedAt,
                t.DispatchedAt,
                t.CompletedAt))
            .ToListAsync(ct);

    private static RankedItem RankCard(
        CardRow card,
        List<BoundRow>? bound,
        IReadOnlyDictionary<Guid, Guid> ownerAgents,
        DateTime now)
    {
        BoundRow? openBound = null;
        BoundRow? newest = null;
        if (bound is { Count: > 0 })
        {
            newest = bound[0];
            openBound = bound.FirstOrDefault(t => IsOpenBound(t.Status));
        }

        var workerRow = openBound ?? newest;
        var (group, reason) = ClassifyCard(card.Status, card.WorkflowRunStatus, openBound?.Status, card.OwnerSessionId);
        var worker = workerRow is null ? null : ToWorker(workerRow);
        var stage = card.StageName ?? (newest is null ? null : newest.Role.ToString());
        Guid? ownerAgentId = card.OwnerSessionId is { } sessionId
            && ownerAgents.TryGetValue(sessionId, out var agentId)
                ? agentId
                : null;

        var workingDirectory = card.ProjectPath
            ?? card.AssignedAgentDirectory
            ?? workerRow?.RepoPath
            ?? workerRow?.WorkingDirectory;

        var effective = CardRanking.EffectiveUrgency(card.Urgency, card.DueAt, now);
        var order = CardRanking.OrderKey(card.Importance, card.Urgency, card.DueAt, card.Position, card.CreatedAt, now);
        var item = new HomeTaskItemDto(
            Key: $"card:{card.Id:N}",
            Source: HomeTaskSource.Card,
            Id: card.Id,
            Identifier: card.Identifier,
            Title: card.Title,
            TerminalReason: card.TerminalReason,
            Group: group,
            State: card.Status.ToString(),
            HumanReason: reason,
            Stage: stage,
            WorkflowRunStatus: card.WorkflowRunStatus,
            Importance: card.Importance,
            EffectiveUrgency: effective,
            Quadrant: CardRanking.Quadrant(card.Importance, effective),
            Rank: order.Rank,
            UrgentSince: card.UrgentSince,
            BoardId: card.BoardId,
            Worker: worker,
            OwnerAgentId: ownerAgentId,
            AgentKind: null,
            ModelLevel: null,
            EscalatedFrom: null,
            Role: null,
            CostUsd: null,
            AgentId: null,
            AgentName: null,
            AgentSessionId: null,
            ReadAt: null,
            DeliverablePath: null,
            DeliverableRef: null,
            WorkingDirectory: workingDirectory,
            RepoPath: null,
            WorktreePath: card.WorktreePath,
            CreatedAt: card.CreatedAt,
            StartedAt: card.StartedAt,
            UpdatedAt: card.UpdatedAt,
            CompletedAt: card.CompletedAt);

        var waitingSince = reason == HomeTaskHumanReason.Question
            ? workerRow?.CompletedAt ?? workerRow?.DispatchedAt ?? workerRow?.CreatedAt ?? card.UpdatedAt
            : card.UpdatedAt;
        var runningAt = openBound?.DispatchedAt ?? card.StartedAt ?? card.CreatedAt;

        return new RankedItem(
            item,
            ReasonRank(reason),
            waitingSince,
            runningAt,
            NextSourceRank: 0,
            order.Rank,
            order.Position,
            order.Due,
            card.CreatedAt,
            card.UpdatedAt,
            card.CompletedAt ?? DateTime.MinValue);
    }

    private static RankedItem RankTask(TaskRow task)
    {
        var (group, reason) = ClassifyTask(task.Status);
        var item = new HomeTaskItemDto(
            Key: $"task:{task.Id:N}",
            Source: HomeTaskSource.Delegation,
            Id: task.Id,
            Identifier: DelegationReportFormatter.Short(task.Id),
            Title: task.Title,
            TerminalReason: null,
            Group: group,
            State: task.Status.ToString(),
            HumanReason: reason,
            Stage: task.Role.ToString(),
            WorkflowRunStatus: null,
            Importance: null,
            EffectiveUrgency: null,
            Quadrant: null,
            Rank: null,
            UrgentSince: null,
            BoardId: null,
            Worker: null,
            OwnerAgentId: null,
            AgentKind: task.AgentKind,
            ModelLevel: task.ModelLevel,
            EscalatedFrom: task.EscalatedFrom,
            Role: task.Role,
            CostUsd: task.CostUsd,
            AgentId: task.AgentId,
            AgentName: task.AgentName,
            AgentSessionId: task.AgentSessionId,
            ReadAt: task.ReadAt,
            DeliverablePath: task.DeliverablePath,
            DeliverableRef: task.DeliverableRef,
            WorkingDirectory: task.WorkingDirectory,
            RepoPath: task.RepoPath,
            WorktreePath: task.WorktreePath,
            CreatedAt: task.CreatedAt,
            StartedAt: task.DispatchedAt,
            UpdatedAt: task.CompletedAt ?? task.DispatchedAt ?? task.CreatedAt,
            CompletedAt: task.CompletedAt);

        var waitingSince = task.CompletedAt ?? task.DispatchedAt ?? task.CreatedAt;
        var runningAt = task.DispatchedAt ?? task.CreatedAt;

        return new RankedItem(
            item,
            ReasonRank(reason),
            waitingSince,
            runningAt,
            NextSourceRank: 1,
            Rank: 10,
            Position: int.MaxValue,
            Due: DateTime.MaxValue,
            task.CreatedAt,
            item.UpdatedAt,
            task.CompletedAt ?? DateTime.MinValue);
    }

    private static (HomeTaskGroup Group, HomeTaskHumanReason? Reason) ClassifyCard(
        CardStatus status,
        CardWorkflowRunStatus? workflowStatus,
        AgentTaskStatus? openBoundStatus,
        Guid? ownerSessionId)
    {
        if (status == CardStatus.NeedsDecision)
            return (HomeTaskGroup.NeedsHuman, HomeTaskHumanReason.Decision);
        if (workflowStatus == CardWorkflowRunStatus.WaitingForHumanReview)
            return (HomeTaskGroup.NeedsHuman, HomeTaskHumanReason.Gate);
        if (openBoundStatus == AgentTaskStatus.Blocked)
            return (HomeTaskGroup.NeedsHuman, HomeTaskHumanReason.Question);

        var runningWork = openBoundStatus is AgentTaskStatus.Dispatched or AgentTaskStatus.Working;
        if (status == CardStatus.InProgress
            && (runningWork
                || ownerSessionId is not null
                || workflowStatus == CardWorkflowRunStatus.Running))
            return (HomeTaskGroup.Running, null);

        return status switch
        {
            CardStatus.Review => (HomeTaskGroup.Review, HomeTaskHumanReason.Review),
            CardStatus.Done => (HomeTaskGroup.Done, null),
            CardStatus.Canceled => (HomeTaskGroup.Done, null),
            CardStatus.Backlog => (HomeTaskGroup.Next, null),
            CardStatus.InProgress => (HomeTaskGroup.Next, null),
            CardStatus.NeedsDecision => (HomeTaskGroup.NeedsHuman, HomeTaskHumanReason.Decision),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    private static (HomeTaskGroup Group, HomeTaskHumanReason? Reason) ClassifyTask(AgentTaskStatus status) =>
        status switch
        {
            AgentTaskStatus.Blocked => (HomeTaskGroup.NeedsHuman, HomeTaskHumanReason.Question),
            AgentTaskStatus.Dispatched or AgentTaskStatus.Working => (HomeTaskGroup.Running, null),
            AgentTaskStatus.Queued => (HomeTaskGroup.Next, null),
            AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled
                => (HomeTaskGroup.Done, null),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    private static bool IsOpenBound(AgentTaskStatus status) =>
        status is AgentTaskStatus.Queued
            or AgentTaskStatus.Dispatched
            or AgentTaskStatus.Working
            or AgentTaskStatus.Blocked;

    private static int ReasonRank(HomeTaskHumanReason? reason) => reason switch
    {
        HomeTaskHumanReason.Decision => 0,
        HomeTaskHumanReason.Question => 1,
        HomeTaskHumanReason.Gate => 2,
        HomeTaskHumanReason.Review => 3,
        null => 99,
        _ => 99,
    };

    private static HomeTaskWorkerDto ToWorker(BoundRow row) => new(
        row.TaskId,
        DelegationReportFormatter.Short(row.TaskId),
        row.Role,
        row.Status,
        row.AgentKind,
        row.ModelLevel,
        row.AgentId,
        row.AgentName,
        row.AgentSessionId,
        row.CostUsd,
        row.DispatchedAt,
        row.CompletedAt);

    private static int Compare(RankedItem a, RankedItem b)
    {
        var group = a.Item.Group.CompareTo(b.Item.Group);
        if (group != 0) return group;

        switch (a.Item.Group)
        {
            case HomeTaskGroup.NeedsHuman:
            {
                var reason = a.ReasonRank.CompareTo(b.ReasonRank);
                if (reason != 0) return reason;
                var waiting = a.WaitingSince.CompareTo(b.WaitingSince);
                if (waiting != 0) return waiting;
                break;
            }
            case HomeTaskGroup.Running:
            {
                var running = b.RunningAt.CompareTo(a.RunningAt);
                if (running != 0) return running;
                break;
            }
            case HomeTaskGroup.Review:
            {
                var updated = a.UpdatedAt.CompareTo(b.UpdatedAt);
                if (updated != 0) return updated;
                break;
            }
            case HomeTaskGroup.Next:
            {
                var source = a.NextSourceRank.CompareTo(b.NextSourceRank);
                if (source != 0) return source;
                var rank = a.Rank.CompareTo(b.Rank);
                if (rank != 0) return rank;
                var position = a.Position.CompareTo(b.Position);
                if (position != 0) return position;
                var due = a.Due.CompareTo(b.Due);
                if (due != 0) return due;
                var created = a.CreatedAt.CompareTo(b.CreatedAt);
                if (created != 0) return created;
                break;
            }
            case HomeTaskGroup.Done:
            {
                var completed = b.CompletedAt.CompareTo(a.CompletedAt);
                if (completed != 0) return completed;
                break;
            }
        }

        return string.CompareOrdinal(a.Item.Key, b.Item.Key);
    }

    private static List<HomeTaskItemDto> CapDone(List<RankedItem> ranked)
    {
        var items = new List<HomeTaskItemDto>(ranked.Count);
        var done = 0;
        foreach (var row in ranked)
        {
            if (row.Item.Group == HomeTaskGroup.Done)
            {
                if (done >= DoneCap) continue;
                done++;
            }

            items.Add(row.Item);
        }

        return items;
    }

    private sealed record CardRow(
        Guid Id,
        string Identifier,
        string Title,
        string? TerminalReason,
        CardStatus Status,
        CardImportance Importance,
        CardUrgency Urgency,
        DateTime? DueAt,
        DateTime? UrgentSince,
        int? Position,
        Guid BoardId,
        Guid? OwnerSessionId,
        CardWorkflowRunStatus? WorkflowRunStatus,
        string? StageName,
        string? ProjectPath,
        string? AssignedAgentDirectory,
        string? WorktreePath,
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime UpdatedAt,
        DateTime? CompletedAt);

    private sealed record BoundRow(
        Guid CardId,
        Guid TaskId,
        AgentTaskRole Role,
        AgentTaskStatus Status,
        AgentKind AgentKind,
        AgentModelLevel ModelLevel,
        Guid? AgentId,
        string? AgentName,
        Guid? AgentSessionId,
        decimal CostUsd,
        DateTime? DispatchedAt,
        DateTime? CompletedAt,
        DateTime CreatedAt,
        string? RepoPath,
        string WorkingDirectory);

    private sealed record TaskRow(
        Guid Id,
        string Title,
        AgentTaskStatus Status,
        AgentTaskRole Role,
        AgentKind AgentKind,
        AgentModelLevel ModelLevel,
        AgentModelLevel? EscalatedFrom,
        decimal CostUsd,
        Guid? AgentId,
        string? AgentName,
        Guid? AgentSessionId,
        DateTime? ReadAt,
        string? DeliverablePath,
        string? DeliverableRef,
        string WorkingDirectory,
        string? RepoPath,
        string? WorktreePath,
        DateTime CreatedAt,
        DateTime? DispatchedAt,
        DateTime? CompletedAt);

    private sealed record RankedItem(
        HomeTaskItemDto Item,
        int ReasonRank,
        DateTime WaitingSince,
        DateTime RunningAt,
        int NextSourceRank,
        int Rank,
        int Position,
        DateTime Due,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime CompletedAt);
}
