using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>At most two ordinary Git slots in a live request; journal facts never grant authority.</summary>
public sealed class WorktreeGuardedCleanup(IWorktreeCleanupJournal journal, IWorktreeLockDiagnostics diagnostics,
    IWorktreeDeleteAccessProbe probe, TimeProvider clock, ILogger<WorktreeGuardedCleanup> logger)
{
    internal async Task<WorktreeRemoval> RemoveAsync(WorktreeRemovalRequest request, GuardedWorktreeRemoval guarded, CancellationToken ct)
    {
        var context = request.CleanupContext!;
        var result = new WorktreeRemoval(false, false, false, "cleanup_evidence_storage_unavailable");
        WorktreeCleanupReference? reference = null;
        try
        {
            var attempt = await journal.ReadAsync(context, ct);
            if (!Matches(attempt, request)) return result with { Residue = "cleanup_attempt_identity_mismatch" };
            var recovering = attempt.InitialCommandId is not null;
            if (recovering)
            {
                await journal.InterruptAsync(context, "cleanup_invocation_interrupted", ct);
                reference = new WorktreeCleanupPresentation().Reference(await journal.ReadAsync(context, ct));
            }
            var initialCommand = Guid.NewGuid();
            var first = await guarded.RemoveDirectoryAsync(request, token => recovering ? Task.FromResult(false)
                : journal.ConsumeSlotAsync(context, initialCommand, false, token), clock, ct);
            result = first.Result;
            if (first.Outcome is null)
                return await FinishAsync(ct);
            if (first.Result.Residue is null)
            {
                await journal.RecordOutcomeAsync(context, first.Outcome, false, false, ct);
                return await FinishAsync(ct);
            }

            var failureStart = first.FailureTimestamp ?? clock.GetTimestamp();
            var remaining = TimeSpan.FromSeconds(10) - clock.GetElapsedTime(failureStart);
            if (remaining <= TimeSpan.Zero) return await BudgetExpiredAsync();
            using var timeout = new CancellationTokenSource(remaining, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var extra = linked.Token;
            void CheckBudget()
            {
                ct.ThrowIfCancellationRequested();
                if (clock.GetElapsedTime(failureStart) >= TimeSpan.FromSeconds(10)) timeout.Cancel();
                extra.ThrowIfCancellationRequested();
            }
            try
            {
                CheckBudget();
                await journal.RecordOutcomeAsync(context, first.Outcome, false, true, extra);
                CheckBudget();
                WorktreeLockSnapshot handles;
                try { handles = await diagnostics.CaptureAsync(request.Source.WorktreePath, extra); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { handles = new(WorktreeLockStatus.Failed, "DiagnosticFailed", clock.GetUtcNow().UtcDateTime, []); }
                CheckBudget();
                WorktreeNativeSnapshot native;
                try { native = await probe.ObserveAsync(new(request.Source.WorktreePath, request.CommonDirectory, request.GitDirectory), handles.Owners, extra); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { native = new(WorktreeLockStatus.Unavailable, "ProbeFailed", []); }
                CheckBudget();
                var capture = new WorktreeCleanupCapture(context.AttemptId, context.RequestId, context.OperationId,
                    context.TaskId, first.Outcome.At, first.Outcome, handles, native);
                await journal.CaptureAsync(context, capture, extra);
                CheckBudget();
                reference = new WorktreeCleanupPresentation().Reference(await journal.ReadAsync(context, extra));
                try { logger.LogInformation("Cleanup capture {CaptureId} request {RequestId} operation {OperationId} task {TaskId}: {Summary}",
                    context.AttemptId, context.RequestId, context.OperationId, context.TaskId, reference.Summary); }
                catch (Exception) { /* A secondary observation sink cannot change cleanup. */ }
                // Nomination must use the exact bounded evidence whose commit was observed.
                var committed = reference.State == WorktreeCleanupCaptureState.Captured && reference.CaptureJson is not null
                    ? JsonSerializer.Deserialize<WorktreeCleanupCapture>(reference.CaptureJson) : null;
                var nominated = committed is not null && committed.Id == context.AttemptId
                    && committed.RequestId == context.RequestId && committed.OperationId == context.OperationId
                    && committed.TaskId == context.TaskId && committed.GitFailure.NormallyExited
                    && committed.GitFailure.ExitCode is not null and not 0
                    && first.Result.Residue == "worktree_remove_failed" && committed.Native.HasSharingConflict;
                CheckBudget();
                await journal.DecideAsync(context, nominated ? "delete_access_sharing_observed" : "no_retry_nomination", extra);
                if (!nominated) return result with { Diagnostics = reference };
                CheckBudget();
                await Task.Delay(TimeSpan.FromMilliseconds(250), clock, extra);
                CheckBudget();
                var retryCommand = Guid.NewGuid();
                var second = await guarded.RemoveDirectoryAsync(request, async token => {
                    CheckBudget();
                    return await journal.ConsumeSlotAsync(context, retryCommand, true, token);
                }, clock, extra);
                result = second.Result;
                CheckBudget();
                if (second.Outcome is not null)
                    await journal.RecordOutcomeAsync(context, second.Outcome, true, false, extra);
                CheckBudget();
                await journal.DecideAsync(context, second.Result.Residue ?? "retry_directory_complete", extra);
                CheckBudget();
                return await FinishAsync(extra);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { return await BudgetExpiredAsync(); }

            async Task<WorktreeRemoval> BudgetExpiredAsync()
            {
                // Required settlement outlives only the extra-work allowance, never caller cancellation.
                await journal.InterruptAsync(context, "cleanup_additional_budget_expired", ct);
                reference = new WorktreeCleanupPresentation().Reference(await journal.ReadAsync(context, ct));
                return result with { Residue = "cleanup_additional_budget_expired", Diagnostics = reference };
            }

            async Task<WorktreeRemoval> FinishAsync(CancellationToken token)
            {
                if (result.Residue is null)
                    result = await guarded.CompleteBranchAsync(request, result, token);
                return result with { Diagnostics = reference };
            }
        }
        catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); throw; }
        catch (Exception)
        {
            // Preserve independently observed completed components even if storage is unavailable.
            return result with { Residue = result.DirectoryGone && result.Unregistered && result.BranchDeleted ? null : "cleanup_evidence_storage_unavailable",
                Diagnostics = reference ?? new(context.AttemptId, context.RequestId, context.OperationId, null,
                    WorktreeCleanupCaptureState.Interrupted, $"capture={context.AttemptId:N}; EvidenceStorageUnavailable", null) };
        }
    }

    private static bool Matches(WorktreeCleanupAttempt a, WorktreeRemovalRequest r) =>
        a.OperationId == r.LandingId && a.TaskId == r.Source.TaskId
        && a.SourceSha == r.ExpectedSourceSha && a.TargetSha == r.ExpectedTargetSha
        && a.SourceFullRef == r.Source.SourceFullRef && a.TargetFullRef == r.Source.TargetFullRef
        && LandingGit.PathsEqual(a.RepositoryPath, r.Source.RepositoryPath)
        && LandingGit.PathsEqual(a.WorktreePath, r.Source.WorktreePath)
        && LandingGit.PathsEqual(a.GitDirectory, r.GitDirectory) && LandingGit.PathsEqual(a.CommonDirectory, r.CommonDirectory);
}
