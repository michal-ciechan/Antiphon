using System.Security.Cryptography;
using System.Text;
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
///
/// Review c238f9ca D-1: the runner's answer is remembered per task (a <c>runner-retention-sent:&lt;id&gt;</c>
/// cursor). A tree the runner kept is not sent again until its backoff (<c>KeptRecheckMinutes</c> doubling per
/// repeat of the same reason, at most <c>KeptRecheckMaxHours</c>) has passed, unless its task's server facts
/// changed or the runner's reason changed; never-sent tasks go first (oldest first), then the least recently
/// sent. So kept trees cannot starve newer removable ones, and successive dry-run ticks census the whole
/// population. A tick stops starting requests past <c>MaxTickMinutes</c>, or when Enabled (or Execute) is
/// switched off while it runs.
/// </summary>
public sealed class RunnerWorktreeRetentionService
{
    public const string LaneMirror = "RunnerMirror";
    public const string LaneInventory = "RunnerInventory";
    public const string LaneTemp = "RunnerTemp";
    public const string EventPrefix = "runner_mirror_retained";

    // \z, never $: in .NET $ also matches just before a final newline (review d0b57b9c A-3). A name holding a
    // control character is refused before either pattern sees it (WorkspaceRetentionNames).
    private static readonly Regex LeafPattern = new(@"^task-[0-9a-f]{8}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OwnerPrefix = new(@"^task-(?<id>[0-9a-f]{8})(?:\z|[^0-9a-f])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
    private readonly RunnerWorktreeRetentionTicks _ticks;

    public RunnerWorktreeRetentionService(AppDbContext db, ISessionRunnerDirectory runners,
        IOptionsMonitor<RunnerWorktreeRetentionSettings> settings, TimeProvider clock,
        ILogger<RunnerWorktreeRetentionService> logger, RunnerWorktreeRetentionTicks ticks)
    {
        _db = db;
        _runners = runners;
        _settings = settings;
        _clock = clock;
        _logger = logger;
        _ticks = ticks;
    }

    public const string SendCursorPrefix = "runner-retention-sent:";

    /// <summary>
    /// One runner's tick. <paramref name="Examined"/> counts the tasks classified (the whole mirror population of
    /// the runner), <paramref name="Eligible"/> those the server found finished, <paramref name="BackedOff"/> the
    /// eligible ones skipped because the runner kept them recently, <paramref name="Stopped"/> why the tick stopped
    /// sending early (null when it did not). CARD-1794: <paramref name="Sent"/> counts the requests for present trees (the
    /// <c>MaxRemovalsPerTick</c> bound), <paramref name="AbsentSent"/> those for mirrors a complete inventory showed gone,
    /// <paramref name="Queued"/> the eligible tasks left for a later tick by either bound or by a stop (CARD-1796), and
    /// <paramref name="MinTerminalHours"/> the age floor this tick applied (so a configuration override is visible).
    /// </summary>
    public sealed record TickResult(string RunnerId, int Sent, Guid? RunId, int Examined = 0, int Eligible = 0,
        int BackedOff = 0, string? Stopped = null, int AbsentSent = 0, int Queued = 0, int MinTerminalHours = 0);

    /// <summary>
    /// CARD-1794 (4). The operator's one-shot reclaim (the execute route): one Execute tick for one allow-listed runner,
    /// even while the scheduled job runs DryRun (<c>Execute=false</c>). The kill switch (<c>Enabled</c>), the allow-list,
    /// the tick budget and every server and runner check still apply; the temp sweep stays DryRun. It waits for the
    /// process's tick turn; the route itself starts it in the background (<see cref="RunnerWorktreeRetentionTicks.StartExecuteAsync"/>).
    /// </summary>
    public Task<IReadOnlyList<TickResult>> RunExecuteOnceAsync(string runnerId, CancellationToken ct) =>
        RunTickAsync(ct, executeRunner: runnerId);

    /// <summary>
    /// The preview route's DryRun tick for one runner. CARD-1796: it never waits behind another tick; while one runs here
    /// or in another server process (CARD-1804) it refuses 409 <c>tick_in_progress</c>. CARD-1804 (A-3): it runs on the
    /// request, so a client or proxy timeout cancels it mid-tick; that is accepted (a DryRun deletes nothing) and the run
    /// is finished with a Deferred <c>tick_cancelled</c> row.
    /// </summary>
    public async Task<IReadOnlyList<TickResult>> RunPreviewAsync(string runnerId, CancellationToken ct)
    {
        if (!_ticks.TryEnter())
            throw RunnerWorktreeRetentionTicks.TickInProgress();
        try
        {
            if (!await TryLockAcrossProcessesAsync())
                throw RunnerWorktreeRetentionTicks.TickInProgress();
            try
            {
                return await RunEnteredAsync(ct, previewRunner: runnerId);
            }
            finally
            {
                await UnlockAcrossProcessesAsync();
            }
        }
        finally
        {
            _ticks.Exit();
        }
    }

    /// <summary>
    /// One tick over every allow-listed runner. <paramref name="previewRunner"/> forces DryRun for one runner (the preview
    /// route); <paramref name="executeRunner"/> forces Execute for one runner (the operator's one-shot execute route).
    /// CARD-1796 (A-1): it first waits for the process's tick turn, so it never overlaps another tick. CARD-1804 (A-1): then
    /// it takes the database-wide tick lock; while another server process ticks it skips (no run, no census), and that
    /// process's tick covers the population.
    /// </summary>
    public async Task<IReadOnlyList<TickResult>> RunTickAsync(CancellationToken ct, string? previewRunner = null, string? executeRunner = null)
    {
        await _ticks.EnterAsync(ct);
        try
        {
            if (!await TryLockAcrossProcessesAsync())
            {
                _logger.LogInformation("Runner worktree retention tick skipped: another server process holds the tick lock");
                return [];
            }
            try
            {
                return await RunEnteredAsync(ct, previewRunner, executeRunner);
            }
            finally
            {
                await UnlockAcrossProcessesAsync();
            }
        }
        finally
        {
            _ticks.Exit();
        }
    }

    // CARD-1804 (A-1). One key per database: every server process on it shares the tick (a cloned test database has its own).
    private const string TickLockKey = "antiphon.runner-worktree-retention.tick";
    private bool _lockHeld;

    /// <summary>
    /// CARD-1804 (A-1). The cross-process half of the tick turn: a session advisory lock on this context's connection, kept
    /// open until <see cref="UnlockAcrossProcessesAsync"/>. The caller holds the process's turn. It never waits; false
    /// means another server process is ticking. The lock query itself takes no cancellation, so a granted lock is never
    /// left unrecorded on a pooled connection (a pooled connection is also reset on return).
    /// </summary>
    internal async Task<bool> TryLockAcrossProcessesAsync()
    {
        await _db.Database.OpenConnectionAsync(CancellationToken.None);
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT pg_try_advisory_lock(hashtextextended('{TickLockKey}', 0))";
            _lockHeld = await command.ExecuteScalarAsync(CancellationToken.None) is true;
        }
        finally
        {
            if (!_lockHeld)
                await _db.Database.CloseConnectionAsync();
        }
        return _lockHeld;
    }

