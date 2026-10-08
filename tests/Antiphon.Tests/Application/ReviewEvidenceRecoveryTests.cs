using System.Data.Common;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-1137: compare EF entities with EntityScalarSnapshot.Of(db, ...), not default-options
// JsonSerializer: an entity graph serialize holds STJ's process-wide metadata lock for seconds and
// stalls the in-process runner (rule and reason: TerminalRunnerSeatReleaseTests header).
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReviewEvidenceRecoveryTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);
    private static async Task RefusedAsync(ReviewRecoveryWorld w, string label, ReviewEvidenceRecoveryRequest? request = null)
    {
        await Should.ThrowAsync<ConflictException>(() => w.RecoverAsync(request: request));
        await w.UnchangedAsync(label);
    }
    private sealed class Hold(string name) : LandDeliveryBoundary
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public override async Task ReachedAsync(string boundary, Guid task, Guid identity, CancellationToken ct)
        {
            if (boundary != name || Interlocked.Increment(ref _calls) != 1) return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(Guard, ct);
        }
    }
    private sealed class ObserveLocks : DbCommandInterceptor
    {
        public List<Guid> Ids { get; } = [];
        public TaskCompletionSource Attempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FOR UPDATE"))
            { lock (Ids) Ids.Add((Guid)command.Parameters[0].Value!); Attempt.TrySetResult(); }
            return ValueTask.FromResult(result);
        }
    }
    private static async Task ProbeAsync(ReviewRecoveryWorld w, Guid id, string label)
    {
        await using var connection = new NpgsqlConnection(w.World.Schema.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = @id FOR UPDATE NOWAIT", connection);
        command.Parameters.AddWithValue("id", id);
        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync());
        error.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable, label);
    }

    [Test]
    public async Task C1043_PreparedReportChanged()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var hold = new Hold("review-recovery-prepared");
        var run = w.RecoverAsync(hold);
        try { await hold.Entered.Task.WaitAsync(Guard); await w.ChangeAsync(t => t.Result += "changed"); }
        finally { hold.Release.TrySetResult(); }
        (await Should.ThrowAsync<ConflictException>(() => run)).Code.ShouldBe("review_evidence_rebind_prepared_report_changed", "G38");
        await w.UnchangedAsync("G38");
    }
    [Test]
    public async Task C1043_PreparedCoordinatesChanged()
    {
        foreach (var field in new[] { "subject", "ref", "repository", "baseline" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            var hold = new Hold("review-recovery-prepared");
            var run = w.RecoverAsync(hold);
            try
            {
                await hold.Entered.Task.WaitAsync(Guard);
                if (field == "subject") await w.ChangeAsync(t => t.FollowUpOfTaskId = Guid.NewGuid());
                else await w.ChangeAsync(t => { if (field == "ref") t.WorktreeBranch += "moved";
                    else if (field == "repository") t.RepoPath += "other"; else t.ProgressBaselineJson = "{}"; }, w.SubjectId);
            }
            finally { hold.Release.TrySetResult(); }
            await Should.ThrowAsync<ConflictException>(() => run);
            await w.UnchangedAsync("G39/G105/G106 " + field);
        }
    }
    [Test]
    public async Task C1043_RepeatAfterRestart()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var first = await w.RecoverAsync();
        await w.World.RestartServicesAsync();
        var second = await w.RecoverAsync();
        first.Disposition.ShouldBe("bound"); second.Disposition.ShouldBe("already-bound", "G41");
        second.ReviewEvidenceId.ShouldBe(first.ReviewEvidenceId, "G41");
        (await w.RowsAsync()).Count.ShouldBe(2); (await w.AuditsAsync()).ShouldHaveSingleItem();
    }
    [Test]
    public async Task C1043_ConcurrentRecovery()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var hold = new Hold("review-recovery-locked");
        var first = w.RecoverAsync(hold);
        Task<ReviewEvidenceRecoveryResponse>? second = null;
        var locks = new ObserveLocks();
        try
        {
            await hold.Entered.Task.WaitAsync(Guard);
            await ProbeAsync(w, w.ReviewId, "G42");
            second = w.RecoverAsync(interceptors: [locks]);
            await locks.Attempt.Task.WaitAsync(Guard);
            await ProbeAsync(w, w.ReviewId, "G42 competing writer cannot own lock");
            second.IsCompleted.ShouldBeFalse("G42");
        }
        finally { hold.Release.TrySetResult(); await first.WaitAsync(Guard); if (second is not null) await second.WaitAsync(Guard); }
        (await second!).ReviewEvidenceId.ShouldBe((await first).ReviewEvidenceId, "G42");
        (await w.RowsAsync()).Count.ShouldBe(2); (await w.AuditsAsync()).ShouldHaveSingleItem();
        // An automatic successor is a different authority, never idempotent recovery.
        await using var other = await ReviewRecoveryWorld.CreateAsync();
        var prepared = new Hold("review-recovery-prepared"); var stale = other.RecoverAsync(prepared);
        try
        {
            await prepared.Entered.Task.WaitAsync(Guard);
            await other.World.Git.EnsureRunnerAsync();
            await other.ChangeAsync(t => t.Status = AgentTaskStatus.Working);
            await other.World.ReloadAsync();
            await other.World.SettleAsync(other.Report).WaitAsync(Guard);
            other.World.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, "G42 actual automatic settlement");
        }
        finally { prepared.Release.TrySetResult(); }
        await Should.ThrowAsync<ConflictException>(() => stale);
        (await other.RowsAsync()).Count.ShouldBe(2, "G42 automatic/recovery competing authority");
        (await other.RowsAsync()).Single(o => o.Id != other.OldId).Source.ShouldBe(StageOutcomeSource.Delegate);
    }
    private static async Task OverrideWinsAsync(bool subject)
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var hold = new Hold("review-recovery-prepared"); var recovery = w.RecoverAsync(hold);
        try { await hold.Entered.Task.WaitAsync(Guard); await w.FindingAsync(subject ? w.SubjectId : w.ReviewId); }
        finally { hold.Release.TrySetResult(); }
        await Should.ThrowAsync<ConflictException>(() => recovery);
        var rows = await w.RowsAsync(); rows.Count.ShouldBe(2, "G43/G44");
        StageOutcomeService.ActiveLeaves(rows).ShouldHaveSingleItem().Outcome.ShouldBe(StageOutcomeKind.Found);
        // Opposite winner order: recovery commits before the finding. The finding must extend its leaf.
        await using var other = await ReviewRecoveryWorld.CreateAsync();
        var rebound = await other.RecoverAsync();
        var findingHold = new Hold("review-finding-locked");
        var finding = other.FindingAsync(subject ? other.SubjectId : other.ReviewId, findingHold);
        try { await findingHold.Entered.Task.WaitAsync(Guard); await ProbeAsync(other, other.ReviewId, "G44"); }
        finally { findingHold.Release.TrySetResult(); await finding.WaitAsync(Guard); }
        var active = StageOutcomeService.ActiveLeaves(await other.RowsAsync()).ShouldHaveSingleItem();
        active.SupersedesId.ShouldBe(rebound.ReviewEvidenceId, "G43/G44"); active.Outcome.ShouldBe(StageOutcomeKind.Found);
    }
    [Test] public Task C1043_OverrideReviewWins() => OverrideWinsAsync(false);
    [Test] public Task C1043_OverrideSubjectWins() => OverrideWinsAsync(true);
    [Test]
    public async Task C1043_LockOrder()
    {
        foreach (var lower in new[] { true, false })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            await using (var db = w.Db())
            {
                var id = lower ? Guid.Parse("00000000-0000-0000-0000-000000000001") : Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
                db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Title = "lock target", Goal = "lock", WorkingDirectory = w.World.Git.Desktop });
                (await db.StageOutcomes.SingleAsync(o => o.Id == w.OldId)).SubjectTaskId = id;
                await db.SaveChangesAsync();
                var locks = new ObserveLocks();
                await w.FindingAsync(id, interceptors: [locks]).WaitAsync(Guard);
                locks.Ids.ShouldBe(new[] { w.ReviewId, id }.Order().ToList(), "G45 stable GUID order");
            }
        }
    }
    [Test]
    public async Task C1043_PredecessorIdentityChanged()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var hold = new Hold("review-finding-prepared"); var locks = new ObserveLocks();
        var run = w.FindingAsync(w.SubjectId, hold, locks);
        var changed = Guid.NewGuid();
        try
        {
            await hold.Entered.Task.WaitAsync(Guard);
            await using var db = w.Db();
            db.AgentTasks.Add(new AgentTask { Id = changed, RootTaskId = changed, Title = "new Review", Goal = "review", WorkingDirectory = w.World.Git.Desktop });
            db.StageOutcomes.Add(new StageOutcome { Id = Guid.NewGuid(), StageTaskId = changed, SubjectTaskId = w.SubjectId,
                Stage = OrchestrationStage.Review, Source = StageOutcomeSource.Delegate, Outcome = StageOutcomeKind.Clean,
                SupersedesId = w.OldId, RecordedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        finally { hold.Release.TrySetResult(); await run.WaitAsync(Guard); }
        locks.Ids.ShouldContain(changed, "G46 re-prepared under new predecessor identity");
        await using var verify = w.Db();
        var all = await verify.StageOutcomes.ToListAsync();
        StageOutcomeService.ActiveLeaves(all).ShouldHaveSingleItem().Outcome.ShouldBe(StageOutcomeKind.Found, "G46");
    }
    private sealed class ThrowAt(string cut) : LandDeliveryBoundary
    {
        public override Task ReachedAsync(string name, Guid task, Guid identity, CancellationToken ct) =>
            name == cut ? Task.FromException(new IOException("injected recovery write failure")) : Task.CompletedTask;
    }
    [Test]
    public async Task C1043_RecoveryRollback()
    {
        foreach (var cut in new[] { "review-recovery-before-audit", "review-recovery-before-save", "review-recovery-before-commit" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            await Should.ThrowAsync<IOException>(() => w.RecoverAsync(new ThrowAt(cut)));
            await w.UnchangedAsync("G47 " + cut);
            (await w.RecoverAsync()).Disposition.ShouldBe("bound");
        }
    }
    [Test]
    public async Task C1043_NamedPredecessor()
    {
        foreach (var field in new[] { "id", "foreign", "stage", "source", "superseded" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            if (field == "id") { await RefusedAsync(w, "G48", w.Request with { EvidenceId = Guid.NewGuid() }); continue; }
            await using (var db = w.Db())
            {
                var old = await db.StageOutcomes.SingleAsync(o => o.Id == w.OldId);
                if (field == "foreign") old.StageTaskId = w.SubjectId;
                if (field == "stage") old.Stage = OrchestrationStage.Verify;
                if (field == "source") old.Source = StageOutcomeSource.Orchestrator;
                if (field == "superseded") db.StageOutcomes.Add(new StageOutcome { Id = Guid.NewGuid(), StageTaskId = w.ReviewId,
                    Stage = OrchestrationStage.Review, Source = StageOutcomeSource.Orchestrator, Outcome = StageOutcomeKind.Found,
                    RecordedAt = DateTime.UtcNow, SupersedesId = w.OldId });
                await db.SaveChangesAsync();
            }
            var before = JsonSerializer.Serialize(await w.RowsAsync());
            await Should.ThrowAsync<ConflictException>(() => w.RecoverAsync());
            JsonSerializer.Serialize(await w.RowsAsync()).ShouldBe(before, "G48/G107/G108/G109 " + field);
            (await w.AuditsAsync()).ShouldBeEmpty();
        }
    }
    [Test]
    public async Task C1043_BoundInterimIsNotRecovery()
    {
        foreach (var reportScope in new[] { "Interim", "Full" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            await w.ReportAsync(ReviewRecoveryWorld.Body(w.ReviewId, w.SubjectId, w.World.Git.Baseline, scope: reportScope));
            await using (var db = w.Db())
            {
                var old = await db.StageOutcomes.SingleAsync(o => o.Id == w.OldId);
                old.ReviewedSourceSha = w.World.Git.Baseline; old.OrdinaryScopeCompleted = VerificationScope.Interim;
                await db.SaveChangesAsync();
            }
            var before = JsonSerializer.Serialize(await w.RowsAsync());
            await RefusedAsync(w, "G49"); JsonSerializer.Serialize(await w.RowsAsync()).ShouldBe(before, "G49 bound Interim untouched");
        }
    }
    [Test]
    public async Task C1043_ExactStoredDigest()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var stale = w.Request;
        await w.ReportAsync(w.Report.Replace("\n", "\r\n"));
        await RefusedAsync(w, "G50 newline bytes", stale);
        var newline = w.Request;
        await w.ReportAsync(w.Report.Replace("é", "è"));
        await RefusedAsync(w, "G50 non-ASCII bytes", newline);
        (await w.RecoverAsync()).ReportSha256.ShouldBe(w.Request.ExpectedReportSha256, "G50 exact UTF-8");
    }
    [Test]
    public async Task C1043_RecoveryPrerequisites()
    {
        foreach (var invalid in new[] { "result", "profile-null", "profile-other", "blocked", "failed", "working", "interim", "round-null", "role", "stage", "workspace" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            if (invalid == "result") await w.SnapshotAsync();
            await w.ChangeAsync(t =>
            {
                switch (invalid)
                {
                    case "result": t.Result = null; break;
                    case "profile-null": t.VerificationProfileVersion = null; break;
                    case "profile-other": t.VerificationProfileVersion = 2; break;
                    case "blocked": t.Status = AgentTaskStatus.Blocked; break;
                    case "failed": t.Status = AgentTaskStatus.Failed; break;
                    case "working": t.Status = AgentTaskStatus.Working; break;
                    case "interim": t.VerificationRound = VerificationRound.Interim; break;
                    case "round-null": t.VerificationRound = null; break;
                    case "role": t.Role = AgentTaskRole.Code; break;
                    case "stage": t.Stage = OrchestrationStage.Verify; break;
                    case "workspace": t.Workspace = WorkspaceMode.Shared; break;
                }
            });
            await RefusedAsync(w, "G51/G52/G53/G54 " + invalid);
        }
    }
    [Test]
    public async Task C1043_RecoveryReportAuthority()
    {
        foreach (var invalid in new[] { "Found", "missing-finding", "Interim", "None", "Unknown", "false", "missing-clean", "wrong-sha", "wrong-subject" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            var report = ReviewRecoveryWorld.Body(w.ReviewId, w.SubjectId, w.World.Git.Baseline,
                invalid == "Found" ? "Found" : "Clean", invalid is "Interim" or "None" or "Unknown" ? invalid : "Full",
                invalid == "false" ? "false" : "true");
            if (invalid == "missing-finding") report = report.Replace("[antiphon-finding:", "[absent:");
            if (invalid == "missing-clean") report = report.Replace("reviewedSourceClean: true\n", "");
            if (invalid == "wrong-sha") report = report.Replace(w.World.Git.Baseline, new string('a', 40));
            if (invalid == "wrong-subject") report = report.Replace(w.SubjectId.ToString("D"), w.ReviewId.ToString("D"));
            await w.ReportAsync(report); await RefusedAsync(w, "G55/G56 " + invalid);
        }
        await using var positive = await ReviewRecoveryWorld.CreateAsync();
        await positive.ReportAsync("[antiphon-report:01234567 done]\n" + positive.Report);
        (await positive.RecoverAsync()).SubjectTaskId.ShouldBe(positive.SubjectId, "stored body is not truncated at embedded token");
    }
    [Test]
    public async Task C1043_StoredSyncRequired()
    {
        foreach (var invalid in new[] { "missing", "unconfirmed", "dirty", "wrong-ref", "wrong-sha", "unavailable", "pending" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            await w.ChangeAsync(t =>
            {
                var evidence = TaskProgressJson.TryReadEvidence(t.CompletionProgressEvidenceJson)!;
                var sync = evidence.RemoteSync!;
                t.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(evidence with { RemoteSync = invalid switch
                {
                    "missing" => null, "unconfirmed" => sync with { ConfirmedSha = null },
                    "dirty" => sync with { MirrorDirty = true }, "wrong-ref" => sync with { FullRef = "refs/heads/other" },
                    "wrong-sha" => sync with { ConfirmedSha = new string('a', 40) },
                    // ConfirmedSha stays. State is the only predicate this arm is allowed to fail.
                    "pending" => sync with { State = RemoteSettlementSyncState.Pending },
                    _ => sync with { State = RemoteSettlementSyncState.Unavailable },
                } });
            });
            if (invalid == "pending")
            {
                (await Should.ThrowAsync<ConflictException>(() => w.RecoverAsync()))
                    .Code.ShouldBe("review_evidence_rebind_stored_sync_unconfirmed", "G57 pending");
                await w.UnchangedAsync("G57 pending");
            }
            else
                await RefusedAsync(w, "G57 " + invalid);
        }
        await using var ok = await ReviewRecoveryWorld.CreateAsync();
        await ok.ChangeAsync(t => { var evidence = TaskProgressJson.TryReadEvidence(t.CompletionProgressEvidenceJson)!;
            t.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(evidence with { RemoteSync = evidence.RemoteSync! with { State = RemoteSettlementSyncState.Synchronized } }); });
        (await ok.RecoverAsync()).Disposition.ShouldBe("bound", "Synchronized positive");
    }
    [Test]
    public async Task C1043_ReviewRefFreshness()
    {
        foreach (var invalid in new[] { "moved", "missing", "endpoint" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            if (invalid == "missing") await w.World.Git.RunAsync(w.World.Git.Desktop, "push", "origin", "--delete", w.World.Git.Branch);
            if (invalid == "moved")
            {
                await w.World.Git.RunAsync(w.World.Git.Desktop, "commit", "--allow-empty", "-m", "moved Review ref");
                await w.World.Git.RunAsync(w.World.Git.Desktop, "push", "origin", "HEAD:refs/heads/" + w.World.Git.Branch);
            }
            if (invalid == "endpoint") await w.ChangeAsync(t => { var baseline = TaskProgressJson.TryReadBaseline(t.ProgressBaselineJson)!;
                t.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline with { Primary = baseline.Primary with { Remote = baseline.Primary.Remote with { EndpointFingerprint = new string('f', 64) } } }); });
            await RefusedAsync(w, "G58 " + invalid);
        }
    }
    private sealed class TransactionWatch : DbTransactionInterceptor
    {
        public int Active;
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData data, DbTransaction result, CancellationToken ct = default)
        { Active++; return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
            CancellationToken ct = default) { Active--; return Task.CompletedTask; }
    }
    private sealed class GitWatch(IRepositoryMutationLease leases, TransactionWatch transactions) : TaskProgressGit(leases), ITaskProgressGit
    {
        public List<string> Commands { get; } = [];
        public bool UnderTransaction;
        public override Task<LandingGitResult> RunAsync(string repo, IReadOnlyList<string> args, CancellationToken ct)
        { UnderTransaction |= transactions.Active > 0;
            Commands.Add(string.Join(" ", args)); return base.RunAsync(repo, args, ct); }
    }
    [Test]
    public async Task C1043_RecoveryIsReadOnlyToGit()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var transactions = new TransactionWatch();
        var watch = new GitWatch(w.World.Git.Leases, transactions);
        var before = await w.World.Git.HeadAsync();
        (await w.RecoverAsync(git: watch, interceptors: [transactions])).Disposition.ShouldBe("bound");
        watch.UnderTransaction.ShouldBeFalse("G59 Git preparation precedes the database lock");
        watch.Commands.Count.ShouldBeGreaterThan(0, "G59 real exact observations");
        watch.Commands.ShouldNotContain(c => new[] { "push", "merge", "reset", "checkout" }.Any(v => c.StartsWith(v + " ")), "G59");
        (await w.World.Git.HeadAsync()).ShouldBe(before, "G59 checkout unchanged");
    }
    [Test]
    public async Task C1043_ProvenanceEvent()
    {
        foreach (var variant in new[] { "valid", "snapshot", "merge-later", "ambiguous", "missing", "merge-only" })
        {
            await using var w = await ReviewRecoveryWorld.CreateAsync();
            if (variant == "snapshot") await w.SnapshotAsync();
            await using (var db = w.Db())
            {
                var source = await db.AgentTaskEvents.SingleAsync(e => e.Id == w.EventId);
                if (variant == "missing") db.Remove(source);
                if (variant == "merge-only") source.Detail = "Merged branch back";
                if (variant is "merge-later" or "ambiguous" or "snapshot") db.AgentTaskEvents.Add(new AgentTaskEvent {
                    Id = Guid.NewGuid(), AgentTaskId = w.ReviewId, Type = AgentTaskEventType.Completed,
                    At = variant is "ambiguous" or "snapshot" ? source.At : source.At.AddSeconds(1),
                    Detail = variant is "ambiguous" or "snapshot" ? source.Detail : "Merged branch back" });
                await db.SaveChangesAsync();
            }
            if (variant is "missing" or "ambiguous" or "merge-only") await RefusedAsync(w, "G60 " + variant);
            else { await w.RecoverAsync(); ReviewRebindProvenance.TryParse((await w.RowsAsync()).Single(o => o.Id != w.OldId).Ref)!.SourceEventId.ShouldBe(w.EventId, "G60"); }
        }
    }
    [Test]
    public async Task C1043_AuditProvenance()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var before = JsonSerializer.Serialize((await w.RowsAsync()).Single());
        var receipt = await w.RecoverAsync();
        var rows = await w.RowsAsync(); var row = rows.Single(o => o.Id == receipt.ReviewEvidenceId);
        JsonSerializer.Serialize(rows.Single(o => o.Id == w.OldId)).ShouldBe(before, "old row immutable");
        var provenance = ReviewRebindProvenance.TryParse(row.Ref).ShouldNotBeNull();
        provenance.Actor.ShouldBe(w.Actor, "G62"); provenance.ReportSha256.ShouldBe(w.Request.ExpectedReportSha256);
        provenance.SourceEventId.ShouldBe(w.EventId); provenance.ObservedSubjectSha.ShouldBe(w.World.Git.Baseline);
        provenance.ConfirmedReviewSha.ShouldBe(w.World.Git.Baseline); provenance.Mode.ShouldBe("recovery");
        var audit = JsonDocument.Parse((await w.AuditsAsync()).ShouldHaveSingleItem().Detail).RootElement;
        audit.GetProperty("actor").GetString().ShouldBe(w.Actor, "G62");
        audit.GetProperty("previousEvidenceId").GetGuid().ShouldBe(w.OldId);
        audit.GetProperty("reviewEvidenceId").GetGuid().ShouldBe(row.Id);
        audit.GetProperty("reason").GetString().ShouldBe(w.Request.Reason);
        audit.GetProperty("reportSha256").GetString().ShouldBe(provenance.ReportSha256);
        row.SubjectTaskId.ShouldBe(w.SubjectId); row.ReviewedSourceSha.ShouldBe(w.World.Git.Baseline);
        row.Detail.ShouldBe("checked", "successor finding detail comes from the stored final report");
        row.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full);
    }
    [Test]
    public async Task C1043_RecoveryPreservesHistory()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        await w.SnapshotAsync();
        await using var db = w.Db();
        var beforeTask = EntityScalarSnapshot.Of(db, await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.ReviewId));
        var sessions = EntityScalarSnapshot.Of(db, await db.AgentSessions.AsNoTracking().OrderBy(s => s.Id).ToListAsync());
        var notifications = EntityScalarSnapshot.Of(db, await db.AgentTaskLandNotifications.AsNoTracking().ToListAsync());
        await w.RecoverAsync();
        EntityScalarSnapshot.Of(db, await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.ReviewId)).ShouldBe(beforeTask, "G63 Result/status/cost/handoff/receipt");
        EntityScalarSnapshot.Of(db, await db.AgentSessions.AsNoTracking().OrderBy(s => s.Id).ToListAsync()).ShouldBe(sessions, "G63");
        EntityScalarSnapshot.Of(db, await db.AgentTaskLandNotifications.AsNoTracking().ToListAsync()).ShouldBe(notifications, "G63 no new completion");
    }
    [Test]
    public async Task C1043_ManualFindingCannotCopyFull()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync();
        var rebound = await w.RecoverAsync();
        await using var db = w.Db();
        var manual = await new StageOutcomeService(db).RecordFindingAsync(w.ReviewId,
            new("Review", false, "explicit manual", ReviewedSourceSha: w.World.Git.Baseline, ReviewedSourceClean: true), CancellationToken.None);
        manual.SupersedesId.ShouldBe(rebound.ReviewEvidenceId);
        manual.VerificationProfileVersion.ShouldBeNull("G103"); manual.CommissionedRound.ShouldBeNull("G103");
        manual.OrdinaryScopeCompleted.ShouldBeNull("G103");
    }
}
