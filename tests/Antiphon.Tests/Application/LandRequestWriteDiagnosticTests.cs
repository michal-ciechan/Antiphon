using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandRequestWriteDiagnosticTests
{
    [Test]
    [Arguments("monitor")]
    [Arguments("hold")]
    [Arguments("source")]
    [Arguments("protocol")]
    [Arguments("admission")]
    public async Task C883_WriterStampComesFromCommittedToken(string variant)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var beforeWrites = h.Clock.GetUtcNow().UtcDateTime;
        var writes = new WriterTransactionCut(h);
        h.TransactionInterceptor = writes;
        var accepted = await h.RequestAsync();
        await using (var db = h.CreateContext())
        {
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
            row.LastWriterOperation.ShouldBe("admission", customMessage: "D.AdmissionStamped");
            row.LastWriterToken.ShouldBe(row.ConcurrencyToken, customMessage: "D.AdmissionTokenLinked");
            row.LastWriterAt.ShouldNotBeNull("D.AdmissionTimeStamped");
        }
        await using (var db = h.CreateContext())
        {
            var monitor = new AgentTaskLandMonitorService(db, h.Clock,
                Options.Create(new DelegationSettings()), h.Events);
            await monitor.SweepAsync(CancellationToken.None);
        }
        await using (var db = h.CreateContext())
        {
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
            row.LastWriterOperation.ShouldBe("monitor-sweep", customMessage: "D.MonitorStamped");
            row.LastWriterToken.ShouldBe(row.ConcurrencyToken, customMessage: "D.MonitorTokenLinked");
        }
        if (variant == "hold")
        {
            await using var lease = (await h.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>()
                .TryAcquireAsync(h.Fixture.Repository, new Antiphon.Server.Application.Interfaces.RepositoryLeaseOwnerTag(h.Fixture.TaskId, "fixture-writer"), CancellationToken.None)).ShouldNotBeNull();
            (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Held);
        }
        else if (variant is "source" or "protocol") await h.RunQueuedAsync();
        var label = variant switch { "monitor" => "monitor-sweep", "source" => "source-checkpoint", "protocol" => "protocol-progress", _ => variant };
        var receipts = writes.Commits.Where(r => r.LastWriterOperation == label).ToArray();
        receipts.ShouldNotBeEmpty($"D.{variant}.RealWriterCommitted");
        foreach (var row in writes.Commits)
        {
            row.LastWriterToken.ShouldBe(row.ConcurrencyToken, $"D.{row.LastWriterOperation}.CommitTokenLinked");
            row.LastWriterAt.ShouldNotBeNull();
            row.LastWriterAt.Value.ShouldBeGreaterThanOrEqualTo(beforeWrites);
            row.LastWriterAt.Value.ShouldBeLessThanOrEqualTo(h.Clock.GetUtcNow().UtcDateTime);
            row.LastWriterOperation.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Test]
    [Arguments("request-direct")]
    [Arguments("request-hosted")]
    [Arguments("other-entity")]
    public async Task C883_ConflictNamesActualEntityAndTokens(string variant)
    {
        await using var h = new LandingSafetyHarness();
        var entries = new List<RecordingLogEntry>();
        h.Logger = new RecordingLogger<AgentTaskLandService>(entries);
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var accepted = await h.RequestAsync();
        await using var stale = h.CreateContext();
        Guid key, original, attempted;
        if (variant == "other-entity")
        {
            var owner = await stale.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            key = owner.Id; original = owner.ConcurrencyToken;
            await using var winner = h.CreateContext();
            var other = await winner.AgentTasks.SingleAsync(t => t.Id == key);
            other.Title = "committed winner"; other.ConcurrencyToken = Guid.NewGuid();
            await winner.SaveChangesAsync();
            owner.Title = "stale private value"; owner.ConcurrencyToken = attempted = Guid.NewGuid();
        }
        else
        {
            var request = await stale.AgentTaskLandRequests.SingleAsync(r => r.Id == accepted.RequestId);
            key = request.Id; original = request.ConcurrencyToken;
            await using var winner = h.CreateContext();
            await new AgentTaskLandMonitorService(winner, h.Clock, Options.Create(new DelegationSettings()), h.Events).SweepAsync(CancellationToken.None);
            request.HoldDetail = "stale private value"; request.ConcurrencyToken = attempted = Guid.NewGuid();
        }
        var failure = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        failure.Entries.Count.ShouldBe(1, customMessage: "D.RealConflictingEntry");
        Guid observed;
        await using (var observer = h.CreateContext()) observed = (await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId)).ConcurrencyToken;
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] != "check-ref-format") return null;
            await using var atFailure = h.CreateContext();
            observed = (await atFailure.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId)).ConcurrencyToken;
            throw failure;
        };
        if (variant == "request-hosted")
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var hosted = new Antiphon.Server.Infrastructure.Orchestration.AgentTaskLandHostedService(h.Queue,
                h.Services.GetRequiredService<IServiceScopeFactory>(), new CompletionLogger<Antiphon.Server.Infrastructure.Orchestration.AgentTaskLandHostedService>(entries, completion));
            await hosted.StartAsync(CancellationToken.None);
            try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { await hosted.StopAsync(CancellationToken.None); }
        }
        else await h.FailQueuedInSameScopeAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
        row.TerminalFailureCode.ShouldBe("landing_concurrency_conflict", customMessage: "D.ConcurrencyCodePersisted");
        var terminal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == row.TerminalEventId);
        var entity = variant == "other-entity" ? "AgentTask" : "AgentTaskLandRequest";
        terminal.Detail.ShouldContain("entity=" + entity, Case.Sensitive, customMessage: "D.EntryEntityNamed");
        terminal.Detail.ShouldContain($"row={key:N}", Case.Sensitive, customMessage: "D.EntryRowNamed");
        terminal.Detail.ShouldContain($"originalToken={original:N}", Case.Sensitive, customMessage: "D.OriginalTokenNamed");
        terminal.Detail.ShouldContain($"attemptedToken={attempted:N}", Case.Sensitive, customMessage: "D.AttemptedTokenNamed");
        terminal.Detail.ShouldContain("observedDatabaseWriter=", Case.Sensitive, customMessage: "D.ObservedWriterNamed");
        // This is the request token observed at the actual failure, after any intervening start writer.
        terminal.Detail.ShouldContain($"observedToken={observed:N}", customMessage: "D.FreshStoredTokenNamed");
        terminal.Detail.ShouldNotContain("stale private value", Case.Sensitive, customMessage: "D.RawExceptionHidden");
        terminal.Detail.ShouldNotContain("fixture-request-save-conflict", Case.Sensitive, customMessage: "D.RawExceptionHidden");
        original.ShouldNotBe(attempted);
        var log = entries.Single(e => e.State.ContainsKey("ConcurrencySummary"));
        log.State["ConcurrencySummary"]!.ToString().ShouldContain($"row={key:N}", customMessage: "D.SummarySurvivesTrackerClear");
        log.State["Attempt"].ShouldBe(row.Attempt, "D.ExecutingAttemptRetained");
        var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.RequestId == accepted.RequestId && n.Kind == LandNotificationKind.Outcome);
        note.Body.ShouldContain($"originalToken={original:N}", customMessage: "D.SafeSummaryPersistedInNotification");
    }

    [Test]
    public async Task C883_MigrationPreservesLegacyRows()
    {
        var name = "test_c883_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", maintenance);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString) { Database = name }.ConnectionString;
        await using var owned = new IsolatedTestSchema(name, connection);
        var options = TestDbFixture.CreateDbContextOptions(connection);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var position = Array.FindIndex(migrations,
            m => m.EndsWith("_AddLandRequestWriterProvenance", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0, customMessage: "D.MigrationExists");
        migrations[position - 1].EndsWith("_AddReviewSourceClean", StringComparison.Ordinal)
            .ShouldBeTrue("D.ActualImmediatePredecessor");
        await db.GetService<IMigrator>().MigrateAsync(migrations[position - 1]);
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "legacy", Goal = "legacy",
            Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Path.GetTempPath(), RepoPath = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, CompletedAt = now,
        });
        await db.SaveChangesAsync();
        var requestId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var expected = new string('a', 40);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTaskLandRequests" ("Id", "TaskId", "RequestedAt", "ReplyTo", "State", "IsPending",
                "Attempt", "LastEvaluatedAt", "LastProgressAt", "HighestProgress", "HoldEpisode", "ConcurrencyToken",
                "SchemaVersion", "ExpectedSourceSha", "ApprovalKind", "ApprovedAt", "RecoveryLocalBeforeSha")
            VALUES ({requestId}, {taskId}, {now}, 0, 0, FALSE, 1, {now}, {now}, -2, 0, {token},
                2, {expected}, {(int)LandApprovalKind.ExplicitCaller}, {now}, {expected})
            """);
        await db.GetService<IMigrator>().MigrateAsync(migrations[position]);
        await using var observer = new AppDbContext(options);
        var saved = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        saved.ConcurrencyToken.ShouldBe(token, customMessage: "D.LegacyTokenUnchanged");
        saved.ExpectedSourceSha.ShouldBe(expected, customMessage: "D.LegacyApprovalUnchanged");
        saved.RecoveryLocalBeforeSha.ShouldBe(expected, customMessage: "D.LegacyProgressUnchanged");
        saved.LastWriterOperation.ShouldBeNull("D.LegacyWriterNull");
        saved.LastWriterToken.ShouldBeNull("D.LegacyWriterTokenNull");
        saved.LastWriterAt.ShouldBeNull("D.LegacyWriterAtNull");
        saved.RecoveryWitnessRequestId.ShouldBeNull("D.LegacyWitnessNull");
        var witness = Guid.NewGuid();
        saved.RecoveryWitnessRequestId = witness;
        saved.ConcurrencyToken = Guid.NewGuid();
        saved.LastWriterToken = saved.ConcurrencyToken;
        saved.LastWriterOperation = "source-checkpoint";
        saved.LastWriterAt = now;
        await observer.SaveChangesAsync();
        observer.ChangeTracker.Clear();
        saved = await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
        saved.RecoveryWitnessRequestId.ShouldBe(witness, customMessage: "D.NewWitnessRoundTrips");
        saved.LastWriterToken.ShouldBe(saved.ConcurrencyToken, customMessage: "D.NewWriterTokenRoundTrips");
    }
    [Test]
    [Arguments("legacy")]
    [Arguments("token-mismatch")]
    [Arguments("deleted-row")]
    [Arguments("read-unavailable")]
    public async Task C883_MissingOrStaleWriterIsUnknown(string variant)
    {
        await using var h = new LandingSafetyHarness();
        var entries = new List<RecordingLogEntry>();
        h.Logger = new RecordingLogger<AgentTaskLandService>(entries);
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var request = await h.RequestAsync();
        await using (var db = h.CreateContext())
        {
            var row = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == request.RequestId);
            if (variant == "legacy") { row.LastWriterOperation = null; row.LastWriterToken = null; row.LastWriterAt = null; }
            if (variant == "token-mismatch") { row.LastWriterOperation = "monitor-sweep"; row.LastWriterToken = Guid.NewGuid(); }
            if (variant == "deleted-row") db.AgentTaskLandRequests.Remove(row);
            await db.SaveChangesAsync();
        }
        var fault = new DiagnosticReadFault();
        if (variant == "read-unavailable") { h.CommandInterceptor = fault; fault.Armed = true; }
        await h.FailAsync(new DbUpdateConcurrencyException("synthetic private value"));
        var log = entries.Single(e => e.State.ContainsKey("ConcurrencySummary"));
        var summary = log.State["ConcurrencySummary"]!.ToString()!;
        summary.ShouldContain("entity=unknown", customMessage: "D.MissingEntryListSupported");
        summary.ShouldContain("observedDatabaseWriter=unknown", customMessage: "D.UnknownWriterNeverInferred");
        log.State["Code"].ShouldBe("landing_concurrency_conflict", customMessage: "D.DiagnosticReadCannotMaskConflict");
        if (variant == "deleted-row") summary.ShouldContain("observedToken=deleted");
        if (variant == "read-unavailable") { fault.Hits.ShouldBe(1); summary.ShouldContain("observedToken=unavailable"); }
        if (variant != "deleted-row")
        {
            await using var db = h.CreateContext();
            var saved = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
            saved.TerminalFailureCode.ShouldBe("landing_concurrency_conflict");
            (await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == saved.TerminalEventId)).Detail.ShouldContain("observedDatabaseWriter=unknown");
        }
    }

    [Test]
    public async Task C883_SecretPayloadIsAbsent()
    {
        const string marker = "C939_PRIVATE_SQL_password_and_worktree_payload";
        var entries = new List<RecordingLogEntry>();
        await using var h = new LandingSafetyHarness();
        h.Logger = new RecordingLogger<AgentTaskLandService>(entries);
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var request = await h.RequestAsync();
        await using var hostile = h.CreateContext();
        var row = await hostile.AgentTaskLandRequests.SingleAsync(r => r.Id == request.RequestId);
        row.HoldDetail = marker;
        row.ConcurrencyToken = Guid.NewGuid();
        hostile.ChangeTracker.DetectChanges();
        var update = (Microsoft.EntityFrameworkCore.Update.IUpdateEntry)hostile.Entry(row).GetInfrastructure();
        var failure = new DbUpdateConcurrencyException(marker + new string('x', 10000),
            (IReadOnlyList<Microsoft.EntityFrameworkCore.Update.IUpdateEntry>)Enumerable.Repeat(update, 20).ToArray());
        h.Fixture.Git.BeforeCommand = (_, _) => throw failure;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var hosted = new Antiphon.Server.Infrastructure.Orchestration.AgentTaskLandHostedService(h.Queue,
            h.Services.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            new CompletionLogger<Antiphon.Server.Infrastructure.Orchestration.AgentTaskLandHostedService>(entries, completion));
        await hosted.StartAsync(CancellationToken.None);
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { await hosted.StopAsync(CancellationToken.None); }
        foreach (var log in entries)
        {
            log.Message.ShouldNotContain(marker, customMessage: "D.SafeLogMessage");
            log.Exception?.ToString().ShouldNotContain(marker, customMessage: "D.SafeExceptionObject");
            foreach (var field in log.State.Values) field?.ToString()?.ShouldNotContain(marker, customMessage: "D.SafeStructuredState");
        }
        await using var db = h.CreateContext();
        var saved = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        saved.TerminalFailureCode.ShouldBe("landing_concurrency_conflict");
        var terminal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == saved.TerminalEventId);
        terminal.Detail.ShouldNotContain(marker, customMessage: "D.SafeTerminalDetail");
        terminal.Detail.ShouldContain($"row={request.RequestId:N}");
        terminal.Detail.Split("entity=AgentTaskLandRequest").Length.ShouldBe(5, customMessage: "D.EntryListBoundedAtFour");
        terminal.Detail.Length.ShouldBeLessThan(3000, customMessage: "D.BoundedSafeSummary");
        var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.RequestId == request.RequestId && n.Kind == LandNotificationKind.Outcome);
        note.Body.ShouldNotContain(marker, customMessage: "D.SafeImmutableNotification");
        entries.Count(e => e.State.ContainsKey("DiagnosticId")).ShouldBe(2, customMessage: "D.DirectAndHostedLoggersObserved");
    }

    [Test]
    public async Task C883_TerminalSummaryIsIdempotent()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var request = await h.RequestAsync();
        await h.FailAsync(new DbUpdateConcurrencyException("first private failure"));
        await using var before = h.CreateContext();
        var first = await before.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        var detail = (await before.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == first.TerminalEventId)).Detail;
        var note = await before.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.RequestId == request.RequestId && n.Kind == LandNotificationKind.Outcome);
        await h.FailAsync(new IOException("later unrelated failure"));
        await using var after = h.CreateContext();
        var saved = await after.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        saved.FailureDiagnosticId.ShouldBe(first.FailureDiagnosticId, customMessage: "D.OriginalDiagnosticRetained");
        saved.TerminalEventId.ShouldBe(first.TerminalEventId);
        saved.TerminalFailureCode.ShouldBe("landing_concurrency_conflict");
        (await after.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == saved.TerminalEventId)).Detail.ShouldBe(detail);
        var notes = await after.AgentTaskLandNotifications.AsNoTracking().Where(n => n.RequestId == request.RequestId && n.Kind == LandNotificationKind.Outcome).ToListAsync();
        notes.Count.ShouldBe(1, customMessage: "D.OneTerminalNotification");
        notes[0].Body.ShouldBe(note.Body, customMessage: "D.ImmutableOriginalSummary");
        notes[0].ContentDigest.ShouldBe(note.ContentDigest);
        (await after.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.RequestId && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(1);
    }

    [Test]
    public async Task C883_RolledBackWriterIsNotCommitted()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var request = await h.RequestAsync();
        await using var before = h.CreateContext();
        var original = await before.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        var cut = new WriterTransactionCut(h) { AbortCommit = true };
        h.TransactionInterceptor = cut;
        await using (var db = h.CreateContext())
        {
            var monitor = new AgentTaskLandMonitorService(db, h.Clock, Options.Create(new DelegationSettings()), h.Events);
            await Should.ThrowAsync<IOException>(() => monitor.SweepAsync(CancellationToken.None));
        }
        cut.Aborts.ShouldBe(1, customMessage: "D.ActualMonitorCommitAborted");
        cut.Commits.ShouldBeEmpty("D.NoCommittedWriterReceiptOnRollback");
        await using var after = h.CreateContext();
        var saved = await after.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        saved.ConcurrencyToken.ShouldBe(original.ConcurrencyToken, customMessage: "D.RollbackPreservesPriorToken");
        saved.LastWriterOperation.ShouldBe(original.LastWriterOperation);
        saved.LastWriterToken.ShouldBe(original.LastWriterToken);
        saved.LastWriterAt.ShouldBe(original.LastWriterAt);
    }

    private sealed class DiagnosticReadFault : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int Hits { get; private set; }
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("FROM \"AgentTaskLandRequests\"", StringComparison.Ordinal))
            { Armed = false; Hits++; throw new IOException("owned diagnostic read unavailable"); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class WriterTransactionCut(LandingSafetyHarness h) : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public bool AbortCommit { get; set; }
        public int Aborts { get; private set; }
        public List<AgentTaskLandRequest> Commits { get; } = [];
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> TransactionCommittingAsync(
            System.Data.Common.DbTransaction transaction, Microsoft.EntityFrameworkCore.Diagnostics.TransactionEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result, CancellationToken ct = default)
        {
            if (AbortCommit) { AbortCommit = false; Aborts++; throw new IOException("owned transaction abort"); }
            return ValueTask.FromResult(result);
        }
        public override async Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData data, CancellationToken ct = default)
        {
            await using var db = h.CreateContext();
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleOrDefaultAsync(r => r.TaskId == h.Fixture.TaskId && r.IsPending, ct);
            if (row is not null) Commits.Add(row);
        }
    }

    private sealed class CompletionLogger<T>(List<RecordingLogEntry> entries, TaskCompletionSource completion) : Microsoft.Extensions.Logging.ILogger<T>
    {
        private readonly RecordingLogger<T> _inner = new(entries);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _inner.Log(level, id, state, exception, formatter);
            completion.TrySetResult();
        }
    }

}
