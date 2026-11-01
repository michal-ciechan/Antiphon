using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Rolled-up spend for one task. <see cref="StoredCostUsd"/> is the family copy's own cost,
/// so a caller holding a newer row can replace it without recounting descendants.
/// </summary>
public readonly record struct AgentTaskSubtreeFigures(
    decimal SubtreeCostUsd,
    int ChildCount,
    decimal StoredCostUsd);

/// <summary>One definition of a task's rolled-up spend.</summary>
public static class AgentTaskCostWalk
{
    public static IReadOnlyDictionary<Guid, decimal> Calculate(
        IReadOnlyList<AgentTask> subjects, IReadOnlyList<AgentTask> family)
    {
        var byId = IndexById(family);
        var figures = Rollup(family, byId);
        var familyCount = family.Count;
        var result = new Dictionary<Guid, decimal>(subjects.Count);
        foreach (var task in subjects)
        {
            if (result.ContainsKey(task.Id))
                continue;

            // The caller's row wins when it is a different instance from the family copy.
            if (figures.TryGetValue(task.Id, out var row) && byId.TryGetValue(task.Id, out var stored))
            {
                result[task.Id] = row.SubtreeCostUsd + (task.CostUsd - stored.CostUsd);
                continue;
            }

            var total = task.CostUsd;
            foreach (var other in family)
            {
                if (other.Id != task.Id && AgentTaskService.IsDescendantOf(other, task.Id, byId, familyCount))
                    total += other.CostUsd;
            }

            result[task.Id] = total;
        }

        return result;
    }

    /// <summary>
    /// Subtree cost and direct-child count for every id in <paramref name="family"/>.
    /// Each row's cost is added to the ancestors on its parent chain. The chain stops on a
    /// repeated id, a parent that is not in <paramref name="family"/>, or after more than
    /// <c>family.Count</c> steps, matching <see cref="AgentTaskService.IsDescendantOf"/>.
    /// The first row for an id supplies that id's own cost. A later row with the same id does not
    /// add its cost to that id, and the walk continues through the stored row's parent.
    /// </summary>
    public static Dictionary<Guid, AgentTaskSubtreeFigures> Rollup(IReadOnlyList<AgentTask> family) =>
        Rollup(family, IndexById(family));

    internal static Dictionary<Guid, AgentTask> IndexById(IReadOnlyList<AgentTask> family)
    {
        var byId = new Dictionary<Guid, AgentTask>(family.Count);
        foreach (var task in family)
            byId.TryAdd(task.Id, task);
        return byId;
    }

    private static Dictionary<Guid, AgentTaskSubtreeFigures> Rollup(
        IReadOnlyList<AgentTask> family, Dictionary<Guid, AgentTask> byId)
    {
        var costs = new Dictionary<Guid, decimal>(byId.Count);
        var ownCosts = new Dictionary<Guid, decimal>(byId.Count);
        var childCounts = new Dictionary<Guid, int>();
        foreach (var task in family)
        {
            if (ownCosts.TryAdd(task.Id, task.CostUsd))
                costs[task.Id] = task.CostUsd;
            if (task.ParentTaskId is { } parentId && task.Id != parentId)
                childCounts[parentId] = childCounts.GetValueOrDefault(parentId) + 1;
        }

        var visited = new HashSet<Guid>();
        var familyCount = family.Count;
        foreach (var task in family)
        {
            visited.Clear();
            AddToAncestors(task, byId, familyCount, costs, visited);
        }

        var result = new Dictionary<Guid, AgentTaskSubtreeFigures>(costs.Count);
        foreach (var (id, cost) in costs)
            result[id] = new AgentTaskSubtreeFigures(
                cost, childCounts.GetValueOrDefault(id), ownCosts.GetValueOrDefault(id));
        return result;
    }

    private static void AddToAncestors(
        AgentTask origin,
        Dictionary<Guid, AgentTask> byId,
        int familyCount,
        Dictionary<Guid, decimal> costs,
        HashSet<Guid> visited)
    {
        var seen = 0;
        var current = origin;
        while (current.ParentTaskId is { } parentId)
        {
            // Do not pre-seed the origin. A duplicate row (unreachable through the primary key)
            // has to keep walking the first stored row, and that shared id is not a second copy
            // of this row's cost. A repeat after that is a cycle.
            if (!visited.Add(parentId))
                return;
            if (parentId != origin.Id && costs.TryGetValue(parentId, out var ancestorCost))
                costs[parentId] = ancestorCost + origin.CostUsd;
            if (++seen > familyCount)
                return;
            if (!byId.TryGetValue(parentId, out var next))
                return;
            current = next;
        }
    }
}
