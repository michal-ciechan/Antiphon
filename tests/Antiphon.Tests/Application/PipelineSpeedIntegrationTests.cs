using System.Data.Common;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel]
public sealed partial class PipelineSpeedIntegrationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    [Test]
    public async Task C1281_B2AtomicChild()
    {
        await using var w = await C544World.CreateAsync();
        var action = await SeedActionAsync(w);
        var request = ChildRequest(w);
        var pair = await Task.WhenAll(CreateAsync(w, request, action.Id), CreateAsync(w, request, action.Id));
        pair[1].Id.ShouldBe(pair[0].Id, "same-key-child-count == 1");
        await w.RestartAsync();
        (await CreateAsync(w, request, action.Id)).Id.ShouldBe(pair[0].Id, "lost-response-same-child");
        var changed = await Should.ThrowAsync<ConflictException>(() => CreateAsync(w,
            request with { Goal = "different request" }, action.Id), "changed-request-refused");
        changed.Code.ShouldBe("handoff_request_changed", "changed-request-refused");
        foreach (var field in new[] { "attempt", "event", "stage", "card", "project", "recipient", "workflow" })
        {
            var stale = await SeedActionAsync(w);
            await using (var db = w.CreateContext())
            {
                var row = await db.Set<PipelineHandoffAction>().SingleAsync(a => a.Id == stale.Id);
                switch (field)
                {
                    case "attempt": row.Attempt++; break;
                    case "event": row.SettlementEventId = Guid.NewGuid(); break;
                    case "stage": row.NextStage = AgentTaskRole.Review; break;
                    case "card": row.CardId = Guid.NewGuid(); break;
                    case "project": row.ProjectId = Guid.NewGuid(); break;
                    case "recipient": row.RecipientSessionId = Guid.NewGuid(); break;
                    case "workflow": row.WorkflowRunId = Guid.NewGuid(); break;
                }
                await db.SaveChangesAsync();
            }
            await Should.ThrowAsync<ConflictException>(() => CreateAsync(w, request, stale.Id), "stale-handoff-refused " + field);
        }
        await using (var fresh = w.CreateContext())
        {
            (await fresh.Set<PipelineHandoffAction>().SingleAsync(a => a.Id == action.Id)).ChildTaskId.ShouldBe(pair[0].Id);
            (await fresh.AgentTasks.CountAsync(t => t.Id != w.Owner.Id)).ShouldBe(1);
        }
        foreach (var cut in new[] { "link", "commit", "committed" })
        {
            var next = await SeedActionAsync(w);
            var fault = new AdmissionFault(cut);
            await w.RestartAsync(fault);
            await Should.ThrowAsync<IOException>(() => CreateAsync(w, request, next.Id));
            fault.Hits.ShouldBe(1, "cut reached: " + cut);
            await w.RestartAsync();
            await using var fresh = w.CreateContext();
            var row = await fresh.Set<PipelineHandoffAction>().SingleAsync(a => a.Id == next.Id);
            var count = await fresh.AgentTasks.CountAsync(t => t.Id != w.Owner.Id);
            if (cut != "committed")
            {
                row.ChildTaskId.ShouldBeNull("child-and-link-atomic " + cut);
                count.ShouldBe(1, "child-and-link-atomic " + cut);
            }
            else
            {
                row.ChildTaskId.ShouldNotBeNull("child-and-link-atomic committed");
                count.ShouldBe(2);
                (await CreateAsync(w, request, next.Id)).Id.ShouldBe(row.ChildTaskId.Value);
            }
        }
    }

    [Test]
    public async Task C1281_B3AdmissionFence()
    {
        foreach (var terminal in new[] { true, false })
        {
            var locks = new CardLockObserver();
            await using var w = await C544World.CreateAsync(locks);
            await using var close = w.CreateContext();
            await using var tx = await close.Database.BeginTransactionAsync();
            await close.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Cards\" WHERE \"Id\" = {w.Card.Id} FOR UPDATE");
            var create = CreateAsync(w, ChildRequest(w));
            try
            {
                await Task.WhenAny(locks.Attempt.Task, locks.Insertion.Task, create).WaitAsync(Guard);
                locks.Attempt.Task.IsCompleted.ShouldBeTrue("admission-waits-for-card-lock");
                create.IsCompleted.ShouldBeFalse("admission-waits-for-card-lock");
                await ProbeCardLockAsync(w);
                var card = await close.Cards.SingleAsync(c => c.Id == w.Card.Id);
                card.Status = terminal ? CardStatus.Done : CardStatus.NeedsDecision;
                card.ConcurrencyToken = Guid.NewGuid();
                await close.SaveChangesAsync();
            }
            finally
            {
                await tx.CommitAsync();
                try { await create.WaitAsync(Guard); } catch (ConflictException) { }
            }
            var error = await Should.ThrowAsync<ConflictException>(() => create, "post-close-implementation-refused");
            error.Code.ShouldBe(terminal ? "card_implementation_closed" : "card_admission_changed");
            (await w.TaskCountAsync()).ShouldBe(0);
            // Explicitly commissioned post-Done verification retains ordinary admission.
            (await CreateAsync(w, ChildRequest(w) with { Role = AgentTaskRole.Mutation })).Id.ShouldNotBe(Guid.Empty);
            (await CreateAsync(w, ChildRequest(w) with { Card = null, Title = "unbound" })).CardId.ShouldBeNull();
        }

        var hold = new HoldAdmissionCommit();
        await using var queued = await C544World.CreateAsync();
        await queued.RestartAsync(hold);
        var insertion = CreateAsync(queued, ChildRequest(queued));
        try
        {
            await Task.WhenAny(hold.Entered.Task, insertion).WaitAsync(Guard);
            hold.Entered.Task.IsCompleted.ShouldBeTrue("insertion-transaction-reached");
            await ProbeCardLockAsync(queued);
        }
        finally { hold.Release.TrySetResult(); await insertion.WaitAsync(Guard); }
        // S0 supplies the fence only: exercise the close writer's required locked read,
        // without pretending the not-yet-implemented B-3 close service exists.
        await using var contender = queued.CreateContext();
        await using var closeTx = await contender.Database.BeginTransactionAsync();
        await contender.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Cards\" WHERE \"Id\" = {queued.Card.Id} FOR UPDATE");
        (await contender.AgentTasks.AnyAsync(t => t.CardId == queued.Card.Id && t.Status == AgentTaskStatus.Queued))
            .ShouldBeTrue("queued-implementation-not-canceled; close must refuse");
    }

    private static CreateAgentTaskRequest ChildRequest(C544World w) => new("foundation child", Title: "foundation",
        Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, WorkingDirectory: w.RepositoryPath,
        Card: w.Card.Id.ToString());

    private static async Task<AgentTaskCreatedDto> CreateAsync(C544World w, CreateAgentTaskRequest request, Guid? action = null)
    {
        await using var scope = w.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        return action is { } id ? await service.CreateAsync(request, w.Caller(), id, CancellationToken.None, acceptSpend: true)
            : await service.CreateAsync(request, w.Caller(), CancellationToken.None);
    }

    private static async Task<PipelineHandoffAction> SeedActionAsync(C544World w)
    {
        await using var db = w.CreateContext();
        var owner = await db.AgentTasks.SingleAsync(t => t.Id == w.Owner.Id);
        owner.NextStage = PipelineHandoffKind.Code;
        owner.ParentSessionId = w.CallerSessionId;
        var sourceEvent = new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = w.Owner.Id,
            Type = AgentTaskEventType.Completed, At = owner.CompletedAt!.Value };
        db.Add(sourceEvent);
        var action = new PipelineHandoffAction { Id = Guid.NewGuid(), PredecessorTaskId = w.Owner.Id,
            Attempt = w.Owner.Attempt, SettlementEventId = sourceEvent.Id, NextStage = AgentTaskRole.Code,
            CardId = w.Card.Id, ProjectId = w.Project.Id, RecipientSessionId = w.CallerSessionId,
            Actor = "fixture", SettledAt = sourceEvent.At, CreatedAt = sourceEvent.At, ConsumedAt = sourceEvent.At, EligibleAt = sourceEvent.At };
        db.Add(action); await db.SaveChangesAsync(); return action;
    }

    private static async Task ProbeCardLockAsync(C544World w)
    {
        await using var connection = new NpgsqlConnection(w.Schema.ConnectionString); await connection.OpenAsync();
        // FK KEY SHARE alone must not satisfy the admission-fence witness.
        await using var command = new NpgsqlCommand("SELECT 1 FROM \"Cards\" WHERE \"Id\" = @id FOR NO KEY UPDATE NOWAIT", connection);
        command.Parameters.AddWithValue("id", w.Card.Id);
        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync(), "admission-waits-for-card-lock");
        error.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable, "admission-waits-for-card-lock");
    }

    private sealed class CardLockObserver : DbCommandInterceptor
    {
        public TaskCompletionSource Attempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Insertion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Observe(DbCommand command)
        {
            if (command.CommandText.Contains("\"Cards\"")
                && (command.CommandText.Contains("FOR UPDATE") || command.CommandText.Contains("FOR NO KEY UPDATE")))
                Attempt.TrySetResult();
            if (command.CommandText.Contains("INSERT INTO \"AgentTasks\"")) Insertion.TrySetResult();
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Observe(command); return ValueTask.FromResult(result); }
    }

    private sealed class AdmissionFault(string cut) : SaveChangesInterceptor, IDbTransactionInterceptor
    {
        public int Hits { get; private set; }
        private bool _admitting;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            _admitting |= data.Context!.ChangeTracker.Entries<AgentTask>().Any(e => e.State == EntityState.Added);
            var link = data.Context.ChangeTracker.Entries<PipelineHandoffAction>()
                .FirstOrDefault(e => e.State == EntityState.Modified && e.Entity.ChildTaskId != null);
            if (cut == "link" && link is not null)
            {
                (await data.Context.Set<AgentTask>().AnyAsync(t => t.Id == link.Entity.ChildTaskId, ct))
                    .ShouldBeTrue("child insert reached before link failure");
                Fail();
            }
            return result;
        }
        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData data, InterceptionResult result, CancellationToken ct = default)
        { if (_admitting && cut == "commit") Fail(); return ValueTask.FromResult(result); }
        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken ct = default)
        { if (_admitting && cut == "committed") Fail(); return Task.CompletedTask; }
        private void Fail() { Hits++; throw new IOException("admission cut " + cut); }
    }

    private sealed class HoldAdmissionCommit : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData data, InterceptionResult result, CancellationToken ct = default)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(Guard, ct); return result; }
    }

    [Test]
    public async Task C1281_B1Schema()
    {
        await using var w = await C544World.CreateAsync();
        await using var db = w.CreateContext();
        await RequireTableAsync(db, "ReviewEvidenceCorrections");
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var foundation = Array.FindIndex(migrations, m => m.EndsWith("_AddPipelineSpeedFoundation"));
        foundation.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[foundation - 1]);
        var ownerId = w.Owner.Id;
        const string report = "retained report\r\nwith exact bytes";
        // The rolled-back schema predates current AgentTask columns. Touch only legacy columns.
        (await db.AgentTasks.Where(t => t.Id == ownerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Result, report))).ShouldBe(1);
        (await db.AgentTasks.Where(t => t.Id == ownerId).Select(t => t.Result).SingleAsync()).ShouldBe(report);
        var old = new StageOutcome { Id = Guid.NewGuid(), StageTaskId = ownerId,
            Detail = "historical Found", CostUsd = 12.34m, RecordedAt = DateTime.UtcNow };
        db.Add(old);
        await db.SaveChangesAsync();
        await db.GetService<IMigrator>().MigrateAsync();
        db.ChangeTracker.Clear();
        (await db.AgentTasks.SingleAsync(t => t.Id == ownerId)).Result.ShouldBe("retained report\r\nwith exact bytes");
        var retained = await db.StageOutcomes.SingleAsync(o => o.Id == old.Id);
        retained.Detail.ShouldBe("historical Found"); retained.CostUsd.ShouldBe(12.34m);
        var correction = Populated<ReviewEvidenceCorrection>();
        await RoundtripAsync(w, correction);
        await DuplicateAsync(w, Copy(correction), "duplicate-correction-refused");
        var other = Copy(correction); other.CanonicalBundleDigest = "different-bundle";
        await RoundtripAsync(w, other);
        await ImmutableAsync<ReviewEvidenceCorrection>(w, correction.Id, "correction-immutable");
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Test]
    public async Task C1281_B2Schema()
    {
        await using var w = await C544World.CreateAsync();
        await using var db = w.CreateContext();
        await RequireTableAsync(db, "PipelineHandoffActions");
        var action = Populated<PipelineHandoffAction>();
        action.ChildTaskId = null;
        await RoundtripAsync(w, action);
        await DuplicateAsync(w, Copy(action), "duplicate-action-refused");
        foreach (var field in new[] { "Attempt", "SettlementEventId", "NextStage", "WorkflowRunId",
                     "PipelineDefinitionRevisionId", "CurrentStageId", "WorkflowUpdatedAt" })
        {
            var variant = Copy(action);
            var property = typeof(PipelineHandoffAction).GetProperty(field)!;
            var value = property.GetValue(variant);
            property.SetValue(variant, value switch
            {
                Guid => Guid.NewGuid(), DateTime d => d.AddSeconds(1), int i => i + 1,
                AgentTaskRole => AgentTaskRole.Review, _ => throw new InvalidOperationException(field)
            });
            await RoundtripAsync(w, variant);
        }
        var pending = new PipelineHandoffAction { Id = Guid.NewGuid(), PredecessorTaskId = Guid.NewGuid(),
            SettlementEventId = Guid.NewGuid(), NextStage = AgentTaskRole.Code };
        await RoundtripAsync(w, pending);
        await DuplicateAsync(w, Copy(pending), "duplicate-action-refused nullable-workflow");
        for (var i = 1; i <= 2; i++)
            await RoundtripAsync(w, new PipelineHandoffObservation { Id = Guid.NewGuid(), ActionId = action.Id,
                Sequence = i, Actor = "caller", ReasonCode = "capacity", Detail = "held-" + i,
                ObservedAt = new DateTime(2026, 10, 15, 0, i, 0, DateTimeKind.Utc) });
        await w.RestartAsync();
        await using var fresh = w.CreateContext();
        var observations = await fresh.Set<PipelineHandoffObservation>().Where(o => o.ActionId == action.Id)
            .OrderBy(o => o.Sequence).ToListAsync();
        observations.Select(o => o.Detail).ShouldBe(new[] { "held-1", "held-2" });
        await ImmutableAsync<PipelineHandoffObservation>(w, observations[0].Id, "observation-immutable");
    }

    [Test]
    public async Task C1281_B3Schema()
    {
        await using var w = await C544World.CreateAsync();
        await using var db = w.CreateContext();
        await RequireTableAsync(db, "CardCompletionAcceptances");
        var acceptance = Populated<CardCompletionAcceptance>();
        acceptance.AcceptanceItemsJson = "[{\"state\":\"Satisfied\",\"actor\":\"operator\",\"digest\":\"acceptance\"}]";
        acceptance.DeploymentItemsJson = "[{\"state\":\"Pending\",\"build\":\"required-sha\",\"version\":\"v1\"}]";
        acceptance.EmptyObligationsDecisionJson = "{\"category\":\"docs\",\"actor\":\"operator\"}";
        acceptance.CompanionObligationJson = "{\"required\":true,\"pendingPCs\":[65,66]}";
        await RoundtripAsync(w, acceptance);
        await DuplicateAsync(w, Copy(acceptance), "duplicate-acceptance-refused");
        var ready = Copy(acceptance); ready.Revision++; ready.OperationId = null; ready.LandedSha = null;
        ready.ReadyForRelease = true;
        await RoundtripAsync(w, ready);
        await ImmutableAsync<CardCompletionAcceptance>(w, acceptance.Id, "acceptance-immutable");
        var receipt = Populated<CardCompletionReceipt>();
        receipt.CardId = acceptance.CardId; receipt.AcceptanceId = acceptance.Id;
        receipt.AcceptanceRevision = acceptance.Revision;
        await RoundtripAsync(w, receipt);
        await DuplicateAsync(w, Copy(receipt), "duplicate-close-receipt-refused");
        await ImmutableAsync<CardCompletionReceipt>(w, receipt.Id, "close-receipt-immutable");
    }

    private static async Task RequireTableAsync(AppDbContext db, string table) =>
        (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = {table}")
            .SingleAsync()).ShouldBe(1, table + " migrated");

    // These entities deliberately have no navigation properties. Fill every scalar coordinate,
    // then compare an independently loaded row, so omitted persistence is detected for every field.
    private static T Populated<T>() where T : new()
    {
        var entity = new T();
        foreach (var p in typeof(T).GetProperties())
        {
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            object value = type == typeof(Guid) ? Guid.NewGuid()
                : type == typeof(DateTime) ? new DateTime(2026, 10, 15, 1, 2, 3, DateTimeKind.Utc)
                : type == typeof(int) ? 1 : type == typeof(long) ? 42L : type == typeof(bool) ? true
                : type.IsEnum ? AgentTaskRole.Code : p.Name.EndsWith("Json") ? "{\"test\":true}" : "bound-" + p.Name;
            p.SetValue(entity, value);
        }
        return entity;
    }

    private static T Copy<T>(T row)
    {
        var copy = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(row))!;
        typeof(T).GetProperty("Id")!.SetValue(copy, Guid.NewGuid());
        return copy;
    }

    private static async Task RoundtripAsync<T>(C544World w, T entity) where T : class
    {
        var before = JsonSerializer.Serialize(entity);
        await using (var db = w.CreateContext()) { db.Add(entity); await db.SaveChangesAsync(); }
        await using var fresh = w.CreateContext();
        var id = (Guid)typeof(T).GetProperty("Id")!.GetValue(entity)!;
        var loaded = await fresh.Set<T>().FindAsync(id);
        JsonSerializer.Serialize(loaded).ShouldBe(before, typeof(T).Name + " all persisted coordinates");
    }

    private static async Task DuplicateAsync<T>(C544World w, T entity, string label) where T : class
    {
        await using var db = w.CreateContext(); db.Add(entity);
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(), label);
        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation, label);
    }

    private static async Task ImmutableAsync<T>(C544World w, Guid id, string label) where T : class
    {
        foreach (var delete in new[] { false, true })
        foreach (var synchronous in new[] { false, true })
        {
            await using var db = w.CreateContext(); var row = (await db.Set<T>().FindAsync(id))!;
            if (delete) db.Remove(row); else typeof(T).GetProperty("Actor")!.SetValue(row, "forged");
            if (synchronous) Should.Throw<InvalidOperationException>(() => db.SaveChanges(), label);
            else await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(), label);
        }
    }
}
