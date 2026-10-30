using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>A held attempt may finish, but cannot authorize a second replacement chain.</summary>
internal static class TaskSuccessorFence
{
    internal const string Prefix = "ceiling-successor-fence:";

    internal static async Task<string?> ReasonAsync(AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("C1610_MUTANT") == "M4") return null;
        if (task.CeilingHoldEpisodeId is null) return null;
        if (task.CardId is null || task.DispatchedAt is null)
            return Prefix + " card/dispatch lineage is unresolved; inspect before commissioning another stage.";
        var family = await db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId
                || t.RootTaskId == task.Id || t.ParentTaskId == task.Id)
            .Select(t => new { t.Id, t.ParentTaskId }).ToListAsync(ct);
        var descendants = new HashSet<Guid> { task.Id };
        bool added;
        do
        {
            added = false;
            foreach (var member in family)
                if (member.ParentTaskId is { } parent && descendants.Contains(parent))
                    added |= descendants.Add(member.Id);
        } while (added);
        var successors = await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
                && !descendants.Contains(t.Id) && t.RootTaskId != task.Id
                && t.CardId == task.CardId && t.Role == task.Role && t.CreatedAt > task.DispatchedAt)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Id).Take(32).ToListAsync(ct);
        return successors.Count == 0 ? null
            : Prefix + $" episode={task.CeilingHoldEpisodeId:D}; newer same-card/role tasks since dispatch: "
                + string.Join(", ", successors.Select(id => id.ToString("D"))) + ". Inspect before commissioning another stage.";
    }

    internal static async Task ApplyAsync(AppDbContext db, AgentTask task, DateTime now, CancellationToken ct)
    {
        if (await ReasonAsync(db, task, ct) is not { } reason) return;
        task.NextStage = PipelineHandoffKind.Decide;
        task.NextHandoff = reason.Length <= 400 ? reason : reason[..397] + "...";
        if (!db.ChangeTracker.Entries<AgentTaskEvent>().Any(e => e.Entity.AgentTaskId == task.Id
                && e.Entity.Detail.StartsWith(Prefix, StringComparison.Ordinal)))
            db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = task.Id,
                Type = AgentTaskEventType.Warning, Detail = reason, At = now });
    }

    internal static bool Forced(AgentTask task) => task.NextStage == PipelineHandoffKind.Decide
        && task.NextHandoff?.StartsWith(Prefix, StringComparison.Ordinal) == true;
}
