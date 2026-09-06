using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class GrokRulesTransactionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Queue_write_fault_rolls_back_trigger_and_recovery_reconstructs_native_boundary(bool afterWrite)
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await using var db = f.Db();
        var launch = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.Id);
        launch.RulesAcknowledgedAt = DateTime.UtcNow;
        launch.Status = QueuedMessageStatus.Sent;
        var session = await db.AgentSessions.SingleAsync(s => s.Id == f.Id);
        session.GrokRulesState = GrokRulesState.Ready;
        session.GrokRulesReadyAt = DateTime.UtcNow;
        var root = Path.Combine(Path.GetTempPath(), "card0395-tx", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "native.jsonl");
            await File.WriteAllTextAsync(path, GrokRulesCompactionRecoveryTests.NativeBoundary + "\n");
            await using var tailer = new GrokTranscriptTailer(f.Id, path, new SessionRunnerEventHub(), NullLogger.Instance, pollInterval: TimeSpan.FromMilliseconds(20));
            tailer.Start();
            var until = DateTime.UtcNow.AddSeconds(10);
            while (tailer.Snapshot().Entries.Count == 0 && DateTime.UtcNow < until) await Task.Delay(20);
            var native = tailer.Snapshot().Entries.ShouldHaveSingleItem();
            native.Kind.ShouldBe(TranscriptKinds.CompactBoundary);
            var boundary = new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.Id, Sequence = 10,
                Kind = native.Kind, Uuid = native.Uuid, Text = native.Text, InputTokens = native.InputTokens,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow };
            db.TranscriptEntries.Add(boundary);
            await db.SaveChangesAsync();

            var fault = new QueueWriteFault(afterWrite);
            var services = new ServiceCollection();
            services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions()).AddInterceptors(fault).Options));
            await using var provider = services.BuildServiceProvider();
            var crashing = new GrokRulesRefreshService(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, Options.Create(new GrokRulesSettings()));
            await Should.ThrowAsync<InvalidOperationException>(() => crashing.ReconcileAsync(f.Id, CancellationToken.None));
            await using var fresh = f.Db();
            (await fresh.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.Id && m.RulesBoundarySequence != null)).ShouldBe(0);
            (await fresh.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == f.Id)).GrokRulesState.ShouldBe(GrokRulesState.Ready);
            // No process-local latch survives. The already committed native row is sufficient.
            var recovered = new GrokRulesRefreshService(f.Provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, Options.Create(new GrokRulesSettings()));
            await recovered.ReconcileAsync(f.Id, CancellationToken.None);
            var rows = await fresh.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == f.Id && m.RulesBoundarySequence != null).ToListAsync();
            rows.Count.ShouldBe(1, "a crash must not leave a handled watermark without pending queue work");
            rows[0].RulesRefreshKey.ShouldBe("compact:" + boundary.Id.ToString("N"));
            rows[0].Status.ShouldBe(QueuedMessageStatus.Pending);
            rows[0].RulesAcknowledgedAt.ShouldBeNull();
            await recovered.ReconcileAsync(f.Id, CancellationToken.None);
            (await fresh.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.Id && m.RulesBoundarySequence != null)).ShouldBe(1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Database_rejects_duplicate_logical_refresh_keys_independently_of_service_lock()
    {
        await using var f = await GrokRulesInitializationTests.Fixture.CreateAsync();
        await f.Rules.ReconcileAsync(f.Id, CancellationToken.None);
        await using var db = f.Db();
        var first = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == f.Id);
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.Id,
            Sequence = 2, Body = "duplicate logical read", Origin = QueuedMessageOrigin.System,
            CreatedAt = DateTime.UtcNow, RulesRefreshKey = first.RulesRefreshKey });
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        error.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe("23505");
        await using var verify = f.Db();
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.Id)).ShouldBe(1);
    }

    private sealed class QueueWriteFault(bool afterWrite) : SaveChangesInterceptor
    {
        private bool armed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            armed = eventData.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added && e.Entity.RulesBoundarySequence != null);
            if (armed && !afterWrite) throw new InvalidOperationException("injected before queue write");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (armed && afterWrite) throw new InvalidOperationException("injected after queue write before transaction commit");
            return ValueTask.FromResult(result);
        }
    }
}
