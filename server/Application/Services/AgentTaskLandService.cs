using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The deterministic, explicit continuation from a reviewed Worktree branch to a pushed target.
/// It is deliberately separate from settlement merge-back: orchestration tasks retain their
/// current immediate parent-branch behaviour, while a reviewed root waits for this operation.
/// </summary>
public sealed class AgentTaskLandService
{
    private readonly AppDbContext _db;
    private readonly DelegationWorktreeService _worktrees;
    private readonly AgentTaskService _tasks;
    private readonly AgentTaskLandQueue _queue;
    private readonly SessionMessageQueueService _messages;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly DelegationSettings _settings;
    private readonly ILogger<AgentTaskLandService> _logger;

    public AgentTaskLandService(
        AppDbContext db,
        DelegationWorktreeService worktrees,
        AgentTaskService tasks,
        AgentTaskLandQueue queue,
        SessionMessageQueueService messages,
        IEventBus eventBus,
        TimeProvider clock,
        IOptions<DelegationSettings> settings,
        ILogger<AgentTaskLandService> logger)
    {
        _db = db;
        _worktrees = worktrees;
        _tasks = tasks;
        _queue = queue;
        _messages = messages;
        _eventBus = eventBus;
        _clock = clock;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Persist and queue an explicit land request. The endpoint returns before git runs.</summary>
    public async Task<LandRequestResult> RequestAsync(Guid taskId, string? verifyFilter, CancellationToken ct)
    {
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId.ToString());
        if (task.Workspace != WorkspaceMode.Worktree)
            throw new ConflictException("Only a Worktree task can be landed.");
        if (task.Status != AgentTaskStatus.Succeeded)
            throw new ConflictException($"Task {DelegationReportFormatter.Short(task.Id)} must have succeeded before it can land.");

        var shortId = DelegationReportFormatter.Short(task.Id);
        if (_queue.IsActive(taskId))
        {
            var requested = task.LandRequestedAt?.ToString("u") ?? "unknown";
            var state = task.LandStartedAt is null
                ? ", queued"
                : $", started {task.LandStartedAt:u}, attempt {task.LandAttempt}";
            throw new ConflictException(
                $"Task {shortId} land is running in this server: requested {requested}{state}. Wait for its outcome event.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var filter = ClipFilter(verifyFilter);
        var previous = task.LandRequestedAt;
        task.LandRequestedAt = now;
        task.LandVerifyFilter = filter;
        task.LandStartedAt = null;
        task.LandAttempt = 0;
        task.ConcurrencyToken = Guid.NewGuid();
        _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.LandRequested,
            filter is null
                ? "Land requested (build verification only when rebase replays commits)."
                : $"Land requested with test filter: {filter}", now));
        var status = "queued";
        if (previous is not null)
        {
            _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Warning,
                $"Previous land request at {previous:u} was not running (server restarted); replaced by this request.",
                now));
            status = "requeued";
        }

        await _db.SaveChangesAsync(ct);
        // false means a concurrent request won the race a moment ago; the row is set and the
        // winner will run it.
        _queue.TryEnqueue(taskId, filter);
        await PublishAsync(task, ct);
        return new LandRequestResult(task.Id, status);
    }

    /// <summary>Run one background land request. A Shared-writer hold leaves the request pending.</summary>
    public async Task<LandRunResult> RunAsync(Guid taskId, string? verifyFilter, CancellationToken ct)
    {
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return LandRunResult.Complete;
        if (task.LandRequestedAt is null)
            return LandRunResult.Complete;
        if (task.Status == AgentTaskStatus.Blocked)
            return LandRunResult.Complete;
        if (task.Status != AgentTaskStatus.Succeeded || task.Workspace != WorkspaceMode.Worktree)
        {
            ClearPending(task);
            await _db.SaveChangesAsync(ct);
            return LandRunResult.Complete;
        }

        var filter = task.LandVerifyFilter ?? verifyFilter;

        if (await _worktrees.IsAlreadyLandedAsync(task, ct))
            return await CleanupAlreadyLandedAsync(task, ct);

        var holder = await FindSharedWriterAsync(task, ct);
        if (holder is not null)
        {
            var alreadyHeld = await _db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Held
                && task.LandRequestedAt != null
                && e.At >= task.LandRequestedAt, ct);
            if (!alreadyHeld)
            {
                _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Held,
                    $"Held: land waits for running Shared write task {DelegationReportFormatter.Short(holder.Id)} \"{holder.Title}\" in this repo.",
                    _clock.GetUtcNow().UtcDateTime));
                await _db.SaveChangesAsync(ct);
                await PublishAsync(task, ct);
            }
            return LandRunResult.Held;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        task.LandStartedAt = now;
        task.LandAttempt += 1;
        task.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);

        var rebaseWatch = Stopwatch.StartNew();
        var prepared = await _worktrees.PrepareLandAsync(task, ct);
        var rebaseSeconds = ElapsedSeconds(rebaseWatch);
        if (prepared.Conflicted)
        {
            task.Status = AgentTaskStatus.Blocked;
            task.FailureReason = $"Rebase onto {prepared.Target} conflicted in {prepared.ConflictFiles.Count} file(s).";
            task.ConcurrencyToken = Guid.NewGuid();
            _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Conflicted,
                $"Conflicts: {string.Join(", ", prepared.ConflictFiles)}", _clock.GetUtcNow().UtcDateTime));
            var merge = await _tasks.CreateMergeTaskAsync(task, prepared.ConflictFiles, ct, prepared.Target);
            Record(task, OrchestrationStage.Rebase, StageOutcomeKind.Found, rebaseSeconds,
                string.Join(", ", prepared.ConflictFiles),
                merge is null ? "merge task cap reached" : merge.Id.ToString("D"));
            await _db.SaveChangesAsync(ct);
            await DeliverAsync(task, merge is null
                ? $"land conflict on {prepared.Target}: {string.Join(", ", prepared.ConflictFiles)}; merge task cap reached"
                : $"land conflict on {prepared.Target}: merge task {DelegationReportFormatter.Short(merge.Id)} is resolving and will finish landing", ct);
            await PublishAsync(task, ct);
            return LandRunResult.Complete;
        }

        if (!prepared.Succeeded)
        {
            Record(task, OrchestrationStage.Rebase, StageOutcomeKind.Failed, rebaseSeconds,
                prepared.Detail ?? "Land preparation failed.");
            await RefuseAsync(task, prepared.Detail ?? "Land preparation failed.", ct);
            return LandRunResult.Complete;
        }

        var verifyWatch = Stopwatch.StartNew();
        var verification = prepared.BaseMoved
            ? await VerifyAsync(task.WorktreePath!, filter, ct)
            : LandVerification.Success("build skipped (base unchanged)");
        var verifySeconds = prepared.BaseMoved ? ElapsedSeconds(verifyWatch) : 0;
        Record(task, OrchestrationStage.Rebase, StageOutcomeKind.Clean, rebaseSeconds);
        if (!prepared.BaseMoved)
        {
            Record(task, OrchestrationStage.Verify, StageOutcomeKind.Skipped, verifySeconds,
                "build skipped (base unchanged)");
        }
        else if (verification.Ok)
        {
            Record(task, OrchestrationStage.Verify, StageOutcomeKind.Clean, verifySeconds, verification.Description);
        }
        else
        {
            Record(task, OrchestrationStage.Verify, StageOutcomeKind.Found, verifySeconds,
                $"{verification.Step} failed:\n{verification.Tail}", task.WorktreePath);
            await RefuseAsync(task, $"{verification.Step} failed:\n{verification.Tail}", ct);
            return LandRunResult.Complete;
        }

        // CARD-0215: probe same-card kept branches against the rebased HEAD before the
        // worktree is removed. Warn, do not refuse — a superseded plan is a legitimate state.
        var (unlandedMarker, unlandedWarnings) = await CollectUnlandedSiblingsAsync(
            task, task.WorktreePath!, ct);

        var cleanupWatch = Stopwatch.StartNew();
        var finalized = await _worktrees.FinalizeLandAsync(task, prepared.Target!, ct);
        var cleanupSeconds = ElapsedSeconds(cleanupWatch);
        if (!finalized.Pushed)
        {
            Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, cleanupSeconds,
                finalized.Detail ?? "Land finalization failed.");
            await RefuseAsync(task, finalized.Detail ?? "Land finalization failed.", ct);
            return LandRunResult.Complete;
        }

        var verify = prepared.BaseMoved
            ? verification.Description
            : "build skipped (base unchanged)";
        if (finalized.Residue is not null)
        {
            Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, cleanupSeconds,
                finalized.Residue);
            var residueOutcome = $"landed {prepared.Branch} -> {prepared.Target} as {finalized.Sha}, pushed "
                + $"(origin/{prepared.Target}={finalized.Sha}), verify: {verify}, cleanup incomplete: {finalized.Residue}";
            await SettleLandedAsync(task, AgentTaskEventType.LandedWithResidue,
                AppendUnlandedMarker(residueOutcome, unlandedMarker), unlandedWarnings, ct);
            return LandRunResult.Complete;
        }

        Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Clean, cleanupSeconds);
        var outcome = $"landed {prepared.Branch} -> {prepared.Target} as {finalized.Sha}, pushed "
            + $"(origin/{prepared.Target}={finalized.Sha}), verify: {verify}, worktree removed";
        await SettleLandedAsync(task, AgentTaskEventType.Landed,
            AppendUnlandedMarker(outcome, unlandedMarker), unlandedWarnings, ct);
        return LandRunResult.Complete;
    }

    private async Task<LandRunResult> CleanupAlreadyLandedAsync(AgentTask task, CancellationToken ct)
    {
        var cleanupWatch = Stopwatch.StartNew();
        var cleaned = await _worktrees.CleanupAlreadyLandedAsync(task, ct);
        var cleanupSeconds = ElapsedSeconds(cleanupWatch);
        if (cleaned.PushFailure is { } pushFailure)
        {
            Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, cleanupSeconds, pushFailure);
            await RefuseAsync(task, pushFailure, ct);
            return LandRunResult.Complete;
        }

        var sha = cleaned.Sha ?? "unknown";
        var branch = task.WorktreeBranch ?? "(branch)";
        var target = cleaned.Target;

        if (cleaned.Removal.IsClean)
        {
            Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Clean, cleanupSeconds,
                "nothing left to clean");
            var outcome = $"landed {branch} -> {target} as {sha}, pushed "
                + $"(origin/{target}={sha}), nothing left to clean";
            await SettleLandedAsync(task, AgentTaskEventType.Landed, outcome, [], ct);
            return LandRunResult.Complete;
        }

        Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, cleanupSeconds,
            cleaned.Removal.Residue ?? "cleanup incomplete");
        var residueOutcome = $"landed {branch} -> {target} as {sha}, pushed "
            + $"(origin/{target}={sha}), cleanup incomplete: {cleaned.Removal.Residue}";
        await SettleLandedAsync(task, AgentTaskEventType.LandedWithResidue, residueOutcome, [], ct);
        return LandRunResult.Complete;
    }

    private async Task SettleLandedAsync(
        AgentTask task,
        AgentTaskEventType type,
        string outcome,
        IReadOnlyList<string> warnings,
        CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var warning in warnings)
            _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Warning, warning, now));
        _db.AgentTaskEvents.Add(Event(task.Id, type, outcome, now));
        ClearPending(task);
        await _db.SaveChangesAsync(ct);
        await DeliverAsync(task, outcome, ct);
        await PublishAsync(task, ct);
    }

    private static string AppendUnlandedMarker(string outcome, string? marker) =>
        marker is null ? outcome : $"{outcome}, {marker}";

    /// <summary>
    /// Same-card kept Worktree branches whose tip is not an ancestor of the rebased HEAD
    /// (CARD-0215). Branch gone = already landed; not an ancestor = stranded, warn.
    /// </summary>
    private async Task<(string? Marker, IReadOnlyList<string> Warnings)> CollectUnlandedSiblingsAsync(
        AgentTask task, string rebasedHeadRepo, CancellationToken ct)
    {
        if (task.CardId is null || task.RepoPath is null || !Directory.Exists(rebasedHeadRepo))
            return (null, []);

        var siblings = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id
                && t.CardId == task.CardId
                && t.Workspace == WorkspaceMode.Worktree
                && (t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Blocked)
                && t.WorktreeBranch != null)
            .Select(t => new { t.Id, t.WorktreeBranch, t.RepoPath, t.WorktreePath })
            .ToListAsync(ct);
        if (siblings.Count == 0)
            return (null, []);

        var cardIdentifier = await _db.Cards.AsNoTracking()
            .Where(c => c.Id == task.CardId)
            .Select(c => c.Identifier)
            .FirstOrDefaultAsync(ct) ?? "the card";

        var tokens = new List<string>();
        var warnings = new List<string>();
        foreach (var sibling in siblings)
        {
            var branch = sibling.WorktreeBranch!;
            if (!DelegationWorktreeService.SharesRepo(task.RepoPath, sibling.RepoPath)
                && !DelegationWorktreeService.SharesRepo(task.RepoPath, sibling.WorktreePath))
                continue;
            if (!await _worktrees.KeptBranchExistsAsync(task.RepoPath, branch, ct))
                continue;
            if (await _worktrees.IsAncestorOfBaseAsync(rebasedHeadRepo, branch, "HEAD", ct))
                continue;

            var shortId = DelegationReportFormatter.Short(sibling.Id);
            tokens.Add($"{shortId}:{branch}");
            warnings.Add(
                $"{cardIdentifier}'s kept branch {branch} (task {shortId}) is not an ancestor of the rebased HEAD.");
        }

        return tokens.Count == 0
            ? (null, [])
            : ($"unlanded-sibling={string.Join(",", tokens)}", warnings);
    }

    private async Task<AgentTask?> FindSharedWriterAsync(AgentTask task, CancellationToken ct)
    {
        var candidates = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id && t.Workspace == WorkspaceMode.Shared
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
            .Where(AgentTaskRoles.NotSpecialist)
            .ToListAsync(ct);
        return IsHeldBehindSharedWriter(task, candidates)
            ? candidates.First(t => t.Id != task.Id && t.Workspace == WorkspaceMode.Shared
                && !AgentTaskRoles.IsSpecialist(t.Role)
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                && ScopeResolver.KeyFor(t.RepoPath, t.WorkingDirectory)
                    == ScopeResolver.KeyFor(task.RepoPath, task.WorkingDirectory))
            : null;
    }

    /// <summary>Pure form of the Shared-writer rule, kept visible for the lease contract tests.</summary>
    internal static bool IsHeldBehindSharedWriter(AgentTask landing, IEnumerable<AgentTask> candidates)
    {
        var key = ScopeResolver.KeyFor(landing.RepoPath, landing.WorkingDirectory);
        return candidates.Any(t => t.Id != landing.Id && t.Workspace == WorkspaceMode.Shared
            && !AgentTaskRoles.IsSpecialist(t.Role)
            && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
            && ScopeResolver.KeyFor(t.RepoPath, t.WorkingDirectory) == key);
    }

    /// <summary>
    /// Re-enqueue pending lands this process does not hold. Runs immediately at boot and every
    /// <see cref="DelegationSettings.LandSweepSeconds"/>.
    /// </summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        var stale = await _db.AgentTasks
            .Where(t => t.LandRequestedAt != null
                && t.Status != AgentTaskStatus.Succeeded
                && t.Status != AgentTaskStatus.Blocked)
            .ToListAsync(ct);
        foreach (var row in stale)
            ClearPending(row);
        if (stale.Count > 0)
            await _db.SaveChangesAsync(ct);

        var pending = await _db.AgentTasks
            .Where(t => t.LandRequestedAt != null
                && t.Status == AgentTaskStatus.Succeeded
                && t.Workspace == WorkspaceMode.Worktree)
            .ToListAsync(ct);
        var maxAttempts = Math.Clamp(_settings.LandMaxAttempts, 1, 10);
        foreach (var row in pending)
        {
            if (_queue.IsActive(row.Id))
                continue;
            if (row.LandAttempt >= maxAttempts)
            {
                var last = row.LandStartedAt?.ToString("u") ?? "unknown";
                await RefuseAsync(row,
                    $"land interrupted {row.LandAttempt} times without finishing (last started {last}); not retried automatically — run -Land again",
                    ct);
                continue;
            }

            if (row.LandStartedAt is not null)
            {
                _db.AgentTaskEvents.Add(Event(row.Id, AgentTaskEventType.Warning,
                    $"Land attempt {row.LandAttempt} started {row.LandStartedAt:u} did not finish (server restarted); re-running.",
                    _clock.GetUtcNow().UtcDateTime));
                row.LandStartedAt = null;
                row.ConcurrencyToken = Guid.NewGuid();
                await _db.SaveChangesAsync(ct);
            }

            _queue.TryEnqueue(row.Id, row.LandVerifyFilter);
        }
    }

    /// <summary>
    /// Drain-side failure: <c>RunAsync</c> threw. Writes <c>LandRefused</c>, keeps the branch,
    /// clears the pending request. If this write itself throws the row stays pending for the sweep.
    /// </summary>
    public async Task FailAsync(Guid taskId, Exception exception, CancellationToken ct)
    {
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return;
        await PersistRefusalAsync(task, $"land failed: {exception.Message}", exception.Message, ct);
    }

    private async Task RefuseAsync(AgentTask task, string detail, CancellationToken ct) =>
        await PersistRefusalAsync(task, $"land refused: {detail}", detail, ct);

    private async Task PersistRefusalAsync(AgentTask task, string line, string warningDetail, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.LandRefused, line, now));
        _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Warning,
            $"Land refused; branch {task.WorktreeBranch} and worktree {task.WorktreePath} were kept. {warningDetail}",
            now));
        ClearPending(task);
        await _db.SaveChangesAsync(ct);
        await DeliverAsync(task, line, ct);
        await PublishAsync(task, ct);
    }

    private static void ClearPending(AgentTask task)
    {
        task.LandRequestedAt = null;
        task.LandVerifyFilter = null;
        task.LandStartedAt = null;
        task.ConcurrencyToken = Guid.NewGuid();
    }

    private static string? ClipFilter(string? verifyFilter)
    {
        if (string.IsNullOrWhiteSpace(verifyFilter))
            return null;
        var trimmed = verifyFilter.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }

    private async Task DeliverAsync(AgentTask task, string body, CancellationToken ct)
    {
        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return;
        try
        {
            await _messages.EnqueueAsync(parentSession, body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"land:{task.Id:N}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not deliver land outcome for task {ShortId}", DelegationReportFormatter.Short(task.Id));
        }
    }

    internal static async Task<LandVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
    {
        try
        {
            // CARD-0331: a kill mid-build leaves bin-land/; the next VerifyAsync overwrites it
            // and the finally still deletes it.
            var build = await RunProcessAsync(worktree, ct, "dotnet", "build", "--property:OutputPath=bin-land/");
            if (!build.Ok)
                return LandVerification.Failure("build", Tail(build));
            if (string.IsNullOrWhiteSpace(filter))
                return LandVerification.Success("build OK");

            var tests = await RunProcessAsync(worktree, ct, "dotnet", "test", "--property:OutputPath=bin-land/", "--", "--treenode-filter", filter);
            return tests.Ok
                ? LandVerification.Success($"build OK, {DescribeTests(tests)}")
                : LandVerification.Failure("tests", Tail(tests));
        }
        finally
        {
            var output = Path.Combine(worktree, "bin-land");
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string cwd, CancellationToken ct, string file, params string[] args)
    {
        var start = new ProcessStartInfo { FileName = file, WorkingDirectory = cwd, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {file}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new ProcessResult(process.ExitCode == 0, await stdout, await stderr);
    }

    private static string Tail(ProcessResult result)
    {
        var text = (result.StdOut + "\n" + result.StdErr).Trim();
        return text.Length <= 1800 ? text : text[^1800..];
    }

    private static string DescribeTests(ProcessResult result)
    {
        var text = result.StdOut + "\n" + result.StdErr;
        var total = System.Text.RegularExpressions.Regex.Match(text, @"total:\s*(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var failed = System.Text.RegularExpressions.Regex.Match(text, @"failed:\s*(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return total.Success && failed.Success && failed.Groups[1].Value == "0"
            ? $"tests {total.Groups[1].Value}/{total.Groups[1].Value}"
            : "tests OK";
    }

    private void Record(
        AgentTask task,
        OrchestrationStage stage,
        StageOutcomeKind outcome,
        int durationSeconds,
        string detail = "",
        string? @ref = null)
    {
        _db.StageOutcomes.Add(new StageOutcome
        {
            Id = Guid.NewGuid(),
            Stage = stage,
            Outcome = outcome,
            Source = StageOutcomeSource.Server,
            SubjectTaskId = task.Id,
            CardId = task.CardId,
            DurationSeconds = durationSeconds,
            Detail = Clip(detail),
            Ref = @ref,
            RecordedAt = _clock.GetUtcNow().UtcDateTime,
        });
    }

    private static int ElapsedSeconds(Stopwatch watch) =>
        (int)Math.Clamp(Math.Round(watch.Elapsed.TotalSeconds), 0, int.MaxValue);

    internal static string Clip(string detail) =>
        detail.Length <= StageOutcome.DetailMaxLength
            ? detail
            : detail[..StageOutcome.DetailMaxLength];

    private Task PublishAsync(AgentTask task, CancellationToken ct) =>
        _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);

    private static AgentTaskEvent Event(Guid taskId, AgentTaskEventType type, string detail, DateTime at) =>
        new() { Id = Guid.NewGuid(), AgentTaskId = taskId, Type = type, Detail = detail, At = at };

    private sealed record ProcessResult(bool Ok, string StdOut, string StdErr);
}

internal sealed record LandVerification(bool Ok, string Step, string Tail, string Description)
{
    public static LandVerification Success(string description) => new(true, string.Empty, string.Empty, description);
    public static LandVerification Failure(string step, string tail) => new(false, step, tail, string.Empty);
}

public sealed record LandRequestResult(Guid TaskId, string Status);
public enum LandRunResult { Complete, Held }