    /// <summary>CARD-1804 (A-1). Releases the lock <see cref="TryLockAcrossProcessesAsync"/> took; a no-op when none is held.</summary>
    internal async Task UnlockAcrossProcessesAsync()
    {
        if (!_lockHeld)
            return;
        _lockHeld = false;
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT pg_advisory_unlock(hashtextextended('{TickLockKey}', 0))";
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Closing the connection below returns it to the pool, which resets it and so releases the lock as well.
            _logger.LogWarning("Runner worktree retention tick lock release failed: {Reason}", ex.Message);
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// CARD-1804 (A-2). The execute route saves its run before it answers 202, so the run id it hands out always names a
    /// row (unfinished until the background tick finishes it). The caller holds the turn and the tick lock.
    /// </summary>
    internal async Task<Guid> StartExecuteRunAsync(string runnerId, RunnerWorktreeRetentionSettings settings, CancellationToken ct)
    {
        var run = NewRun(Guid.NewGuid(), runnerId, settings, execute: true);
        _db.WorktreeResidueRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run.Id;
    }

    private WorktreeResidueRun NewRun(Guid id, string runnerId, RunnerWorktreeRetentionSettings settings, bool execute) => new()
    {
        Id = id, StartedAt = Now(), Execute = execute, Preview = !execute,
        ActionBudget = settings.MaxRemovalsPerTick, Scope = "runner-retention:" + runnerId,
    };

    /// <summary>
    /// CARD-1804 (A-2). Finishes a run its tick left unfinished, with one Deferred row naming why: <paramref name="reason"/>
    /// (<c>tick_failed</c>, <c>tick_cancelled</c>), or, for a tick that returned without running this runner,
    /// <c>tick_stopped_disabled</c> or <c>runner_not_listed</c>. A finished or missing run is left alone. It takes no caller
    /// cancellation (host stop is what cancelled the tick), only its own bound.
    /// </summary>
    internal async Task FinishUnfinishedRunAsync(Guid runId, string runnerId, string? reason)
    {
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var run = await _db.WorktreeResidueRuns.SingleOrDefaultAsync(r => r.Id == runId, bound.Token);
        if (run is null || run.FinishedAt is not null)
            return;
        reason ??= _settings.CurrentValue.Enabled ? "runner_not_listed" : "tick_stopped_disabled";
        Row(run, LaneMirror, WorktreeResidueCandidateOutcome.Deferred, reason, null, null, null, runnerId);
        run.FinishedAt = Now();
        await _db.SaveChangesAsync(bound.Token);
        _logger.LogWarning("Runner worktree retention {RunnerId} run {RunId} finished without its tick: {Reason}", runnerId, runId, reason);
    }

    /// <summary>The tick body; the caller holds the tick turn. <paramref name="executeRunId"/> names the forced runner's run.</summary>
    internal async Task<IReadOnlyList<TickResult>> RunEnteredAsync(CancellationToken ct, string? previewRunner = null,
        string? executeRunner = null, Guid? executeRunId = null)
    {
        var settings = _settings.CurrentValue;
        if (!settings.Enabled)
            return [];
        if (settings.Execute && settings.Runners.Count == 0)
            _logger.LogWarning("RunnerWorktreeRetention:Execute is true but Runners is empty; no runner is contacted");
        var results = new List<TickResult>();
        var tickStarted = Now();
        foreach (var runnerId in settings.Runners.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            if ((previewRunner is not null && runnerId != previewRunner) || (executeRunner is not null && runnerId != executeRunner))
                continue;
            var forced = executeRunner is not null && previewRunner is null;
            var execute = forced || (settings.Execute && previewRunner is null);
            results.Add(await RunRunnerAsync(runnerId, settings, execute, forced, writeAll: previewRunner is not null || forced, tickStarted,
                forced ? executeRunId : null, ct));
        }

        return results;
    }

    private async Task<TickResult> RunRunnerAsync(string runnerId, RunnerWorktreeRetentionSettings settings, bool execute, bool forced,
        bool writeAll, DateTime tickStarted, Guid? runId, CancellationToken ct)
    {
        var now = Now();
        // CARD-1804 (A-2): the execute route saved its run before answering; any other tick creates its own here.
        var run = runId is { } existing ? await _db.WorktreeResidueRuns.SingleOrDefaultAsync(r => r.Id == existing, ct) : null;
        if (run is null)
        {
            run = NewRun(runId ?? Guid.NewGuid(), runnerId, settings, execute);
            _db.WorktreeResidueRuns.Add(run);
            // CARD-1796 (A-2): the run is readable while it runs (no FinishedAt yet).
            await _db.SaveChangesAsync(ct);
        }

        try
        {
            return await CensusRunnerAsync(run, runnerId, settings, execute, forced, writeAll, tickStarted, now, ct);
        }
        catch (Exception ex)
        {
            await FailRunAsync(run, runnerId, ex is OperationCanceledException && ct.IsCancellationRequested ? "tick_cancelled" : "tick_failed", ex);
            throw;
        }
    }

    /// <summary>
    /// CARD-1804 (A-2). A tick that threw or was cancelled finishes its run with a Deferred <c>tick_failed</c> or
    /// <c>tick_cancelled</c> row. The rows it had evaluated are saved with it; when that save fails too (the tracked state
    /// may be what failed), only the run is finished.
    /// </summary>
    private async Task FailRunAsync(WorktreeResidueRun run, string runnerId, string reason, Exception failure)
    {
        _logger.LogWarning("Runner worktree retention {RunnerId} run {RunId} {Reason}: {Failure}", runnerId, run.Id, reason, failure.Message);
        try
        {
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Row(run, LaneMirror, WorktreeResidueCandidateOutcome.Deferred, reason, null, null, null, runnerId);
            run.FinishedAt = Now();
            await _db.SaveChangesAsync(bound.Token);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Runner worktree retention {RunnerId} run {RunId} could not save its evaluated rows: {Reason}", runnerId, run.Id, ex.Message);
        }

        _db.ChangeTracker.Clear();
        try
        {
            await FinishUnfinishedRunAsync(run.Id, runnerId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Runner worktree retention {RunnerId} run {RunId} could not be finished", runnerId, run.Id);
        }
    }

    private async Task<TickResult> CensusRunnerAsync(WorktreeResidueRun run, string runnerId, RunnerWorktreeRetentionSettings settings,
        bool execute, bool forced, bool writeAll, DateTime tickStarted, DateTime now, CancellationToken ct)
    {

        var client = await EligibleClientAsync(runnerId, ct);
        if (client is null)
        {
            Row(run, LaneMirror, WorktreeResidueCandidateOutcome.Held, "runner_not_eligible", null, null, null, runnerId);
            run.FinishedAt = Now();
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Runner worktree retention skipped runner {RunnerId}: runner_not_eligible", runnerId);
            return new TickResult(runnerId, 0, run.Id, MinTerminalHours: settings.MinTerminalHours);
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

        // D-1: order and back off by the runner's last answer for each task.
        var sendCursors = await LoadCursorsAsync(eligible.Select(t => SendCursorKey(t.Id)).ToList(), ct);
        var ordered = new List<AgentTask>();
        var retries = new List<(AgentTask Task, DateTime LastSent)>();
        var backedOff = 0;
        foreach (var task in eligible)
        {
            var last = sendCursors.TryGetValue(SendCursorKey(task.Id), out var cursor) ? SendState.Parse(cursor, Fingerprint(task)) : null;
            if (last is null)
                ordered.Add(task);
            else if (cursor!.NotBefore is { } notBefore && notBefore > now)
            {
                backedOff++;
                HeldRow(run, cursors, task, "kept_recheck_pending", runnerId, writeAll, now);
            }
            else
                retries.Add((task, cursor.LastEvaluatedAt));
        }
        ordered.AddRange(retries.OrderBy(r => r.LastSent).Select(r => r.Task));

        // CARD-1794 (1): a complete inventory shows which eligible mirrors are already gone. Present trees go first on
        // MaxRemovalsPerTick; gone ones follow on their own MaxAbsentPerTick bound, unpaced, so a backlog of already-absent
        // rows can no longer spend the removal budget. The runner still answers for every one (already_absent clears it).
        var listed = inventory is { Complete: true }
            ? inventory.Entries.Where(e => e.Root == "worktrees").Select(e => e.Name).ToHashSet(StringComparer.Ordinal)
            : null;
        bool Gone(AgentTask task) => listed is not null && !listed.Contains(Leaf(task.Id));
        var presentQueue = ordered.Where(task => !Gone(task)).ToList();
        var goneQueue = ordered.Where(Gone).ToList();
        var queue = presentQueue.Take(settings.MaxRemovalsPerTick).Select(task => (Task: task, Gone: false))
            .Concat(goneQueue.Take(settings.MaxAbsentPerTick).Select(task => (Task: task, Gone: true)))
            .ToList();

        var sent = 0;
        var absentSent = 0;
        var queued = 0;
        string? stopped = null;
        for (var index = 0; index < queue.Count; index++)
        {
            var (candidate, gone) = queue[index];
            if (!gone && sent > 0 && settings.PauseBetweenSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(settings.PauseBetweenSeconds), _clock, ct);
            stopped = StopReason(execute, forced, tickStarted);
            if (stopped is not null)
            {
                Row(run, LaneMirror, WorktreeResidueCandidateOutcome.Deferred, stopped, null, null, null, runnerId);
                // CARD-1796 (A-4): the unsent in-bound remainder is queued for a later tick, as the past-bound tasks are.
                foreach (var (task, _) in queue.Skip(index))
                {
                    queued++;
                    HeldRow(run, cursors, task, "queued_next_tick", runnerId, writeAll, now);
                }
                _logger.LogWarning("Runner worktree retention {RunnerId} stopped sending after {Sent} request(s): {Reason}", runnerId, sent, stopped);
                break;
            }
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
            if (gone)
                absentSent++;
            else
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

            await RecordAsync(run, fresh, request, response, failure, runnerId, sendCursors, ct);
        }

        // CARD-1794 (1): every eligible task past a bound is a census row, never a missing one.
        foreach (var task in presentQueue.Skip(settings.MaxRemovalsPerTick).Concat(goneQueue.Skip(settings.MaxAbsentPerTick)))
        {
            queued++;
            HeldRow(run, cursors, task, "queued_next_tick", runnerId, writeAll, now);
        }

        if (inventory is not null)
            await RecordInventoryAsync(run, inventory, population, runnerId, ct);
        await SweepTempAsync(run, client, runnerId, preview: writeAll, tickStarted, ct);
        run.FinishedAt = Now();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Runner worktree retention {RunnerId} examined {Examined} eligible {Eligible} backedOff {BackedOff} sent {Sent} absentSent {AbsentSent} queued {Queued} minTerminalHours {MinTerminalHours}{Stopped}",
            runnerId, population.Count, eligible.Count, backedOff, sent, absentSent, queued, settings.MinTerminalHours,
            stopped is null ? "" : " stopped=" + stopped);
        return new TickResult(runnerId, sent, run.Id, population.Count, eligible.Count, backedOff, stopped, absentSent, queued,
            settings.MinTerminalHours);
    }

    /// <summary>
    /// CARD-1677 S2 (plan §3.5). One bounded temp sweep per runner per tick, under its own switch
    /// (<c>TempSweep:Enabled</c>, re-read right before the send, as are the kill switch and the tick budget) and in
    /// DryRun unless <c>TempSweep:Execute</c> (the preview route always sends DryRun). The runner decides what is a
    /// registered, old, unheld entry; every candidate and every unregistered prefix is a Lane RunnerTemp row.
    /// </summary>
    private async Task SweepTempAsync(WorktreeResidueRun run, PhoneHomeRunnerClient client, string runnerId, bool preview,
        DateTime tickStarted, CancellationToken ct)
    {
        var temp = _settings.CurrentValue.TempSweep;
        if (!temp.Enabled)
            return;
        var stopped = StopReason(execute: false, forced: false, tickStarted);
        if (stopped is not null || !_settings.CurrentValue.TempSweep.Enabled)
        {
            Row(run, LaneTemp, WorktreeResidueCandidateOutcome.Deferred, stopped ?? "tick_stopped_disabled", null, null, null, runnerId);
            await _db.SaveChangesAsync(ct);
            return;
        }
        var descriptor = await _runners.DescribeAsync(runnerId, ct);
        if (descriptor?.Capabilities?.Features?.Contains(RunnerCapabilityFeatures.TempSweepV1, StringComparer.Ordinal) != true)
        {
            Row(run, LaneTemp, WorktreeResidueCandidateOutcome.Held, "runner_not_eligible", null, null, null, runnerId);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var families = new Dictionary<string, bool>(temp.Families, StringComparer.Ordinal);
        families.TryAdd(RunnerTempSweepSettings.ClaudeScratch, false);
        var request = new PhoneHomeTempSweepRequest(temp.Execute && !preview ? TempSweepModes.Execute : TempSweepModes.DryRun,
            Guid.NewGuid(), temp.MinAgeHours, temp.MaxEntriesPerTick, temp.MaxBytesPerTick, families);
        PhoneHomeTempSweepResponse? response = null;
        string? failure = null;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromMinutes(15));
            response = await client.TempSweepAsync(request, budget.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failure = ex.Message;
        }

        if (response is null || response.Reason is not null)
        {
            Row(run, LaneTemp, WorktreeResidueCandidateOutcome.Deferred, response?.Reason ?? "unknown", null, null, null, runnerId)
                .OperationId = request.RequestId;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Runner temp sweep {RunnerId} mode {Mode}: {Reason} {Detail}", runnerId, request.Mode,
                response?.Reason ?? "unknown", response?.Detail ?? failure ?? "");
            return;
        }

        var root = (response.Root ?? "/tmp").TrimEnd('/');
        foreach (var candidate in response.Candidates)
        {
            var (outcome, reason) = candidate.Verdict switch
            {
                TempSweepCodes.Removed => (WorktreeResidueCandidateOutcome.Removed, TempSweepCodes.Removed),
                TempSweepCodes.WouldRemove => (WorktreeResidueCandidateOutcome.Preview, TempSweepCodes.WouldRemove),
                TempSweepCodes.Kept when !string.IsNullOrWhiteSpace(candidate.Reason) => (WorktreeResidueCandidateOutcome.Held, candidate.Reason!),
                _ => (WorktreeResidueCandidateOutcome.Deferred, "unknown"),
            };
            run.Candidates++;
            if (outcome == WorktreeResidueCandidateOutcome.Held)
                run.Held++;
            var row = Row(run, LaneTemp, outcome, reason, null, root + "/" + candidate.Name,
                "temp:" + candidate.Family + ";bytes=" + candidate.Bytes, runnerId);
            row.OperationId = request.RequestId;
            row.DirectoryRemoved = outcome == WorktreeResidueCandidateOutcome.Removed ? true : null;
        }
        foreach (var name in response.Unregistered)
            Row(run, LaneTemp, WorktreeResidueCandidateOutcome.Held, "unregistered", null, root + "/" + name.Prefix,
                "temp:unregistered;count=" + name.Count + ";bytes=" + name.Bytes + (name.BytesComplete ? "" : "+"), runnerId)
                .OperationId = request.RequestId;
        if (!response.Complete)
            Row(run, LaneTemp, WorktreeResidueCandidateOutcome.Deferred, "temp_scan_incomplete", null, root, null, runnerId);
        if (response.Truncated)
            Row(run, LaneTemp, WorktreeResidueCandidateOutcome.Deferred, "temp_report_truncated", null, root, null, runnerId);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Runner temp sweep {RunnerId} mode {Mode}: candidates {Candidates} removed {Removed} ({RemovedBytes} bytes) unregistered prefixes {Unregistered}{Truncated}",
            runnerId, request.Mode, response.Candidates.Count, response.Removed, response.RemovedBytes, response.Unregistered.Count,
            response.Truncated ? " truncated" : "");
    }

