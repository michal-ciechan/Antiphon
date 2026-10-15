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
public sealed class PipelineSpeedIntegrationTests
{
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
        var owner = await db.AgentTasks.SingleAsync(t => t.Id == w.Owner.Id);
        owner.Result = "retained report\r\nwith exact bytes";
        var old = new StageOutcome { Id = Guid.NewGuid(), StageTaskId = owner.Id,
            Detail = "historical Found", CostUsd = 12.34m, RecordedAt = DateTime.UtcNow };
        db.Add(old);
        await db.SaveChangesAsync();
        await db.GetService<IMigrator>().MigrateAsync();
        db.ChangeTracker.Clear();
        (await db.AgentTasks.SingleAsync(t => t.Id == owner.Id)).Result.ShouldBe("retained report\r\nwith exact bytes");
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
    }

    private static async Task RequireTableAsync(AppDbContext db, string table) =>
        (await db.Database.SqlQueryInterpolated<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = {table}")
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
