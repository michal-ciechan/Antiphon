using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0824 S4a (amendment §3.2, test design T-D-1..T-D-8). The automatic release of terminal desktop
/// <c>card-task-&lt;id8&gt;</c> worktrees. Each hourly tick reads the candidates from one DB query (oldest
/// <c>CompletedAt</c> first), holds every task a server fact keeps (live, settling, land, sync debt, shared
/// path, live session, missing report, open dependent, leased slot, a release someone else filed), then for at
/// most <c>MaxReleasesPerTick</c> candidates runs a read-only pre-check (leaf, root, reparse point, main worktree,
/// clean status, HEAD on the branch, the source contained in its own origin branch or else the target) and
/// files a SYSTEM release through <see cref="TaskWorktreeRetirementService.ReleaseSystemAsync"/>, retired
/// through <see cref="TaskWorktreeRetirementService.TryRetireAsync"/> only: the guarded removal re-proves every
/// fact under the repository lease. A dirty or unpushed tree is never removed; its Held row (path, branch, head,
/// origin tip, reason) is the evidence. A refused task backs off (60 min doubling to 24 h) and stops after
/// <c>MaxRefusedAttempts</c>. Every evaluation is a run candidate row (<c>Scope = desktop-legacy-release</c>,
/// Lane <c>DesktopLegacyPolicy</c>; no migration).
///
/// Review 1dcb8d90 D-2: every pre-check, whatever its outcome, counts against <c>MaxReleasesPerTick</c>. A task the
/// pre-check (or the filing) held is remembered per task (a <c>desktop-legacy-check:&lt;id&gt;</c> cursor) and not
/// pre-checked again until its backoff (the T-D-4 shape: <c>RefusedRecheckMinutes</c> doubling to
/// <c>RefusedRecheckMaxHours</c>) has passed; a missing directory with no release is settled (recorded once, never
/// re-checked) until the task's facts change. Never-checked tasks go first, oldest first, then the least recently
/// checked, so a held backlog cannot starve a newer eligible tree. An unchanged Held or over-cap row is not rewritten,
/// and the pause follows only a tick's real filing or removal.
/// </summary>
public sealed class DesktopLegacyReleasePolicyService
{
    public const string Scope = "desktop-legacy-release";
    public const string Lane = "DesktopLegacyPolicy";
    public const string ReleaseReason = "CARD-0824 S4a terminal>=48h";
    public const string MirrorOwnedReason = "runner_mirror_owned_by_retention";
    /// <summary>CARD-1714: the guarded removal refused before the move: a path the no-follow delete cannot open.</summary>
    public const string PathTooLong = "worktree_path_too_long";