    /// <summary>
    /// Review A-2/A-3: re-read before every request, so the kill switch and the tick budget act between requests. The
    /// operator's one-shot execute (<paramref name="forced"/>) is not stopped by <c>Execute=false</c>, only by the kill switch.
    /// </summary>
    private string? StopReason(bool execute, bool forced, DateTime tickStarted)
    {
        var current = _settings.CurrentValue;
        if (!current.Enabled)
            return "tick_stopped_disabled";
        if (execute && !forced && !current.Execute)
            return "tick_stopped_execute_off";
        return Now() - tickStarted >= TimeSpan.FromMinutes(current.MaxTickMinutes) ? "tick_budget_exhausted" : null;
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
        if (WorkspaceRetentionNames.HasControlCharacter(path))
            return WorkspaceRetentionCodes.NameControlCharacter;
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
        PhoneHomeWorkspaceRetainResponse? response, string? failure, string runnerId,
        Dictionary<string, WorktreeResidueCandidateCursor> sendCursors, CancellationToken ct)
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

        if (!cleared)
            RecordSend(sendCursors, task, outcome, reason, now);
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
            var path = settings.WorktreeRoot.TrimEnd('/') + "/" + entry.Name;
            if (WorkspaceRetentionNames.HasControlCharacter(entry.Name))
            {
                Row(run, LaneInventory, WorktreeResidueCandidateOutcome.Held, WorkspaceRetentionCodes.NameControlCharacter, null, path, null, runnerId);
                continue;
            }
            var match = OwnerPrefix.Match(entry.Name);
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

