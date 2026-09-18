using System.Net;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0546 S2 (V-6..V-9): <c>GET /api/agent-tasks?status=</c> over HTTP, on the real host.
///
/// <c>ParseStatuses</c> is private to the endpoint, so its case-insensitivity, comma handling
/// and the <c>422 validation_failed</c> refusal of an unrecognised value can only be pinned
/// through a round trip. Assertions read the raw JSON with <see cref="JsonDocument"/> rather than
/// deserialising into the DTO, so an enum that started serialising numerically, or under a
/// renamed member, cannot pass by being parsed back into the old name.
///
/// Every request is scoped by the test's own <c>rootId</c>, so rows other tests on this host
/// seeded can never make an assertion inexact.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerClass)]
public class AgentTaskListEndpointTests
{
    private readonly AntiphonWebAppFactory _factory;

    public AgentTaskListEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    // ---- V-6 -------------------------------------------------------------------------------

    [Test]
    [Arguments("Working")]
    [Arguments("working")]
    [Arguments("WORKING")]
    public async Task the_status_name_is_parsed_case_insensitively_and_serialised_by_name(string spelling)
    {
        var root = Guid.NewGuid();
        var working = await SeedAsync(root, AgentTaskStatus.Working);
        await SeedAsync(root, AgentTaskStatus.Dispatched);

        using var client = _factory.CreateClient();
        var rows = await ListAsync(client, $"/api/agent-tasks?rootId={root}&status={spelling}");

        rows.Select(Id).ShouldBe([working]);
        rows.Single().GetProperty("status").ValueKind.ShouldBe(JsonValueKind.String, "the enum must serialise by name, never numerically");
        rows.Single().GetProperty("status").GetString().ShouldBe("Working");
    }

    // ---- V-7 -------------------------------------------------------------------------------

    [Test]
    public async Task a_comma_list_returns_the_union()
    {
        var root = Guid.NewGuid();
        var dispatched = await SeedAsync(root, AgentTaskStatus.Dispatched);
        var working = await SeedAsync(root, AgentTaskStatus.Working);
        await SeedAsync(root, AgentTaskStatus.Succeeded);

        using var client = _factory.CreateClient();
        var rows = await ListAsync(client, $"/api/agent-tasks?rootId={root}&status=Dispatched,Working");

        rows.Select(Id).ShouldBe([dispatched, working], ignoreOrder: true);
    }

    [Test]
    [Arguments("Working,")]
    [Arguments(" Working ")]
    [Arguments(",Working")]
    public async Task empty_entries_and_padding_in_the_list_are_ignored(string spelling)
    {
        var root = Guid.NewGuid();
        var working = await SeedAsync(root, AgentTaskStatus.Working);
        await SeedAsync(root, AgentTaskStatus.Dispatched);

        using var client = _factory.CreateClient();
        var rows = await ListAsync(
            client, $"/api/agent-tasks?rootId={root}&status={Uri.EscapeDataString(spelling)}");

        rows.Select(Id).ShouldBe([working]);
    }

    // ---- V-8 -------------------------------------------------------------------------------

    [Test]
    public async Task an_unrecognised_status_is_a_422_naming_the_value()
    {
        var root = Guid.NewGuid();
        await SeedAsync(root, AgentTaskStatus.Working);

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/api/agent-tasks?rootId={root}&status=Bogus");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("validation_failed");
        var errors = problem.RootElement.GetProperty("errors").GetProperty("status");
        errors.GetArrayLength().ShouldBe(1);
        errors[0].GetString().ShouldContain("Bogus");
    }

    [Test]
    public async Task a_list_with_one_bad_entry_is_refused_whole_not_partially_applied()
    {
        var root = Guid.NewGuid();
        await SeedAsync(root, AgentTaskStatus.Working);

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/api/agent-tasks?rootId={root}&status=Working,Bogus");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.RootElement.GetProperty("errors").GetProperty("status")[0].GetString().ShouldContain("Bogus");
        problem.RootElement.ValueKind.ShouldBe(JsonValueKind.Object, "a refusal carries no partial row list");
    }

    [Test]
    public async Task an_undefined_numeric_status_is_a_422()
    {
        // Enum.TryParse accepts any integer token; only Enum.IsDefined turns 99 into a refusal.
        var root = Guid.NewGuid();
        await SeedAsync(root, AgentTaskStatus.Working);

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/api/agent-tasks?rootId={root}&status=99");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.RootElement.GetProperty("errors").GetProperty("status")[0].GetString().ShouldContain("99");
    }

    // ---- V-9 -------------------------------------------------------------------------------

    [Test]
    public async Task include_checks_decides_whether_a_specialist_working_row_is_listed()
    {
        var root = Guid.NewGuid();
        var codeWorking = await SeedAsync(root, AgentTaskStatus.Working, AgentTaskRole.Code);
        var checkWorking = await SeedAsync(root, AgentTaskStatus.Working, AgentTaskRole.Check);

        using var client = _factory.CreateClient();

        var hidden = await ListAsync(client, $"/api/agent-tasks?rootId={root}&status=Working&includeChecks=false");
        hidden.Select(Id).ShouldBe([codeWorking]);

        var byDefault = await ListAsync(client, $"/api/agent-tasks?rootId={root}&status=Working");
        byDefault.Select(Id).ShouldBe([codeWorking], "the default is server-side hiding");

        var shown = await ListAsync(client, $"/api/agent-tasks?rootId={root}&status=Working&includeChecks=true");
        shown.Select(Id).ShouldBe([codeWorking, checkWorking], ignoreOrder: true);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.ValueKind.ShouldBe(JsonValueKind.Array, body);
        return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task<Guid> SeedAsync(Guid root, AgentTaskStatus status, AgentTaskRole role = AgentTaskRole.Code)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = root,
            Title = $"list-endpoint {status} {id:N}",
            Goal = $"list-endpoint {status}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            Status = status,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = @"C:\tmp\list-endpoint",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled
                ? DateTime.UtcNow
                : null,
        });
        await db.SaveChangesAsync();
        return id;
    }
}