    // \z, never $: in .NET $ also matches just before a final newline (CARD-1677 review d0b57b9c A-3).
    private static readonly Regex LeafPattern = new(@"^card-task-(?<id>[0-9a-f]{8})\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly AgentTaskStatus[] Terminal = [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled];
    private static readonly AgentTaskStatus[] Open =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];
    private static readonly AgentTaskSyncDebtState[] OpenDebt =
        [AgentTaskSyncDebtState.Pending, AgentTaskSyncDebtState.Ready, AgentTaskSyncDebtState.Held];
    private static readonly SessionStatus[] LiveSession =
        [SessionStatus.Created, SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly AppDbContext _db;
    private readonly TaskWorktreeRetirementService _retirement;
    private readonly ILandingGit _git;
    private readonly IRepositoryMutationLease _leases;
    private readonly IOptionsMonitor<WorktreeLegacyReleaseSettings> _settings;
    private readonly IOptionsMonitor<RunnerWorktreeRetentionSettings> _retention;
    private readonly GitSettings _gitSettings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DesktopLegacyReleasePolicyService> _logger;

    public DesktopLegacyReleasePolicyService(AppDbContext db, TaskWorktreeRetirementService retirement, ILandingGit git,
        IRepositoryMutationLease leases, IOptionsMonitor<WorktreeLegacyReleaseSettings> settings,
        IOptionsMonitor<RunnerWorktreeRetentionSettings> retention,
        IOptions<GitSettings> gitSettings, TimeProvider clock, ILogger<DesktopLegacyReleasePolicyService> logger)
    {
        _db = db;
        _retirement = retirement;
        _git = git;
        _leases = leases;
        _settings = settings;
        _retention = retention;
        _gitSettings = gitSettings.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Test-only (S-4): invoked with the row's task id (or empty) after each candidate row is recorded.</summary>
    internal Action<Guid>? OnRow { get; set; }
    internal Func<string, Guid, CancellationToken, Task>? DesktopBoundaryAsync { get; set; }

    public sealed record TickResult(Guid? RunId, int Examined, int Eligible, int Filed, int Retired, int BackedOff, string? Stopped);

    public sealed record SettingsRead(bool Enabled, string? Reason, WorktreeLegacyReleaseSettings? Settings);

    /// <summary>
    /// T-D-1/G-63. The current settings, never throwing: an invalid hot reload (the validators refuse it, so the
    /// monitor throws) counts as disabled (fail safe) and logs one Warning per read.
    /// </summary>
    public static SettingsRead ReadSettings(IOptionsMonitor<WorktreeLegacyReleaseSettings> monitor, ILogger? logger = null)
    {
        WorktreeLegacyReleaseSettings current;
        try
        {
            current = monitor.CurrentValue;
        }
        catch (OptionsValidationException ex)
        {
            logger?.LogWarning("WorktreeLegacyRelease settings are invalid; desktop legacy release is treated as disabled: {Failures}",
                string.Join("; ", ex.Failures));
            return new(false, "settings_invalid", null);
        }
        return current.Enabled ? new(true, null, current) : new(false, "disabled", current);
    }

    /// <summary>V-27/G-35/G-36: exactly <c>card-task-&lt;id8&gt;</c> of this task, end-anchored with <c>\z</c>.</summary>
    public static bool IsLegacyLeaf(string? leaf, string id8)
    {
        if (leaf is null) return false;
        var match = LeafPattern.Match(leaf);
        return match.Success && string.Equals(match.Groups["id"].Value, id8, StringComparison.Ordinal);
    }

    internal static string RetryCursorKey(Guid taskId) => "desktop-legacy-retry:" + taskId.ToString("N");

    internal static string RowCursorKey(Guid taskId) => "desktop-legacy:" + taskId.ToString("N");

    internal static string CheckCursorKey(Guid taskId) => "desktop-legacy-check:" + taskId.ToString("N");

    public async Task<TickResult> RunTickAsync(CancellationToken ct, bool preview = false)
    {
        var read = ReadSettings(_settings, _logger);
        // G-1: a disabled tick does nothing at all. T-D-8: the operator preview runs while disabled.
        if (read.Settings is null || (!preview && !read.Enabled))
            return new TickResult(null, 0, 0, 0, 0, 0, read.Reason);
        var settings = read.Settings;
        // G-5: the preview is dry whatever Execute says.
        var execute = settings.Execute && !preview;
        var tickStarted = Now();
        var run = new WorktreeResidueRun
        {
            Id = Guid.NewGuid(), StartedAt = tickStarted, Execute = execute, Preview = preview,
            ActionBudget = settings.MaxReleasesPerTick, Scope = Scope,
        };
        _db.WorktreeResidueRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var root = ResolveRoot();
        // T-D-7: one DB query; a task whose current attempt is already retired is no candidate.
        var candidates = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Workspace == WorkspaceMode.Worktree && t.WorktreePath != null
                && !_db.TaskWorktreeRetirements.Any(r => r.TaskId == t.Id && r.TaskAttempt == t.Attempt
                    && r.State == WorktreeRetirementState.Complete))
            .OrderBy(t => t.CompletedAt).ThenBy(t => t.Id)
            .ToListAsync(ct);
        var facts = await Facts.LoadAsync(_db, null, ct);
        var rowCursors = preview ? new Dictionary<string, WorktreeResidueCandidateCursor>(StringComparer.Ordinal)
            : await LoadCursorsAsync(candidates.Select(t => RowCursorKey(t.Id)).ToList(), ct);
        // The retry (T-D-4) and pre-check (D-2) cursors of every candidate, in one load.
        var taskCursors = await LoadCursorsAsync(candidates.SelectMany(t => new[] { RetryCursorKey(t.Id), CheckCursorKey(t.Id) }).ToList(), ct);
        var released = await _db.TaskWorktreeRetirements.AsNoTracking().Where(r => r.Active)
            .Select(r => new { r.TaskId, r.TaskAttempt }).ToListAsync(ct);
        var activeRelease = released.Select(r => (r.TaskId, r.TaskAttempt)).ToHashSet();

        var eligible = new List<AgentTask>();
        foreach (var task in candidates)
        {
            if (DesktopBoundaryAsync is not null) await DesktopBoundaryAsync("before:legacy-candidate", task.Id, ct);
            var reason = Classify(task, facts, settings, root, Now());
            if (reason is null && DesktopBoundaryAsync is not null) await DesktopBoundaryAsync("unsafe:legacy-candidate", task.Id, ct);
            if (reason is null) eligible.Add(task);
            else await CursorRowAsync(run, rowCursors, task, WorktreeResidueCandidateOutcome.Held, reason, preview, ct);
        }

        // T-D-7: never-checked first (oldest first), then due retries and re-checks by last evaluation; backed-off,
        // exhausted and settled rows are Held and consume no cap (G-58).
        var ordered = new List<AgentTask>();
        var due = new List<(AgentTask Task, DateTime Last)>();
        var backedOff = 0;
        foreach (var task in eligible)
        {
            var retry = RetryState.Parse(taskCursors.GetValueOrDefault(RetryCursorKey(task.Id)));
            var check = CheckState.Parse(taskCursors.GetValueOrDefault(CheckCursorKey(task.Id)), Fingerprint(task));
            if (retry is not null && retry.Count >= settings.MaxRefusedAttempts)
                await CursorRowAsync(run, rowCursors, task, WorktreeResidueCandidateOutcome.Held, "retry_exhausted", preview, ct);
            else if (retry?.NotBefore is { } notBefore && notBefore > Now())
            {
                backedOff++;
                await CursorRowAsync(run, rowCursors, task, WorktreeResidueCandidateOutcome.Held, "refused_recheck_pending", preview, ct);
            }
            else if (check is { Kind: CheckState.Settled })
                await CursorRowAsync(run, rowCursors, task, WorktreeResidueCandidateOutcome.Held, check.Reason, preview, ct);
            else if (check is { Kind: CheckState.Held, NotBefore: { } recheck } && recheck > Now())
            {
                backedOff++;
                await CursorRowAsync(run, rowCursors, task, WorktreeResidueCandidateOutcome.Held, check.Reason, preview, ct);
            }
            else if (!activeRelease.Contains((task.Id, task.Attempt)) && DirectoryMissing(task.WorktreePath!))
            {
                // D-2: one stat, no git and no cap. A filed release is never settled here: a tree its command set aside
                // is finished by the guarded removal.
                if (!preview) RecordCheck(taskCursors, task, "directory_absent", settled: true, settings);
                await CursorRowAsync(run, rowCursors, task, WorktreeResidueCandidateOutcome.Held, "directory_absent", preview, ct);
            }
            else if (retry is null && check is null)
                ordered.Add(task);
            else
                due.Add((task, retry is null ? check!.Last : check is null ? retry.Last : Max(retry.Last, check.Last)));
        }
        ordered.AddRange(due.OrderBy(r => r.Last).ThenBy(r => r.Task.CompletedAt).Select(r => r.Task));

        var prechecks = 0;
        var wrote = false;
        var filed = 0;
        var retired = 0;
        string? stopped = null;
        foreach (var candidate in ordered)
        {
            // G-7/G-9 (D-2): the cap bounds the pre-checks, whatever their outcome, before any per-candidate git call.
            if (prechecks >= settings.MaxReleasesPerTick)
            {
                await CursorRowAsync(run, rowCursors, candidate, WorktreeResidueCandidateOutcome.Deferred, "over_cap", preview, ct);
                continue;
            }
            // D-2: the pause follows only a real filing or removal; A-2: the switch is read after it.
            if (wrote && settings.PauseBetweenSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(settings.PauseBetweenSeconds), _clock, ct);
            stopped = StopReason(preview, execute, tickStarted);
            if (stopped is not null)
            {
                await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, stopped, null, null, ct);
                _logger.LogWarning("Desktop legacy release stopped after {Prechecks} pre-check(s): {Reason}", prechecks, stopped);
                break;
            }

            prechecks++;
            var outcome = await ActAsync(run, candidate, settings, root, execute, rowCursors, taskCursors, preview, tickStarted, ct);
            wrote = outcome.Wrote;
            if (outcome.Filed) filed++;
            if (outcome.Retired) retired++;
            if (outcome.Stopped is not null)
            {
                stopped = outcome.Stopped;
                _logger.LogWarning("Desktop legacy release stopped after {Prechecks} pre-check(s): {Reason}", prechecks, stopped);
                break;
            }
        }

        // CARD-1708: an executing tick the switch did not stop prunes salvage refs past their retention; Review
        // 9222cb00 B-3: the switch is re-read before each delete, and a refusal stops the tick.
        if (execute && stopped is null && StopReason(preview, execute, tickStarted) is null)
            stopped = await PruneSalvageAsync(settings, tickStarted, ct);

        run.FinishedAt = Now();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Desktop legacy release run {RunId} preview={Preview} execute={Execute} examined {Examined} eligible {Eligible} backedOff {BackedOff} filed {Filed} retired {Retired}{Stopped}",
            run.Id, preview, execute, candidates.Count, eligible.Count, backedOff, filed, retired, stopped is null ? "" : " stopped=" + stopped);
        return new TickResult(run.Id, candidates.Count, eligible.Count, filed, retired, backedOff, stopped);
    }

    /// <summary><c>Wrote</c>: the candidate filed a release or ran the guarded removal (the pause follows it).
    /// <c>Stopped</c> is set when the switch or the tick budget refused this row after its pre-check; the row is
    /// already written, so the tick breaks without a second deferred row.</summary>
    private sealed record ActOutcome(bool Wrote, bool Filed, bool Retired, string? Stopped = null);

    private async Task<ActOutcome> ActAsync(WorktreeResidueRun run, AgentTask candidate, WorktreeLegacyReleaseSettings settings,
        string root, bool execute, Dictionary<string, WorktreeResidueCandidateCursor> rowCursors,
        Dictionary<string, WorktreeResidueCandidateCursor> taskCursors, bool preview, DateTime tickStarted, CancellationToken ct)
    {
        var stage = "release_failed";
        try
        {
            // I-8 (the CARD-1677 rule): re-read the row and every server fact right before acting.
            var fresh = await _db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == candidate.Id, ct);
            var recheck = fresh is null ? "task_absent"
                : Classify(fresh, await Facts.LoadAsync(_db, fresh.Id, ct), settings, root, Now());
            if (recheck is not null || !string.Equals(fresh!.WorktreePath, candidate.WorktreePath, StringComparison.Ordinal))
            {
                await CursorRowAsync(run, rowCursors, fresh ?? candidate, WorktreeResidueCandidateOutcome.Held,
                    recheck ?? "path_changed", preview, ct);
                return new(false, false, false);
            }

            var release = await _db.TaskWorktreeRetirements
                .SingleOrDefaultAsync(r => r.TaskId == fresh.Id && r.TaskAttempt == fresh.Attempt && r.Active, ct);
            // Review 1dcb8d90 A-3: a release someone else filed is theirs, in a dry run too (never would_release).
            if (release is not null
                && !string.Equals(release.CallerIdentity, TaskWorktreeRetirementService.SystemCallerIdentity, StringComparison.Ordinal))
            {
                await HoldAsync(run, rowCursors, taskCursors, fresh, "release_active_other", settled: false, settings, preview, ct);
                return new(false, false, false);
            }
            // A filed release whose command already started is re-proved by the guarded removal itself; a tree it
            // set aside or half removed must be finished, not held by a pre-check that no longer applies.
            Precheck? check = null;
            if (release is null || release.CommandIntentId is null)
            {
                // Keep the classified revision across both rereads. A fresh eligible row is not
                // permission to release a revision that this tick never classified.
                check = await PrecheckAsync(candidate, ct);
                if (check.Reason is not null)
                {
                    _logger.LogInformation(
                        "Desktop legacy release held task {TaskId} path {Path} branch {Branch} head {Head} origin {Origin}: {Reason}",
                        fresh.Id, fresh.WorktreePath, fresh.WorktreeBranch, check.Head ?? "-", check.OriginTip ?? "-", check.Reason);
                    await HoldAsync(run, rowCursors, taskCursors, fresh, check.Reason,
                        settled: check.Reason == "directory_absent" && release is null, settings, preview, ct);
                    return new(false, false, false);
                }
                // A dry run's pass moves the task behind the never-checked ones (its census reaches every tree). An
                // executing pass resets a held state only once its filing stands (below).
                if (!preview && !execute)
                    RecordCheck(taskCursors, fresh, "would_release", settled: false, settings);
            }

            // CARD-1717 D-1: re-read after the pre-check (and on a command that already started, which skips it)
            // before filing or TryRetire. The captured execute flag keeps a dry tick on would_release.
            var stoppedNow = StopReason(preview, execute, tickStarted);
            if (stoppedNow is not null)
            {
                await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, stoppedNow, fresh, release?.Id, ct);
                return new(false, false, false, stoppedNow);
            }

            if (DesktopBoundaryAsync is not null) await DesktopBoundaryAsync("before:legacy-action", fresh.Id, ct);
            fresh = await _db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == fresh.Id, ct);
            if (DesktopWorkspaceCleanup.Reason(fresh) is { } desktopReason)
            {
                await HoldAsync(run, rowCursors, taskCursors, fresh, desktopReason, settled: false, settings, preview, ct);
                return new(false, false, false);
            }
            if (fresh.ConcurrencyToken != candidate.ConcurrencyToken)
            {
                await HoldAsync(run, rowCursors, taskCursors, fresh, "task_changed", settled: false, settings, preview, ct);
                return new(false, false, false);
            }
            if (DesktopBoundaryAsync is not null) await DesktopBoundaryAsync("unsafe:legacy-action", fresh.Id, ct);
            if (!execute)
            {
                await RowAsync(run, WorktreeResidueCandidateOutcome.Preview, "would_release", fresh, release?.Id, ct);
                return new(false, false, false);
            }

            var filedNow = false;
            if (release is null)
            {
                IReadOnlyList<WorktreeHandoffDispositionDto>? dispositions = fresh.NextStage is null ? null :
                [
                    new WorktreeHandoffDispositionDto(fresh.NextStage, WorktreeHandoffDispositionKind.Superseded, null, null,
                        $"CARD-0824 S4a: terminal >= 48h; branch {check!.OriginRef} on origin at {check.OriginTip}"),
                ];
                try
                {
                    var dto = await _retirement.ReleaseSystemAsync(fresh.Id,
                        new SystemReleaseRequest(fresh.ConcurrencyToken, check!.Head!, ReleaseReason, dispositions), ct);
                    filedNow = true;
                    release = await _db.TaskWorktreeRetirements.SingleAsync(r => r.Id == dto.Id, ct);
                }
                catch (ConflictException ex)
                {
                    // Each server exclusion has one owner: the service's own code is recorded (masking rule). Review
                    // 4ed13efe A-1: the held state the pre-check found is still current here, so a refused filing backs
                    // off like a pre-check hold (60 min doubling to 24 h) instead of being re-checked every hour.
                    await HoldAsync(run, rowCursors, taskCursors, fresh, ex.Code ?? "release_refused", settled: false, settings, preview, ct);
                    return new(false, false, false);
                }
            }
            // An executing pass resets a held state it already has (no new cursor per filing), once a release stands.
            if (check is not null && taskCursors.ContainsKey(CheckCursorKey(fresh.Id)))
                RecordCheck(taskCursors, fresh, "passed", settled: false, settings);

            stage = "retire_failed";
            var removed = await _retirement.TryRetireAsync(release, run.Id, ct);
            if (removed.Residue == "repository_lease_required")
            {
                // G-59: a busy repository is a deferral; no attempt is counted.
                await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, "repository_lease_required", fresh, release.Id, ct);
                return new(filedNow, filedNow, false);
            }
            var row = new WorktreeResidueRunCandidate();
            if (removed.IsClean)
            {
                var owned = !string.IsNullOrWhiteSpace(fresh.RemoteWorktreePath)
                    && TaskWorktreeRetirementService.MirrorOwnedByRetention(fresh.RunnerId, _retention);
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Removed, owned ? MirrorOwnedReason : "removed",
                    fresh, release.Id, ct, save: false);
                ClearRetry(taskCursors, fresh.Id);
            }
            else if (removed.Residue == PathTooLong && !removed.DirectoryGone && !removed.Unregistered && !removed.BranchDeleted)
            {
                // CARD-1714: the guarded removal refused before the move-aside because the no-follow delete cannot
                // open a path that long. Nothing moved; the tree is held (backing off) rather than ending Partial.
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Held, PathTooLong, fresh, release.Id, ct, save: false);
                StampRetry(taskCursors, fresh.Id, PathTooLong, settings);
            }
            else if (removed.Residue is TaskWorktreeRetirementService.LegacyReleaseDisabled
                or TaskWorktreeRetirementService.LegacyReleaseExecuteOff
                or TaskWorktreeRetirementService.LegacyReleasePaced)
            {
                // The executor already refused before any delete. Do not stamp a retry: toggling the switch
                // must not burn MaxRefusedAttempts, and the pending cursor stays as it was.
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, removed.Residue, fresh, release.Id, ct, save: false);
            }
            else if (removed.DirectoryGone || removed.Unregistered || removed.BranchDeleted)
            {
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Partial, removed.Residue ?? "partial", fresh, release.Id, ct, save: false);
                StampRetry(taskCursors, fresh.Id, removed.Residue ?? "partial", settings);
            }
            else
            {
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Refused, removed.Residue ?? "refused", fresh, release.Id, ct, save: false);
                StampRetry(taskCursors, fresh.Id, removed.Residue ?? "refused", settings);
            }
            row.DirectoryRemoved = removed.DirectoryGone;
            row.RegistrationRemoved = removed.Unregistered;
            row.BranchRemoved = removed.BranchDeleted;
            row.RunnerId = fresh.RunnerId;
            await _db.SaveChangesAsync(ct);
            OnRow?.Invoke(fresh.Id);
            return new(true, filedNow, removed.IsClean);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // G-57: one failing row never ends the tick. Whatever the failed unit of work left tracked is dropped
            // so the next row does not replay it; the run, its rows and the cursors stay.
            _logger.LogWarning(ex, "Desktop legacy release of task {TaskId} failed at {Stage}", candidate.Id, stage);
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
                if (entry.State != EntityState.Unchanged && entry.Entity is not (WorktreeResidueRun
                        or WorktreeResidueRunCandidate or WorktreeResidueCandidateCursor))
                    entry.State = EntityState.Detached;
            StampRetry(taskCursors, candidate.Id, stage, settings);
            await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, stage, candidate, null, ct);
            return new(false, false, false);
        }
    }

    /// <summary>
    /// T-D-3: the policy's own read-only proof before it files. The guarded removal re-proves each fact under the
    /// lease, so the two are independent guards. Dirty, unpushed and unverifiable trees are never filed.
    /// </summary>
    private sealed record Precheck(string? Reason, string? Head = null, string? OriginRef = null, string? OriginTip = null);

    private async Task<Precheck> PrecheckAsync(AgentTask task, CancellationToken ct)
    {
        if (DesktopBoundaryAsync is not null) await DesktopBoundaryAsync("before:legacy-precheck", task.Id, ct);
        var current = await _db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        if (DesktopWorkspaceCleanup.Reason(current) is { } desktopReason) return new(desktopReason);
        if (current.ConcurrencyToken != task.ConcurrencyToken) return new("task_changed");
        if (DesktopBoundaryAsync is not null) await DesktopBoundaryAsync("unsafe:legacy-precheck", task.Id, ct);
        var path = task.WorktreePath!;
        try
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new("directory_absent"); }
            // G-39: a link at the leaf is refused before filing (LinkTarget alone reads null on a readlink error).
            if (attributes.HasFlag(FileAttributes.ReparsePoint) || new DirectoryInfo(path).LinkTarget is not null)
                return new("reparse_point");
            if (!attributes.HasFlag(FileAttributes.Directory))
                return new("directory_absent");
            // G-45: a linked worktree has a .git FILE and is not the first registration (the main working tree).
            var dotGit = Path.Combine(path, ".git");
            if (!File.Exists(dotGit) || File.GetAttributes(dotGit).HasFlag(FileAttributes.ReparsePoint))
                return new("main_worktree");
            var registrations = await _git.RegistrationsAsync(task.RepoPath ?? "", ct);
            var index = registrations.ToList().FindIndex(r => PathsEqual(r.Path, path));
            if (index <= 0)
                return new(index == 0 ? "main_worktree" : "unregistered");

            // G-25/G-26: tracked and untracked changes both count. Debug 52410842: git status exits 0 with nothing
            // listed under a directory it cannot open (past MAX_PATH, unreadable) and only warns, so the listing
            // proves clean only with core.longpaths (Git for Windows) and a silent stderr; a warning holds the tree.
            var status = await _git.RunAsync(path,
                ["-c", "core.longpaths=true", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
            if (!status.Succeeded || status.Output.Length != 0)
                return new("source_dirty");
            if (status.WroteStandardError)
                return new("status_untrusted");
            var head = await _git.RunAsync(path, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
            var source = FullRef(task.WorktreeBranch);
            var branch = await _git.RunAsync(task.RepoPath!, ["rev-parse", "--verify", source + "^{commit}"], ct);
            var headSha = head.Output.Trim();
            if (!head.Succeeded || !branch.Succeeded || !GitObjectId.IsFull(headSha)
                || !string.Equals(headSha, branch.Output.Trim(), StringComparison.Ordinal))
                return new("source_changed", head.Succeeded ? headSha : null);

            // G-27/G-28/G-32: the own origin branch when origin names it, else the merge target; an absent branch is
            // never "pushed", and any read failure fails closed.
            var target = FullRef(string.IsNullOrWhiteSpace(task.MergeTargetRef)
                ? (string.IsNullOrWhiteSpace(_gitSettings.DefaultBranch) ? "master" : _gitSettings.DefaultBranch)
                : task.MergeTargetRef);
            var listed = await _git.RunAsync(task.RepoPath!, ["ls-remote", "origin", source, target], ct);
            if (!listed.Succeeded)
                return new("remote_unverified", headSha);
            var tips = listed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r').Split('\t'))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[1], StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First()[0], StringComparer.Ordinal);
            var originRef = tips.ContainsKey(source) ? source : target;
            if (!tips.TryGetValue(originRef, out var tip) || !GitObjectId.IsFull(tip))
                return new(tips.ContainsKey(target) || tips.ContainsKey(source) ? "remote_unverified" : "unpushed", headSha, originRef);
            // Only an origin object already present locally is compared; nothing is fetched (G-6).
            var local = await _git.RunAsync(task.RepoPath!, ["rev-parse", "--verify", "--quiet", tip + "^{commit}"], ct);
            if (!local.Succeeded)
                return new("remote_unverified", headSha, originRef, tip);
            var contained = await _git.RunAsync(task.RepoPath!, ["merge-base", "--is-ancestor", headSha, tip], ct);
            if (contained.ExitCode == 1)
                return new("unpushed", headSha, originRef, tip);
            if (!contained.Succeeded)
                return new("remote_unverified", headSha, originRef, tip);
            return new(null, headSha, originRef, tip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            return new("inspection_error");
        }
    }

    /// <summary>The server facts, in reason order. Null means the task may be pre-checked.</summary>
    private static string? Classify(AgentTask task, Facts facts, WorktreeLegacyReleaseSettings settings, string root, DateTime now)
    {
        if (DesktopWorkspaceCleanup.Reason(task) is { } desktopReason) return desktopReason;
        // G-13: Blocked is not terminal (§8).
        if (!Terminal.Contains(task.Status))
            return "live_owner";
        // G-11/G-12: >= MinTerminalHours, and a null CompletedAt is the age floor's refusal.
        if (task.CompletedAt is not { } completed || now - completed < TimeSpan.FromHours(settings.MinTerminalHours))
            return "settling";
        var path = task.WorktreePath ?? "";
        // G-37: control characters are refused before any pattern sees the name.
        if (WorkspaceRetentionNames.HasControlCharacter(path) || WorkspaceRetentionNames.HasControlCharacter(task.WorktreeBranch))
            return WorkspaceRetentionCodes.NameControlCharacter;
        // G-38: a relative or non-normalized path (`..`) is outside the root before any name is read.
        if (!Normalized(path))
            return "outside_root";
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        // G-42 (L-9): a runner workspace slot leaf belongs to the slot pool.
        if (leaf.StartsWith("ws-", StringComparison.Ordinal))
            return "slot_managed";
        if (!IsLegacyLeaf(leaf, task.Id.ToString("N")[..8]))
            return "leaf_mismatch";
        if (!DirectChildOfRoot(path, root))
            return "outside_root";
        // G-43: a leased runner slot keeps the task's desktop half.
        if (task.RemoteWorkspaceSlotId is not null && task.RemoteWorkspaceSlotReleasedAt is null)
            return "slot_leased";
        if (task.LandRequestedAt is not null || task.LandStartedAt is not null || facts.PendingLand.Contains(task.Id))
            return "land_pending";
        if (facts.SyncDebt.Contains(task.Id))
            return "sync_debt";
        if (facts.OpenTasks.Any(t => t.Id != task.Id && PathsEqual(t.WorktreePath, task.WorktreePath)))
            return "path_shared";
        if ((task.AgentSessionId is { } bound && facts.LiveSessions.Contains(bound)) || facts.LiveSessionTasks.Contains(task.Id))
            return "live_session";
        // G-19: the system never sets MissingReportReviewed.
        if (string.IsNullOrWhiteSpace(task.Result))
            return "report_missing";
        // G-20/G-21 (T-D-5): deleting the local branch could break a dependent's worktree creation. Review 1dcb8d90 D-1:
        // a queued task names its base only in WorktreeBaseRequestedRef (delegate.ps1 -StartRef) or
        // RequestedWorktreeBaseTaskId until it is provisioned.
        // Review 4ed13efe A-3: both base fields go through the one normaliser, which holds on every reading git could give.
        var branch = BranchName(task.WorktreeBranch);
        if (facts.OpenTasks.Any(t => t.Id != task.Id
                && (t.ParentTaskId == task.Id || t.RequestedWorktreeBaseTaskId == task.Id
                    || (branch.Length > 0 && (NamesBranch(t.WorktreeBaseRef, branch) || NamesBranch(t.WorktreeBaseRequestedRef, branch))))))
            return "dependent_open";
        return null;
    }

    private string? StopReason(bool preview, bool execute, DateTime tickStarted)
    {
        var read = ReadSettings(_settings, _logger);
        // G-2/G-3: re-read before each row. The preview runs while disabled, so only its budget stops it.
        if (!preview && !read.Enabled)
            return "tick_stopped_disabled";
        if (execute && read.Settings?.Execute != true)
            return "tick_stopped_execute_off";
        var minutes = read.Settings?.MaxTickMinutes ?? 1;
        return Now() - tickStarted >= TimeSpan.FromMinutes(minutes) ? "tick_budget_exhausted" : null;
    }

    /// <summary>
    /// The CARD-1446 cursor rule for a Held or over-cap row: an unchanged one is not rewritten (nor its cursor touched,
    /// review 1dcb8d90 D-2) on a scheduled tick. A preview writes every row and reads no row cursor.
    /// </summary>
    private async Task CursorRowAsync(WorktreeResidueRun run, Dictionary<string, WorktreeResidueCandidateCursor> cursors,
        AgentTask task, WorktreeResidueCandidateOutcome outcome, string reason, bool writeAll, CancellationToken ct)
    {
        if (!writeAll)
        {
            var key = RowCursorKey(task.Id);
            var outcomeKey = Lane + "|" + outcome + "|" + reason;
            cursors.TryGetValue(key, out var cursor);
            if (string.Equals(cursor?.LastOutcomeKey, outcomeKey, StringComparison.Ordinal))
            {
                run.Candidates++;
                if (outcome == WorktreeResidueCandidateOutcome.Held) run.Held++;
                else if (outcome == WorktreeResidueCandidateOutcome.Deferred) run.Deferred++;
                return;
            }
            if (cursor is null)
            {
                cursor = new WorktreeResidueCandidateCursor { Id = Guid.NewGuid(), CandidateKey = key, LastEvaluatedAt = Now() };
                _db.WorktreeResidueCandidateCursors.Add(cursor);
                cursors[key] = cursor;
            }
            cursor.LastOutcomeKey = outcomeKey;
            cursor.LastEvaluatedAt = Now();
        }
        await RowAsync(run, outcome, reason, task, null, ct);
    }

    /// <summary>D-2: a pre-check or filing hold; remembered (backoff or settled) on a scheduled tick, never by a preview.</summary>
    private async Task HoldAsync(WorktreeResidueRun run, Dictionary<string, WorktreeResidueCandidateCursor> rowCursors,
        Dictionary<string, WorktreeResidueCandidateCursor> taskCursors, AgentTask task, string reason, bool settled,
        WorktreeLegacyReleaseSettings settings, bool preview, CancellationToken ct)
    {
        if (!preview) RecordCheck(taskCursors, task, reason, settled, settings);
        await CursorRowAsync(run, rowCursors, task, WorktreeResidueCandidateOutcome.Held, reason, preview, ct);
    }

    /// <summary>
    /// D-2: <c>check|&lt;kind&gt;|&lt;repeats&gt;|&lt;fingerprint&gt;|&lt;reason&gt;</c>. A hold backs off
    /// <c>RefusedRecheckMinutes x 2^(repeats - 1)</c>, at most <c>RefusedRecheckMaxHours</c>; a settled hold (a missing
    /// directory) has no recheck time; a passed or dry check only moves the task behind the never-checked ones.
    /// The fingerprint voids the state when the task's facts change.
    /// </summary>
    private void RecordCheck(Dictionary<string, WorktreeResidueCandidateCursor> cursors, AgentTask task, string reason,
        bool settled, WorktreeLegacyReleaseSettings settings)
    {
        var key = CheckCursorKey(task.Id);
        if (!cursors.TryGetValue(key, out var cursor))
        {
            cursor = new WorktreeResidueCandidateCursor { Id = Guid.NewGuid(), CandidateKey = key, LastEvaluatedAt = Now() };
            _db.WorktreeResidueCandidateCursors.Add(cursor);
            cursors[key] = cursor;
        }
        var fingerprint = Fingerprint(task);
        var bounded = reason.Length <= 80 ? reason : reason[..80];
        var held = reason is not ("passed" or "would_release");
        var previous = CheckState.Parse(cursor, fingerprint);
        var repeats = !held || settled ? 0 : previous is { Kind: CheckState.Held } ? Math.Min(previous.Repeats + 1, 30) : 1;
        var kind = settled ? CheckState.Settled : held ? CheckState.Held : CheckState.Checked;
        cursor.LastOutcomeKey = string.Join('|', "check", kind,
            repeats.ToString(System.Globalization.CultureInfo.InvariantCulture), fingerprint, bounded);
        cursor.LastEvaluatedAt = Now();
        cursor.NotBefore = kind == CheckState.Held
            ? Now().AddMinutes(Math.Min(settings.RefusedRecheckMinutes * Math.Pow(2, repeats - 1), settings.RefusedRecheckMaxHours * 60.0))
            : null;
    }

    private sealed record CheckState(string Kind, int Repeats, string Reason, DateTime Last, DateTime? NotBefore)
    {
        public const string Held = "held";
        public const string Settled = "settled";
        public const string Checked = "checked";

        /// <summary>Null when there is no state or it was recorded for other task facts.</summary>
        public static CheckState? Parse(WorktreeResidueCandidateCursor? cursor, string fingerprint)
        {
            var parts = cursor?.LastOutcomeKey?.Split('|', 5);
            if (parts is not { Length: 5 } || parts[0] != "check" || parts[3] != fingerprint
                || parts[1] is not (Held or Settled or Checked)
                || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var repeats))
                return null;
            return new CheckState(parts[1], repeats, parts[4], cursor!.LastEvaluatedAt, cursor.NotBefore);
        }
    }

    /// <summary>The task facts a pre-check answer was given for (the CARD-1677 send fingerprint shape).</summary>
    private static string Fingerprint(AgentTask task)
    {
        var facts = string.Join("\n", task.Status, task.Attempt, task.CompletedAt?.ToString("O") ?? "-",
            task.WorktreePath ?? "-", task.WorktreeBranch ?? "-", task.DesktopWorkspaceKind, task.TaskRefWorkspaceBindingId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(facts)))[..16].ToLowerInvariant();
    }

    private static bool DirectoryMissing(string path)
    {
        try
        {
            File.GetAttributes(path);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Unreadable is not missing: the budgeted pre-check decides.
            return false;
        }
    }

    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;

    private async Task<WorktreeResidueRunCandidate> RowAsync(WorktreeResidueRun run, WorktreeResidueCandidateOutcome outcome,
        string reason, AgentTask? task, Guid? retirementId, CancellationToken ct, bool save = true)
    {
        var row = new WorktreeResidueRunCandidate
        {
            Id = Guid.NewGuid(), RunId = run.Id, Lane = Lane, Outcome = outcome.ToString(), ReasonCode = reason,
            TaskId = task?.Id, RetirementId = retirementId, Path = task?.WorktreePath, Branch = task?.WorktreeBranch,
            TaskAttempt = task?.Attempt, EvaluatedAt = Now(),
        };
        run.Candidates++;
        switch (outcome)
        {
            case WorktreeResidueCandidateOutcome.Held: run.Held++; break;
            case WorktreeResidueCandidateOutcome.Removed: run.Removed++; run.ActionsAccepted++; break;
            case WorktreeResidueCandidateOutcome.Refused: run.Refused++; run.ActionsAccepted++; break;
            case WorktreeResidueCandidateOutcome.Partial: run.Partial++; run.ActionsAccepted++; break;
            case WorktreeResidueCandidateOutcome.Deferred: run.Deferred++; break;
        }
        _db.WorktreeResidueRunCandidates.Add(row);
        if (save)
        {
            await _db.SaveChangesAsync(ct);
            OnRow?.Invoke(task?.Id ?? Guid.Empty);
        }
        return row;
    }

    /// <summary>T-D-4: <c>retry|&lt;count&gt;|&lt;reason&gt;</c>; NotBefore = now + min(60 x 2^(count-1) min, 24 h).</summary>
    private void StampRetry(Dictionary<string, WorktreeResidueCandidateCursor> cursors, Guid taskId, string reason,
        WorktreeLegacyReleaseSettings settings)
    {
        var key = RetryCursorKey(taskId);
        if (!cursors.TryGetValue(key, out var cursor))
        {
            cursor = new WorktreeResidueCandidateCursor { Id = Guid.NewGuid(), CandidateKey = key, LastEvaluatedAt = Now() };
            _db.WorktreeResidueCandidateCursors.Add(cursor);
            cursors[key] = cursor;
        }
        var count = Math.Min((RetryState.Parse(cursor)?.Count ?? 0) + 1, 1000);
        var minutes = Math.Min(settings.RefusedRecheckMinutes * Math.Pow(2, Math.Min(count - 1, 30)),
            settings.RefusedRecheckMaxHours * 60.0);
        var bounded = reason.Length <= 80 ? reason : reason[..80];
        cursor.LastOutcomeKey = "retry|" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + bounded;
        cursor.LastEvaluatedAt = Now();
        cursor.NotBefore = Now().AddMinutes(minutes);
    }

    private static void ClearRetry(Dictionary<string, WorktreeResidueCandidateCursor> cursors, Guid taskId)
    {
        if (!cursors.TryGetValue(RetryCursorKey(taskId), out var cursor)) return;
        cursor.LastOutcomeKey = "retired";
        cursor.NotBefore = null;
    }

    private sealed record RetryState(int Count, DateTime Last, DateTime? NotBefore)
    {
        public static RetryState? Parse(WorktreeResidueCandidateCursor? cursor)
        {
            var parts = cursor?.LastOutcomeKey?.Split('|', 3);
            if (parts is not { Length: 3 } || parts[0] != "retry"
                || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count))
                return null;
            return new RetryState(count, cursor!.LastEvaluatedAt, cursor.NotBefore);
        }
    }

    private async Task<Dictionary<string, WorktreeResidueCandidateCursor>> LoadCursorsAsync(List<string> keys, CancellationToken ct)
    {
        var cursors = new Dictionary<string, WorktreeResidueCandidateCursor>(StringComparer.Ordinal);
        if (keys.Count == 0) return cursors;
        foreach (var chunk in keys.Chunk(1000))
            foreach (var cursor in await _db.WorktreeResidueCandidateCursors.Where(c => chunk.Contains(c.CandidateKey)).ToListAsync(ct))
                cursors[cursor.CandidateKey] = cursor;
        return cursors;
    }

    /// <summary>The batch-loaded server facts (all candidates at once; one task for the pre-act re-read).</summary>
    private sealed record Facts(HashSet<Guid> PendingLand, HashSet<Guid> SyncDebt, List<OpenTask> OpenTasks,
        HashSet<Guid> LiveSessions, HashSet<Guid> LiveSessionTasks)
    {
        public static async Task<Facts> LoadAsync(AppDbContext db, Guid? taskId, CancellationToken ct)
        {
            var land = await db.AgentTaskLandRequests.AsNoTracking()
                .Where(r => r.IsPending && (taskId == null || r.TaskId == taskId)).Select(r => r.TaskId).ToListAsync(ct);
            var debt = await db.AgentTaskSyncDebts.AsNoTracking()
                .Where(d => OpenDebt.Contains(d.State) && (taskId == null || d.TaskId == taskId)).Select(d => d.TaskId).ToListAsync(ct);
            var open = await db.AgentTasks.AsNoTracking().Where(t => Open.Contains(t.Status))
                .Select(t => new OpenTask(t.Id, t.WorktreePath, t.ParentTaskId, t.WorktreeBaseRef, t.WorktreeBaseRequestedRef,
                    t.RequestedWorktreeBaseTaskId)).ToListAsync(ct);
            var live = await db.AgentSessions.AsNoTracking().Where(s => LiveSession.Contains(s.Status)).Select(s => s.Id).ToListAsync(ct);
            var liveTasks = live.Count == 0 ? [] : await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentSessionId != null && live.Contains(e.AgentSessionId.Value) && (taskId == null || e.AgentTaskId == taskId))
                .Select(e => e.AgentTaskId).Distinct().ToListAsync(ct);
            return new([.. land], [.. debt], open, [.. live], [.. liveTasks]);
        }
    }

    private sealed record OpenTask(Guid Id, string? WorktreePath, Guid? ParentTaskId, string? WorktreeBaseRef,
        string? WorktreeBaseRequestedRef, Guid? RequestedWorktreeBaseTaskId);

    private static bool Normalized(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(path), Comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>G-38: the normalized path must be the recorded path and a direct child of the managed root.</summary>
    private static bool DirectChildOfRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Normalized(path)) return false;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(root), Comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private string ResolveRoot()
    {
        var configured = _gitSettings.WorktreeBasePath;
        return string.IsNullOrWhiteSpace(configured) ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), Comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string FullRef(string? value) => string.IsNullOrWhiteSpace(value) ? "refs/heads/master"
        : value.StartsWith("refs/", StringComparison.Ordinal) ? value : "refs/heads/" + value;

    private static readonly string[] BranchPrefixes = ["refs/heads/", "heads/", "refs/remotes/origin/", "remotes/origin/", "origin/"];

    /// <summary>The task's own branch as a local branch name.</summary>
    private static string BranchName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value;
    }

    /// <summary>
    /// Review 4ed13efe A-3 (CARD-1701): whether a recorded start or base ref, in any spelling git resolves, may name the
    /// local <paramref name="branch"/>. A branch name never holds <c>^</c>, <c>~</c>, <c>:</c> or <c>@{</c>
    /// (check-ref-format), so each starts a revision suffix (<c>^{commit}</c>, <c>^0</c>, <c>~0</c>, <c>@{u}</c>) and is cut
    /// off. The rest matches with or without one leading <c>refs/heads/</c>, <c>heads/</c> or (conservatively, as before)
    /// an origin remote-tracking prefix, ignoring case: a Windows loose-ref store resolves a ref case-insensitively.
    /// Every doubtful reading holds the tree, so a dependent can never lose its local base branch.
    /// </summary>
    private static bool NamesBranch(string? value, string branch)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var name = value.Trim();
        var cut = name.IndexOfAny(['^', '~', ':']);
        var reflog = name.IndexOf("@{", StringComparison.Ordinal);
        if (reflog >= 0 && (cut < 0 || reflog < cut)) cut = reflog;
        if (cut >= 0) name = name[..cut];
        if (string.Equals(name, branch, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var prefix in BranchPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(name[prefix.Length..], branch, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// CARD-1708: in each repository a system release ran in, delete the salvage refs whose own reflog is at least
    /// <c>SalvageRefRetentionDays</c> old. Review 9222cb00 B-2: only under the repository lease the release holds, and
    /// only the refs of a task whose every release is Complete and finished at least the retention ago (read under that
    /// lease); an unfinished or retried release keeps its refs. B-3: the switch is re-read before every delete; its
    /// refusal is returned and nothing more is deleted. A younger ref, or one without a readable time, is kept; a busy
    /// lease skips the repository and a failure is logged.
    /// </summary>
    private async Task<string?> PruneSalvageAsync(WorktreeLegacyReleaseSettings settings, DateTime tickStarted,
        CancellationToken ct)
    {
        var keep = TimeSpan.FromDays(settings.SalvageRefRetentionDays);
        Func<CancellationToken, Task<string?>> live = _ => Task.FromResult(StopReason(false, true, tickStarted));
        var repositories = await _db.TaskWorktreeRetirements.AsNoTracking()
            .Where(r => r.CallerIdentity == TaskWorktreeRetirementService.SystemCallerIdentity)
            .Select(r => r.RepositoryPath).Distinct().ToListAsync(ct);
        foreach (var repository in repositories.Where(Directory.Exists).Order(StringComparer.Ordinal))
        {
            if (await live(ct) is { } refused) return refused;
            try
            {
                await using var lease = await _leases.TryAcquireAsync(repository, ct);
                if (lease is null)
                {
                    _logger.LogInformation("Desktop legacy release kept the salvage refs in {Repository}: repository lease busy", repository);
                    continue;
                }
                var cutoff = Now() - keep;
                var releases = await _db.TaskWorktreeRetirements.AsNoTracking()
                    .Where(r => _db.TaskWorktreeRetirements.Any(s => s.TaskId == r.TaskId && s.RepositoryPath == repository
                        && s.CallerIdentity == TaskWorktreeRetirementService.SystemCallerIdentity))
                    .Select(r => new { r.TaskId, r.State, r.RetirementCompletedAt }).ToListAsync(ct);
                var prunable = releases.GroupBy(r => r.TaskId)
                    .Where(g => g.All(r => r.State == WorktreeRetirementState.Complete
                        && r.RetirementCompletedAt is { } done && done <= cutoff))
                    .Select(g => WorktreeReflogSalvage.Scope(g.Key)).ToHashSet(StringComparer.Ordinal);
                var (pruned, stopped) = await WorktreeReflogSalvage.PruneAsync(_git, repository, Now(), keep, prunable, live, ct);
                if (pruned > 0)
                    _logger.LogInformation("Desktop legacy release pruned {Count} salvage ref(s) in {Repository}", pruned, repository);
                if (stopped is not null) return stopped;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TimeoutException)
            {
                _logger.LogWarning(ex, "Desktop legacy release could not prune salvage refs in {Repository}", repository);
            }
        }
        return null;
    }
}
