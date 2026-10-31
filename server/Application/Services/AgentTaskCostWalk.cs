using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>Rolled-up spend for one task: its own cost plus every descendant in the family.</summary>
public readonly record struct AgentTaskSubtreeFigures(decimal SubtreeCostUsd, int ChildCount);

/// <summary>One definition of a task's rolled-up spend.</summary>
public static class AgentTaskCostWalk
{
    public static IReadOnlyDictionary<Guid, decimal> Calculate(
        IReadOnlyList<AgentTask> subjects, IReadOnlyList<AgentTask> family)
    {
        var result = new Dictionary<Guid, decimal>();
        foreach (var task in subjects)
        {
            if (result.ContainsKey(task.Id)) continue;
            var total = task.CostUsd;
            foreach (var other in family)
                if (other.Id != task.Id && AgentTaskService.IsDescendantOf(other, task.Id, family)) total += other.CostUsd;
            result[task.Id] = total;
        }
        return result;
    }

    /// <summary>
    /// Subtree cost and direct-child count for every id in <paramref name="family"/>.
    /// Own cost comes from the first row for that id. Descendant costs are not added yet:
    /// the list path still rescans the family per row.
    /// </summary>
    public static Dictionary<Guid, AgentTaskSubtreeFigures> Rollup(IReadOnlyList<AgentTask> family)
    {
        var result = new Dictionary<Guid, AgentTaskSubtreeFigures>(family.Count);
        foreach (var task in family)
            result.TryAdd(task.Id, new AgentTaskSubtreeFigures(task.CostUsd, 0));
        return result;
    }
}
