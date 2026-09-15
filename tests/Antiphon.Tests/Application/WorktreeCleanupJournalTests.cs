using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
public sealed class WorktreeCleanupJournalTests
{
    [Test]
    public async Task C443_AttemptRequestUnique()
    {
        await using var h = await Store.CreateAsync();
        var rows = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => h.Journal.GetOrCreateAsync(h.Identity, default)));
        rows.Select(a => a.Id).Distinct().Count().ShouldBe(1);
        await using var db = h.Db();
        var duplicate = JsonSerializer.Deserialize<WorktreeCleanupAttempt>(JsonSerializer.Serialize(rows[0]))!;
        duplicate.Id = Guid.NewGuid(); db.WorktreeCleanupAttempts.Add(duplicate);
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        ((PostgresException)error.InnerException!).SqlState.ShouldBe("23505");
    }

    [Test]
    public async Task C443_AttemptCoordinatesImmutable()
    {
        await using var h = await Store.CreateAsync();
        var row = await h.BeginAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => h.Journal.GetOrCreateAsync(
            h.Identity with { SourceSha = new string('b', 40) }, default));
        (await h.ReadAsync()).SourceSha.ShouldBe(row.SourceSha);
    }

    [Test]
    public async Task C443_UnknownAttemptCommitReloaded()
    {
        await using var h = await Store.CreateAsync();
        h.Fault.AfterSave = true;
        var row = await h.BeginAsync();
        h.Fault.Thrown.ShouldBeTrue();
        (await h.ReadAsync()).Id.ShouldBe(row.Id);
    }

    [Test]
    public async Task C443_InitialSlotSpentAcrossRestart()
    {
        await using var h = await Store.CreateAsync(); await h.BeginAsync();
        var command = Guid.NewGuid();
        (await h.Journal.ConsumeSlotAsync(h.Context, command, false, default)).ShouldBeTrue();
        var restarted = new WorktreeCleanupJournal(h.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        (await restarted.ConsumeSlotAsync(h.Context, Guid.NewGuid(), false, default)).ShouldBeFalse();
        (await h.ReadAsync()).InitialCommandId.ShouldBe(command);
    }

    [Test]
    public async Task C443_RetrySlotSpentAcrossRestart()
    {
        await using var h = await Store.CreateAsync(); await h.CapturedAsync();
        var command = Guid.NewGuid();
        (await h.Journal.ConsumeSlotAsync(h.Context, command, true, default)).ShouldBeTrue();
        var restarted = new WorktreeCleanupJournal(h.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        (await restarted.ConsumeSlotAsync(h.Context, Guid.NewGuid(), true, default)).ShouldBeFalse();
        (await h.ReadAsync()).RetryCommandId.ShouldBe(command);
    }

    [Test]
    public async Task C443_FirstFailureWriteOnce()
    {
        await using var h = await Store.CreateAsync(); await h.CapturedAsync();
        var before = await h.ReadAsync();
        await h.Journal.ConsumeSlotAsync(h.Context, Guid.NewGuid(), true, default);
        await h.Journal.RecordOutcomeAsync(h.Context, h.Outcome with { ExitCode = 1, At = h.Outcome.At.AddSeconds(1) }, true, true, default);
        var after = await h.ReadAsync();
        after.FirstGitFailureJson.ShouldBe(before.FirstGitFailureJson);
        after.CaptureAt.ShouldBe(before.CaptureAt);
        JsonSerializer.Deserialize<WorktreeGitOutcome>(after.LastGitOutcomeJson!)!.ExitCode.ShouldBe(1);
    }

    [Test]
    public async Task C443_CaptureWriteOnce()
    {
        await using var h = await Store.CreateAsync(); var capture = await h.CapturedAsync();
        var original = (await h.ReadAsync()).CaptureJson;
        await h.Journal.CaptureAsync(h.Context, capture, default);
        await Should.ThrowAsync<InvalidOperationException>(() => h.Journal.CaptureAsync(h.Context,
            capture with { At = capture.At.AddMinutes(1) }, default));
        (await h.ReadAsync()).CaptureJson.ShouldBe(original);
    }

    [Test]
    public async Task C443_JournalUtf8Bound()
    {
        await using var h = await Store.CreateAsync(); await h.PendingAsync();
        var capture = h.Capture() with { Handles = new(WorktreeLockStatus.OwnersObserved, "Observed", h.Outcome.At,
            Enumerable.Range(1, 32).Select(i => new WorktreeLockOwner(new string('\u00e9', 300), i, new string('\u00e9', 300))).ToArray()) };
        await h.Journal.CaptureAsync(h.Context, capture, default);
        var row = await h.ReadAsync();
        Encoding.UTF8.GetByteCount(row.CaptureJson!).ShouldBeLessThanOrEqualTo(32768);
        JsonSerializer.Deserialize<WorktreeCleanupCapture>(row.CaptureJson!)!.Omitted.ShouldBeGreaterThan(0);
        row.Summary!.Length.ShouldBeLessThanOrEqualTo(600);
    }

    [Test]
    public async Task C443_ContextCannotBorrowAttempt()
    {
        await using var h = await Store.CreateAsync(); await h.BeginAsync();
        foreach (var context in new[] { h.Context with { RequestId = Guid.NewGuid() },
                     h.Context with { OperationId = Guid.NewGuid() }, h.Context with { TaskId = Guid.NewGuid() } })
            await Should.ThrowAsync<InvalidOperationException>(() => h.Journal.ConsumeSlotAsync(context, Guid.NewGuid(), false, default));
        (await h.ReadAsync()).InitialCommandId.ShouldBeNull();
    }

    [Test]
    public async Task C443_JournalDoesNotUseTrackedEvidence()
    {
        await using var h = await Store.CreateAsync(); await h.BeginAsync();
        await using var db = h.Db();
        var tracked = await db.WorktreeCleanupAttempts.SingleAsync();
        tracked.CaptureState = WorktreeCleanupCaptureState.Captured;
        (await h.ReadAsync()).CaptureState.ShouldBe(WorktreeCleanupCaptureState.NotNeeded);
        await h.PendingAsync();
        (await h.ReadAsync()).CaptureState.ShouldBe(WorktreeCleanupCaptureState.Pending);
    }

    [Test]
    public async Task C443_PendingCaptureInterrupted()
    {
        await using var h = await Store.CreateAsync(); await h.PendingAsync();
        var before = await h.ReadAsync();
        await h.Journal.InterruptAsync(h.Context, "worker_interrupted", default);
        var after = await h.ReadAsync();
        after.CaptureState.ShouldBe(WorktreeCleanupCaptureState.Interrupted);
        after.FirstGitFailureJson.ShouldBe(before.FirstGitFailureJson);
        after.CaptureAt.ShouldBe(before.CaptureAt);
        after.CaptureJson.ShouldBeNull();
    }

    [Test]
    public async Task C443_CaptureAcknowledgementReloaded()
    {
        await using var h = await Store.CreateAsync(); await h.PendingAsync();
        h.Fault.AfterSave = true;
        await h.Journal.CaptureAsync(h.Context, h.Capture(), default);
        h.Fault.Thrown.ShouldBeTrue();
        (await h.ReadAsync()).CaptureState.ShouldBe(WorktreeCleanupCaptureState.Captured);
    }

    [Test]
    public async Task C443_LostResultIsNotInvented()
    {
        await using var h = await Store.CreateAsync(); await h.BeginAsync();
        await h.Journal.ConsumeSlotAsync(h.Context, Guid.NewGuid(), false, default);
        await h.Journal.InterruptAsync(h.Context, "worker_interrupted", default);
        var row = await h.ReadAsync();
        row.FirstGitFailureJson.ShouldBeNull(); row.LastGitOutcomeJson.ShouldBeNull(); row.CaptureJson.ShouldBeNull();
        row.CaptureState.ShouldBe(WorktreeCleanupCaptureState.Interrupted);
    }

    [Test]
    public async Task C443_JournalConcurrencyRejectsLostUpdate()
    {
        await using var h = await Store.CreateAsync(); await h.CapturedAsync();
        await using var stale = h.Db();
        var old = await stale.WorktreeCleanupAttempts.SingleAsync();
        await h.Journal.ConsumeSlotAsync(h.Context, Guid.NewGuid(), true, default);
        old.Summary = "stale";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        (await h.ReadAsync()).RetryCommandId.ShouldNotBeNull();
        (await h.ReadAsync()).Summary.ShouldNotBe("stale");
    }

    [Test]
    public async Task C443_UnknownAttemptSchemaRefuses()
    {
        await using var h = await Store.CreateAsync(); await h.BeginAsync();
        await using (var db = h.Db())
        { var row = await db.WorktreeCleanupAttempts.SingleAsync(); row.SchemaVersion = 999; await db.SaveChangesAsync(); }
        await Should.ThrowAsync<InvalidOperationException>(() => h.Journal.ReadAsync(h.Context, default));
        await Should.ThrowAsync<InvalidOperationException>(() => h.Journal.ConsumeSlotAsync(h.Context, Guid.NewGuid(), false, default));
    }

    [Test]
    public async Task C443_JournalScopeLifetime()
    {
        await using var h = await Store.CreateAsync(); await h.BeginAsync();
        await using (var scope = h.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IWorktreeCleanupJournal>().ReadAsync(h.Context, default)).Id.ShouldBe(h.Context.AttemptId);
        (await h.ReadAsync()).Id.ShouldBe(h.Context.AttemptId);
    }

    internal sealed class SaveFault : SaveChangesInterceptor
    {
        public bool AfterSave;
        public bool Thrown;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (AfterSave) { AfterSave = false; Thrown = true; throw new IOException("lost_acknowledgement"); }
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class Store : IAsyncDisposable
    {
        public IsolatedTestSchema Schema = null!;
        public ServiceProvider Services = null!;
        public SaveFault Fault { get; } = new();
        public WorktreeCleanupIdentity Identity = null!;
        public WorktreeCleanupContext Context = null!;
        public IWorktreeCleanupJournal Journal => Services.GetRequiredService<IWorktreeCleanupJournal>();
        public WorktreeGitOutcome Outcome { get; } = new("worktree remove", 128, "git_exit_nonzero", null, DateTime.UtcNow, true);
        public AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>(
            TestDbFixture.CreateDbContextOptions(Schema.ConnectionString)).AddInterceptors(Fault).Options);

        public static async Task<Store> CreateAsync()
        {
            var h = new Store { Schema = await TestDbFixture.CreateIsolatedSchemaAsync() };
            var services = new ServiceCollection(); services.AddSingleton(TimeProvider.System);
            services.AddScoped(_ => h.Db()); services.AddSingleton<IWorktreeCleanupJournal, WorktreeCleanupJournal>();
            h.Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var taskId = Guid.NewGuid(); var requestId = Guid.NewGuid(); var opId = Guid.NewGuid();
            var source = new string('a', 40); var target = new string('c', 40);
            await using var db = h.Db();
            var task = new AgentTask { Id = taskId, RootTaskId = taskId, Title = "journal", Goal = "journal", WorkingDirectory = "repo", CreatedAt = DateTime.UtcNow };
            db.AgentTasks.Add(task); await db.SaveChangesAsync();
            db.AgentTaskLandRequests.Add(new() { Id = requestId, TaskId = taskId, RequestedAt = DateTime.UtcNow });
            db.AgentTaskLandings.Add(new() { Id = opId, TaskId = taskId, RepositoryPath = "repo", WorktreePath = "tree",
                CommonDirectory = "common", GitDirectory = "admin", SourceFullRef = "refs/heads/source", TargetFullRef = "refs/heads/master",
                OriginalSourceSha = source, VerifiedSourceSha = source, TargetBeforeSha = target,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            task.CurrentLandRequestId = requestId; await db.SaveChangesAsync();
            h.Identity = new(requestId, opId, taskId, "repo", "tree", "common", "admin", "refs/heads/source", "refs/heads/master", source, target);
            return h;
        }
        public async Task<WorktreeCleanupAttempt> BeginAsync()
        {
            var row = await Journal.GetOrCreateAsync(Identity, default);
            Context = new(row.Id, row.RequestId, row.OperationId, row.TaskId); return row;
        }
        public async Task PendingAsync()
        {
            await BeginAsync(); await Journal.ConsumeSlotAsync(Context, Guid.NewGuid(), false, default);
            await Journal.RecordOutcomeAsync(Context, Outcome, false, true, default);
        }
        public WorktreeCleanupCapture Capture() => new(Context.AttemptId, Context.RequestId, Context.OperationId, Context.TaskId,
            Outcome.At, Outcome, new(WorktreeLockStatus.Unavailable, "InsufficientPrivileges", Outcome.At, []),
            new(WorktreeLockStatus.Partial, "Observed", [new("DeleteAccessOpen", ".", Outcome.At, false, 32, true)]));
        public async Task<WorktreeCleanupCapture> CapturedAsync()
        { await PendingAsync(); var capture = Capture(); await Journal.CaptureAsync(Context, capture, default); return capture; }
        public Task<WorktreeCleanupAttempt> ReadAsync() => Journal.ReadAsync(Context, default);
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await Schema.DisposeAsync(); }
    }
}
