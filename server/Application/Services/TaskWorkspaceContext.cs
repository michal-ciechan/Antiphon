using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>Identity, Git object repository and execution cwd are different coordinates.</summary>
public sealed record TaskWorkspaceContext(DesktopWorkspaceKind Kind, Guid? BindingId, string IdentityPath,
    string GitWorkingDirectory, string? CommonDirectory, string? ExecutionDirectory, string FullRef, string? BaseSha);

public sealed class TaskWorkspaceContextResolver(AppDbContext db)
{
    public Task<TaskWorkspaceContext> ResolveAsync(AgentTask task, CancellationToken ct) => Task.FromResult(
        new TaskWorkspaceContext(DesktopWorkspaceKind.Checkout, null, task.WorktreePath ?? "",
            task.WorktreePath ?? task.WorkingDirectory, null, task.WorktreePath,
            "refs/heads/" + task.WorktreeBranch, task.WorktreeBaseSha));

    public string IdentityForNewBinding(string configuredRoot, Guid taskId) =>
        Path.Combine(configuredRoot, "task-" + taskId.ToString("N")[..8]);

    public Task<TaskWorkspaceContext> RecordPlannedAsync(AgentTask task, string commonDirectory, string configuredRoot,
        CancellationToken ct) => ResolveAsync(task, ct);
}
