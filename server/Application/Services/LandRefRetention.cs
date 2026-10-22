using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1432 S2 (R2, D-3): after a land's terminal result is committed, and while its repository
/// lease is still held, delete that task's <c>refs/antiphon/land/&lt;task&gt;/</c> and
/// <c>refs/antiphon/progress/&lt;task&gt;/</c> refs in one old-value-checked transaction. A protected
/// task (<see cref="AntiphonRefProtection"/>) loses only its observation pins. A failure is a
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
            var deletes = isProtected ? refs.Where(r => IsObservation(r.Ref)).ToList() : refs;
            if (deletes.Count == 0) return [];
            var result = await git.UpdateRefsAsync(task.RepoPath, deletes, ct);
            if (!result.Succeeded)
            {
                logger.LogWarning("Land ref retention delete failed task={TaskId} refs={Count} reason={Reason}",
                    task.Id, deletes.Count, result.Diagnostic);
                return null;
            }
            logger.LogInformation("Land ref retention task={TaskId} protected={Protected} deleted={Count} kept={Kept}",
                task.Id, isProtected, deletes.Count, refs.Count - deletes.Count);
            return deletes.Select(d => d.Ref).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Land ref retention failed task={TaskId}", task.Id);
            return null;
        }
    }

    public static string LandPrefix(Guid taskId) => $"refs/antiphon/land/{taskId:N}/";
    public static string ProgressPrefix(Guid taskId) => $"refs/antiphon/progress/{taskId:N}/";

    /// <summary>An observation pin: a path segment ending in <c>-observed</c>, or a <c>source-recheck</c> segment.
    /// Recovery pins (<c>source</c>, <c>target-before</c>, <c>prepared</c>), <c>adopt/*</c> and progress pins stay.</summary>
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
