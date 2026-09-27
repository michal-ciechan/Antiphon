using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Data.Seeding;
using Antiphon.Server.Migrations;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Migrations;

public sealed class PipelineDefinitionMigrationTests
{
    [Test]
    [Category("Unit")]
    public void AddPipelineDefinitions_operations_create_tables_repoint_runs_and_add_the_open_run_index()
    {
        var up = new AddPipelineDefinitions().UpOperations.ToList();
        up.OfType<CreateTableOperation>().Select(o => o.Name)
            .ShouldBe(["PipelineDefinitionRevisions", "PipelineDefinitions"]);
        var dataStep = up.OfType<SqlOperation>().Single();
        dataStep.Sql.ShouldContain("DELETE FROM \"CardWorkflowStages\"");
        dataStep.Sql.ShouldContain("DELETE FROM \"CardWorkflowRuns\"");
        up.IndexOf(dataStep).ShouldBeLessThan(up.FindIndex(o => o is AddColumnOperation c
            && c.Table == "CardWorkflowRuns" && !c.IsNullable));
        foreach (var name in new[] { "AgentId", "WorkflowTemplateId", "WorkflowDefinitionSnapshot" })
            up.OfType<DropColumnOperation>().ShouldContain(o => o.Table == "CardWorkflowRuns" && o.Name == name);
        foreach (var name in new[] { "ExecutorType", "ModelName", "GateRequired", "SystemPrompt" })
            up.OfType<DropColumnOperation>().ShouldContain(o => o.Table == "CardWorkflowStages" && o.Name == name);
        foreach (var name in new[] { "PipelineDefinitionId", "PipelineDefinitionRevisionId" })
            up.OfType<AddColumnOperation>().ShouldContain(o => o.Table == "CardWorkflowRuns" && o.Name == name && !o.IsNullable);
        up.OfType<AddColumnOperation>().ShouldContain(o => o.Table == "CardWorkflowStages" && o.Name == "AllowedNextJson"
            && o.ColumnType == "jsonb" && !o.IsNullable);
        up.OfType<AddColumnOperation>().ShouldContain(o => o.Table == "AgentTasks" && o.Name == "CardWorkflowStageId" && o.IsNullable);
        up.OfType<CreateIndexOperation>().ShouldContain(o => o.Name == "IX_CardWorkflowRuns_CardId_Open"
            && o.IsUnique && o.Filter == "\"Status\" IN (0, 1)");
        up.OfType<CreateIndexOperation>().ShouldContain(o => o.Name == "IX_CardWorkflowStages_RunId_Role" && o.IsUnique);
        new AddPipelineDefinitions().DownOperations[0].ShouldBeOfType<SqlOperation>();
    }

    [Test]
    [Category("Integration")]
    public async Task Up_deletes_every_legacy_run_and_stage_before_the_not_null_adds()
    {
        await using var fixture = await LegacyFixture.CreateAsync(linkCard: false);
        await fixture.UpgradeAsync();
        (await fixture.Db.CardWorkflowRuns.CountAsync()).ShouldBe(0);
        (await fixture.Db.CardWorkflowStages.CountAsync()).ShouldBe(0);
        (await fixture.Db.Cards.AnyAsync(c => c.Id == fixture.CardId)).ShouldBeTrue();
        (await fixture.Db.Agents.AnyAsync(a => a.Id == fixture.AgentId)).ShouldBeTrue();
        (await fixture.Db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        fixture.Db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Test]
    [Category("Integration")]
    public async Task Up_nulls_a_card_pointer_at_a_deleted_legacy_run()
    {
        await using var fixture = await LegacyFixture.CreateAsync(linkCard: true);
        await fixture.UpgradeAsync();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.Db.Cards.SingleAsync(c => c.Id == fixture.CardId)).ActiveWorkflowRunId.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    [Arguments(CardWorkflowRunStatus.Queued, CardWorkflowRunStatus.Queued, true)]
    [Arguments(CardWorkflowRunStatus.Queued, CardWorkflowRunStatus.Running, true)]
    [Arguments(CardWorkflowRunStatus.Running, CardWorkflowRunStatus.Running, true)]
    [Arguments(CardWorkflowRunStatus.Completed, CardWorkflowRunStatus.Queued, false)]
    [Arguments(CardWorkflowRunStatus.Canceled, CardWorkflowRunStatus.Queued, false)]
    [Arguments(CardWorkflowRunStatus.Failed, CardWorkflowRunStatus.Running, false)]
    public async Task Open_run_index_refuses_a_second_open_run_and_allows_a_closed_one(
        CardWorkflowRunStatus first, CardWorkflowRunStatus second, bool refuse)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var cardId = await SeedCardAsync(db);
        await DatabaseSeeder.SeedPipelineDefinitionsAsync(db, PipelineDefinitions.StandardPipeline, CancellationToken.None);
        var definition = await db.PipelineDefinitions.SingleAsync(d => d.Id == PipelineDefinitions.StandardPipelineId);
        db.CardWorkflowRuns.Add(NewRun(cardId, definition.ActiveRevisionId!.Value, first));
        await db.SaveChangesAsync();
        db.CardWorkflowRuns.Add(NewRun(cardId, definition.ActiveRevisionId.Value, second));
        if (refuse)
        {
            var ex = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
            postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            postgres.ConstraintName.ShouldBe("IX_CardWorkflowRuns_CardId_Open");
        }
        else
        {
            await db.SaveChangesAsync();
            (await db.CardWorkflowRuns.CountAsync(r => r.CardId == cardId)).ShouldBe(2);
        }
    }

