using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1432 S2 (R2, D-3): after a land's terminal result is committed, and while its repository
/// lease is still held, delete that task's superseded <c>refs/antiphon/land/&lt;task&gt;/</c> and
/// <c>refs/antiphon/progress/&lt;task&gt;/</c> refs in one old-value-checked transaction. A protected
/// task (<see cref="AntiphonRefProtection"/>) loses only its observation pins. An unprotected task
/// keeps the recovery pins of its latest landing operation and of every Refused one (Review 9e126982
/// D1: CARD-0448 V-26 and the refused-retry pin rule) and loses everything else. A failure is a
/// warning and never changes the land outcome; the S3 sweep is the backstop.
/// </summary>
public sealed class LandRefRetention(AppDbContext db, ILandingGit git, ILogger logger)
{
    public const string ListFormat = "--format=%(refname) %(objectname)";

    /// <summary>The refs deleted, or null when nothing ran or the deletion failed.</summary>
    public async Task<IReadOnlyList<string>?> ReleaseAsync(AgentTask task, RepositoryLease lease, CancellationToken ct)
    {
        _ = lease; // The caller's held lease is the only writer of packed-refs for this repository.
        try
        {
            var land = LandPrefix(task.Id);
            var progress = ProgressPrefix(task.Id);
            var listed = await git.RunAsync(task.RepoPath, ["for-each-ref", ListFormat, land, progress], ct);
            if (!listed.Succeeded)
            {
                logger.LogWarning("Land ref retention list failed task={TaskId} reason={Reason}", task.Id, listed.Diagnostic);
                return null;
            }
            var refs = Parse(listed.Output);
            if (refs.Count == 0) return [];
            var isProtected = (await AntiphonRefProtection.QueryAsync(db, [task.Id], ct)).Contains(task.Id);
            HashSet<Guid> retained = isProtected ? [] : await RetainedOperationsAsync(task, ct);
            var deletes = refs.Where(r => IsDeletable(r.Ref, task.Id, isProtected, retained)).ToList();
            if (deletes.Count == 0) return [];
            var result = await git.UpdateRefsAsync(task.RepoPath, deletes, ct);
            if (!result.Succeeded)
            {
                logger.LogWarning("Land ref retention delete failed task={TaskId} refs={Count} reason={Reason}",
                    task.Id, deletes.Count, result.Diagnostic);
                return null;
            }
            logger.LogInformation("Land ref retention task={TaskId} protected={Protected} retainedOperations={Retained} deleted={Count} kept={Kept}",
                task.Id, isProtected, retained.Count, deletes.Count, refs.Count - deletes.Count);
            return deletes.Select(d => d.Ref).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Land ref retention failed task={TaskId}", task.Id);
            return null;
        }
    }

    /// <summary>
    /// The landing operations whose recovery pins stay after this task's land: its latest operation
    /// (the task's active landing, and the newest row by creation time) and every Refused operation,
    /// which a retry resumes from its pins. Any other operation of the task is superseded.
    /// </summary>
    public async Task<HashSet<Guid>> RetainedOperationsAsync(AgentTask task, CancellationToken ct)
    {
        var landings = await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == task.Id)
            .Select(o => new { o.Id, o.Phase, o.CreatedAt, o.UpdatedAt }).ToListAsync(ct);
        var retained = new HashSet<Guid>(landings.Where(o => o.Phase == LandPhase.Refused).Select(o => o.Id));
        var latest = landings.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.UpdatedAt).FirstOrDefault();
        if (latest is not null) retained.Add(latest.Id);
        if (task.ActiveLandingId is Guid active) retained.Add(active);
        return retained;
    }

    /// <summary>
    /// Whether a terminal land deletes <paramref name="fullRef"/>. An observation pin always goes. A
    /// protected task keeps everything else. An unprotected task also loses its progress pins and every
    /// <c>land/&lt;task&gt;/&lt;operation&gt;/</c> subtree whose operation is not retained; a retained
    /// operation keeps its recovery pins (<c>source</c>, <c>target-before</c>, <c>prepared</c>, <c>adopt/*</c>).
    /// </summary>
    public static bool IsDeletable(string fullRef, Guid taskId, bool isProtected, IReadOnlySet<Guid> retainedOperations)
    {
        if (IsObservation(fullRef)) return true;
        if (isProtected) return false;
        if (fullRef.StartsWith(ProgressPrefix(taskId), StringComparison.Ordinal)) return true;
        var land = LandPrefix(taskId);
        if (!fullRef.StartsWith(land, StringComparison.Ordinal)) return false;
        var rest = fullRef.AsSpan(land.Length);
        var slash = rest.IndexOf('/');
        var operation = slash < 0 ? rest : rest[..slash];
        return !(Guid.TryParseExact(operation, "N", out var id) && retainedOperations.Contains(id));
    }

    public static string LandPrefix(Guid taskId) => $"refs/antiphon/land/{taskId:N}/";
    public static string ProgressPrefix(Guid taskId) => $"refs/antiphon/progress/{taskId:N}/";

    /// <summary>An observation pin: a path segment ending in <c>-observed</c>, or a <c>source-recheck</c> segment.
    /// Recovery pins (<c>source</c>, <c>target-before</c>, <c>prepared</c>), <c>adopt/*</c> and progress pins are not.</summary>
    public static bool IsObservation(string fullRef) =>
        fullRef.StartsWith("refs/antiphon/land/", StringComparison.Ordinal)
        && fullRef.Split('/').Any(s => s == "source-recheck" || s.EndsWith("-observed", StringComparison.Ordinal));

    public static List<(string Ref, string Oid)> Parse(string output)
    {
        var refs = new List<(string Ref, string Oid)>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = line.LastIndexOf(' ');
            if (space <= 0) throw new FormatException("for_each_ref_output_malformed");
            refs.Add((line[..space], line[(space + 1)..]));
        }
        return refs;
    }
}
