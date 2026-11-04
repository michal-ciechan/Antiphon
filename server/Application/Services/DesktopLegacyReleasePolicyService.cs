using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
/// </summary>
public sealed class DesktopLegacyReleasePolicyService
{
    public const string Scope = "desktop-legacy-release";
    public const string Lane = "DesktopLegacyPolicy";
    public const string ReleaseReason = "CARD-0824 S4a terminal>=48h";
    public const string MirrorOwnedReason = "runner_mirror_owned_by_retention";

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
    private readonly IOptionsMonitor<WorktreeLegacyReleaseSettings> _settings;
    private readonly IOptionsMonitor<RunnerWorktreeRetentionSettings> _retention;
    private readonly GitSettings _gitSettings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DesktopLegacyReleasePolicyService> _logger;

    public DesktopLegacyReleasePolicyService(AppDbContext db, TaskWorktreeRetirementService retirement, ILandingGit git,
        IOptionsMonitor<WorktreeLegacyReleaseSettings> settings, IOptionsMonitor<RunnerWorktreeRetentionSettings> retention,
        IOptions<GitSettings> gitSettings, TimeProvider clock, ILogger<DesktopLegacyReleasePolicyService> logger)
    {
        _db = db;
        _retirement = retirement;
        _git = git;
        _settings = settings;
        _retention = retention;
        _gitSettings = gitSettings.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Test-only (S-4): invoked with the row's task id (or empty) after each candidate row is recorded.</summary>
    internal Action<Guid>? OnRow { get; set; }

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
        var retryCursors = await LoadCursorsAsync(candidates.Select(t => RetryCursorKey(t.Id)).ToList(), ct);

        var eligible = new List<AgentTask>();
        foreach (var task in candidates)
        {
            var reason = Classify(task, facts, settings, root, Now());
            if (reason is null) eligible.Add(task);
            else await HeldRowAsync(run, rowCursors, task, reason, preview, ct);
        }

        // T-D-7: never-attempted first, then due retries by last attempt; backed-off and exhausted rows are Held
        // and consume no cap (G-58).
        var ordered = new List<AgentTask>();
        var retries = new List<(AgentTask Task, DateTime Last)>();
        var backedOff = 0;
        foreach (var task in eligible)
        {
            var retry = RetryState.Parse(retryCursors.GetValueOrDefault(RetryCursorKey(task.Id)));
            if (retry is null) { ordered.Add(task); continue; }
            if (retry.Count >= settings.MaxRefusedAttempts)
                await HeldRowAsync(run, rowCursors, task, "retry_exhausted", preview, ct);
            else if (retry.NotBefore is { } notBefore && notBefore > Now())
            {
                backedOff++;
                await HeldRowAsync(run, rowCursors, task, "refused_recheck_pending", preview, ct);
            }
            else
                retries.Add((task, retry.Last));
        }
        ordered.AddRange(retries.OrderBy(r => r.Last).ThenBy(r => r.Task.CompletedAt).Select(r => r.Task));

        var acted = 0;
        var filed = 0;
        var retired = 0;
        string? stopped = null;
        foreach (var candidate in ordered)
        {
            // G-7/G-9: the cap applies before any per-candidate filesystem or git call.
            if (acted >= settings.MaxReleasesPerTick)
            {
                await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, "over_cap", candidate, null, ct);
                continue;
            }
            stopped = StopReason(preview, execute, tickStarted);
            if (stopped is not null)
            {
                await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, stopped, null, null, ct);
                _logger.LogWarning("Desktop legacy release stopped after {Acted} candidate(s): {Reason}", acted, stopped);
                break;
            }
            if (acted > 0 && settings.PauseBetweenSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(settings.PauseBetweenSeconds), _clock, ct);

