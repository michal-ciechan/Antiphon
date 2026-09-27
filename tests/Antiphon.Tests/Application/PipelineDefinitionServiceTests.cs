using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Data.Seeding;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PipelineDefinitionServiceTests
{
    [Test]
    [Arguments("Docs", "pipeline_stage_role_not_stage")]
    [Arguments("Custom", "pipeline_stage_role_not_stage")]
    [Arguments("Check", "pipeline_stage_role_not_stage")]
    [Arguments("Code twice", "pipeline_stage_role_duplicate")]
    [Arguments("empty", "pipeline_stages_empty")]
    [Arguments("board-api", "pipeline_stage_bundle_not_stage")]
    [Arguments("stage-nope", "pipeline_stage_bundle_unknown")]
    [Arguments("ship", "pipeline_stage_next_unknown")]
    [Arguments("mutation without Mutation", "pipeline_stage_next_not_in_revision")]
    public async Task Create_refuses_invalid_stages(string shape, string expected)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var service = new PipelineDefinitionService(db, TimeProvider.System);
        IReadOnlyList<PipelineStageSpec> stages = shape switch
        {
            "Docs" => [new(AgentTaskRole.Docs, "stage-code", ["none"])],
            "Custom" => [new(AgentTaskRole.Custom, "stage-code", ["none"])],
            "Check" => [new(AgentTaskRole.Check, "stage-code", ["none"])],
            "Code twice" => [Code(), Code()],
            "empty" => [],
            "board-api" => [new(AgentTaskRole.Code, "board-api", ["none"])],
            "stage-nope" => [new(AgentTaskRole.Code, "stage-nope", ["none"])],
            "ship" => [new(AgentTaskRole.Code, "stage-code", ["ship"])],
            _ => [new(AgentTaskRole.Code, "stage-code", ["mutation"])]
        };
        var ex = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
            new CreatePipelineDefinitionRequest("Bad " + shape, "", stages), CancellationToken.None));
        ex.Code.ShouldBe(expected);
        (await db.PipelineDefinitions.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Create_accepts_valid_shapes_and_rejects_bad_or_taken_names()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var service = new PipelineDefinitionService(db, TimeProvider.System);
        var created = await service.CreateAsync(new("Mine", "description", [Code()]), CancellationToken.None);
        created.Source.ShouldBe(PipelineDefinitionSource.Custom);
        created.ActiveRevision!.RevisionNumber.ShouldBe(1);
        created.ActiveRevisionId.ShouldBe(created.ActiveRevision.Id);
        created.ActiveRevision.Stages.Single().Role.ShouldBe(AgentTaskRole.Code);
        (await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(
            new("Mine", "", [Code()]), CancellationToken.None))).Code.ShouldBe("pipeline_definition_name_taken");
        foreach (var name in new[] { "", new string('x', 201) })
            (await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(
                new(name, "", [Code()]), CancellationToken.None))).Code.ShouldBe("pipeline_definition_name_invalid");
    }

    [Test]
    public async Task BuiltIn_refuses_revision_and_archive_but_clones()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        await DatabaseSeeder.SeedPipelineDefinitionsAsync(db, PipelineDefinitions.StandardPipeline, CancellationToken.None);
        var service = new PipelineDefinitionService(db, TimeProvider.System);
        (await Should.ThrowAsync<ConflictException>(() => service.AddRevisionAsync(
            PipelineDefinitions.StandardPipelineId, new([Code()], "x"), CancellationToken.None)))
            .Code.ShouldBe("pipeline_definition_builtin");
        (await Should.ThrowAsync<ConflictException>(() => service.ArchiveAsync(
            PipelineDefinitions.StandardPipelineId, new(), CancellationToken.None)))
            .Code.ShouldBe("pipeline_definition_builtin");
        var clone = await service.CloneAsync(PipelineDefinitions.StandardPipelineId, "Mine", CancellationToken.None);
        var builtin = await service.GetAsync(PipelineDefinitions.StandardPipelineId, CancellationToken.None);
        clone.Source.ShouldBe(PipelineDefinitionSource.Custom);
        clone.ActiveRevision!.ContentHash.ShouldBe(builtin.ActiveRevision!.ContentHash);
        clone.ActiveRevision.Stages.Select(s => s.Role).ShouldBe(builtin.ActiveRevision.Stages.Select(s => s.Role));
    }

    [Test]
    public async Task AddRevision_appends_N_plus_1_and_moves_the_active_pointer_in_one_transaction()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var service = new PipelineDefinitionService(db, TimeProvider.System);
        var created = await service.CreateAsync(new("Mine", "", [Code()]), CancellationToken.None);
        var revised = await service.AddRevisionAsync(created.Id,
            new([new(AgentTaskRole.Code, "stage-code", ["code", "none"])], "tighten"), CancellationToken.None);
        revised.ActiveRevision!.RevisionNumber.ShouldBe(2);
        revised.ActiveRevision.ChangeNote.ShouldBe("tighten");
        revised.ActiveRevisionId.ShouldNotBe(created.ActiveRevisionId);
        (await db.PipelineDefinitionRevisions.CountAsync(r => r.DefinitionId == created.Id)).ShouldBe(2);
        (await Should.ThrowAsync<ValidationException>(() => service.AddRevisionAsync(created.Id,
            new([new(AgentTaskRole.Code, "stage-code", ["ship"])], null), CancellationToken.None)))
            .Code.ShouldBe("pipeline_stage_next_unknown");
        (await db.PipelineDefinitionRevisions.CountAsync(r => r.DefinitionId == created.Id)).ShouldBe(2);
        (await Should.ThrowAsync<ValidationException>(() => service.AddRevisionAsync(created.Id,
            new([Code()], new string('x', 401)), CancellationToken.None)))
            .Code.ShouldBe("pipeline_revision_note_too_long");
    }

    [Test]
    public async Task Archived_definition_is_refused_at_pointer_write_but_an_existing_pointer_still_resolves()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        await DatabaseSeeder.SeedPipelineDefinitionsAsync(db, PipelineDefinitions.StandardPipeline, CancellationToken.None);
        var service = new PipelineDefinitionService(db, TimeProvider.System);
        var custom = await service.CreateAsync(new("Archived", "", [Code()]), CancellationToken.None);
        var (board, card) = await SeedBoardAsync(db);
        board.PipelineDefinitionId = custom.Id;
        await db.SaveChangesAsync();
        await service.ArchiveAsync(custom.Id, new("old", "tester"), CancellationToken.None);
        (await Should.ThrowAsync<ConflictException>(() => new BoardService(db, new MockEventBus(), TimeProvider.System)
            .SetPipelineAsync(board.Id, custom.Id, CancellationToken.None))).Code.ShouldBe("pipeline_definition_archived");
        var resolved = await new PipelineResolution(db).ResolveForCardAsync(card, CancellationToken.None);
        resolved.Definition.Id.ShouldBe(custom.Id);
        resolved.Source.ShouldBe("board");
        (await service.ListAsync(false, CancellationToken.None)).Select(d => d.Id).ShouldNotContain(custom.Id);
        await service.UnarchiveAsync(custom.Id, CancellationToken.None);
        (await new BoardService(db, new MockEventBus(), TimeProvider.System)
            .SetPipelineAsync(board.Id, custom.Id, CancellationToken.None)).PipelineDefinitionId.ShouldBe(custom.Id);
    }

    internal static PipelineStageSpec Code() => new(AgentTaskRole.Code, "stage-code", ["code", "decide"]);
    internal static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    internal static async Task<(Board Board, Card Card)> SeedBoardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project { Id = Guid.NewGuid(), Name = "p-" + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "", CreatedAt = now, UpdatedAt = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Project = project,
            Name = "b-" + Guid.NewGuid().ToString("N"), CreatedAt = now, UpdatedAt = now };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog",
            Name = "Backlog", CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0558", Title = "Pipeline", Description = "fixture", LabelsJson = "[]",
            Status = CardStatus.Backlog, ConcurrencyToken = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
        db.Add(project); db.Add(board); db.Add(column); db.Add(card);
        await db.SaveChangesAsync();
        return (board, card);
    }
}
