using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1677 S1 (plan §3.1-§3.2, D-2). The server half of runner worktree retention: it decides which
/// tasks are FINISHED (terminal for <c>MinTerminalHours</c>, no land pending, no sync debt, no open task on
/// the path, no live session, report preserved) and sends one runner-verified <c>WorkspaceRetain</c> per
/// candidate, strictly one at a time, re-reading the row right before each send. It never decides that a
/// tree is safe to delete: the runner refuses on any doubt. Every evaluation is a run candidate row
/// (<c>Scope = runner-retention:&lt;runner&gt;</c>; lanes RunnerMirror and RunnerInventory; no migration, D-8).
/// </summary>
public sealed class RunnerWorktreeRetentionService
{
    public const string LaneMirror = "RunnerMirror";
    public const string LaneInventory = "RunnerInventory";
    public const string EventPrefix = "runner_mirror_retained";

    private static readonly Regex LeafPattern = new(@"^task-[0-9a-f]{8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OwnerPrefix = new(@"^task-(?<id>[0-9a-f]{8})(?:$|[^0-9a-f])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly AgentTaskStatus[] Terminal = [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled];
    private static readonly AgentTaskStatus[] Open =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];
    private static readonly AgentTaskSyncDebtState[] OpenDebt =
        [AgentTaskSyncDebtState.Pending, AgentTaskSyncDebtState.Ready, AgentTaskSyncDebtState.Held];
    private static readonly SessionStatus[] LiveSession =
        [SessionStatus.Created, SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly ISessionRunnerDirectory _runners;
    private readonly IOptionsMonitor<RunnerWorktreeRetentionSettings> _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<RunnerWorktreeRetentionService> _logger;

    public RunnerWorktreeRetentionService(AppDbContext db, ISessionRunnerDirectory runners,
        IOptionsMonitor<RunnerWorktreeRetentionSettings> settings, TimeProvider clock,
        ILogger<RunnerWorktreeRetentionService> logger)
    {
        _db = db;
        _runners = runners;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public sealed record TickResult(string RunnerId, int Sent, Guid? RunId);

    /// <summary>One tick over every allow-listed runner. <paramref name="previewRunner"/> forces DryRun for one runner (the preview route).</summary>
    public async Task<IReadOnlyList<TickResult>> RunTickAsync(CancellationToken ct, string? previewRunner = null)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            return [];
        if (settings.Execute && settings.Runners.Count == 0)
            _logger.LogWarning("RunnerWorktreeRetention:Execute is true but Runners is empty; no runner is contacted");
        var results = new List<TickResult>();
        foreach (var runnerId in settings.Runners.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            if (previewRunner is not null && runnerId != previewRunner)
                continue;
            var execute = settings.Execute && previewRunner is null;
            results.Add(await RunRunnerAsync(runnerId, settings, execute, writeAll: previewRunner is not null, ct));
        }

        return results;
    }

    private async Task<TickResult> RunRunnerAsync(string runnerId, RunnerWorktreeRetentionSettings settings, bool execute, bool writeAll, CancellationToken ct)
    {
        var now = Now();
        var run = new WorktreeResidueRun
        {
            Id = Guid.NewGuid(), StartedAt = now, Execute = execute, Preview = !execute,
            ActionBudget = settings.MaxRemovalsPerTick, Scope = "runner-retention:" + runnerId,
        };
        _db.WorktreeResidueRuns.Add(run);

        var client = await EligibleClientAsync(runnerId, ct);
        if (client is null)
        {
            Row(run, LaneMirror, WorktreeResidueCandidateOutcome.Held, "runner_not_eligible", null, null, null, runnerId);
            run.FinishedAt = Now();
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Runner worktree retention skipped runner {RunnerId}: runner_not_eligible", runnerId);
            return new TickResult(runnerId, 0, run.Id);
        }

        PhoneHomeWorkspaceRetentionInventoryResponse? inventory = null;
        try
        {
            inventory = await client.GetWorkspaceRetentionInventoryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Runner worktree retention inventory of {RunnerId} failed: {Reason}", runnerId, ex.Message);
        }

        var population = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.RunnerId == runnerId && t.RemoteWorktreePath != null)
            .OrderBy(t => t.CompletedAt).ThenBy(t => t.Id)
            .ToListAsync(ct);
        var cursors = await LoadCursorsAsync(population.Select(t => CursorKey(t.Id)).ToList(), ct);
        var eligible = new List<AgentTask>();
        foreach (var task in population)
        {
            var reason = await ClassifyAsync(task, runnerId, settings, ct);
            if (reason is null)
                eligible.Add(task);
            else
                HeldRow(run, cursors, task, reason, runnerId, writeAll, now);
        }

        var sent = 0;
        foreach (var candidate in eligible.Take(settings.MaxRemovalsPerTick))
        {
            if (sent > 0 && settings.PauseBetweenSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(settings.PauseBetweenSeconds), _clock, ct);
            // I-8: re-read the row right before the send; any changed server fact skips it.
            var fresh = await _db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == candidate.Id, ct);
            var recheck = fresh is null ? "task_absent" : await ClassifyAsync(fresh, runnerId, settings, ct);
            if (recheck is not null || fresh!.RemoteWorktreePath != candidate.RemoteWorktreePath)
            {
                HeldRow(run, cursors, fresh ?? candidate, recheck ?? "path_changed", runnerId, writeAll: true, Now());
                continue;
            }

            var request = new PhoneHomeWorkspaceRetainRequest(fresh.Id, fresh.RemoteWorktreePath!, fresh.WorktreeBranch!,
                execute ? WorkspaceRetainModes.Execute : WorkspaceRetainModes.DryRun, Guid.NewGuid());
            sent++;
            PhoneHomeWorkspaceRetainResponse? response = null;
            string? failure = null;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
                response = await client.RetainWorkspaceAsync(request, budget.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failure = ex.Message;
            }

            await RecordAsync(run, fresh, request, response, failure, runnerId, ct);
        }