            var outcome = await ActAsync(run, candidate, settings, root, execute, rowCursors, retryCursors, preview, ct);
            if (outcome.Acted) acted++;
            if (outcome.Filed) filed++;
            if (outcome.Retired) retired++;
        }

        run.FinishedAt = Now();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Desktop legacy release run {RunId} preview={Preview} execute={Execute} examined {Examined} eligible {Eligible} backedOff {BackedOff} filed {Filed} retired {Retired}{Stopped}",
            run.Id, preview, execute, candidates.Count, eligible.Count, backedOff, filed, retired, stopped is null ? "" : " stopped=" + stopped);
        return new TickResult(run.Id, candidates.Count, eligible.Count, filed, retired, backedOff, stopped);
    }

    private sealed record ActOutcome(bool Acted, bool Filed, bool Retired);

    private async Task<ActOutcome> ActAsync(WorktreeResidueRun run, AgentTask candidate, WorktreeLegacyReleaseSettings settings,
        string root, bool execute, Dictionary<string, WorktreeResidueCandidateCursor> rowCursors,
        Dictionary<string, WorktreeResidueCandidateCursor> retryCursors, bool preview, CancellationToken ct)
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
                await HeldRowAsync(run, rowCursors, fresh ?? candidate, recheck ?? "path_changed", true, ct);
                return new(false, false, false);
            }

            var release = await _db.TaskWorktreeRetirements
                .SingleOrDefaultAsync(r => r.TaskId == fresh.Id && r.TaskAttempt == fresh.Attempt && r.Active, ct);
            // A filed release whose command already started is re-proved by the guarded removal itself; a tree it
            // set aside or half removed must be finished, not held by a pre-check that no longer applies.
            Precheck? check = null;
            if (release is null || release.CommandIntentId is null)
            {
                check = await PrecheckAsync(fresh, ct);
                if (check.Reason is not null)
                {
                    _logger.LogInformation(
                        "Desktop legacy release held task {TaskId} path {Path} branch {Branch} head {Head} origin {Origin}: {Reason}",
                        fresh.Id, fresh.WorktreePath, fresh.WorktreeBranch, check.Head ?? "-", check.OriginTip ?? "-", check.Reason);
                    await HeldRowAsync(run, rowCursors, fresh, check.Reason, true, ct);
                    return new(false, false, false);
                }
            }

            if (!execute)
            {
                await RowAsync(run, WorktreeResidueCandidateOutcome.Preview, "would_release", fresh, release?.Id, ct);
                return new(true, false, false);
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
                    // Each server exclusion has one owner: the service's own code is recorded (masking rule).
                    await HeldRowAsync(run, rowCursors, fresh, ex.Code ?? "release_refused", true, ct);
                    return new(false, false, false);
                }
            }
            else if (!string.Equals(release.CallerIdentity, TaskWorktreeRetirementService.SystemCallerIdentity, StringComparison.Ordinal))
            {
                await HeldRowAsync(run, rowCursors, fresh, "release_active_other", true, ct);
                return new(false, false, false);
            }

            stage = "retire_failed";
            var removed = await _retirement.TryRetireAsync(release, run.Id, ct);
            if (removed.Residue == "repository_lease_required")
            {
                // G-59: a busy repository is a deferral; no attempt is counted.
                await RowAsync(run, WorktreeResidueCandidateOutcome.Deferred, "repository_lease_required", fresh, release.Id, ct);
                return new(false, filedNow, false);
            }
            var row = new WorktreeResidueRunCandidate();
            if (removed.IsClean)
            {
                var owned = !string.IsNullOrWhiteSpace(fresh.RemoteWorktreePath)
                    && TaskWorktreeRetirementService.MirrorOwnedByRetention(fresh.RunnerId, _retention);
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Removed, owned ? MirrorOwnedReason : "removed",
                    fresh, release.Id, ct, save: false);
                ClearRetry(retryCursors, fresh.Id);
            }
            else if (removed.DirectoryGone || removed.Unregistered || removed.BranchDeleted)
            {
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Partial, removed.Residue ?? "partial", fresh, release.Id, ct, save: false);
                StampRetry(retryCursors, fresh.Id, removed.Residue ?? "partial", settings);
            }
            else
            {
                row = await RowAsync(run, WorktreeResidueCandidateOutcome.Refused, removed.Residue ?? "refused", fresh, release.Id, ct, save: false);
                StampRetry(retryCursors, fresh.Id, removed.Residue ?? "refused", settings);
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
            StampRetry(retryCursors, candidate.Id, stage, settings);
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

            // G-25/G-26: tracked and untracked changes both count.
            var status = await _git.RunAsync(path, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
            if (!status.Succeeded || status.Output.Length != 0)
                return new("source_dirty");
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
        // G-20/G-21 (T-D-5): deleting the local branch could break a dependent's worktree creation.
        var branch = BranchName(task.WorktreeBranch);
        if (facts.OpenTasks.Any(t => t.Id != task.Id
                && (t.ParentTaskId == task.Id
                    || (branch.Length > 0 && string.Equals(BranchName(t.WorktreeBaseRef), branch, StringComparison.Ordinal)))))
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

    private async Task HeldRowAsync(WorktreeResidueRun run, Dictionary<string, WorktreeResidueCandidateCursor> cursors,
        AgentTask task, string reason, bool writeAll, CancellationToken ct)
    {
        run.Held++;
        // The CARD-1446 cursor rule: an unchanged Held row is not rewritten every scheduled tick.
        if (!writeAll)
        {
            var key = RowCursorKey(task.Id);
            if (!cursors.TryGetValue(key, out var cursor))
            {
                cursor = new WorktreeResidueCandidateCursor { Id = Guid.NewGuid(), CandidateKey = key, LastEvaluatedAt = Now() };
                _db.WorktreeResidueCandidateCursors.Add(cursor);
                cursors[key] = cursor;
            }
            var outcomeKey = Lane + "|" + nameof(WorktreeResidueCandidateOutcome.Held) + "|" + reason;
            var unchanged = string.Equals(cursor.LastOutcomeKey, outcomeKey, StringComparison.Ordinal);
            cursor.LastOutcomeKey = outcomeKey;
            cursor.LastEvaluatedAt = Now();
            if (unchanged)
            {
                run.Candidates++;
                await _db.SaveChangesAsync(ct);
                return;
            }
        }
        await RowAsync(run, WorktreeResidueCandidateOutcome.Held, reason, task, null, ct, countHeld: false);
    }

    private async Task<WorktreeResidueRunCandidate> RowAsync(WorktreeResidueRun run, WorktreeResidueCandidateOutcome outcome,
        string reason, AgentTask? task, Guid? retirementId, CancellationToken ct, bool save = true, bool countHeld = true)
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
            case WorktreeResidueCandidateOutcome.Held when countHeld: run.Held++; break;
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
                .Select(t => new OpenTask(t.Id, t.WorktreePath, t.ParentTaskId, t.WorktreeBaseRef)).ToListAsync(ct);
            var live = await db.AgentSessions.AsNoTracking().Where(s => LiveSession.Contains(s.Status)).Select(s => s.Id).ToListAsync(ct);
            var liveTasks = live.Count == 0 ? [] : await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentSessionId != null && live.Contains(e.AgentSessionId.Value) && (taskId == null || e.AgentTaskId == taskId))
                .Select(e => e.AgentTaskId).Distinct().ToListAsync(ct);
            return new([.. land], [.. debt], open, [.. live], [.. liveTasks]);
        }
    }

    private sealed record OpenTask(Guid Id, string? WorktreePath, Guid? ParentTaskId, string? WorktreeBaseRef);

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

    /// <summary>A start/base ref in any of its recorded spellings, as a local branch name.</summary>
    private static string BranchName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        foreach (var prefix in new[] { "refs/heads/", "refs/remotes/origin/", "origin/" })
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return value[prefix.Length..];
        return value;
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
