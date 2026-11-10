using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1450 (CARD-1432 F-5): the repository ref sweep. Terminal tasks and tasks with no row are
/// judged by <see cref="LandRefRetention.IsDeletable"/> and
/// <see cref="LandRefRetention.RetainedOperationsAsync"/>, the same rule a completed land uses, so
/// the latest landing and every Refused landing keep their recovery pins. The deletion is one
/// <c>update-ref --stdin</c> batch, which <see cref="ILandingGit.UpdateRefsAsync"/> runs under the
/// repository child journal. A task that is not terminal is left untouched.
/// </summary>
public sealed class RepositoryMaintenanceService(AppDbContext db, ILandingGit git, ILogger logger)
{
    private static readonly AgentTaskStatus[] Terminal =
        [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled];

    private static readonly HashSet<Guid> NoRetained = [];

    /// <summary>The refs deleted, or null when the list or the deletion failed.</summary>
    public async Task<IReadOnlyList<string>?> SweepRefsAsync(string repository, CancellationToken ct)
    {
        try
        {
            var listed = await git.RunAsync(repository,
                ["for-each-ref", LandRefRetention.ListFormat, "refs/antiphon/land", "refs/antiphon/progress"], ct);
            if (!listed.Succeeded)
            {
                logger.LogWarning("Repository maintenance ref list failed reason={Reason}", listed.Diagnostic);
                return null;
            }

            var refs = LandRefRetention.Parse(listed.Output);
            if (refs.Count == 0) return [];

            var byTask = new Dictionary<Guid, List<(string Ref, string Oid)>>();
            foreach (var row in refs)
            {
                if (!TryTaskId(row.Ref, out var taskId)) continue;
                if (!byTask.TryGetValue(taskId, out var list))
                    byTask[taskId] = list = [];
                list.Add(row);
            }
            if (byTask.Count == 0) return [];

            var ids = byTask.Keys.ToList();
            var tasks = await db.AgentTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            var known = tasks.ToDictionary(t => t.Id);
            var protectedIds = await AntiphonRefProtection.QueryAsync(db, ids, ct);
            var retention = new LandRefRetention(db, git, logger);
            var deletes = new List<(string Ref, string Oid)>();
            foreach (var (taskId, rows) in byTask)
            {
                if (known.TryGetValue(taskId, out var task))
                {
                    if (!Terminal.Contains(task.Status)) continue;
                    var isProtected = protectedIds.Contains(taskId);
                    IReadOnlySet<Guid> retained = isProtected ? NoRetained : await retention.RetainedOperationsAsync(task, ct);
                    foreach (var row in rows)
                        if (LandRefRetention.IsDeletable(row.Ref, taskId, isProtected, retained))
                            deletes.Add(row);
                }
                else
                {
                    foreach (var row in rows)
                        if (LandRefRetention.IsDeletable(row.Ref, taskId, false, NoRetained))
                            deletes.Add(row);
                }
            }

            if (deletes.Count == 0) return [];
            var result = await git.UpdateRefsAsync(repository, deletes, ct);
            if (!result.Succeeded)
            {
                logger.LogWarning("Repository maintenance ref sweep failed refs={Count} reason={Reason}",
                    deletes.Count, result.Diagnostic);
                return null;
            }

            logger.LogInformation("Repository maintenance ref sweep deleted={Count}", deletes.Count);
            return deletes.Select(d => d.Ref).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Repository maintenance ref sweep failed");
            return null;
        }
    }

    /// <summary>The task id in <c>refs/antiphon/{land,progress}/&lt;task&gt;/</c>, or false for any other ref.</summary>
    internal static bool TryTaskId(string fullRef, out Guid taskId)
    {
        const string land = "refs/antiphon/land/";
        const string progress = "refs/antiphon/progress/";
        ReadOnlySpan<char> rest;
        if (fullRef.StartsWith(land, StringComparison.Ordinal)) rest = fullRef.AsSpan(land.Length);
        else if (fullRef.StartsWith(progress, StringComparison.Ordinal)) rest = fullRef.AsSpan(progress.Length);
        else
        {
            taskId = default;
            return false;
        }

        var slash = rest.IndexOf('/');
        var token = slash < 0 ? rest : rest[..slash];
        return Guid.TryParseExact(token, "N", out taskId);
    }
}
