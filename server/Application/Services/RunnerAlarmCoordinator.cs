using System.Globalization;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class RunnerAlarmCoordinator(
    IRunnerEligibilitySnapshotSource runners,
    IRunnerAlarmExclusion exclusion,
    RunnerAlarmState state,
    RepositoryChildJournalInspector journals,
    AppDbContext db,
    IRunnerAlarmNotifier notifier,
    CompletionNoteFlushQueue flushes,
    IOptions<AlarmSettings> settings,
    ILogger<RunnerAlarmCoordinator> logger)
{
    private static readonly AgentTaskStatus[] OpenStatuses =
    [
        AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked,
    ];

    public async Task EvaluateRunnersAsync(DateTimeOffset now, CancellationToken ct)
    {
        var options = settings.Value;
        var grace = TimeSpan.FromSeconds(options.RunnerGraceSeconds);
        var current = state.Current;
        var episodes = current.Episodes.ToDictionary(episode => episode.RunnerId, StringComparer.Ordinal);
        var resolved = new Dictionary<string, DateTimeOffset>(current.LastResolvedAt, StringComparer.Ordinal);
        var pending = current.PendingRecoveries.ToList();
        var recoveryHints = await DrainPendingRecoveriesAsync(pending, ct);
        Publish(current, episodes, resolved, pending);
        await RehintAsync(recoveryHints, ct);
        Exception? failure = null;
        foreach (var snapshot in runners.Snapshots())
        {
            if (!snapshot.Enabled)
                continue;
            try
            {
                await EvaluateRunnerAsync(snapshot, now, grace, episodes, resolved, pending, ct);
                Publish(current, episodes, resolved, pending);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A later runner must not drop notes the earlier runners already sent (G-30).
                logger.LogWarning(ex, "Runner {RunnerId} alarm pass failed", snapshot.RunnerId);
                Publish(current, episodes, resolved, pending);
                failure ??= ex;
            }
        }

        Publish(current, episodes, resolved, pending);
        if (failure is not null)
            throw failure;
    }

    private async Task EvaluateRunnerAsync(RunnerEligibilitySnapshot snapshot, DateTimeOffset now, TimeSpan grace,
        Dictionary<string, RunnerOutageEpisode> episodes, Dictionary<string, DateTimeOffset> resolved,
        List<PendingRecoveryNote> pending, CancellationToken ct)
    {
        episodes.TryGetValue(snapshot.RunnerId, out var episode);
        var excluded = exclusion.Excluded(snapshot.RunnerId);
        if (excluded is not null)
        {
            if (episode is { RaisedAt: not null })
                await ResolveAsync(episode, now, pending, ct);
            if (episode is not null)
                logger.LogDebug("Runner {RunnerId} alarm closed because it is {Reason}", snapshot.RunnerId, excluded);
            episodes.Remove(snapshot.RunnerId);
            return;
        }

        if (snapshot.Eligible)
        {
            if (episode is null)
                return;
            if (episode.RaisedAt is not null)
                await ResolveAsync(episode, now, pending, ct);
            resolved[snapshot.RunnerId] = now;
            episodes.Remove(snapshot.RunnerId);
            return;
        }

        if (episode is null)
        {
            var downSince = now;
            if (snapshot.LastDisconnectAtUtc is { } disconnected && disconnected <= now
                && (!resolved.TryGetValue(snapshot.RunnerId, out var last) || disconnected > last))
                downSince = disconnected;
            episodes[snapshot.RunnerId] = new RunnerOutageEpisode(
                snapshot.RunnerId, snapshot.DisplayName, downSince, snapshot.DisconnectReason,
                null, 0, 0, [], []);
            return;
        }

        if (episode.RaisedAt is null)
        {
            if (now - episode.DownSince < grace)
            {
                episodes[snapshot.RunnerId] = episode with { LastReason = snapshot.DisconnectReason ?? episode.LastReason };
                return;
            }

            var counts = await LoadPinnedAsync(snapshot.RunnerId, ct);
            var live = await LiveSessionsAsync(snapshot.RunnerId, ct);
            var notified = episode.NotifiedSessionIds.ToList();
            var noted = episode.NotedRows.ToList();
            await NotifyCallersAsync(snapshot, episode.DownSince, now, counts, notified, noted, recovered: false, ct);
            episodes[snapshot.RunnerId] = episode with
            {
                LastReason = snapshot.DisconnectReason ?? episode.LastReason,
                RaisedAt = now,
                PinnedOpenTasks = counts.Count,
                LiveSessions = live,
                NotifiedSessionIds = notified,
                NotedRows = noted,
            };
            logger.LogWarning("Runner {RunnerId} unavailable for {Minutes:0.0} min ({Reason})",
                snapshot.RunnerId, (now - episode.DownSince).TotalMinutes, snapshot.DisconnectReason);
            return;
        }

        // A caller pinned after the raise was never told, so this pass sends that caller one
        // outage note. D-3's "nothing else" is the feed row: refresh the counts and the reason,
        // and do not send a second note to a caller already in NotifiedSessionIds.
        var refreshed = await LoadPinnedAsync(snapshot.RunnerId, ct);
        var liveSessions = await LiveSessionsAsync(snapshot.RunnerId, ct);
        var stillNotified = episode.NotifiedSessionIds.ToList();
        var stillNoted = episode.NotedRows.ToList();
        await NotifyCallersAsync(snapshot, episode.DownSince, now, refreshed, stillNotified, stillNoted, recovered: false, ct);
        episodes[snapshot.RunnerId] = episode with
        {
            LastReason = snapshot.DisconnectReason ?? episode.LastReason,
            PinnedOpenTasks = refreshed.Count,
            LiveSessions = liveSessions,
            NotifiedSessionIds = stillNotified,
            NotedRows = stillNoted,
        };
        await RehintAsync(stillNoted.Select(row => row.RowId).ToArray(), ct);
    }

    private void Publish(RunnerAlarmSnapshot basis, Dictionary<string, RunnerOutageEpisode> episodes,
        Dictionary<string, DateTimeOffset> resolved, List<PendingRecoveryNote> pending) =>
        state.Publish(basis with
        {
            Episodes = episodes.Values.ToArray(),
            LastResolvedAt = resolved,
            PendingRecoveries = pending.ToArray(),
        });

    public async Task EvaluateJournalsAsync(IReadOnlyList<string>? repositories, DateTimeOffset now, CancellationToken ct)
    {
        if (!settings.Value.JournalEnabled)
        {
            state.Publish(state.Current with { Journals = [] });
            return;
        }

        var staleAfter = TimeSpan.FromMinutes(settings.Value.JournalStaleMinutes);
        var paths = repositories ?? await RegisteredRepositoriesAsync(ct);
        var findings = repositories is null
            ? new Dictionary<string, JournalFinding>(StringComparer.Ordinal)
            : state.Current.Journals.ToDictionary(
                finding => Path.TrimEndingDirectorySeparator(Path.GetFullPath(finding.CommonDirectory)),
                StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            try
            {
                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    logger.LogDebug("Skipping journal inspection for missing path {Path}", path);
                    continue;
                }

                JournalInspection inspection;
                try
                {
                    inspection = await journals.InspectAsync(path, staleAfter, now, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Journal path {Path} is not a repository; reading it as a common directory", path);
                    inspection = await journals.InspectCommonAsync(path, staleAfter, now, ct);
                }

                var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inspection.CommonDirectory ?? path));
                if (inspection.Findings.Count == 0)
                    findings.Remove(key);
                else
                    findings[key] = new JournalFinding(path, inspection.CommonDirectory ?? path, inspection.StaleCount, now)
                    {
                        Records = inspection.Findings.Select(record => new JournalAlarmRecord(
                            record.File, record.State, record.Age, record.ProcessId, record.WrittenAt)).ToArray(),
                    };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Skipping journal inspection for {Path}", path);
            }
        }

        state.Publish(state.Current with { Journals = findings.Values.ToArray() });
    }

    public async Task<IReadOnlyList<string>> RegisteredRepositoriesAsync(CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking()
            .Where(project => project.ArchivedAt == null && project.LocalRepositoryPath != null && project.LocalRepositoryPath != "")
            .Select(project => project.LocalRepositoryPath!)
            .ToListAsync(ct);
        var open = await db.AgentTasks.AsNoTracking()
            .Where(task => task.RepoPath != null && task.RepoPath != "")
            .Where(task => OpenStatuses.Contains(task.Status))
            .Where(AgentTaskRoles.NotSpecialist)
            .Select(task => task.RepoPath!)
            .ToListAsync(ct);
        var pending = await db.AgentTaskLandRequests.AsNoTracking()
            .Where(request => request.IsPending)
            .Join(db.AgentTasks.Where(task => task.RepoPath != null && task.RepoPath != ""),
                request => request.TaskId, task => task.Id, (_, task) => task.RepoPath!)
            .ToListAsync(ct);
        return projects.Concat(open).Concat(pending).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task ResolveAsync(RunnerOutageEpisode episode, DateTimeOffset now,
        List<PendingRecoveryNote> pending, CancellationToken ct)
    {
        var minutes = (now - episode.DownSince).TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture);
        var header = $"[runner {episode.RunnerId} recovered]";
        var body = header + "\n"
            + $"Runner '{episode.DisplayName}' is dispatch-eligible again after {minutes} min down (since {episode.DownSince:O}). Queued work bound to it dispatches on the next tick.";
        foreach (var session in episode.NotifiedSessionIds)
        {
            try
            {
                var rowId = await notifier.NotifyAsync(session, header, body, ct);
                pending.Add(new PendingRecoveryNote(episode.RunnerId, session, rowId == Guid.Empty ? null : rowId, header, body));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Runner {RunnerId} recovery note failed for {SessionId}", episode.RunnerId, session);
                pending.Add(new PendingRecoveryNote(episode.RunnerId, session, null, header, body));
            }
        }

        logger.LogInformation("Runner {RunnerId} recovered after {Minutes} min", episode.RunnerId, minutes);
    }

    /// <summary>
    /// Retry a recovery insert that never returned a row id, and collect still-Pending recovery
    /// rows for a re-hint. Sent, canceled, and rows the notifier never stored are dropped.
    /// </summary>
    private async Task<List<Guid>> DrainPendingRecoveriesAsync(List<PendingRecoveryNote> pending, CancellationToken ct)
    {
        if (pending.Count == 0)
            return [];
        var kept = new List<PendingRecoveryNote>(pending.Count);
        var rehint = new List<Guid>();
        foreach (var note in pending)
        {
            if (note.RowId is not Guid rowId)
            {
                try
                {
                    var created = await notifier.NotifyAsync(note.SessionId, note.Header, note.Body, ct);
                    if (created == Guid.Empty)
                    {
                        kept.Add(note);
                        continue;
                    }

                    kept.Add(note with { RowId = created });
                    rehint.Add(created);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Runner {RunnerId} recovery note failed for {SessionId}", note.RunnerId, note.SessionId);
                    kept.Add(note);
                }

                continue;
            }

            try
            {
                var row = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(message => message.Id == rowId)
                    .Select(message => new { message.Status, message.DeliveryAttempts })
                    .SingleOrDefaultAsync(ct);
                if (row is null || row.Status != QueuedMessageStatus.Pending)
                    continue;
                kept.Add(note);
                if (row.DeliveryAttempts == 0)
                    rehint.Add(rowId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A later note's read must not drop a row id an earlier retry already saved.
                logger.LogWarning(ex, "Runner {RunnerId} recovery status read failed for {SessionId}", note.RunnerId, note.SessionId);
                kept.Add(note);
            }
        }

        pending.Clear();
        pending.AddRange(kept);
        return rehint;
    }

    private async Task NotifyCallersAsync(RunnerEligibilitySnapshot snapshot, DateTimeOffset downSince, DateTimeOffset now,
        List<PinnedTask> tasks, List<Guid> notified, List<NotedAlarmRow> noted, bool recovered, CancellationToken ct)
    {
        var minutes = (now - downSince).TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture);
        var header = $"[runner {snapshot.RunnerId} unavailable]";
        foreach (var group in tasks.Where(task => task.ReplyTo == AgentTaskReplyTo.Session && task.ParentSessionId is Guid)
                     .GroupBy(task => task.ParentSessionId!.Value))
        {
            if (notified.Contains(group.Key))
                continue;
            var ids = string.Join(", ", group.Select(task => DelegationReportFormatter.Short(task.Id)));
            var body = header + "\n"
                + $"Runner '{snapshot.DisplayName}' has not been dispatch-eligible for {minutes} min (since {downSince:O}; last reason: {snapshot.DisconnectReason}). {group.Count()} of your open tasks are pinned to it: {ids}. New dispatches to it stay Queued until it recovers; Antiphon sends a note when it does. Do not reroute them silently.";
            try
            {
                var rowId = await notifier.NotifyAsync(group.Key, header, body, ct);
                notified.Add(group.Key);
                noted.Add(new NotedAlarmRow(group.Key, rowId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Runner {RunnerId} note failed for {SessionId}", snapshot.RunnerId, group.Key);
            }
        }

        _ = recovered;
    }

    private async Task RehintAsync(IReadOnlyCollection<Guid> rowIds, CancellationToken ct)
    {
        if (rowIds.Count == 0)
            return;
        var pending = await db.SessionQueuedMessages.AsNoTracking()
            .Where(message => rowIds.Contains(message.Id) && message.Status == QueuedMessageStatus.Pending && message.DeliveryAttempts == 0)
            .Select(message => message.AgentSessionId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var session in pending)
            flushes.TryEnqueue(session);
    }

    private async Task<List<PinnedTask>> LoadPinnedAsync(string runnerId, CancellationToken ct) =>
        await db.AgentTasks.AsNoTracking()
            .Where(task => task.RunnerId == runnerId && OpenStatuses.Contains(task.Status))
            .Where(AgentTaskRoles.NotSpecialist)
            .Select(task => new PinnedTask(task.Id, task.ParentSessionId, task.ReplyTo))
            .ToListAsync(ct);

    private async Task<int> LiveSessionsAsync(string runnerId, CancellationToken ct) =>
        await db.AgentSessions.AsNoTracking().CountAsync(session => session.RunnerId == runnerId
            && (session.Status == SessionStatus.Created || session.Status == SessionStatus.Starting
                || session.Status == SessionStatus.Running || session.Status == SessionStatus.Stopping), ct);

    private sealed record PinnedTask(Guid Id, Guid? ParentSessionId, AgentTaskReplyTo ReplyTo);
}
