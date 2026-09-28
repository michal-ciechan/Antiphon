using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data.Seeding;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PipelineResolutionTests
{
    [Test]
    [Arguments("board")]
    [Arguments("project")]
    [Arguments("builtin")]
    public async Task Board_pointer_outranks_project_default_which_outranks_the_built_in(string source)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = PipelineDefinitionServiceTests.Context(schema);
        await DatabaseSeeder.SeedPipelineDefinitionsAsync(db, PipelineDefinitions.StandardPipeline, CancellationToken.None);
        var (board, card) = await PipelineDefinitionServiceTests.SeedBoardAsync(db);
        var service = new PipelineDefinitionService(db, TimeProvider.System);
        var projectDefinition = await service.CreateAsync(new CreatePipelineDefinitionRequest(
            "Project pipeline", "", [PipelineDefinitionServiceTests.Code()]), CancellationToken.None);
        var boardDefinition = await service.CreateAsync(new CreatePipelineDefinitionRequest(
            "Board pipeline", "", [PipelineDefinitionServiceTests.Code()]), CancellationToken.None);
        board.Project.DefaultPipelineDefinitionId = source == "builtin" ? null : projectDefinition.Id;
        board.PipelineDefinitionId = source == "board" ? boardDefinition.Id : null;
        await db.SaveChangesAsync();

        var resolution = await new PipelineResolution(db).ResolveForCardAsync(card, CancellationToken.None);
        resolution.Source.ShouldBe(source);
        resolution.Definition.Id.ShouldBe(source switch
        {
            "board" => boardDefinition.Id,
            "project" => projectDefinition.Id,
            _ => PipelineDefinitions.StandardPipelineId
        });
        resolution.Revision.ContentHash.Length.ShouldBe(8);
        resolution.Revision.RevisionNumber.ShouldBe(1);

        if (source != "builtin")
        {
            var changed = await service.AddRevisionAsync(resolution.Definition.Id,
                new AddPipelineDefinitionRevisionRequest([PipelineDefinitionServiceTests.Code()], "new"),
                CancellationToken.None);
            (await new PipelineResolution(db).ResolveForCardAsync(card, CancellationToken.None))
                .Revision.Id.ShouldBe(changed.ActiveRevisionId!.Value);
        }
    }
}
