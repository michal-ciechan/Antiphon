using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

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
}