    [Test]
    [Category("Integration")]
    public async Task Down_removes_post_A_runs_then_restores_gen1_columns_and_Up_returns_to_head()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var cardId = await SeedCardAsync(db);
        await DatabaseSeeder.SeedPipelineDefinitionsAsync(db, PipelineDefinitions.StandardPipeline, CancellationToken.None);
        var revisionId = (await db.PipelineDefinitions.SingleAsync(d => d.Id == PipelineDefinitions.StandardPipelineId)).ActiveRevisionId!.Value;
        db.CardWorkflowRuns.Add(NewRun(cardId, revisionId, CardWorkflowRunStatus.Queued));
        await db.SaveChangesAsync();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var target = migrations[^2];
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(target);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"CardWorkflowRuns\"").SingleAsync()).ShouldBe(0);
        await migrator.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Test]
    [Category("Integration")]
    [Arguments("StagesJson")]
    [Arguments("RevisionNumber")]
    [Arguments("DefinitionId")]
    public async Task Revision_columns_cannot_change_after_insert(string column)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await DatabaseSeeder.SeedPipelineDefinitionsAsync(db, PipelineDefinitions.StandardPipeline, CancellationToken.None);
        db.ChangeTracker.Clear();
        var revision = await db.PipelineDefinitionRevisions.SingleAsync();
        switch (column)
        {
            case "StagesJson": revision.StagesJson = "[]"; break;
            case "RevisionNumber": revision.RevisionNumber++; break;
            case "DefinitionId": revision.DefinitionId = Guid.NewGuid(); break;
        }
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static CardWorkflowRun NewRun(Guid cardId, Guid revisionId, CardWorkflowRunStatus status) => new()
    {
        Id = Guid.NewGuid(), CardId = cardId, PipelineDefinitionId = PipelineDefinitions.StandardPipelineId,
        PipelineDefinitionRevisionId = revisionId, WorkflowName = PipelineDefinitions.StandardPipelineName,
        Status = status, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static async Task<Guid> SeedCardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project { Id = Guid.NewGuid(), Name = "p-" + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "", CreatedAt = now, UpdatedAt = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "B",
            CreatedAt = now, UpdatedAt = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Backlog",
            StateKey = "backlog", CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0001", Title = "Fixture", Description = "fixture", Status = CardStatus.Backlog,
            LabelsJson = "[]", ConcurrencyToken = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.Add(project); db.Add(board); db.Add(column); db.Add(card);
        await db.SaveChangesAsync();
        return card.Id;
    }

    private sealed class LegacyFixture : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        public AppDbContext Db { get; }
        public Guid CardId { get; }
        public Guid AgentId { get; }

        private LegacyFixture(IsolatedTestSchema schema, AppDbContext db, Guid cardId, Guid agentId)
        { _schema = schema; Db = db; CardId = cardId; AgentId = agentId; }

        public static async Task<LegacyFixture> CreateAsync(bool linkCard)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var cardId = await SeedCardAsync(db);
            var now = DateTime.UtcNow;
            var agent = new Agent { Id = Guid.NewGuid(), Name = "legacy-" + Guid.NewGuid().ToString("N"),
                Slug = "legacy-" + Guid.NewGuid().ToString("N"), WorkingDirectory = "/tmp", CreatedAt = now, UpdatedAt = now };
            var template = new WorkflowTemplate { Id = Guid.NewGuid(), Name = "legacy-" + Guid.NewGuid().ToString("N"),
                Description = "legacy", YamlDefinition = "name: legacy\nstages: []", CreatedAt = now, UpdatedAt = now };
            db.Add(agent); db.Add(template);
            await db.SaveChangesAsync();
            var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CardWorkflowRuns\" (\"Id\",\"CardId\",\"AgentId\",\"WorkflowTemplateId\",\"WorkflowName\",\"WorkflowDefinitionSnapshot\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ({first},{cardId},{agent.Id},{template.Id},'legacy','name: legacy',0,{now},{now}),({second},{cardId},{agent.Id},NULL,'legacy 2','name: legacy',0,{now},{now})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CardWorkflowStages\" (\"Id\",\"CardWorkflowRunId\",\"StageOrder\",\"Name\",\"ExecutorType\",\"GateRequired\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ({Guid.NewGuid()},{first},0,'A','agent',false,0,{now},{now}),({Guid.NewGuid()},{first},1,'B','agent',false,0,{now},{now}),({Guid.NewGuid()},{second},0,'A','agent',false,0,{now},{now})");
            if (linkCard)
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Cards\" SET \"ActiveWorkflowRunId\"={first} WHERE \"Id\"={cardId}");
            return new LegacyFixture(schema, db, cardId, agent.Id);
        }

        public async Task UpgradeAsync() => await Db.GetService<IMigrator>().MigrateAsync();
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _schema.DisposeAsync(); }
    }
}
