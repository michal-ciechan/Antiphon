using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

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
    private readonly ILogger<AgentTaskLandService> _logger;

    public AgentTaskLandService(
        AppDbContext db,
        DelegationWorktreeService worktrees,
        AgentTaskService tasks,
        AgentTaskLandQueue queue,
        SessionMessageQueueService messages,
        IEventBus eventBus,
        TimeProvider clock,
        ILogger<AgentTaskLandService> logger)
    {
        _db = db;
        _worktrees = worktrees;
        _tasks = tasks;
        _queue = queue;
        _messages = messages;
        _eventBus = eventBus;
        _clock = clock;
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

        var latestRequest = await _db.AgentTaskEvents
            .Where(e => e.AgentTaskId == taskId && (e.Type == AgentTaskEventType.LandRequested
                || e.Type == AgentTaskEventType.Landed
                || e.Type == AgentTaskEventType.LandRefused
                || e.Type == AgentTaskEventType.LandedWithResidue))
            .OrderByDescending(e => e.At).Select(e => e.Type).FirstOrDefaultAsync(ct);
        if (latestRequest == AgentTaskEventType.LandRequested)
            throw new ConflictException($"Task {DelegationReportFormatter.Short(task.Id)} already has a land operation queued.");

        var now = _clock.GetUtcNow().UtcDateTime;
        _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.LandRequested,
            string.IsNullOrWhiteSpace(verifyFilter)
                ? "Land requested (build verification only when rebase replays commits)."
                : $"Land requested with test filter: {verifyFilter.Trim()}", now));
        await _db.SaveChangesAsync(ct);
        _queue.TryEnqueue(taskId, verifyFilter?.Trim());
        await PublishAsync(task, ct);
        return new LandRequestResult(task.Id, "queued");
    }

    /// <summary>Run one background land request. A Shared-writer hold leaves the request pending.</summary>
    public async Task<LandRunResult> RunAsync(Guid taskId, string? verifyFilter, CancellationToken ct)
    {
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || task.Status != AgentTaskStatus.Succeeded || task.Workspace != WorkspaceMode.Worktree)
            return LandRunResult.Complete;

        if (await _worktrees.IsAlreadyLandedAsync(task, ct))
            return await CleanupAlreadyLandedAsync(task, ct);

        var holder = await FindSharedWriterAsync(task, ct);
        if (holder is not null)
        {
            var requestedAt = await _db.AgentTaskEvents.Where(e => e.AgentTaskId == task.Id
                    && e.Type == AgentTaskEventType.LandRequested)
                .OrderByDescending(e => e.At).Select(e => (DateTime?)e.At).FirstOrDefaultAsync(ct);
            var alreadyHeld = await _db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Held && requestedAt != null && e.At >= requestedAt, ct);
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
            ? await VerifyAsync(task.WorktreePath!, verifyFilter, ct)
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
            await SettleLandedAsync(task, AgentTaskEventType.LandedWithResidue, residueOutcome, ct);
            return LandRunResult.Complete;
        }

        Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Clean, cleanupSeconds);
        var outcome = $"landed {prepared.Branch} -> {prepared.Target} as {finalized.Sha}, pushed "
            + $"(origin/{prepared.Target}={finalized.Sha}), verify: {verify}, worktree removed";
        await SettleLandedAsync(task, AgentTaskEventType.Landed, outcome, ct);
        return LandRunResult.Complete;
    }

    private async Task<LandRunResult> CleanupAlreadyLandedAsync(AgentTask task, CancellationToken ct)
    {
        var cleanupWatch = Stopwatch.StartNew();
        var cleaned = await _worktrees.CleanupAlreadyLandedAsync(task, ct);
        var cleanupSeconds = ElapsedSeconds(cleanupWatch);
        var sha = cleaned.Sha ?? "unknown";
        var branch = task.WorktreeBranch ?? "(branch)";
        var target = cleaned.Target;

        if (cleaned.Removal.IsClean)
        {
            Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Clean, cleanupSeconds,
                "nothing left to clean");
            var outcome = $"landed {branch} -> {target} as {sha}, pushed "
                + $"(origin/{target}={sha}), nothing left to clean";
            await SettleLandedAsync(task, AgentTaskEventType.Landed, outcome, ct);
            return LandRunResult.Complete;
        }

        Record(task, OrchestrationStage.Cleanup, StageOutcomeKind.Failed, cleanupSeconds,
            cleaned.Removal.Residue ?? "cleanup incomplete");
        var residueOutcome = $"landed {branch} -> {target} as {sha}, pushed "
            + $"(origin/{target}={sha}), cleanup incomplete: {cleaned.Removal.Residue}";
        await SettleLandedAsync(task, AgentTaskEventType.LandedWithResidue, residueOutcome, ct);
        return LandRunResult.Complete;
    }

    private async Task SettleLandedAsync(
        AgentTask task, AgentTaskEventType type, string outcome, CancellationToken ct)
    {
        _db.AgentTaskEvents.Add(Event(task.Id, type, outcome, _clock.GetUtcNow().UtcDateTime));
        await _db.SaveChangesAsync(ct);
        await DeliverAsync(task, outcome, ct);
        await PublishAsync(task, ct);
    }

    private async Task<AgentTask?> FindSharedWriterAsync(AgentTask task, CancellationToken ct)
    {
        var candidates = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id && t.Workspace == WorkspaceMode.Shared
                && t.Role != AgentTaskRole.Check
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
            .ToListAsync(ct);
        return IsHeldBehindSharedWriter(task, candidates)
            ? candidates.First(t => t.Id != task.Id && t.Workspace == WorkspaceMode.Shared
                && t.Role != AgentTaskRole.Check
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
            && t.Role != AgentTaskRole.Check
            && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
            && ScopeResolver.KeyFor(t.RepoPath, t.WorkingDirectory) == key);
    }

    private async Task RefuseAsync(AgentTask task, string detail, CancellationToken ct)
    {
        var line = $"land refused: {detail}";
        _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.LandRefused, line, _clock.GetUtcNow().UtcDateTime));
        _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Warning,
            $"Land refused; branch {task.WorktreeBranch} and worktree {task.WorktreePath} were kept. {detail}",
            _clock.GetUtcNow().UtcDateTime));
        await _db.SaveChangesAsync(ct);
        await DeliverAsync(task, line, ct);
        await PublishAsync(task, ct);
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
