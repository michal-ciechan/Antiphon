using System.Net;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0505 V-3. Loopback routes through the production exception middleware.</summary>
[Category("Integration")]
public class DispatchConcurrencyWireTests
{
    [Test]
    public async Task Global_get_put_roundtrip()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await using var host = await DispatchConcurrencyWireHost.Start(shop);
        var body = await Json(await host.GetAsync("/api/dispatch-concurrency"));
        body.GetProperty("supportedRoles").GetArrayLength().ShouldBe(14);
        body.GetProperty("ranges").GetProperty("parallelMin").GetInt32().ShouldBe(1);
        body.GetProperty("ranges").GetProperty("parallelMax").GetInt32().ShouldBe(512);
        body.GetProperty("ranges").GetProperty("queuedMin").GetInt32().ShouldBe(0);
        body.GetProperty("ranges").GetProperty("queuedMax").GetInt32().ShouldBe(4096);
        body.GetProperty("seed").GetProperty("origin").GetString().ShouldBe("startupConfiguration");
        body.GetProperty("seed").GetProperty("importedAt").GetDateTime().ShouldBe(DispatchConcurrencyTestHost.SeedInstant.UtcDateTime);
        body.GetProperty("revision").GetInt64().ShouldBe(1);
        body.GetProperty("effective").GetProperty("mode").GetString().ShouldBe("LegacyOpen");
        body.GetProperty("effective").GetProperty("maxParallelSource").GetString().ShouldBe("default");

