using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public enum AgentTaskUnscopedMode
{
    Exclude = 0,
    Include = 1,
    Only = 2,
}

public sealed record AgentTaskScopeRequest(
    Guid? ProjectId,
    Guid? BoardId,
    AgentTaskUnscopedMode Unscoped);

public readonly record struct AgentTaskResolvedScope(
    Guid? ProjectId,
    string? ProjectName,
    Guid? BoardId,
    string? BoardName,
    AgentTaskScopeSource Source);

public sealed record AgentTaskCardScopeBinding(
    Guid BoardId,
    string BoardName,
    Guid ProjectId,
    string ProjectName);

public sealed record AgentTaskBoardScopeBinding(
    Guid Id,
    string Name,
    Guid ProjectId,
    string ProjectName);

public sealed class AgentTaskScopeLabels
{
    public static AgentTaskScopeLabels Empty { get; } = new()
    {
        CardIdentifiers = new Dictionary<Guid, string>(),
        CardBindings = new Dictionary<Guid, AgentTaskCardScopeBinding>(),
        Projects = new Dictionary<Guid, string>(),
        Boards = new Dictionary<Guid, AgentTaskBoardScopeBinding>(),
    };

    public required IReadOnlyDictionary<Guid, string> CardIdentifiers { get; init; }
    public required IReadOnlyDictionary<Guid, AgentTaskCardScopeBinding> CardBindings { get; init; }
    public required IReadOnlyDictionary<Guid, string> Projects { get; init; }
    public required IReadOnlyDictionary<Guid, AgentTaskBoardScopeBinding> Boards { get; init; }
}

/// <summary>
/// CARD-0515. Shared list/summary scope: query-key refusal, request parse, identity resolution,
/// conflict, membership and exclusion accounting.
/// </summary>
public static class AgentTaskScope
{
    public static readonly string[] SupportedListQueryKeys =
    [
        "rootId",
        "since",
        "status",
        "includeChecks",
        "projectId",
        "boardId",
        "unscoped",
    ];

    public static AgentTaskScopeRequest? Parse(Guid? projectId, Guid? boardId, string? unscoped)
    {
        var hasId = projectId is not null || boardId is not null;
        if (unscoped is not null)
        {
            if (!hasId)
            {
                throw new ValidationException(
                    "unscoped",
                    "unscoped requires boardId or projectId.");
            }

            if (!TryParseMode(unscoped, out var mode))
            {
                throw new ValidationException(
                    "unscoped",
                    $"'{unscoped}' is not a valid unscoped mode.");
            }

            return new AgentTaskScopeRequest(projectId, boardId, mode);
        }

        if (!hasId) return null;
        return new AgentTaskScopeRequest(projectId, boardId, AgentTaskUnscopedMode.Exclude);
    }

    public static bool TryParseMode(string? value, out AgentTaskUnscopedMode mode)
    {
        mode = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("exclude", StringComparison.OrdinalIgnoreCase))
        {
            mode = AgentTaskUnscopedMode.Exclude;
            return true;
        }

        if (value.Equals("include", StringComparison.OrdinalIgnoreCase))
        {
            mode = AgentTaskUnscopedMode.Include;
            return true;
        }

        if (value.Equals("only", StringComparison.OrdinalIgnoreCase))
        {
            mode = AgentTaskUnscopedMode.Only;
            return true;
        }

