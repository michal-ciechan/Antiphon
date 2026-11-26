using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;
using static Antiphon.Tests.Application.SessionTranscriptSummaryMigrationTests;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
[NotInParallel]
public sealed class SessionTranscriptSummaryMigrationInvariantTests
{
    [Test]
    public void Summary_model_is_metadata_only_and_invalid_by_default()
    {
        using var db = Metadata();
        var entity = db.Model.FindEntityType("Antiphon.Server.Domain.Entities.SessionTranscriptSummary");
        entity.ShouldNotBeNull("S2 adds durable metadata without transcript payloads");
        entity.GetProperties().Select(p => p.Name).Order().ShouldBe(new[] {
            "AgentSessionId", "SchemaVersion", "Valid", "NextBackfillAttemptAt", "Count", "NewestEffectiveTimestamp",
            "LastSequence", "LastEntryId", "LastKind", "LastTimestamp", "LastCreatedAt", "EndSequence", "EndTimestamp",
            "HasActivity", "HasNullTimestampActivity", "ActivityTimestamp", "LastTurnEndSequence", "LastTurnTitleSequence", "LastUserPromptSequence"
        }.Order());
        entity.FindPrimaryKey()!.Properties.Select(p => p.Name).ShouldBe(["AgentSessionId"]);
        entity.GetForeignKeys().Single().DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);
        entity.FindProperty("Valid")!.GetDefaultValue().ShouldBe(false);
        entity.GetIndexes().ShouldContain(i => i.GetFilter() == "NOT \"Valid\" OR \"SchemaVersion\" <> 1");
    }

    [Test]
    public void Up_is_additive_bounded_and_Down_refuses_data_loss()
    {
        using var db = Metadata();
        var current = Current(db);
        string.CompareOrdinal(current, "20261110213738_Card1294ShadowCohort").ShouldBeGreaterThan(0);
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[current], db.Database.ProviderName!);
        migration.UpOperations.ShouldAllBe(op => op.GetType() == typeof(CreateTableOperation) || op.GetType() == typeof(CreateIndexOperation) || op.GetType() == typeof(SqlOperation));
        migration.UpOperations.OfType<CreateTableOperation>().Select(op => op.Name).ShouldBe(["SessionTranscriptSummaries"]);
        migration.UpOperations.OfType<CreateIndexOperation>().ShouldAllBe(op => op.Table == "SessionTranscriptSummaries");
        var sql = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(op => op.Sql));
        sql.ShouldContain("SET LOCAL lock_timeout = '5s'");
        sql.ShouldNotContain("UPDATE \"TranscriptEntries\"");
        sql.ShouldNotContain("DELETE FROM \"TranscriptEntries\"");
        sql.ShouldNotContain("INSERT INTO \"TranscriptEntries\"");
        sql.ShouldNotContain("\r");
        Should.Throw<NotSupportedException>(() => _ = migration.DownOperations);
    }
}