    private static string Leaf(Guid taskId) => "task-" + taskId.ToString("N")[..8];

    internal static string CursorKey(Guid taskId) => "runner-retention:" + taskId.ToString("N");

    internal static string SendCursorKey(Guid taskId) => SendCursorPrefix + taskId.ToString("N");

    /// <summary>
    /// D-1. The runner's answer for a task that keeps its path. A keep backs the task off for
    /// <c>KeptRecheckMinutes x 2^(repeats - 1)</c>, at most <c>KeptRecheckMaxHours</c>. A reason that differs from the
    /// previous keep is re-sent at once (the next tick), but two changes in a row back off, so a flapping
    /// reason cannot hold a slot every tick. A dry-run <c>would_remove</c> and an unknown outcome never back off;
    /// they only move the task behind the never-sent ones.
    /// </summary>
    private void RecordSend(Dictionary<string, WorktreeResidueCandidateCursor> cursors, AgentTask task,
        WorktreeResidueCandidateOutcome outcome, string reason, DateTime now)
    {
        var key = SendCursorKey(task.Id);
        if (!cursors.TryGetValue(key, out var cursor))
        {
            cursor = new WorktreeResidueCandidateCursor { Id = Guid.NewGuid(), CandidateKey = key, LastEvaluatedAt = now };
            _db.WorktreeResidueCandidateCursors.Add(cursor);
            cursors[key] = cursor;
        }

        var fingerprint = Fingerprint(task);
        var previous = SendState.Parse(cursor, fingerprint);
        var bounded = reason.Length <= 80 ? reason : reason[..80];
        SendState next;
        DateTime? notBefore = null;
        if (outcome is WorktreeResidueCandidateOutcome.Held or WorktreeResidueCandidateOutcome.Refused)
        {
            var kept = previous is { Kind: SendState.Kept or SendState.KeptChanged };
            if (kept && previous!.Reason != bounded && previous.Kind == SendState.Kept)
                next = new SendState(SendState.KeptChanged, previous.Repeats, bounded);
            else
            {
                var repeats = kept ? Math.Min(previous!.Repeats + 1, 20) : 1;
                next = new SendState(SendState.Kept, repeats, bounded);
                var settings = _settings.CurrentValue;
                var minutes = Math.Min(settings.KeptRecheckMinutes * Math.Pow(2, repeats - 1), settings.KeptRecheckMaxHours * 60.0);
                notBefore = now.AddMinutes(minutes);
            }
        }
        else
            next = new SendState(outcome == WorktreeResidueCandidateOutcome.Preview ? SendState.Dry : SendState.Unknown, 0, bounded);

        cursor.LastOutcomeKey = next.Format(fingerprint);
        cursor.LastEvaluatedAt = now;
        cursor.NotBefore = notBefore;
    }

