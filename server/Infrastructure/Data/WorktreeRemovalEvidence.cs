using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

public sealed class WorktreeRemovalEvidence(IServiceScopeFactory scopes) : IWorktreeRemovalEvidence
{
    public async Task<AgentTaskLanding?> ReadAsync(Guid operationId, CancellationToken ct)
    {
        // Independent context: unsaved tracked mutations cannot authorize deletion.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var op = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, ct);
        if (op is null) return null;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == op.TaskId, ct);
        if (task is null || task.ActiveLandingId != op.Id
            || task.Status != Domain.Enums.AgentTaskStatus.Succeeded
            || FullRef(task.WorktreeBranch) != op.SourceFullRef
            || FullRef(task.MergeTargetRef ?? "master") != op.TargetFullRef
            || !SamePath(task.RepoPath, op.RepositoryPath) || !SamePath(task.WorktreePath, op.WorktreePath)) return null;
        return op;
    }

    private static string? FullRef(string? value) => value is null ? null
        : value.StartsWith("refs/", StringComparison.Ordinal) ? value : "refs/heads/" + value;
    private static bool SamePath(string? left, string right) => left is not null && string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