        return false;
    }

    public static string ModeWire(AgentTaskUnscopedMode mode) => mode switch
    {
        AgentTaskUnscopedMode.Include => "include",
        AgentTaskUnscopedMode.Only => "only",
        _ => "exclude",
    };

    public static AgentTaskResolvedScope Resolve(AgentTask task, AgentTaskScopeLabels labels)
    {
        Guid? boardId = null;
        string? boardName = null;
        if (task.CardId is Guid cardId && labels.CardBindings.TryGetValue(cardId, out var card))
        {
            boardId = card.BoardId;
            boardName = card.BoardName;
        }

        if (task.ProjectId is Guid stored)
        {
            labels.Projects.TryGetValue(stored, out var storedName);
            return new AgentTaskResolvedScope(
                stored, storedName, boardId, boardName, AgentTaskScopeSource.Task);
        }

        if (task.CardId is Guid derivedId
            && labels.CardBindings.TryGetValue(derivedId, out var derived))
        {
            return new AgentTaskResolvedScope(
                derived.ProjectId, derived.ProjectName, boardId, boardName, AgentTaskScopeSource.Card);
        }

        return new AgentTaskResolvedScope(null, null, boardId, boardName, AgentTaskScopeSource.None);
    }

    public static void EnsureNoConflict(AgentTaskScopeRequest request, AgentTaskScopeLabels labels)
    {
        if (request.BoardId is not Guid boardId || request.ProjectId is not Guid projectId)
            return;
        if (labels.Boards.TryGetValue(boardId, out var board) && board.ProjectId != projectId)
            throw new ScopeConflictException();
    }

    public static AgentTaskScopeEchoDto? Echo(AgentTaskScopeRequest? request, AgentTaskScopeLabels labels)
    {
        if (request is null) return null;

        string? projectName = null;
        if (request.ProjectId is Guid projectId)
            labels.Projects.TryGetValue(projectId, out projectName);

        string? boardName = null;
        if (request.BoardId is Guid boardId && labels.Boards.TryGetValue(boardId, out var board))
            boardName = board.Name;

        return new AgentTaskScopeEchoDto(
            request.ProjectId,
            projectName,
            request.BoardId,
            boardName,
            ModeWire(request.Unscoped));
    }

    public static bool Matches(AgentTaskResolvedScope resolved, AgentTaskScopeRequest request)
    {
        if (request.Unscoped == AgentTaskUnscopedMode.Only)
            return resolved.Source == AgentTaskScopeSource.None;

        var projectOk = request.ProjectId is null || resolved.ProjectId == request.ProjectId;
        var boardOk = request.BoardId is null || resolved.BoardId == request.BoardId;
        var scopedMatch = projectOk && boardOk && resolved.Source != AgentTaskScopeSource.None;
        if (request.Unscoped == AgentTaskUnscopedMode.Include)
            return scopedMatch || resolved.Source == AgentTaskScopeSource.None;
        return scopedMatch;
    }

    public static (IReadOnlyList<AgentTask> Items, AgentTaskListExcludedDto Excluded) Partition(
        IReadOnlyList<AgentTask> candidates,
        AgentTaskScopeLabels labels,
        AgentTaskScopeRequest? request)
    {
        if (request is null)
        {
            return (candidates, new AgentTaskListExcludedDto(0, 0, []));
        }

        EnsureNoConflict(request, labels);

        var items = new List<AgentTask>(candidates.Count);
        var withheld = new List<AgentTaskResolvedScope>();
        foreach (var task in candidates)
        {
            var resolved = Resolve(task, labels);
            if (Matches(resolved, request))
                items.Add(task);
            else
                withheld.Add(resolved);
        }

        return (items, Account(withheld));
    }

    public static AgentTaskListExcludedDto Account(IReadOnlyList<AgentTaskResolvedScope> withheld)
    {
        var unscoped = withheld.Count(row => row.Source == AgentTaskScopeSource.None);
        var byProject = withheld
            .Where(row => row.Source != AgentTaskScopeSource.None && row.ProjectId is not null)
            .GroupBy(row => row.ProjectId!.Value)
            .Select(group => new AgentTaskExcludedByProjectDto(
                group.Key,
                group.Select(row => row.ProjectName).FirstOrDefault(name => name is not null),
                group.Count()))
            .OrderBy(row => row.ProjectName, StringComparer.Ordinal)
            .ThenBy(row => row.ProjectId)
            .ToList();
        return new AgentTaskListExcludedDto(unscoped + byProject.Sum(row => row.Count), unscoped, byProject);
    }
}