    /// <summary>The task's server facts a runner answer was given for; a change voids the answer.</summary>
    private static string Fingerprint(AgentTask task)
    {
        var facts = string.Join("\n", task.Status, task.CompletedAt?.ToString("O") ?? "-", task.RemoteWorktreePath ?? "-", task.WorktreeBranch ?? "-");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts)))[..16].ToLowerInvariant();
    }

    /// <summary><c>send|&lt;kind&gt;|&lt;repeats&gt;|&lt;fingerprint&gt;|&lt;reason&gt;</c> in the cursor's LastOutcomeKey.</summary>
    private sealed record SendState(string Kind, int Repeats, string Reason)
    {
        public const string Kept = "kept";
        public const string KeptChanged = "kept-changed";
        public const string Dry = "dry";
        public const string Unknown = "unknown";

        public string Format(string fingerprint) =>
            string.Join('|', "send", Kind, Repeats.ToString(System.Globalization.CultureInfo.InvariantCulture), fingerprint, Reason);

        /// <summary>Null when there is no answer for these facts (never sent, unreadable, or the task changed since).</summary>
        public static SendState? Parse(WorktreeResidueCandidateCursor? cursor, string fingerprint)
        {
            var parts = cursor?.LastOutcomeKey?.Split('|', 5);
            if (parts is not { Length: 5 } || parts[0] != "send" || parts[3] != fingerprint
                || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var repeats))
                return null;
            return new SendState(parts[1], repeats, parts[4]);
        }
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
