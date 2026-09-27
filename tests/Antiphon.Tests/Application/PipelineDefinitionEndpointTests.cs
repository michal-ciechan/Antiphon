using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public sealed class PipelineDefinitionEndpointTests(AntiphonWebAppFactory factory)
{
    [Before(Test)]
    public Task ResetAsync() => factory.ResetAsync();

    [Test]
    public async Task List_and_get_return_the_seeded_built_in()
    {
        using var client = factory.CreateClient();
        var list = await client.GetAsync("/api/pipeline-definitions");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var rows = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var builtIn = rows.RootElement.EnumerateArray().Single(d =>
            d.GetProperty("id").GetGuid() == PipelineDefinitions.StandardPipelineId);
        builtIn.GetProperty("source").GetString().ShouldBe("BuiltIn");
        builtIn.GetProperty("activeRevision").GetProperty("revisionNumber").GetInt32().ShouldBe(1);
        builtIn.GetProperty("activeRevision").GetProperty("stages")[3]
            .GetProperty("role").GetString().ShouldBe("Code");
        (await client.GetAsync($"/api/pipeline-definitions/{PipelineDefinitions.StandardPipelineId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"/api/pipeline-definitions/{Guid.NewGuid()}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Post_validates_and_creates()
    {
        using var client = factory.CreateClient();
        var name = "Endpoint " + Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/pipeline-definitions", new
        {
            name, description = "", stages = new[] { new { role = "Code", bundleKey = "stage-code", allowedNext = new[] { "code", "decide" } } }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location!.ToString().ShouldStartWith("/api/pipeline-definitions/");
        var invalid = await client.PostAsJsonAsync("/api/pipeline-definitions", new
        {
            name = "Invalid " + Guid.NewGuid().ToString("N"), description = "",
            stages = new[] { new { role = "Docs", bundleKey = "stage-code", allowedNext = new[] { "none" } } }
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadAsStringAsync()).ShouldContain("pipeline_stage_role_not_stage");
    }

    [Test]
    public async Task Revisions_clone_archive_unarchive_round_trip()
    {
        using var client = factory.CreateClient();
        var clone = await client.PostAsJsonAsync(
            $"/api/pipeline-definitions/{PipelineDefinitions.StandardPipelineId}/clone",
            new { name = "Clone " + Guid.NewGuid().ToString("N") });
        clone.StatusCode.ShouldBe(HttpStatusCode.Created, await clone.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await clone.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        var revise = await client.PostAsJsonAsync($"/api/pipeline-definitions/{id}/revisions", new
        {
            stages = new[] { new { role = "Code", bundleKey = "stage-code", allowedNext = new[] { "code" } } },
            changeNote = "shorter"
        });
        revise.StatusCode.ShouldBe(HttpStatusCode.OK, await revise.Content.ReadAsStringAsync());
        using var revised = JsonDocument.Parse(await revise.Content.ReadAsStringAsync());
        revised.RootElement.GetProperty("activeRevision").GetProperty("revisionNumber").GetInt32().ShouldBe(2);
        (await client.PostAsJsonAsync($"/api/pipeline-definitions/{PipelineDefinitions.StandardPipelineId}/revisions",
            new { stages = new[] { new { role = "Code", bundleKey = "stage-code", allowedNext = new[] { "code" } } } }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync($"/api/pipeline-definitions/{id}/archive", new { reason = "old" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetStringAsync($"/api/pipeline-definitions/{id}")).ShouldContain("archivedAt");
        (await client.PostAsync($"/api/pipeline-definitions/{id}/unarchive", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Board_and_project_pointer_routes_write_and_expose_pipeline()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (board, _) = await PipelineDefinitionServiceTests.SeedBoardAsync(db);
        var custom = await new PipelineDefinitionService(db, TimeProvider.System).CreateAsync(
            new("Pointer " + Guid.NewGuid().ToString("N"), "", [PipelineDefinitionServiceTests.Code()]),
            CancellationToken.None);
        using var client = factory.CreateClient();
        var project = await client.PutAsJsonAsync($"/api/projects/{board.ProjectId}/pipeline",
            new { pipelineDefinitionId = custom.Id });
        project.StatusCode.ShouldBe(HttpStatusCode.OK, await project.Content.ReadAsStringAsync());
        using var projectJson = JsonDocument.Parse(await project.Content.ReadAsStringAsync());
        projectJson.RootElement.GetProperty("pipeline").GetProperty("source").GetString().ShouldBe("project");
        var boardResponse = await client.PutAsJsonAsync($"/api/boards/{board.Id}/pipeline",
            new { pipelineDefinitionId = custom.Id });
        boardResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await boardResponse.Content.ReadAsStringAsync());
        using var boardJson = JsonDocument.Parse(await boardResponse.Content.ReadAsStringAsync());
        boardJson.RootElement.GetProperty("pipeline").GetProperty("source").GetString().ShouldBe("board");
        boardJson.RootElement.GetProperty("pipeline").GetProperty("definitionId").GetGuid().ShouldBe(custom.Id);
        var inherited = await client.PutAsJsonAsync($"/api/boards/{board.Id}/pipeline",
            new { pipelineDefinitionId = (Guid?)null });
        inherited.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var inheritedJson = JsonDocument.Parse(await inherited.Content.ReadAsStringAsync());
        inheritedJson.RootElement.GetProperty("pipeline").GetProperty("source").GetString().ShouldBe("project");
        var builtin = await client.PutAsJsonAsync($"/api/projects/{board.ProjectId}/pipeline",
            new { pipelineDefinitionId = (Guid?)null });
        builtin.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var builtinJson = JsonDocument.Parse(await builtin.Content.ReadAsStringAsync());
        builtinJson.RootElement.GetProperty("pipeline").GetProperty("source").GetString().ShouldBe("builtin");
        (await client.PostAsJsonAsync($"/api/pipeline-definitions/{custom.Id}/archive", new { reason = "retired" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/api/boards/{board.Id}/pipeline", new { pipelineDefinitionId = custom.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await client.PutAsJsonAsync($"/api/projects/{board.ProjectId}/pipeline", new { pipelineDefinitionId = Guid.NewGuid() }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