        if (inventory is not null)
            await RecordInventoryAsync(run, inventory, population, runnerId, ct);
        run.FinishedAt = Now();
        await _db.SaveChangesAsync(ct);
        return new TickResult(runnerId, sent, run.Id);
    }

    /// <summary>D-11: only a live phone-home runner that advertises workspaceRetentionV1; never the local runner.</summary>
    private async Task<PhoneHomeRunnerClient?> EligibleClientAsync(string runnerId, CancellationToken ct)
    {
        PhoneHomeRunnerClient? client;
        try
        {
            client = _runners.Resolve(runnerId) as PhoneHomeRunnerClient;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
        if (client is null)
            return null;
        var descriptor = await _runners.DescribeAsync(runnerId, ct);
        return descriptor?.Capabilities?.Features?.Contains(RunnerCapabilityFeatures.WorkspaceRetentionV1, StringComparer.Ordinal) == true
            ? client
            : null;
    }

    /// <summary>Plan §3.2, rechecked from the row right before each send. Null means eligible.</summary>
    private async Task<string?> ClassifyAsync(AgentTask task, string runnerId, RunnerWorktreeRetentionSettings settings, CancellationToken ct)
    {
        if (task.RunnerId != runnerId || task.Workspace != WorkspaceMode.Worktree)
            return "outside_scope";
        if (!Terminal.Contains(task.Status))
            return "live_owner";
        // A null CompletedAt is the age floor's refusal, not the status set's (M-8).
        if (task.CompletedAt is not { } completed || completed > Now().AddHours(-settings.MinTerminalHours))
            return "settling";
        var path = task.RemoteWorktreePath ?? "";
        var root = settings.WorktreeRoot.TrimEnd('/');
        var leaf = path.StartsWith(root + "/", StringComparison.Ordinal) ? path[(root.Length + 1)..] : "";
        if (!LeafPattern.IsMatch(leaf) || leaf != "task-" + task.Id.ToString("N")[..8])
            return "outside_scope";
        if (string.IsNullOrWhiteSpace(task.WorktreeBranch))
            return "outside_scope";
        if (task.RemoteWorkspaceSlotId is not null)
            return "slot_managed";
        if (task.SourceLandingOperationId is not null || task.RepairSourceTaskId is not null)
            return "outside_scope";
        if (task.Role == AgentTaskRole.Mutation)
            return "mutation_excluded";
        if (task.LandRequestedAt is not null || task.LandStartedAt is not null
            || await _db.AgentTaskLandRequests.AsNoTracking().AnyAsync(r => r.TaskId == task.Id && r.IsPending, ct))
            return "land_pending";
        if (await _db.AgentTaskSyncDebts.AsNoTracking().AnyAsync(d => d.TaskId == task.Id && OpenDebt.Contains(d.State), ct))
            return "sync_debt";
        if (await _db.AgentTasks.AsNoTracking().AnyAsync(t => t.Id != task.Id && Open.Contains(t.Status)
                && (t.RemoteWorktreePath == path || t.WorktreePath == path), ct))
            return "path_shared";
        var sessionIds = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.AgentSessionId != null)
            .Select(e => e.AgentSessionId!.Value)
            .ToListAsync(ct);
        if (task.AgentSessionId is { } bound)
            sessionIds.Add(bound);
        if (sessionIds.Count > 0
            && await _db.AgentSessions.AsNoTracking().AnyAsync(s => sessionIds.Contains(s.Id) && LiveSession.Contains(s.Status), ct))
            return "live_session";
        // D-9: the database Result is the durable report; a missing one whose file lives in the mirror holds it.
        if (task.Result is null && task.ResultFilePath is { } file
            && file.Replace('\\', '/').StartsWith(path + "/", StringComparison.Ordinal))
            return "report_unpreserved";
        return null;
    }

    private async Task RecordAsync(WorktreeResidueRun run, AgentTask task, PhoneHomeWorkspaceRetainRequest request,
        PhoneHomeWorkspaceRetainResponse? response, string? failure, string runnerId, CancellationToken ct)
    {
        var now = Now();
        var verdict = response?.Verdict;
        WorktreeResidueCandidateOutcome outcome;
        string reason;
        var cleared = false;
        switch (verdict)
        {
            case WorkspaceRetentionCodes.Removed:
                outcome = WorktreeResidueCandidateOutcome.Removed;
                reason = WorkspaceRetentionCodes.Removed;
                cleared = true;
                break;
            case WorkspaceRetentionCodes.AlreadyAbsent when response!.RetiringResidue == false:
                outcome = WorktreeResidueCandidateOutcome.Removed;
                reason = WorkspaceRetentionCodes.AlreadyAbsent;
                cleared = true;
                break;
            case WorkspaceRetentionCodes.WouldRemove:
                outcome = WorktreeResidueCandidateOutcome.Preview;
                reason = WorkspaceRetentionCodes.WouldRemove;
                break;
            case WorkspaceRetentionCodes.RetiringRefused:
                outcome = WorktreeResidueCandidateOutcome.Refused;
                reason = WorkspaceRetentionCodes.RetiringRefused;
                break;
            case WorkspaceRetentionCodes.Kept when !string.IsNullOrWhiteSpace(response!.Reason):
                outcome = WorktreeResidueCandidateOutcome.Held;
                reason = response.Reason!;
                break;
            default:
                // A transport loss, an error frame or an answer this server cannot read: the path stays.
                outcome = WorktreeResidueCandidateOutcome.Deferred;
                reason = "unknown";
                break;
        }

        if (cleared)
        {
            var tracked = await _db.AgentTasks.SingleAsync(t => t.Id == task.Id, ct);
            if (tracked.RemoteWorktreePath == request.Path)
            {
                tracked.RemoteWorktreePath = null;
                _db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Warning, At = now,
                    Detail = $"{EventPrefix}: runner '{runnerId}' {reason} the finished task's mirror {request.Path} "
                        + $"(head {response!.HeadSha ?? "-"}, origin {response.OriginTip ?? "-"}, phaseAMs {response.PhaseAMs?.ToString() ?? "-"}, "
                        + $"phaseBMs {response.PhaseBMs?.ToString() ?? "-"}, nestedIgnoredRepos {response.NestedIgnoredRepos?.ToString() ?? "-"}).",
                });
            }
        }

        var row = Row(run, LaneMirror, outcome, reason, task.Id, request.Path, task.WorktreeBranch, runnerId);
        row.DirectoryRemoved = cleared ? true : null;
        row.RegistrationRemoved = cleared ? true : null;
        row.OperationId = request.RequestId;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Runner worktree retention {RunnerId} task {TaskId} path {Path} mode {Mode}: {Outcome} {Reason} {Detail}{Failure}",
            runnerId, task.Id, request.Path, request.Mode, outcome, reason, response?.Detail ?? "", failure is null ? "" : " failure=" + failure);
    }

    /// <summary>Plan §3.4. Names that match no task are reported, never sent, in any mode.</summary>
    private async Task RecordInventoryAsync(WorktreeResidueRun run, PhoneHomeWorkspaceRetentionInventoryResponse inventory,
        List<AgentTask> population, string runnerId, CancellationToken ct)
    {
        var settings = _settings.CurrentValue;
        var known = population.Select(t => "task-" + t.Id.ToString("N")[..8]).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in inventory.Entries)
        {
            if (entry.Root == "retiring")
            {
                Row(run, LaneInventory, WorktreeResidueCandidateOutcome.Held, "retiring_residue", null,
                    settings.RetiringRoot.TrimEnd('/') + "/" + entry.Name, null, runnerId);
                continue;
            }
            if (entry.SlotLeaf || known.Contains(entry.Name))
                continue;
            var match = OwnerPrefix.Match(entry.Name);
            var path = settings.WorktreeRoot.TrimEnd('/') + "/" + entry.Name;
            if (!match.Success)
            {
                Row(run, LaneInventory, WorktreeResidueCandidateOutcome.Held, "identity_unknown", null, path, null, runnerId);
                continue;
            }
            var prefix = match.Groups["id"].Value;
            var owners = await _db.AgentTasks.AsNoTracking()
                .Where(t => t.Id.ToString().StartsWith(prefix))
                .Select(t => new { t.Id, t.Status })
                .Take(2)
                .ToListAsync(ct);
            if (owners.Count > 1)
            {
                Row(run, LaneInventory, WorktreeResidueCandidateOutcome.Held, "identity_ambiguous", null, path, null, runnerId);
                continue;
            }
            // An exact task-<id8> of a task without a mirror row, or a suffixed name (task-cffa0c4b-uA): report with the owner hint.
            var row = Row(run, LaneInventory, WorktreeResidueCandidateOutcome.Held, "identity_unknown",
                owners.SingleOrDefault()?.Id, path, null, runnerId);
            row.Branch = owners.Count == 1 ? $"owner_hint:task-{prefix}:{owners[0].Status}" : $"owner_hint:task-{prefix}:none";
        }

        await _db.SaveChangesAsync(ct);
    }

    private void HeldRow(WorktreeResidueRun run, Dictionary<string, WorktreeResidueCandidateCursor> cursors,
        AgentTask task, string reason, string runnerId, bool writeAll, DateTime now)
    {
        run.Held++;
        run.Candidates++;
        if (!ShouldWriteRow(cursors, CursorKey(task.Id), reason, writeAll, now))
            return;
        Row(run, LaneMirror, WorktreeResidueCandidateOutcome.Held, reason, task.Id, task.RemoteWorktreePath, task.WorktreeBranch, runnerId);
        _logger.LogInformation("Runner worktree retention {RunnerId} held task {TaskId} path {Path}: {Reason}",
            runnerId, task.Id, task.RemoteWorktreePath, reason);
    }

    private WorktreeResidueRunCandidate Row(WorktreeResidueRun run, string lane, WorktreeResidueCandidateOutcome outcome,
        string reason, Guid? taskId, string? path, string? branch, string runnerId)
    {
        var row = new WorktreeResidueRunCandidate
        {
            Id = Guid.NewGuid(), RunId = run.Id, Lane = lane, Outcome = outcome.ToString(), ReasonCode = reason,
            TaskId = taskId, Path = path, Branch = branch, RunnerId = runnerId, EvaluatedAt = Now(),
        };
        if (outcome == WorktreeResidueCandidateOutcome.Removed) run.Removed++;
        if (outcome == WorktreeResidueCandidateOutcome.Refused) run.Refused++;
        if (outcome == WorktreeResidueCandidateOutcome.Deferred) run.Deferred++;
        _db.WorktreeResidueRunCandidates.Add(row);
        return row;
    }

    /// <summary>
    /// D-8: an unchanged Held row is not rewritten every scheduled tick, in either mode (the CARD-1446 cursor
    /// rule); the operator preview route and a send-time recheck write every row.
    /// </summary>
    private bool ShouldWriteRow(Dictionary<string, WorktreeResidueCandidateCursor> cursors, string key, string reason, bool writeAll, DateTime now)
    {
        if (writeAll)
            return true;
        if (!cursors.TryGetValue(key, out var cursor))
        {
            cursor = new WorktreeResidueCandidateCursor { Id = Guid.NewGuid(), CandidateKey = key, LastEvaluatedAt = now };
            _db.WorktreeResidueCandidateCursors.Add(cursor);
            cursors[key] = cursor;
        }
        var outcomeKey = LaneMirror + "|" + nameof(WorktreeResidueCandidateOutcome.Held) + "|" + reason;
        var unchanged = string.Equals(cursor.LastOutcomeKey, outcomeKey, StringComparison.Ordinal);
        cursor.LastOutcomeKey = outcomeKey;
        cursor.LastEvaluatedAt = now;
        return !unchanged;
    }

    private async Task<Dictionary<string, WorktreeResidueCandidateCursor>> LoadCursorsAsync(List<string> keys, CancellationToken ct)
    {
        var cursors = new Dictionary<string, WorktreeResidueCandidateCursor>(StringComparer.Ordinal);
        if (keys.Count == 0)
            return cursors;
        foreach (var cursor in await _db.WorktreeResidueCandidateCursors.Where(c => keys.Contains(c.CandidateKey)).ToListAsync(ct))
            cursors[cursor.CandidateKey] = cursor;
        return cursors;
    }

    internal static string CursorKey(Guid taskId) => "runner-retention:" + taskId.ToString("N");

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
