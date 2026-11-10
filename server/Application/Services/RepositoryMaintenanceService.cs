using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
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
/// <para>
/// CARD-1442 (CARD-1432 F-2, D-5): <c>refs/antiphon/retirement/*</c> and
/// <c>refs/antiphon/start-ref-probe</c> stay. They belong to their own operations, and this sweep
/// never deletes them. It counts them and logs each one older than <see cref="TransientStaleAge"/>.
/// Age is the reflog's last unix time when that file exists, otherwise the loose ref file's last
/// write time. A ref that is only packed has no per-ref time and is counted as ageUnknown.
/// </para>
/// </summary>
public sealed class RepositoryMaintenanceService(AppDbContext db, ILandingGit git, ILogger logger)
{
    internal static readonly TimeSpan TransientStaleAge = TimeSpan.FromHours(24);
    private const int StaleRefLogCap = 20;
    private static readonly Regex ReflogStamp = new(@"\s(\d+)\s([+-]\d{4})$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

            try { await ReportTransientRefsAsync(repository, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Repository maintenance transient ref report failed");
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

    /// <summary>Count retirement and start-ref-probe refs. Log the ones older than 24 hours. Delete none.</summary>
    private async Task ReportTransientRefsAsync(string repository, CancellationToken ct)
    {
        LandingGitResult listed;
        try
        {
            listed = await git.RunAsync(repository,
                ["for-each-ref", "--format=%(refname)", "refs/antiphon/retirement", "refs/antiphon/start-ref-probe"], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Repository maintenance transient ref list failed");
            return;
        }
        if (!listed.Succeeded)
        {
            logger.LogWarning("Repository maintenance transient ref list failed reason={Reason}", listed.Diagnostic);
            return;
        }

        var names = new List<string>();
        foreach (var line in listed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (IsRetirement(line) || IsStartRefProbe(line)) names.Add(line);

        string? common = null;
        if (names.Count > 0)
        {
            try { common = await git.CommonDirectoryAsync(repository, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Repository maintenance transient ref age skipped");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var retirement = 0;
        var staleRetirement = 0;
        var probe = 0;
        var staleProbe = 0;
        var unknown = 0;
        var stale = new List<string>();
        foreach (var name in names)
        {
            var isRetirement = IsRetirement(name);
            if (isRetirement) retirement++;
            else probe++;
            var updated = common is null ? null : RefUpdatedUtc(common, name);
            if (updated is null)
            {
                unknown++;
                continue;
            }
            if (now - updated.Value <= TransientStaleAge) continue;
            if (isRetirement) staleRetirement++;
            else staleProbe++;
            stale.Add(name);
        }

        logger.LogInformation(
            "Repository maintenance transient refs retirement={Retirement} staleRetirement={StaleRetirement} startRefProbe={StartRefProbe} staleStartRefProbe={StaleStartRefProbe} ageUnknown={AgeUnknown}",
            retirement, staleRetirement, probe, staleProbe, unknown);
        stale.Sort(StringComparer.Ordinal);
        var shown = Math.Min(stale.Count, StaleRefLogCap);
        for (var i = 0; i < shown; i++)
            logger.LogWarning("Repository maintenance stale transient ref {Ref}", stale[i]);
        if (stale.Count > StaleRefLogCap)
            logger.LogWarning("Repository maintenance stale transient refs truncated shown={Shown} total={Total}",
                StaleRefLogCap, stale.Count);
    }

    private static bool IsRetirement(string fullRef) =>
        fullRef.StartsWith("refs/antiphon/retirement/", StringComparison.Ordinal);

    private static bool IsStartRefProbe(string fullRef) =>
        fullRef == "refs/antiphon/start-ref-probe"
        || fullRef.StartsWith("refs/antiphon/start-ref-probe/", StringComparison.Ordinal);

    private static DateTimeOffset? RefUpdatedUtc(string common, string fullRef)
    {
        var reflog = RefPath(common, "logs/" + fullRef);
        if (reflog is not null && File.Exists(reflog) && TryReflogUpdated(reflog, out var fromLog))
            return fromLog;
        var loose = RefPath(common, fullRef);
        if (loose is null || !File.Exists(loose)) return null;
        return new DateTimeOffset(File.GetLastWriteTimeUtc(loose), TimeSpan.Zero);
    }

    private static bool TryReflogUpdated(string path, out DateTimeOffset updated)
    {
        updated = default;
        var last = LastLine(path);
        if (last is null) return false;
        var tab = last.IndexOf('\t');
        var header = tab < 0 ? last : last[..tab];
        var match = ReflogStamp.Match(header);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var unix))
            return false;
        try { updated = DateTimeOffset.FromUnixTimeSeconds(unix); }
        catch (ArgumentOutOfRangeException) { return false; }
        return true;
    }

    private static string? LastLine(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var take = (int)Math.Min(stream.Length, 4096);
            if (take == 0) return null;
            stream.Seek(-take, SeekOrigin.End);
            var buffer = new byte[take];
            stream.ReadExactly(buffer);
            var text = Encoding.UTF8.GetString(buffer).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
            var nl = text.LastIndexOf('\n');
            return nl < 0 ? text : text[(nl + 1)..];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? RefPath(string common, string relative)
    {
        var parts = relative.Split('/');
        if (parts.Any(part => part.Length == 0 || part == "." || part == "..")) return null;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(common));
        var full = root;
        foreach (var part in parts)
            full = Path.Combine(full, part);
        full = Path.GetFullPath(full);
        var prefix = root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal) ? full : null;
    }
}
