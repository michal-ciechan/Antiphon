using System.Net;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class AgentTaskScopedListEndpointTests
{
    [Test]
    public async Task Unscoped_list_always_returns_envelope()
    {
        await using var host = await BootSeededAsync();
        using var response = await host.Client.GetAsync("/api/agent-tasks");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        doc.RootElement.GetProperty("scope").ValueKind.ShouldBe(JsonValueKind.Null);
        Ids(doc).ShouldBe(ScopedAgentTaskListFixture.FleetOrder);
        doc.RootElement.GetProperty("excluded").GetProperty("total").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("excluded").GetProperty("unscoped").GetInt32().ShouldBe(0);
        doc.RootElement.GetProperty("excluded").GetProperty("byProject").GetArrayLength().ShouldBe(0);
        var first = doc.RootElement.GetProperty("items")[0];
        first.GetProperty("id").GetGuid().ShouldBe(ScopedAgentTaskListFixture.X1);
        first.GetProperty("status").GetString().ShouldBe("Working");
        first.GetProperty("title").GetString().ShouldBe(ScopedAgentTaskListFixture.X1.ToString("N")[..8]);

        await using var empty = new AntiphonWebAppFactory();
        using var emptyClient = empty.CreateClient();
        using var emptyResponse = await emptyClient.GetAsync("/api/agent-tasks");
        using var emptyDoc = JsonDocument.Parse(await emptyResponse.Content.ReadAsStringAsync());
        emptyDoc.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
        emptyDoc.RootElement.GetProperty("scope").ValueKind.ShouldBe(JsonValueKind.Null);
        emptyDoc.RootElement.GetProperty("excluded").GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task Project_and_board_predicates_select_exact_rows()
    {
        await using var host = await BootSeededAsync();
        await AssertScopeAsync(host.Client, $"projectId={ScopedAgentTaskListFixture.ProjectX:D}",
            "projectX", AgentTaskUnscopedMode.Exclude,
            ScopedAgentTaskListFixture.ProjectX, ScopedAgentTaskListFixture.AntiphonName, null, null);
        await AssertScopeAsync(host.Client, $"boardId={ScopedAgentTaskListFixture.BoardB1:D}",
            "boardB1", AgentTaskUnscopedMode.Exclude,
            null, null, ScopedAgentTaskListFixture.BoardB1, ScopedAgentTaskListFixture.BoardB1Name);
        await AssertScopeAsync(
            host.Client,
            $"boardId={ScopedAgentTaskListFixture.BoardB1:D}&projectId={ScopedAgentTaskListFixture.ProjectX:D}",
            "both", AgentTaskUnscopedMode.Exclude,
            ScopedAgentTaskListFixture.ProjectX, ScopedAgentTaskListFixture.AntiphonName,
            ScopedAgentTaskListFixture.BoardB1, ScopedAgentTaskListFixture.BoardB1Name);

        using var x = await GetAsync(host.Client, $"/api/agent-tasks?projectId={ScopedAgentTaskListFixture.ProjectX:D}");
        Ids(x).ShouldNotContain(ScopedAgentTaskListFixture.Y1);
        Ids(x).ShouldNotContain(ScopedAgentTaskListFixture.Y3);
        using var b1 = await GetAsync(host.Client, $"/api/agent-tasks?boardId={ScopedAgentTaskListFixture.BoardB1:D}");
        Ids(b1).ShouldNotContain(ScopedAgentTaskListFixture.X3);
        Ids(b1).ShouldNotContain(ScopedAgentTaskListFixture.X4);
        x.RootElement.GetProperty("scope").GetProperty("projectId").GetGuid()
            .ShouldBe(ScopedAgentTaskListFixture.ProjectX);
    }

    [Test]
    public async Task Unscoped_modes_have_exact_membership()
    {
        await using var host = await BootSeededAsync();
        var kinds = new (string Name, string Query)[]
        {
            ("projectX", $"projectId={ScopedAgentTaskListFixture.ProjectX:D}"),
            ("boardB1", $"boardId={ScopedAgentTaskListFixture.BoardB1:D}"),
            ("both", $"boardId={ScopedAgentTaskListFixture.BoardB1:D}&projectId={ScopedAgentTaskListFixture.ProjectX:D}"),
        };
        foreach (var (name, query) in kinds)
        {
            foreach (var mode in new[] { AgentTaskUnscopedMode.Exclude, AgentTaskUnscopedMode.Include, AgentTaskUnscopedMode.Only })
            {
                foreach (var includeChecks in new[] { false, true })
                {
                    var qs = query + $"&unscoped={AgentTaskScope.ModeWire(mode)}"
                        + (includeChecks ? "&includeChecks=true" : "");
                    using var doc = await GetAsync(host.Client, "/api/agent-tasks?" + qs);
                    Ids(doc).ShouldBe(ScopedAgentTaskListFixture.ExpectedItems(name, mode));
                    if (mode != AgentTaskUnscopedMode.Only)
                        Ids(doc).ShouldNotContain(ScopedAgentTaskListFixture.Y1);
                    if (mode == AgentTaskUnscopedMode.Only)
                    {
                        Ids(doc).ShouldBe([ScopedAgentTaskListFixture.N1, ScopedAgentTaskListFixture.N2]);
                        Ids(doc).ShouldNotContain(ScopedAgentTaskListFixture.X1);
                    }
                }
            }

            using var omitted = await GetAsync(host.Client, "/api/agent-tasks?" + query);
            Ids(omitted).ShouldBe(ScopedAgentTaskListFixture.ExpectedItems(name, AgentTaskUnscopedMode.Exclude));
        }

        await using var noneHost = new AntiphonWebAppFactory();
        using var noneClient = noneHost.CreateClient();
        using (var scope = noneHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(ScopedAgentTaskListFixture.Project(ScopedAgentTaskListFixture.ProjectX, ScopedAgentTaskListFixture.AntiphonName));
            await db.SaveChangesAsync();
            await ScopedAgentTaskListFixture.SeedAllNoneAsync(db, 2);
        }

        using var onlyNone = await GetAsync(noneClient, $"/api/agent-tasks?projectId={ScopedAgentTaskListFixture.ProjectX:D}&unscoped=only");
        onlyNone.RootElement.GetProperty("items").GetArrayLength().ShouldBe(2);

        await using var zero = new AntiphonWebAppFactory();
        using var zeroClient = zero.CreateClient();
        using var zeroDoc = await GetAsync(zeroClient, $"/api/agent-tasks?projectId={ScopedAgentTaskListFixture.ProjectX:D}");
        zeroDoc.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
        zeroDoc.RootElement.GetProperty("excluded").GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task Unscoped_without_scope_is_refused()
    {
        await using var host = await BootSeededAsync();
        foreach (var path in new[] { "/api/agent-tasks?unscoped=exclude", "/api/agent-tasks/summary?unscoped=include" })
        {
            using var response = await host.Client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().ShouldBe("validation_failed");
        }
    }

    [Test]
    public async Task Invalid_unscoped_mode_is_refused()
    {
        await using var host = await BootSeededAsync();
        var id = ScopedAgentTaskListFixture.ProjectX;
        foreach (var path in new[]
                 {
                     $"/api/agent-tasks?projectId={id:D}&unscoped=banana",
                     $"/api/agent-tasks?projectId={id:D}&unscoped=",
                     $"/api/agent-tasks/summary?projectId={id:D}&unscoped=banana",
                 })
        {
            using var response = await host.Client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().ShouldBe("validation_failed");
        }
    }

    [Test]
    public async Task Conflicting_board_project_is_refused()
    {
        await using var host = await BootSeededAsync();
        var conflict =
            $"boardId={ScopedAgentTaskListFixture.BoardB1:D}&projectId={ScopedAgentTaskListFixture.ProjectY:D}";
        foreach (var mode in new[] { "exclude", "include", "only" })
        {
            foreach (var prefix in new[] { "/api/agent-tasks?", "/api/agent-tasks/summary?" })
            {
                using var response = await host.Client.GetAsync(prefix + conflict + "&unscoped=" + mode);
                response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                problem.RootElement.GetProperty("code").GetString().ShouldBe("scope_conflict");
            }
        }

        using var ok = await host.Client.GetAsync(
            $"/api/agent-tasks?boardId={ScopedAgentTaskListFixture.BoardB1:D}&projectId={ScopedAgentTaskListFixture.ProjectX:D}");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Unknown_list_query_keys_are_refused()
    {
        await using var host = await BootSeededAsync();
        var b1 = ScopedAgentTaskListFixture.BoardB1;
        foreach (var qs in new[]
                 {
                     $"board={b1:D}",
                     "board_id=1",
                     "projectName=Antiphon",
                     "_=1",
                     "t=1",
                     $"rootId={ScopedAgentTaskListFixture.RootShared:D}&board={b1:D}",
                     "board=",
                 })
        {
            using var response = await host.Client.GetAsync("/api/agent-tasks?" + qs);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().ShouldBe("unknown_query_parameter");
            problem.RootElement.GetProperty("parameter").GetString().ShouldNotBeNull();
            var supported = problem.RootElement.GetProperty("supported").EnumerateArray()
                .Select(e => e.GetString()).ToArray();
            supported.ShouldBe(AgentTaskScope.SupportedListQueryKeys);
        }

        using var accepted = await host.Client.GetAsync(
            $"/api/agent-tasks?ROOTID={ScopedAgentTaskListFixture.RootShared:D}&IncludeChecks=false");
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var summaryUnknown = await host.Client.GetAsync("/api/agent-tasks/summary?board=1");
        summaryUnknown.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Unknown_scope_ids_never_widen()
    {
        await using var host = await BootSeededAsync();
        var unknown = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        foreach (var qs in new[]
                 {
                     $"boardId={unknown:D}",
                     $"projectId={unknown:D}",
                     $"boardId={Guid.Empty:D}",
                     $"boardId={unknown:D}&projectId={ScopedAgentTaskListFixture.ProjectX:D}",
                 })
        {
            using var doc = await GetAsync(host.Client, "/api/agent-tasks?" + qs);
            Ids(doc).ShouldBeEmpty();
            doc.RootElement.GetProperty("excluded").GetProperty("total").GetInt32().ShouldBe(9);
            doc.RootElement.GetProperty("scope").ValueKind.ShouldNotBe(JsonValueKind.Null);
        }

        using var include = await GetAsync(host.Client, $"/api/agent-tasks?boardId={unknown:D}&unscoped=include");
        Ids(include).ShouldBe([ScopedAgentTaskListFixture.N1, ScopedAgentTaskListFixture.N2]);
        using var only = await GetAsync(host.Client, $"/api/agent-tasks?projectId={unknown:D}&unscoped=only");
        Ids(only).ShouldBe([ScopedAgentTaskListFixture.N1, ScopedAgentTaskListFixture.N2]);

        using var malformed = await host.Client.GetAsync("/api/agent-tasks?projectId=not-a-guid");
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var absent = await GetAsync(host.Client, "/api/agent-tasks");
        Ids(absent).Count.ShouldBe(9);
    }

    [Test]
    public async Task Exclusions_partition_the_candidate_set()
    {
        await using var host = await BootSeededAsync(includeY2Twin: true);
        using (var scope = host.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var y2Task = Guid.Parse("22222222-2222-2222-2222-222222222229");
            db.Add(ScopedAgentTaskListFixture.TaskRow(
                y2Task, ScopedAgentTaskListFixture.ProjectY2, null,
                @"C:\src\gym-stat-2", @"C:\src\gym-stat-2",
                ScopedAgentTaskListFixture.T.AddSeconds(40), y2Task));
            await db.SaveChangesAsync();
        }

        using var x = await GetAsync(host.Client, $"/api/agent-tasks?projectId={ScopedAgentTaskListFixture.ProjectX:D}");
        AssertExcluded(x, total: 6, unscoped: 2);
        ByProject(x).ShouldContainKey(ScopedAgentTaskListFixture.ProjectY);
        ByProject(x)[ScopedAgentTaskListFixture.ProjectY].ShouldBe(3);
        ByProject(x).ShouldContainKey(ScopedAgentTaskListFixture.ProjectY2);
        ByProject(x)[ScopedAgentTaskListFixture.ProjectY2].ShouldBe(1);
        (Ids(x).Count + x.RootElement.GetProperty("excluded").GetProperty("total").GetInt32()).ShouldBe(10);

        using var b1 = await GetAsync(host.Client, $"/api/agent-tasks?boardId={ScopedAgentTaskListFixture.BoardB1:D}");
        (Ids(b1).Count + b1.RootElement.GetProperty("excluded").GetProperty("total").GetInt32()).ShouldBe(10);

        using var both = await GetAsync(
            host.Client,
            $"/api/agent-tasks?boardId={ScopedAgentTaskListFixture.BoardB1:D}&projectId={ScopedAgentTaskListFixture.ProjectX:D}");
        (Ids(both).Count + both.RootElement.GetProperty("excluded").GetProperty("total").GetInt32()).ShouldBe(10);

        using var working = await GetAsync(
            host.Client,
            $"/api/agent-tasks?projectId={ScopedAgentTaskListFixture.ProjectX:D}&status=Succeeded");
        working.RootElement.GetProperty("excluded").GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task Scoped_summary_matches_full_history_rows()
    {
        await using var host = await BootSeededAsync();
        using (var scope = host.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = Guid.Parse("77777777-7777-7777-7777-777777777771");
            db.Add(ScopedAgentTaskListFixture.TaskRow(
                old, ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath,
                ScopedAgentTaskListFixture.T.AddDays(-30), old,
                AgentTaskStatus.Succeeded, completedAt: ScopedAgentTaskListFixture.T.AddDays(-29),
                costUsd: 1.5m));
            db.Add(ScopedAgentTaskListFixture.TaskRow(
                Guid.Parse("77777777-7777-7777-7777-777777777772"),
                ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath,
                ScopedAgentTaskListFixture.T.AddSeconds(50),
                Guid.Parse("77777777-7777-7777-7777-777777777772"),
                AgentTaskStatus.Blocked, costUsd: 0.2m));
            db.Add(ScopedAgentTaskListFixture.TaskRow(
                Guid.Parse("77777777-7777-7777-7777-777777777773"),
                ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath,
                ScopedAgentTaskListFixture.T.AddSeconds(51),
                Guid.Parse("77777777-7777-7777-7777-777777777773"),
                role: AgentTaskRole.Check, costUsd: 9m));
            await db.SaveChangesAsync();
        }

        using var list = await GetAsync(host.Client, $"/api/agent-tasks?projectId={ScopedAgentTaskListFixture.ProjectX:D}");
        using var summary = await GetAsync(host.Client, $"/api/agent-tasks/summary?projectId={ScopedAgentTaskListFixture.ProjectX:D}");
        AssertSummaryMatches(list, summary);

        using var fleetList = await GetAsync(host.Client, "/api/agent-tasks");
        using var fleetSummary = await GetAsync(host.Client, "/api/agent-tasks/summary");
        AssertSummaryMatches(fleetList, fleetSummary);

        using var unknown = await GetAsync(
            host.Client, $"/api/agent-tasks/summary?projectId={Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"):D}");
        unknown.RootElement.GetProperty("active").GetInt32().ShouldBe(0);
        unknown.RootElement.GetProperty("blocked").GetInt32().ShouldBe(0);
        unknown.RootElement.GetProperty("runs").GetInt32().ShouldBe(0);
    }

    private static async Task<Host> BootSeededAsync(bool includeY2Twin = false)
    {
        var factory = new AntiphonWebAppFactory();
        var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ScopedAgentTaskListFixture.SeedFleetAsync(db, includeY2Twin);
        }

        return new Host(factory, client);
    }

    private static async Task AssertScopeAsync(
        HttpClient client, string query, string kind, AgentTaskUnscopedMode mode,
        Guid? projectId, string? projectName, Guid? boardId, string? boardName)
    {
        using var doc = await GetAsync(client, "/api/agent-tasks?" + query);
        Ids(doc).ShouldBe(ScopedAgentTaskListFixture.ExpectedItems(kind, mode));
        var scope = doc.RootElement.GetProperty("scope");
        if (projectId is null) scope.GetProperty("projectId").ValueKind.ShouldBe(JsonValueKind.Null);
        else scope.GetProperty("projectId").GetGuid().ShouldBe(projectId.Value);
        if (projectName is null) scope.GetProperty("projectName").ValueKind.ShouldBe(JsonValueKind.Null);
        else scope.GetProperty("projectName").GetString().ShouldBe(projectName);
        if (boardId is null) scope.GetProperty("boardId").ValueKind.ShouldBe(JsonValueKind.Null);
        else scope.GetProperty("boardId").GetGuid().ShouldBe(boardId.Value);
        if (boardName is null) scope.GetProperty("boardName").ValueKind.ShouldBe(JsonValueKind.Null);
        else scope.GetProperty("boardName").GetString().ShouldBe(boardName);
        scope.GetProperty("unscoped").GetString().ShouldBe(AgentTaskScope.ModeWire(mode));
    }

    private static void AssertExcluded(JsonDocument doc, int total, int unscoped)
    {
        var excluded = doc.RootElement.GetProperty("excluded");
        excluded.GetProperty("total").GetInt32().ShouldBe(total);
        excluded.GetProperty("unscoped").GetInt32().ShouldBe(unscoped);
        var sum = excluded.GetProperty("unscoped").GetInt32()
            + excluded.GetProperty("byProject").EnumerateArray().Sum(e => e.GetProperty("count").GetInt32());
        sum.ShouldBe(total);
    }

    private static Dictionary<Guid, int> ByProject(JsonDocument doc) =>
        doc.RootElement.GetProperty("excluded").GetProperty("byProject").EnumerateArray()
            .ToDictionary(e => e.GetProperty("projectId").GetGuid(), e => e.GetProperty("count").GetInt32());

    private static void AssertSummaryMatches(JsonDocument list, JsonDocument summary)
    {
        var items = list.RootElement.GetProperty("items").EnumerateArray().ToList();
        var active = items.Count(e => e.GetProperty("status").GetString() is "Dispatched" or "Working");
        var blocked = items.Count(e => e.GetProperty("status").GetString() == "Blocked");
        var runs = items.Select(e => e.GetProperty("rootTaskId").GetGuid()).Distinct().Count();
        var cost = items.Sum(e => e.GetProperty("costUsd").GetDecimal());
        summary.RootElement.GetProperty("active").GetInt32().ShouldBe(active);
        summary.RootElement.GetProperty("blocked").GetInt32().ShouldBe(blocked);
        summary.RootElement.GetProperty("runs").GetInt32().ShouldBe(runs);
        summary.RootElement.GetProperty("totalCostUsd").GetDecimal().ShouldBe(cost);
        var byStatus = summary.RootElement.GetProperty("byStatus");
        byStatus.EnumerateObject().Sum(p => p.Value.GetInt32()).ShouldBe(items.Count);
        items.Select(e => e.GetProperty("role").GetString()).ShouldNotContain("Check");
        items.Select(e => e.GetProperty("role").GetString()).ShouldNotContain("Distill");
        items.Select(e => e.GetProperty("role").GetString()).ShouldNotContain("Diagnose");
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body);
    }

    private static List<Guid> Ids(JsonDocument doc) =>
        doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

    private sealed class Host(AntiphonWebAppFactory factory, HttpClient client) : IAsyncDisposable
    {
        public AntiphonWebAppFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
