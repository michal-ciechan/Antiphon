using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1610. A Failed task holds its session only while it is the current owner of that
/// unfinished session and of its agent, and the session generation is proved.
/// Later means (DispatchedAt ?? CreatedAt), then CreatedAt, then Id.
/// Janitor, attention and task detail all use this. An unproved generation is not a hold.
/// </summary>
internal static class FailedTaskSessionOwnership
{
    internal readonly record struct Binding(
        Guid Id,
        Guid? AgentId,
        Guid? SessionId,
        AgentTaskStatus Status,
        DateTime CreatedAt,
        DateTime? DispatchedAt);

    public static int Compare(Binding left, Binding right)
    {
        var stamp = (left.DispatchedAt ?? left.CreatedAt).CompareTo(right.DispatchedAt ?? right.CreatedAt);
        if (stamp != 0)
            return stamp;
        var created = left.CreatedAt.CompareTo(right.CreatedAt);
        if (created != 0)
            return created;
        return left.Id.CompareTo(right.Id);
    }

    /// <summary>
    /// Sessions whose latest task is Failed, whose generation is proved, and whose Failed
    /// task is also the latest task on its agent. A later task on the session or the agent
    /// releases the hold. Callers bound the session set; this does not cap or skip an owner.
    /// </summary>
    public static async Task<Dictionary<Guid, Binding>> CurrentOwnersAsync(
        AppDbContext db,
        IReadOnlyDictionary<Guid, (SessionStatus Status, DateTime StartedAt)> sessions,
        CancellationToken ct)
    {
        if (sessions.Count == 0)
            return [];

        var sessionIds = sessions.Keys.ToList();
        var onSessions = await LoadAsync(db.AgentTasks.AsNoTracking()
            .Where(task => task.AgentSessionId != null && sessionIds.Contains(task.AgentSessionId.Value)), ct);
        var agentIds = onSessions.Where(task => task.AgentId != null)
            .Select(task => task.AgentId!.Value)
            .Distinct()
            .ToList();
        var onAgents = agentIds.Count == 0
            ? new List<Binding>()
            : await LoadAsync(db.AgentTasks.AsNoTracking()
                .Where(task => task.AgentId != null && agentIds.Contains(task.AgentId.Value)), ct);
        return CurrentOwners(sessions, onSessions, onAgents);
    }

    public static Dictionary<Guid, Binding> CurrentOwners(
        IReadOnlyDictionary<Guid, (SessionStatus Status, DateTime StartedAt)> sessions,
        IReadOnlyList<Binding> tasksOnSessions,
        IReadOnlyList<Binding> tasksOnAgents)
    {
        var latestOnSession = new Dictionary<Guid, Binding>();
        foreach (var task in tasksOnSessions)
        {
            if (task.SessionId is not Guid sessionId || !sessions.ContainsKey(sessionId))
                continue;
            if (!latestOnSession.TryGetValue(sessionId, out var current) || Compare(task, current) > 0)
                latestOnSession[sessionId] = task;
        }

        var latestOnAgent = new Dictionary<Guid, Binding>();
        foreach (var task in tasksOnAgents)
        {
            if (task.AgentId is not Guid agentId)
                continue;
            if (!latestOnAgent.TryGetValue(agentId, out var current) || Compare(task, current) > 0)
                latestOnAgent[agentId] = task;
        }

        var owners = new Dictionary<Guid, Binding>();
        foreach (var (sessionId, session) in sessions)
        {
            if (!latestOnSession.TryGetValue(sessionId, out var latest))
                continue;
            if (latest.Status != AgentTaskStatus.Failed || latest.SessionId != sessionId)
                continue;
            if (session.Status is not (SessionStatus.Created or SessionStatus.Starting
                or SessionStatus.Running or SessionStatus.Stopping))
                continue;
            if (session.StartedAt == default)
                continue;
            // The Failed task is in the agent load, so a different agent-latest is a later task.
            if (latest.AgentId is Guid agent
                && latestOnAgent.TryGetValue(agent, out var agentLatest)
                && agentLatest.Id != latest.Id)
                continue;
            owners[sessionId] = latest;
        }

        return owners;
    }

    private static async Task<List<Binding>> LoadAsync(IQueryable<AgentTask> query, CancellationToken ct)
    {
        var rows = await query
            .Select(task => new
            {
                task.Id,
                task.AgentId,
                task.AgentSessionId,
                task.Status,
                task.CreatedAt,
                task.DispatchedAt,
            })
            .ToListAsync(ct);
        return rows.Select(task => new Binding(
            task.Id, task.AgentId, task.AgentSessionId, task.Status, task.CreatedAt, task.DispatchedAt)).ToList();
    }
}
