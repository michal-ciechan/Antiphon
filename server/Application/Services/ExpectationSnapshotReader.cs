using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Read-only board snapshot. Does not tick dispatch, run git, or touch a repository journal.
/// </summary>
public sealed class ExpectationSnapshotReader
{
    private static readonly AgentTaskStatus[] OpenStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
        AgentTaskStatus.Blocked,
    ];

    private static readonly SessionStatus[] LiveSessions =
    [
        SessionStatus.Starting,
        SessionStatus.Running,
        SessionStatus.Stopping,
    ];

    private readonly AppDbContext _db;

    public ExpectationSnapshotReader(AppDbContext db) => _db = db;

    public async Task<ExpectationSnapshot> ReadAsync(
        ExpectationDirectiveSettings directive,
        string configDigest,
        DateTime asOf,
        ExpectationProbeInput probes,
        CancellationToken ct)
    {
        asOf = SpecifyUtc(asOf);
        var active = ExpectationDirectiveActivity.HasEffects(
            new ExpectationWatchdogSettings { Enabled = true, Directives = [directive] },
            directive,
            new DateTimeOffset(asOf, TimeSpan.Zero));
        if (probes.Unavailable)
        {
            return new ExpectationSnapshot
            {
                AsOf = asOf,
                DirectiveActive = active,
                ProbeUnknown = true,
                ProbeError = probes.Error ?? "probe unavailable",
            };
        }

        var board = await _db.Boards.AsNoTracking()
            .Where(row => row.Id == directive.BoardId)
            .Select(row => new { row.Id, row.ProjectId })
            .SingleOrDefaultAsync(ct);
        if (board is null)
        {
            return new ExpectationSnapshot
            {
                AsOf = asOf,
                DirectiveActive = false,
                ProbeUnknown = true,
                ProbeError = "board not found",
            };
        }

        var scope = new ExpectationScopeContext(board.Id, board.ProjectId, directive.AgentId);
        var windowStart = asOf - ExpectationWindows.Queue;
        var cardBindings = await _db.Cards.AsNoTracking()
            .Where(card => card.BoardId == board.Id || card.Board.ProjectId == board.ProjectId)
            .Select(card => new { card.Id, card.BoardId, ProjectId = card.Board.ProjectId })
            .ToListAsync(ct);
        var cardById = cardBindings.ToDictionary(card => card.Id);
        var cardIds = cardById.Keys.ToList();

        var taskQuery = _db.AgentTasks.AsNoTracking().Where(task =>
            OpenStatuses.Contains(task.Status)
            || (task.DispatchedAt != null && task.DispatchedAt > windowStart && task.DispatchedAt <= asOf));
        taskQuery = cardIds.Count == 0
            ? taskQuery.Where(task => task.CardId == null && task.ProjectId == board.ProjectId)
            : taskQuery.Where(task =>
                (task.CardId != null && cardIds.Contains(task.CardId.Value))
                || (task.CardId == null && task.ProjectId == board.ProjectId));

        var loaded = await taskQuery
            .Select(task => new TaskRow(
                task.Id,
                task.CardId,
                task.ProjectId,
                task.ParentSessionId,
                task.Status,
                task.Role,
                task.RunnerId,
                task.RepoPath,
                task.CreatedAt))
            .ToListAsync(ct);

        var parentIds = loaded
            .Where(task => task.CardId is null && task.ParentSessionId is not null)
            .Select(task => task.ParentSessionId!.Value)
            .Distinct()
            .ToList();
        var parents = parentIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _db.AgentSessions.AsNoTracking()
                .Where(session => parentIds.Contains(session.Id))
                .Select(session => new { session.Id, session.StandingAgentId })
                .ToDictionaryAsync(session => session.Id, session => session.StandingAgentId, ct);

        var included = new List<ScopedTask>();
        var ambiguous = 0;
        foreach (var task in loaded)
        {
            Guid? cardBoard = null;
            Guid? cardProject = null;
            if (task.CardId is Guid cardId && cardById.TryGetValue(cardId, out var binding))
            {
                cardBoard = binding.BoardId;
                cardProject = binding.ProjectId;
            }

            Guid? standing = null;
            var sessionKnown = false;
            if (task.ParentSessionId is Guid parentId && parents.TryGetValue(parentId, out var owner))
            {
                sessionKnown = true;
                standing = owner;
            }

            var decision = ExpectationScope.Decide(
                new ExpectationTaskScopeFacts(cardBoard, task.ProjectId, cardProject, standing, sessionKnown),
                scope);
            if (decision == ExpectationScopeDecision.AmbiguousUnbound)
                ambiguous++;
            if (decision != ExpectationScopeDecision.Included)
                continue;
            included.Add(new ScopedTask(task, ExpectationRepository.For(task.RepoPath, board.Id)));
        }

        var throughput = included.Where(task => !AgentTaskRoles.IsSpecialist(task.Row.Role)).ToList();
        var watchIds = throughput.Select(task => task.Row.Id).ToList();
        var events = watchIds.Count == 0
            ? []
            : await _db.AgentTaskEvents.AsNoTracking()
                .Where(row => watchIds.Contains(row.AgentTaskId)
                    && row.At <= asOf
                    && (row.Type == AgentTaskEventType.Dispatched
                        || row.Type == AgentTaskEventType.Created
                        || row.Type == AgentTaskEventType.Retried
                        || row.Type == AgentTaskEventType.Escalated
                        || row.Type == AgentTaskEventType.Rerouted
                        || row.Type == AgentTaskEventType.Held
                        || row.Type == AgentTaskEventType.HeldAged))
                .Select(row => new EventRow(row.Id, row.AgentTaskId, row.Type, row.At, row.Detail))
                .ToListAsync(ct);

        DateTime? lastDispatch = null;
        foreach (var row in events)
        {
            if (row.Type != AgentTaskEventType.Dispatched)
                continue;
            if (lastDispatch is null || row.At > lastDispatch)
                lastDispatch = row.At;
        }

        var queued = new List<ExpectationQueuedTask>();
        foreach (var task in throughput.Where(task => task.Row.Status == AgentTaskStatus.Queued))
        {
            var taskEvents = events.Where(row => row.TaskId == task.Row.Id).ToList();
            var stint = ExpectationQueueStint.Start(
                SpecifyUtc(task.Row.CreatedAt),
                taskEvents.Select(row => new ExpectationStintEvent(row.Type, SpecifyUtc(row.At))));
            var hold = taskEvents
                .Where(row =>
                    (row.Type is AgentTaskEventType.Held or AgentTaskEventType.HeldAged)
                    && SpecifyUtc(row.At) >= stint)
                .OrderByDescending(row => row.At)
                .ThenByDescending(row => row.Id)
                .FirstOrDefault();
            ExpectationHoldClass? holdClass = hold is null
                ? null
                : DispatchHoldDetails.Classify(hold.Detail).Class;
            if (holdClass is null
                && probes.Runners.TryGetValue(ExpectationSubjects.RunnerKey(task.Row.RunnerId), out var runnerProbe))
            {
                holdClass = runnerProbe.Available switch
                {
                    null => ExpectationHoldClass.Unknown,
                    false => ExpectationHoldClass.RunnerUnavailable,
                    _ => null,
                };
            }

            queued.Add(new ExpectationQueuedTask
            {
                TaskId = task.Row.Id,
                RunnerId = task.Row.RunnerId,
                RepositoryScope = task.RepositoryScope,
                StintStartedAt = stint,
                HoldClass = holdClass,
                HoldDetail = hold?.Detail,
                HoldObservedAt = hold is null ? null : SpecifyUtc(hold.At),
            });
        }

        var state = await _db.ExpectationWatchStates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.DirectiveId == directive.Id, ct);
        var configChanged = state is not null && !string.Equals(state.ConfigDigest, configDigest, StringComparison.Ordinal);
        var episodes = await _db.ExpectationEpisodes.AsNoTracking()
            .Where(row => row.DirectiveId == directive.Id && row.ResolvedAt == null && row.ConfigDigest == configDigest)
            .Select(row => new ExpectationOpenEpisode
            {
                Kind = row.Kind,
                SubjectKey = row.SubjectKey,
                FirstObservedAt = row.FirstObservedAt,
                Evidence = row.Evidence,
            })
            .ToListAsync(ct);

        var lanes = new List<ExpectationLaneSnapshot>();
        foreach (var target in directive.Targets ?? [])
        {
            var key = ExpectationSubjects.RunnerKey(target.RunnerId);
            var matching = throughput.Where(task =>
                ExpectationSubjects.SameRunner(task.Row.RunnerId, target.RunnerId)).ToList();
            var subject = ExpectationSubjects.Capacity(directive.Id, target.RunnerId);
            var deficit = episodes.FirstOrDefault(row =>
                row.Kind == ExpectationEpisodeKind.CapacityDeficit && row.SubjectKey == subject);
            lanes.Add(new ExpectationLaneSnapshot
            {
                RunnerId = string.IsNullOrWhiteSpace(target.RunnerId) ? null : target.RunnerId.Trim(),
                Target = target.InFlightTarget,
                Running = matching.Count(task =>
                    task.Row.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working),
                Queued = matching.Count(task => task.Row.Status == AgentTaskStatus.Queued),
                Blocked = matching.Count(task => task.Row.Status == AgentTaskStatus.Blocked),
                DeficitSince = configChanged ? null : deficit?.FirstObservedAt,
                RunningTaskIds = matching
                    .Where(task => task.Row.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
                    .Select(task => task.Row.Id)
                    .ToList(),
                QueuedTaskIds = matching
                    .Where(task => task.Row.Status == AgentTaskStatus.Queued)
                    .Select(task => task.Row.Id)
                    .ToList(),
            });
        }

        var backlog = await ReadBacklogAsync(board.Id, ct);
        return new ExpectationSnapshot
        {
            AsOf = asOf,
            DirectiveActive = active,
            ConfigChanged = configChanged,
            RepositoryScope = ExpectationRepository.For(null, board.Id),
            LastScopedDispatchAt = lastDispatch is null ? null : SpecifyUtc(lastDispatch.Value),
            EligibleBacklog = backlog.Count,
            BacklogCandidateIds = backlog,
            AmbiguousUnboundExcluded = ambiguous,
            IncludedTaskIds = throughput.Select(task => task.Row.Id).ToList(),
            Queued = queued,
            Lanes = lanes,
            Admission = Admission(directive, probes, asOf),
            OpenEpisodes = episodes,
        };
    }

    private async Task<IReadOnlyList<Guid>> ReadBacklogAsync(Guid boardId, CancellationToken ct)
    {
        var cards = await _db.Cards.AsNoTracking()
            .Include(card => card.ExternalIssueRef)
            .Where(card => card.BoardId == boardId
                && card.Status == CardStatus.Backlog
                && card.ArchivedAt == null)
            .ToListAsync(ct);
        if (cards.Count == 0)
            return [];

        var cardIds = cards.Select(card => card.Id).ToList();
        var liveCardIds = await _db.AgentSessions.AsNoTracking()
            .Where(session => session.CardId != null
                && cardIds.Contains(session.CardId.Value)
                && LiveSessions.Contains(session.Status))
            .Select(session => session.CardId!.Value)
            .ToListAsync(ct);
        var openCardIds = await _db.AgentTasks.AsNoTracking()
            .Where(task => task.CardId != null
                && cardIds.Contains(task.CardId.Value)
                && OpenStatuses.Contains(task.Status))
            .Select(task => task.CardId!.Value)
            .ToListAsync(ct);
        var live = liveCardIds.ToHashSet();
        var open = openCardIds.ToHashSet();
        return cards
            .Where(card => card.AssignedAgentId is null
                && card.OwnerSessionId is null
                && card.AutoDispatchHeldAt is null
                && !BoardService.NeedsHumanReview(card)
                && !live.Contains(card.Id)
                && !open.Contains(card.Id))
            .Select(card => card.Id)
            .ToList();
    }

    private static List<ExpectationAdmissionCandidate> Admission(
        ExpectationDirectiveSettings directive, ExpectationProbeInput probes, DateTime asOf)
    {
        var settings = probes.QuotaSettings ?? new SubscriptionQuotaGateSettings();
        var results = new List<ExpectationAdmissionCandidate>();
        foreach (var target in directive.Targets ?? [])
        {
            foreach (var candidate in target.Candidates ?? [])
            {
                var probe = probes.Candidates.FirstOrDefault(row =>
                    ExpectationSubjects.SameRunner(row.RunnerId, target.RunnerId)
                    && row.AgentKind == candidate.AgentKind
                    && row.ModelLevel == candidate.ModelLevel
                    && string.Equals(
                        (row.SubscriptionKey ?? string.Empty).Trim(),
                        (candidate.SubscriptionKey ?? string.Empty).Trim(),
                        StringComparison.Ordinal));
                var state = probe is null
                    ? ExpectationAdmissionState.Unknown
                    : ExpectationAdmission.Classify(probe.Sample, probe.SampleKnown, probe.ModelHeld, settings, asOf);
                results.Add(new ExpectationAdmissionCandidate
                {
                    RunnerId = target.RunnerId,
                    AgentKind = candidate.AgentKind,
                    ModelLevel = candidate.ModelLevel,
                    SubscriptionKey = candidate.SubscriptionKey,
                    State = state,
                });
            }
        }

        return results;
    }

    private static DateTime SpecifyUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record TaskRow(
        Guid Id,
        Guid? CardId,
        Guid? ProjectId,
        Guid? ParentSessionId,
        AgentTaskStatus Status,
        AgentTaskRole Role,
        string? RunnerId,
        string? RepoPath,
        DateTime CreatedAt);

    private sealed record ScopedTask(TaskRow Row, string RepositoryScope);

    private sealed record EventRow(Guid Id, Guid TaskId, AgentTaskEventType Type, DateTime At, string Detail);
}