        var put = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {
              "expectedRevision": 1,
              "overrides": {"mode":"SeparateQueues","maxParallel":6,"maxQueued":0,"roles":{"Code":{"maxParallel":null,"maxQueued":3}}},
              "reason": "Operator requested a finite queue.",
              "provenance": "Human"
            }
            """);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = await Json(put);
        saved.GetProperty("revision").GetInt64().ShouldBe(2);
        saved.GetProperty("effective").GetProperty("mode").GetString().ShouldBe("SeparateQueues");
        saved.GetProperty("effective").GetProperty("maxParallel").GetInt32().ShouldBe(6);
        saved.GetProperty("effective").GetProperty("maxParallelSource").GetString().ShouldBe("global");
        saved.GetProperty("effective").GetProperty("maxQueued").GetInt32().ShouldBe(0);
        var code = saved.GetProperty("effective").GetProperty("roles").EnumerateArray()
            .Single(role => role.GetProperty("role").GetString() == "Code");
        code.GetProperty("maxParallel").ValueKind.ShouldBe(JsonValueKind.Null);
        code.GetProperty("maxParallelSource").GetString().ShouldBe("global");
        code.GetProperty("maxQueued").GetInt32().ShouldBe(3);
        code.GetProperty("maxQueuedSource").GetString().ShouldBe("global");

        var again = await Json(await host.GetAsync("/api/dispatch-concurrency"));
        again.GetProperty("revision").GetInt64().ShouldBe(2);
        again.GetProperty("effective").GetProperty("mode").GetString().ShouldBe("SeparateQueues");
        await using var db = shop.Db();
        var row = await db.DispatchConcurrencySettings.AsNoTracking().SingleAsync(item => item.ScopeKey == "global");
        row.Revision.ShouldBe(2);
        row.LastProvenance.ShouldBe("Human");
    }

    [Test]
    public async Task Project_get_put_clear_roundtrip()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await using var host = await DispatchConcurrencyWireHost.Start(shop);
        var project = await Json(await host.GetAsync($"/api/projects/{shop.ProjectP}/dispatch-concurrency"));
        project.GetProperty("revision").GetInt64().ShouldBe(0);
        project.GetProperty("globalRevision").GetInt64().ShouldBe(1);

        var put = await host.SendAsync(HttpMethod.Put, $"/api/projects/{shop.ProjectP}/dispatch-concurrency", """
            {"expectedRevision":0,"expectedGlobalRevision":1,"overrides":{"roles":{"Code":{"maxParallel":2}}},"reason":"two code","provenance":"Human"}
            """);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var other = await Json(await host.GetAsync($"/api/projects/{shop.ProjectQ}/dispatch-concurrency"));
        other.GetProperty("revision").GetInt64().ShouldBe(0);
        other.GetProperty("effective").GetProperty("roles").EnumerateArray()
            .Single(role => role.GetProperty("role").GetString() == "Code")
            .GetProperty("maxParallel").GetInt32().ShouldBe(5);

        var clear = await host.SendAsync(HttpMethod.Put, $"/api/projects/{shop.ProjectP}/dispatch-concurrency", """
            {"expectedRevision":1,"expectedGlobalRevision":1,"overrides":{},"reason":"clear","provenance":"Human"}
            """);
        clear.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(clear)).GetProperty("revision").GetInt64().ShouldBe(2);

        (await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":1,"overrides":{"roles":{"Code":{"maxParallel":3}}},"reason":"lower","provenance":"Human"}
            """)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var inherited = await Json(await host.GetAsync($"/api/projects/{shop.ProjectP}/dispatch-concurrency"));
        inherited.GetProperty("revision").GetInt64().ShouldBe(2);
        inherited.GetProperty("effective").GetProperty("roles").EnumerateArray()
            .Single(role => role.GetProperty("role").GetString() == "Code")
            .GetProperty("maxParallelSource").GetString().ShouldBe("global");

        var missing = Guid.NewGuid();
        (await host.GetAsync($"/api/projects/{missing}/dispatch-concurrency")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await host.SendAsync(HttpMethod.Put, $"/api/projects/{missing}/dispatch-concurrency", """
            {"expectedRevision":0,"expectedGlobalRevision":1,"overrides":{},"reason":"missing","provenance":"Human"}
            """)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using var db = shop.Db();
        (await db.DispatchConcurrencySettings.CountAsync(row => row.ProjectId == missing)).ShouldBe(0);
    }

    [Test]
    public async Task Missing_fields_invalid_body_and_unknown_project_write_nothing()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.ReadGlobalAsync();
        await using var host = await DispatchConcurrencyWireHost.Start(shop);
        var before = await FingerprintAsync(shop);
        string[] bodies =
        [
            """{"overrides":{},"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":null,"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":{},"provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":{},"reason":"x"}""",
            """{"expectedRevision":1,"overrides":{},"reason":"   ","provenance":"Human"}""",
            "{\"expectedRevision\":1,\"overrides\":{},\"reason\":\"" + new string('a', 401) + "\",\"provenance\":\"Human\"}",
            """{"expectedRevision":"1","overrides":{},"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":{},"reason":"x","provenance":1}""",
            """{"expectedRevision":1,"overrides":{},"reason":"x","provenance":"Migration"}""",
            """{"expectedRevision":1,"overrides":{},"reason":"x","provenance":"Human","callerTaskId":"00000000-0000-0000-0000-000000000001"}""",
            """{"expectedRevision":1,"expectedRevision":1,"overrides":{},"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":{"maxParallel":0},"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":{"mode":null},"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":{"mode":1},"reason":"x","provenance":"Human"}""",
            """{"expectedRevision":1,"overrides":[],"reason":"x","provenance":"Human"}""",
            "{",
            "",
            """{"expectedRevision":1,"overrides":{},"reason":"x","provenance":"Human"} trailing""",
        ];
        foreach (var body in bodies)
        {
            var response = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", body);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, body);
            (await Json(response)).GetProperty("code").GetString().ShouldBe("validation_failed", "invalid-wire-no-write");
            (await FingerprintAsync(shop)).ShouldBe(before, "invalid-wire-no-write");
        }

        var missing = Guid.NewGuid();
        var unknown = await host.SendAsync(HttpMethod.Put, $"/api/projects/{missing}/dispatch-concurrency", """
            {"expectedRevision":0,"expectedGlobalRevision":1,"overrides":{},"reason":"nope","provenance":"Human"}
            """);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound, "invalid-wire-no-write");
        (await FingerprintAsync(shop)).ShouldBe(before, "invalid-wire-no-write");

        var reasonOne = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":1,"overrides":{},"reason":"y","provenance":"Human"}
            """);
        reasonOne.StatusCode.ShouldBe(HttpStatusCode.OK, "reason-one");
        var reasonBound = await host.SendAsync(
            HttpMethod.Put,
            "/api/dispatch-concurrency",
            "{\"expectedRevision\":2,\"overrides\":{},\"reason\":\"" + new string('b', 400) + "\",\"provenance\":\"Human\"}");
        reasonBound.StatusCode.ShouldBe(HttpStatusCode.OK, "reason-400");
        var explicitNull = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":2,"overrides":{"maxQueued":null},"reason":"null","provenance":"Human"}
            """);
        explicitNull.StatusCode.ShouldBe(HttpStatusCode.OK, "explicit-null");
        var savedNull = await Json(explicitNull);
        savedNull.GetProperty("revision").GetInt64().ShouldBe(3, "explicit-null");
        savedNull.GetProperty("overrides").GetProperty("maxQueued").ValueKind.ShouldBe(JsonValueKind.Null, "explicit-null");
    }

    [Test]
    public async Task Revision_conflict_returns_current_revisions()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.ReadGlobalAsync();
        var pause = new PauseSettingsSaveInterceptor();
        await using var host = await DispatchConcurrencyWireHost.Start(shop, pause);
        var events = shop.Bus.PublishedEvents.Count;
        var stale = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":0,"overrides":{"maxParallel":8},"reason":"stale","provenance":"Human"}
            """);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var staleBody = await Json(stale);
        staleBody.GetProperty("code").GetString().ShouldBe("dispatch_concurrency_revision_conflict");
        staleBody.GetProperty("globalRevision").GetInt64().ShouldBe(1);

        var first = host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":1,"overrides":{"maxParallel":7},"reason":"seven","provenance":"Human"}
            """);
        await pause.AtSave.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":1,"overrides":{"maxParallel":8},"reason":"eight","provenance":"Human"}
            """);
        (await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(shop.ConnectionString, TimeSpan.FromSeconds(10)))
            .ShouldBeTrue();
        pause.Release.TrySetResult();
        var responses = await Task.WhenAll(first, second);
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        var conflict = await Json(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict));
        conflict.GetProperty("code").GetString().ShouldBe("dispatch_concurrency_revision_conflict");
        conflict.GetProperty("globalRevision").GetInt64().ShouldBe(2);

        await shop.PutGlobalAsync(2, "{}", "human", "Human");
        var human = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":3,"overrides":{"maxParallel":4},"reason":"auto","provenance":"Auto"}
            """);
        human.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(human)).GetProperty("code").GetString().ShouldBe("dispatch_concurrency_human");
        shop.Bus.PublishedEvents.Count.ShouldBe(events + 2);
    }

    [Test]
    public async Task Human_reason_and_server_caller_are_audited()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        var token = await SeedCallerAsync(shop);
        var before = await SideAsync(shop);
        await using var host = await DispatchConcurrencyWireHost.Start(shop);
        var response = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":1,"overrides":{"maxParallel":8},"reason":"  padded reason  ","provenance":"Human"}
            """, token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = shop.Db();
        var row = await db.DispatchConcurrencySettings.AsNoTracking().SingleAsync(item => item.ScopeKey == "global");
        row.LastReason.ShouldBe("padded reason", "server-caller-only");
        row.LastCallerTaskId.ShouldBe(shop.ProjectP == Guid.Empty ? null : await CallerIdAsync(db), "server-caller-only");
        row.LastProvenance.ShouldBe("Human", "server-caller-only");
        var history = await db.DispatchConcurrencyRevisions.AsNoTracking().SingleAsync(item => item.Revision == 2);
        history.CallerTaskId.ShouldBe(row.LastCallerTaskId, "server-caller-only");
        history.Reason.ShouldBe("padded reason", "server-caller-only");

        var anonymous = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":2,"overrides":{"maxParallel":7},"reason":"anonymous","provenance":"Human"}
            """);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.OK);
        var anonRow = await db.DispatchConcurrencySettings.AsNoTracking().SingleAsync(item => item.ScopeKey == "global");
        anonRow.LastCallerTaskId.ShouldBeNull("server-caller-only");

        var forgedBefore = await FingerprintAsync(shop);
        var forged = await host.SendAsync(HttpMethod.Put, "/api/dispatch-concurrency", """
            {"expectedRevision":3,"overrides":{},"reason":"forged","provenance":"Human","callerTaskId":"00000000-0000-0000-0000-000000000009"}
            """, token);
        forged.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "server-caller-only");
        (await FingerprintAsync(shop)).ShouldBe(forgedBefore, "server-caller-only");
        shop.Bus.PublishedEvents.Any(item => item.EventName == "DispatchConcurrencyChanged").ShouldBeTrue("server-caller-only");
        (await SideAsync(shop)).ShouldBe(before, "server-caller-only");
    }

    [Test]
    public async Task Revision_routes_are_readonly_and_bounded()
    {
        await using var cold = await DispatchConcurrencyShop.Open();
        await using var coldHost = await DispatchConcurrencyWireHost.Start(cold);
        var empty = await Json(await coldHost.GetAsync("/api/dispatch-concurrency/revisions"));
        empty.GetProperty("revisions").GetArrayLength().ShouldBe(0, "get-is-readonly");
        await using (var db = cold.Db())
            (await db.DispatchConcurrencySettings.CountAsync()).ShouldBe(0, "get-is-readonly");

        await using var shop = await DispatchConcurrencyShop.Open();
        await shop.ReadGlobalAsync();
        for (var queued = 0; queued < 100; queued++)
            await shop.PutGlobalAsync(queued + 1, "{\"maxQueued\":" + queued + "}", "rev " + queued);
        await using var host = await DispatchConcurrencyWireHost.Start(shop);
        var page = await Json(await host.GetAsync("/api/dispatch-concurrency/revisions"));
        page.GetProperty("revisions").GetArrayLength().ShouldBe(50);
        page.GetProperty("nextBeforeRevision").GetInt64().ShouldBe(page.GetProperty("revisions")[49].GetProperty("revision").GetInt64());
        var wide = await Json(await host.GetAsync("/api/dispatch-concurrency/revisions?limit=100"));
        wide.GetProperty("revisions").GetArrayLength().ShouldBe(100);
        var one = await Json(await host.GetAsync("/api/dispatch-concurrency/revisions?limit=1"));
        one.GetProperty("revisions").GetArrayLength().ShouldBe(1);
        var cursor = one.GetProperty("revisions")[0].GetProperty("revision").GetInt64();
        var next = await Json(await host.GetAsync($"/api/dispatch-concurrency/revisions?limit=1&beforeRevision={cursor}"));
        next.GetProperty("revisions")[0].GetProperty("revision").GetInt64().ShouldBeLessThan(cursor);
        var tail = await Json(await host.GetAsync("/api/dispatch-concurrency/revisions?limit=1&beforeRevision=1"));
        tail.GetProperty("revisions").GetArrayLength().ShouldBe(0);
        tail.GetProperty("nextBeforeRevision").ValueKind.ShouldBe(JsonValueKind.Null);

        foreach (var path in new[]
        {
            "/api/dispatch-concurrency/revisions?limit=0",
            "/api/dispatch-concurrency/revisions?limit=101",
            "/api/dispatch-concurrency/revisions?beforeRevision=-1",
        })
        {
            var rejected = await host.GetAsync(path);
            rejected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, path);
        }

        var missing = Guid.NewGuid();
        (await host.GetAsync($"/api/projects/{missing}/dispatch-concurrency/revisions")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "get-is-readonly");
        var fingerprint = await FingerprintAsync(shop);
        await host.GetAsync("/api/dispatch-concurrency/revisions");
        await host.GetAsync($"/api/projects/{shop.ProjectP}/dispatch-concurrency/revisions");
        (await FingerprintAsync(shop)).ShouldBe(fingerprint, "get-is-readonly");
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task<string> FingerprintAsync(DispatchConcurrencyShop shop)
    {
        await using var db = shop.Db();
        var rows = await db.DispatchConcurrencySettings.AsNoTracking()
            .OrderBy(row => row.ScopeKey)
            .Select(row => row.ScopeKey + ":" + row.Revision + ":" + row.OverridesJson)
            .ToListAsync();
        var history = await db.DispatchConcurrencyRevisions.CountAsync();
        return history + "|" + string.Join(",", rows);
    }

    private static async Task<string> SeedCallerAsync(DispatchConcurrencyShop shop)
    {
        var (token, hash) = AgentTaskService.NewToken();
        var id = Guid.NewGuid();
        await using var db = shop.Db();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "caller",
            Goal = "caller",
            Kind = AgentTaskKind.Orchestrator,
            Role = AgentTaskRole.Custom,
            Status = AgentTaskStatus.Working,
            ProjectId = shop.ProjectP,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = "/tmp/card-0505",
            TokenHash = hash,
            CreatedAt = shop.Clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync();
        return token;
    }

    private static async Task<Guid> CallerIdAsync(Antiphon.Server.Infrastructure.Data.AppDbContext db) =>
        await db.AgentTasks.AsNoTracking().Where(task => task.Title == "caller").Select(task => task.Id).SingleAsync();

    private static async Task<string> SideAsync(DispatchConcurrencyShop shop)
    {
        await using var db = shop.Db();
        var budget = await db.HostBudgets.AsNoTracking().SingleAsync();
        var routing = await db.RunnerRoutingSettings.AsNoTracking().SingleAsync();
        var pins = await db.RoutingPins.CountAsync();
        return $"{budget.Revision}:{budget.MaxInFlight}|{routing.Revision}:{routing.GlobalRunnerId}|{pins}";
    }
}
