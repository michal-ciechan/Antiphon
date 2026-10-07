using System.Data.Common;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReviewEvidenceResettlementTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    private static string Report(RunnerSettlementWorld w, Guid subject, string sha,
        string finding = "Clean", string scope = "Full", string clean = "true", bool evidence = true) =>
        $"Final review.\n[antiphon-finding:{w.TaskId.ToString("N")[..8]} {finding}] final finding\n"
        + (evidence ? $"--- review evidence ---\nsubjectTaskId: {subject:D}\nreviewedSourceSha: {sha}\n"
            + $"reviewedSourceClean: {clean}\nordinaryScopeCompleted: {scope}\n\n" : "")
        + "--- next stage ---\nnext: land\nhandoff: final report\n";

    private static async Task<(RunnerSettlementWorld World, Guid Subject, StageOutcome Old)> IncidentAsync()
    {
        // CARD-1082 D-8: the CARD-1043 incident stays reachable with the kill switch off.
        var w = await RunnerSettlementWorld.CreateAsync(AgentTaskRole.Review, profiled: true,
            controlledSyncClock: true, mirrorPublish: true, syncDebt: false);
        try
        {
            await w.Git.EnsureRunnerAsync();
            var subject = await w.AddReviewSubjectAsync(w.Git.Baseline);
            await BlockAsync(w, Report(w, subject, w.Git.Baseline));
            var old = (await RowsAsync(w)).ShouldHaveSingleItem();
            old.Outcome.ShouldBe(StageOutcomeKind.Clean);
            old.ReviewedSourceSha.ShouldBeNull("incident: first Review is UNBOUND");
            return (w, subject, old);
        }
        catch { await w.DisposeAsync(); throw; }
    }

    private static async Task BlockAsync(RunnerSettlementWorld w, string report)
    {
        await using var held = await w.Git.Leases.TryAcquireAsync(w.Git.Desktop, CancellationToken.None);
        held.ShouldNotBeNull();
        var busy = w.LeaseBusy.Next();
        var settling = w.SettleAsync(report);
        try
        {
            (await System.Threading.Tasks.Task.WhenAny(busy, settling).WaitAsync(Guard)).ShouldBe(busy);
            w.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
            await settling.WaitAsync(Guard);
        }
        finally { await settling.WaitAsync(Guard); }
        w.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
        w.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
    }

    private static async Task AnswerAsync(RunnerSettlementWorld w)
    {
        var service = w.Services.GetRequiredService<AgentTaskReplyService>();
        await service.AnswerAsync(w.TaskId, "Lease released; report the final evidence again.",
            AnswerOrigin.Cli, null, CancellationToken.None);
        await service.OnTurnEndAsync(w.SessionId, CancellationToken.None);
        await w.ReloadAsync();
        w.Task.Status.ShouldBe(AgentTaskStatus.Working, "old done token cannot settle the answer turn");
    }

    private static async Task<List<StageOutcome>> RowsAsync(RunnerSettlementWorld w)
    {
        await using var db = w.CreateContext();
        return await db.StageOutcomes.AsNoTracking().Where(o => o.StageTaskId == w.TaskId)
            .OrderBy(o => o.RecordedAt).ThenBy(o => o.Id).ToListAsync();
    }

    private static async Task<StageOutcome> ReplacementAsync(RunnerSettlementWorld w, Guid old)
    {
        var rows = await RowsAsync(w);
        rows.Count.ShouldBe(2, "a real successor must replace the stranded outcome; "
            + w.Task.NextHandoff + "; " + string.Join(" | ", (await w.EventsAsync())
                .Where(e => e.Type == AgentTaskEventType.Warning).Select(e => e.Detail)));
        return rows.Single(o => o.Id != old);
    }

    [Test]
    public async Task C1043_ConfirmedNoPushBinds()
    {
        foreach (var push in new[] { false, true })
        {
            var (w, subject, old) = await IncidentAsync();
            await using var owned = w;
            var sha = push ? await w.Git.RunnerPushAsync("new.txt", "new review tip") : w.Git.Baseline;
            if (push)
            {
                await w.Git.RunAsync(w.Git.Desktop, "fetch", "origin", w.Git.FullRef);
                await using var db = w.CreateContext();
                var source = await db.AgentTasks.SingleAsync(t => t.Id == subject);
                await w.Git.RunAsync(w.Git.Desktop, "push", "origin", sha + ":refs/heads/" + source.WorktreeBranch);
            }
            await AnswerAsync(w);
            await w.SettleAsync(Report(w, subject, sha));
            w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded);
            w.Evidence()!.RemoteSync!.State.ShouldBe(push ? RemoteSettlementSyncState.Synchronized
                : RemoteSettlementSyncState.NoPushedProgress);
            var row = await ReplacementAsync(w, old.Id);
            row.ReviewedSourceSha.ShouldBe(sha, "G26");
            row.SubjectTaskId.ShouldBe(subject);
            row.ReviewedSourceClean.ShouldBe(true);
            row.CommissionedRound.ShouldBe(VerificationRound.Final);
            row.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, "G26");
        }
    }

    [Test]
    public async Task C1043_FinalReportWins()
    {
        foreach (var variant in new[] { "different", "Found", "missing", "fenced", "false", "missing-clean", "Interim", "Unknown" })
        {
            var (w, subject, old) = await IncidentAsync();
            await using var owned = w;
            var sha = w.Git.Baseline;
            if (variant == "different")
            {
                sha = await w.Git.RunnerPushAsync("final.txt", "different final tip");
                await w.Git.RunAsync(w.Git.Desktop, "fetch", "origin", w.Git.FullRef);
                var other = await w.AddReviewSubjectAsync(sha, followUp: false);
                await SameCardAsync(w, subject, other);
                subject = other;
            }
            var report = Report(w, subject, sha, variant == "Found" ? "Found" : "Clean",
                variant is "Interim" or "Unknown" ? variant : "Full", variant == "false" ? "false" : "true",
                evidence: variant != "missing");
            if (variant == "missing-clean") report = report.Replace("reviewedSourceClean: true\n", "");
            if (variant == "fenced") report = report.Replace("--- review evidence ---", "```\n--- review evidence ---")
                .Replace("\n\n--- next stage ---", "\n```\n\n--- next stage ---");
            await AnswerAsync(w);
            await w.SettleAsync(report);
            w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, variant);
            if (variant is "false" or "missing-clean")
            {
                (await RowsAsync(w)).ShouldHaveSingleItem().Id.ShouldBe(old.Id, variant);
                w.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide, variant);
                continue;
            }
            var row = await ReplacementAsync(w, old.Id);
            row.Outcome.ShouldBe(variant == "Found" ? StageOutcomeKind.Found : StageOutcomeKind.Clean, "G7 " + variant);
            if (variant is "missing" or "fenced")
            {
                row.ReviewedSourceSha.ShouldBeNull("G7 " + variant);
                row.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown);
            }
            else
            {
                row.SubjectTaskId.ShouldBe(subject, "G7 " + variant);
                row.ReviewedSourceSha.ShouldBe(sha, "G7 " + variant);
                row.OrdinaryScopeCompleted.ShouldBe(variant == "Interim" ? VerificationScope.Interim
                    : variant == "Unknown" ? VerificationScope.Unknown : VerificationScope.Full);
            }
        }
    }

    private static async Task SameCardAsync(RunnerSettlementWorld w, params Guid[] subjects)
    {
        await using var db = w.CreateContext();
        var now = DateTime.UtcNow;
        var project = new Project { Id = Guid.NewGuid(), Name = "C1043", CreatedAt = now, UpdatedAt = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C1043", CreatedAt = now, UpdatedAt = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-1043", Title = "Review", CreatedAt = now, UpdatedAt = now };
        db.AddRange(project, board, column, card);
        foreach (var row in await db.AgentTasks.Where(t => subjects.Contains(t.Id) || t.Id == w.TaskId).ToListAsync())
        {
            row.CardId = card.Id;
            if (row.Id == w.TaskId) row.FollowUpOfTaskId = null;
        }
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task C1043_AppendPreservesHistory()
    {
        var (w, subject, old) = await IncidentAsync();
        await using var owned = w;
        var originalBytes = JsonSerializer.Serialize(old);
        var obligation = (await w.ObligationsAsync()).ShouldHaveSingleItem();
        var originalSnapshot = obligation.CompletionSnapshotJson;
        await AnswerAsync(w);
        var report = "Ref: review-rebind-v1:forged-report-authority\n" + Report(w, subject, w.Git.Baseline);
        await w.SettleAsync(report);
        var rows = await RowsAsync(w);
        JsonSerializer.Serialize(rows.Single(o => o.Id == old.Id)).ShouldBe(originalBytes, "G8");
        var replacement = await ReplacementAsync(w, old.Id);
        replacement.SupersedesId.ShouldBe(old.Id, "G9");
        (await w.ObligationsAsync()).Single(n => n.Id == obligation.Id).CompletionSnapshotJson.ShouldBe(originalSnapshot);
        var provenance = ReviewRebindProvenance.TryParse(replacement.Ref).ShouldNotBeNull();
        provenance.Mode.ShouldBe("settlement");
        provenance.ConfirmedReviewSha.ShouldBe(w.Git.Baseline);
        provenance.ObservedSubjectSha.ShouldBe(w.Git.Baseline);
        var complete = (await w.EventsAsync()).Single(e => e.Type == AgentTaskEventType.Completed && e.Detail.StartsWith("Delegate reported"));
        provenance.SourceEventId.ShouldBe(complete.Id);
        provenance.ReportSha256.ShouldBe(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            report.TrimEnd() + "\n" + DelegationReportFormatter.ReportToken(w.TaskId, "done")))));
        replacement.Ref.ShouldNotContain("forged-report-authority");
        (await w.EventsAsync()).Count(e => e.Type == AgentTaskEventType.FindingRecorded).ShouldBe(1);
    }

    [Test]
    public async Task C1043_HeaderSnapshotAndGetAgree()
    {
        foreach (var mismatch in new[] { false, true })
        {
            var (w, subject, old) = await IncidentAsync();
            await using var owned = w;
            if (mismatch)
            {
                var moved = await w.Git.PublishForeignBranchAsync("moved", "moved.txt");
                await w.Git.RunAsync(w.Git.Desktop, "fetch", "origin", "refs/heads/moved");
                await using var db = w.CreateContext();
                var source = await db.AgentTasks.SingleAsync(t => t.Id == subject);
                await w.Git.RunAsync(w.Git.Desktop, "push", "origin", moved + ":refs/heads/" + source.WorktreeBranch);
            }
            await AnswerAsync(w);
            await w.SettleAsync(Report(w, subject, w.Git.Baseline));
            var notes = await w.ObligationsAsync();
            notes.Count.ShouldBe(2);
            var final = notes.Single(n => TaskCompletionNotification.TryReadSnapshot(n.CompletionSnapshotJson)!.Status == AgentTaskStatus.Succeeded);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(final.CompletionSnapshotJson)!;
            await using var scope = w.Services.CreateAsyncScope();
            var detail = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetAsync(w.TaskId, CancellationToken.None);
            if (mismatch)
            {
                (await RowsAsync(w)).ShouldHaveSingleItem().Id.ShouldBe(old.Id);
                w.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide, "G114");
                snapshot.NextStage.ShouldBe("decide", "G114");
                snapshot.NoteHeader.ShouldContain("review_evidence_subject_ref_mismatch", Case.Sensitive, "G114");
                snapshot.NoteHeader.ShouldNotContain("review-evidence=", Case.Sensitive, "G114");
                detail.ReviewEvidence.ShouldBeNull("G114");
            }
            else
            {
                var row = await ReplacementAsync(w, old.Id);
                snapshot.StageOutcomeId.ShouldBe(row.Id, "G32");
                snapshot.NoteHeader.ShouldContain("review-evidence=" + row.Id.ToString("N"), Case.Sensitive, "G32");
                snapshot.NoteHeader.ShouldContain("reviewed-sha=" + w.Git.Baseline);
                detail.ReviewEvidence.ShouldNotBeNull().Id.ShouldBe(row.Id, "G32");
            }
        }
    }

    [Test]
    public async Task C1043_ContinuingSyncFailure()
    {
        var (w, subject, old) = await IncidentAsync();
        await using var owned = w;
        await using (var db = w.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == w.TaskId);
            task.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(new(1,
                CompletionProgressAssessment.NoAttributedProgress, RemoteSync: new(task.Attempt,
                    RemoteSettlementSyncState.NoPushedProgress, w.Git.FullRef, w.Git.Baseline, w.Git.Baseline)));
            await db.SaveChangesAsync();
        }
        await AnswerAsync(w);
        await BlockAsync(w, Report(w, subject, w.Git.Baseline));
        w.Task.Status.ShouldBe(AgentTaskStatus.Blocked, "G104");
        w.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide, "G104");
        (await RowsAsync(w)).ShouldHaveSingleItem().Id.ShouldBe(old.Id);
        (await w.ObligationsAsync()).Count.ShouldBe(2);
    }

    [Test]
    public async Task C1043_AutomaticEligibility()
    {
        foreach (var variant in new[] { "role", "stage", "failed", "blocked", "orchestrator", "bound", "superseded" })
        {
            var (w, subject, old) = await IncidentAsync();
            await using var owned = w;
            await using (var db = w.CreateContext())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == w.TaskId);
                var predecessor = await db.StageOutcomes.SingleAsync(o => o.Id == old.Id);
                if (variant == "role") task.Role = AgentTaskRole.TestDesign;
                if (variant == "stage")
                {
                    task.Stage = OrchestrationStage.Verify;
                    predecessor.Stage = OrchestrationStage.Verify;
                }
                if (variant == "orchestrator") predecessor.Source = StageOutcomeSource.Orchestrator;
                if (variant == "bound") predecessor.ReviewedSourceSha = w.Git.Baseline;
                if (variant == "superseded") db.StageOutcomes.Add(new StageOutcome
                {
                    Id = Guid.NewGuid(), StageTaskId = w.TaskId, Stage = OrchestrationStage.Review,
                    SupersedesId = old.Id, Source = StageOutcomeSource.Orchestrator,
                    Outcome = StageOutcomeKind.Found, RecordedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            await AnswerAsync(w);
            var report = Report(w, subject, w.Git.Baseline);
            if (variant is "failed" or "blocked") report += DelegationReportFormatter.ReportToken(w.TaskId, variant);
            await w.SettleAsync(report);
            var rows = await RowsAsync(w);
            rows.Count.ShouldBe(variant == "superseded" ? 2 : 1, variant == "role" ? "G1"
                : variant == "stage" ? "G2" : variant is "failed" or "blocked" ? "G3"
                : variant == "orchestrator" ? "G4" : variant == "bound" ? "G5" : "G6");
            rows.Count(o => o.SupersedesId == old.Id).ShouldBe(variant == "superseded" ? 1 : 0, "no sibling successor");
        }
    }

    [Test]
    public async Task C1043_SettlementsSerialize()
    {
        var (w, subject, old) = await IncidentAsync();
        await using var owned = w;
        await AnswerAsync(w);
        var boundary = new HoldBoundary();
        w.UseRealSyncClock = true;
        w.ConfigureServices = s => s.AddSingleton<LandDeliveryBoundary>(boundary);
        await w.RestartServicesAsync();
        await using var second = w.AdditionalServices();
        var loaded = new CountdownEvent(2);
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var services in new[] { w.Services, second })
            services.GetRequiredService<AgentTaskReplyService>().DelayAfterOpenTaskLoadedAsync = async (_, _) =>
            {
                if (loaded.Signal()) both.TrySetResult();
                await both.Task.WaitAsync(Guard);
            };
        await w.SeedReportAsync(Report(w, subject, w.Git.Baseline));
        var firstRun = w.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(w.SessionId, CancellationToken.None);
        var secondRun = second.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(w.SessionId, CancellationToken.None);
        try
        {
            await boundary.Entered.Task.WaitAsync(Guard);
            await using var probe = new NpgsqlConnection(w.Schema.ConnectionString);
            await probe.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = @id FOR UPDATE NOWAIT", probe);
            command.Parameters.AddWithValue("id", w.TaskId);
            var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync());
            error.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable, "G36");
        }
        finally
        {
            boundary.Release.TrySetResult();
            await System.Threading.Tasks.Task.WhenAll(firstRun, secondRun).WaitAsync(Guard);
        }
        (await ReplacementAsync(w, old.Id)).SupersedesId.ShouldBe(old.Id);
        (await w.ObligationsAsync()).Count.ShouldBe(2, "G36 exactly one final completion");
        (await w.EventsAsync()).Count(e => e.Type == AgentTaskEventType.FindingRecorded).ShouldBe(1);
    }

    private sealed class HoldBoundary : LandDeliveryBoundary
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "settlement-before-save") return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(Guard, ct);
        }
    }

    [Test]
    public async Task C1043_PreparationBeforeLock()
    {
        var (w, subject, old) = await IncidentAsync();
        await using var owned = w;
        await AnswerAsync(w);
        var transactions = new TransactionWatch();
        var git = new ObservedGit(w.Git.Leases, transactions);
        w.Interceptors.Add(transactions);
        w.ConfigureServices = s => s.AddSingleton<ITaskProgressGit>(git);
        await w.RestartServicesAsync();
        await w.SettleAsync(Report(w, subject, w.Git.Baseline));
        (await ReplacementAsync(w, old.Id)).ReviewedSourceSha.ShouldBe(w.Git.Baseline);
        git.Observations.ShouldBeGreaterThan(0, "G37 real exact-ref observation executed");
        git.UnderTransaction.ShouldBeFalse("G37 external preparation precedes finalization transaction");
        transactions.Begun.ShouldBeGreaterThan(0);
    }

    private sealed class TransactionWatch : DbTransactionInterceptor
    {
        public int Active;
        public int Begun;
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken ct = default)
        { Interlocked.Increment(ref Active); Interlocked.Increment(ref Begun); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken ct = default) { Interlocked.Decrement(ref Active); return System.Threading.Tasks.Task.CompletedTask; }
    }

    private sealed class ObservedGit(IRepositoryMutationLease leases, TransactionWatch transactions) : TaskProgressGit(leases), ITaskProgressGit
    {
        public int Observations;
        public bool UnderTransaction;
        public new async Task<ProgressRemoteObservation> ObserveExactRefAsync(string repository, string fullRef,
            string? expectedFingerprint, Guid taskId, CancellationToken ct)
        {
            Observations++;
            UnderTransaction |= transactions.Active > 0;
            return await base.ObserveExactRefAsync(repository, fullRef, expectedFingerprint, taskId, ct);
        }
        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
        {
            UnderTransaction |= transactions.Active > 0;
            return await base.RunAsync(repository, args, ct);
        }
    }

    [Test]
    public async Task C1043_SettlementRollback()
    {
        foreach (var cut in new[] { "settlement-before-save", "settlement-before-commit" })
        {
            var (w, subject, old) = await IncidentAsync();
            await using var owned = w;
            await AnswerAsync(w);
            var beforeResult = w.Task.Result;
            w.ConfigureServices = s => s.AddSingleton<LandDeliveryBoundary>(new ThrowBoundary(cut));
            await w.RestartServicesAsync();
            await w.SettleAsync(Report(w, subject, w.Git.Baseline));
            w.Task.Status.ShouldBe(AgentTaskStatus.Working, "G40 " + cut);
            w.Task.Result.ShouldBe(beforeResult, "G40 " + cut);
            (await RowsAsync(w)).ShouldHaveSingleItem().Id.ShouldBe(old.Id, "G40 " + cut);
            (await w.ObligationsAsync()).ShouldHaveSingleItem("G40 " + cut);
            (await w.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.FindingRecorded, "G40 " + cut);
            w.ConfigureServices = null;
            await w.RestartServicesAsync();
            await w.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(w.SessionId, CancellationToken.None);
            await w.ReloadAsync();
            w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await ReplacementAsync(w, old.Id)).ReviewedSourceSha.ShouldBe(w.Git.Baseline);
        }
    }

    /// <summary>
    /// CARD-1082 V-27 / G-15. A Review blocked by desktop dirt, then continued under a held
    /// lease, settles Succeeded Pending. Checkout validation runs before the lease, so the
    /// continuation repairs the dirt first; the held lease is then the only sync failure.
    /// Strict repair refuses that Pending witness, so the unbound row stays and the handoff
    /// is the repair refusal.
    /// </summary>
    [Test]
    public async Task C1082_PendingContinuationRefusesStrictRebind()
    {
        var w = await RunnerSettlementWorld.CreateAsync(AgentTaskRole.Review, profiled: true,
            controlledSyncClock: true, mirrorPublish: true);
        await using var owned = w;
        await w.Git.EnsureRunnerAsync();
        var subject = await w.AddReviewSubjectAsync(w.Git.Baseline);
        var dirt = Path.Combine(w.Git.Worktree, "scratch.txt");
        await File.WriteAllTextAsync(dirt, "desktop dirt");
        var report = Report(w, subject, w.Git.Baseline);

        await w.SettleAsync(report);
        w.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
        w.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Refused);
        w.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.Dirty);
        var old = (await RowsAsync(w)).ShouldHaveSingleItem();
        old.Outcome.ShouldBe(StageOutcomeKind.Clean);
        old.ReviewedSourceSha.ShouldBeNull();

        File.Delete(dirt);
        await AnswerAsync(w);
        await using (var held = await w.Git.Leases.TryAcquireAsync(w.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            var busy = w.LeaseBusy.Next();
            var settling = w.SettleAsync(report);
            try
            {
                (await System.Threading.Tasks.Task.WhenAny(busy, settling).WaitAsync(Guard)).ShouldBe(busy);
                w.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
                await settling.WaitAsync(Guard);
            }
            finally { await settling.WaitAsync(Guard); }
        }

        w.Task.Status.ShouldBe(AgentTaskStatus.Succeeded);
        w.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        w.Task.NextHandoff.ShouldNotBeNull().ShouldStartWith(
            "Review evidence repair refused: review_evidence_sync_unconfirmed");
        var sync = w.Evidence()!.RemoteSync!;
        sync.State.ShouldBe(RemoteSettlementSyncState.Pending);
        sync.ObservedSha.ShouldBe(w.Git.Baseline);
        sync.ConfirmedSha.ShouldBeNull();
        (await RowsAsync(w)).ShouldHaveSingleItem().Id.ShouldBe(old.Id);
        File.Exists(dirt).ShouldBeFalse();
        (await w.Git.HeadAsync()).ShouldBe(w.Git.Baseline);
    }

    private sealed class ThrowBoundary(string cut) : LandDeliveryBoundary
    {
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            boundary == cut ? System.Threading.Tasks.Task.FromException(new IOException("C1043 injected precommit failure"))
                : System.Threading.Tasks.Task.CompletedTask;
    }
}
