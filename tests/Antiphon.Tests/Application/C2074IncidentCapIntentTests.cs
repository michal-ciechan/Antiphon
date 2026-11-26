using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-2074 item 3. <c>PruneIncidentsAsync</c> caps incidents per <c>AgentId</c>, and every
/// incident with a null <c>AgentId</c> shares one bucket: fleet notices (host budget, instruction
/// regeneration), stall incidents whose agent row is gone (group 86) and the runner slot release
/// intents. A pending intent is the only durable record of a release or a deferred kill that
/// <c>RunnerSlotService.ReconcilePendingReleasesAsync</c> still owes, so newer unrelated null
/// rows must never cap it away. CARD-2078 applies the same exemption to age retention.
/// </summary>
[Category("Integration")]
public sealed class C2074IncidentCapIntentTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Null_agent_cap_never_deletes_a_pending_release_intent(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        Guid kill, direct, finished;
        var noise = new List<Guid>();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            // The real deferred-kill producer (CARD-0679 D-7), then aged below every noise row.
            kill = await RunnerSlotService.RecordDeferredKillAsync(
                db, "server2", sessionId, now.AddHours(-4), "deferred kill after a failed remote launch", ct);
            await db.AgentIncidents.Where(i => i.Id == kill)
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.CreatedAt, now.AddHours(-3)), ct);
            direct = Intent(db, sessionId, "pending:server2", now.AddMinutes(-170));
            finished = Intent(db, sessionId, "reconciled", now.AddMinutes(-160));
            for (var i = 0; i < 5; i++)
            {
                var row = new AgentIncident
                {
                    Id = Guid.NewGuid(),
                    Kind = i % 2 == 0 ? AgentIncidentKind.HostBudgetChanged : AgentIncidentKind.TaskProgressStalled,
                    Severity = AlertSeverity.Info,
                    Message = $"c2074 null-agent noise {i}",
                    FailureReason = i == 0 ? "pending:unrelated-fleet-notice" : null,
                    CreatedAt = now.AddMinutes(-60 + i),
                };
                db.AgentIncidents.Add(row);
                noise.Add(row.Id);
            }
            await db.SaveChangesAsync(ct);
            (await db.AgentIncidents.CountAsync(i => i.AgentId == null, ct)).ShouldBe(8, "control: one null bucket");
        }

        var removed = await PruneAsync(schema.ConnectionString, new SupervisionSettings { IncidentCapPerAgent = 3 }, ct);

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var remaining = await db.AgentIncidents.AsNoTracking().Select(i => i.Id).ToListAsync(ct);
            remaining.ShouldContain(kill, "a pending deferred kill is owed until the runner resolves");
            remaining.ShouldContain(direct, "a pending release audit is owed until the runner stops listing the seat");
            remaining.ShouldNotContain(finished, "a reconciled intent is ordinary history and is capped");
            remaining.ShouldNotContain(noise[0], "a pending prefix only exempts runner slot release intents");
            remaining.ShouldNotContain(noise[1]);
            remaining.ShouldBe([kill, direct, noise[2], noise[3], noise[4]], ignoreOrder: true,
                "cap 3 keeps the three newest null rows plus the pending intents");
            removed.ShouldBe(3, "two capped noise rows and the reconciled intent");
            (await db.AgentIncidents.AsNoTracking()
                    .Where(i => i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent && i.FailureReason!.StartsWith("pending:"))
                    .Select(i => i.Id).ToListAsync(ct))
                .ShouldBe([kill, direct], ignoreOrder: true, "the reconcile still finds both obligations");
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task C2078_Age_sweep_keeps_only_old_pending_release_intents(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var old = DateTime.UtcNow.AddDays(-31);
        var sessionId = Guid.NewGuid();
        await using var db = new AppDbContext(options);
        var kill = Intent(db, sessionId, "pending:kill-generation:server2:" + old.Ticks, old);
        var release = Intent(db, sessionId, "pending:server2", old);
        var custody = Intent(db, sessionId, $"pending:custody-release:server2:{old.Ticks}/{Guid.NewGuid():D}/{old.Ticks}", old);
        Intent(db, sessionId, "reconciled", old);
        Intent(db, sessionId, "failed:generation changed", old);
        Intent(db, sessionId, "Pending:server2", old);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(), Kind = AgentIncidentKind.HostBudgetChanged, Severity = AlertSeverity.Info,
            Message = "unrelated fleet notice", FailureReason = "pending:unrelated", CreatedAt = old,
        });
        await db.SaveChangesAsync(ct);

        var removed = await PruneAsync(schema.ConnectionString, new SupervisionSettings(), ct);

        (await db.AgentIncidents.AsNoTracking().Select(i => i.Id).ToListAsync(ct))
            .ShouldBe([kill, release, custody], ignoreOrder: true, "only a pending release obligation survives the age sweep");
        removed.ShouldBe(4);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C2078_Operator_close_stops_retry_and_attention_and_preserves_history_until_prune(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var sessionId = Guid.NewGuid();
        var old = DateTime.UtcNow.AddDays(-31);
        var id = Intent(db, sessionId, $"pending:kill-generation:{host.AllowedRunnerId}:{old.Ticks}", old);
        await db.SaveChangesAsync(ct);
        var attention = AttentionServiceTests.BuildService(host.Local, db: db);

        // The runner is offline. Repeated failed lookups leave one intent and its attention item.
        for (var pass = 0; pass < 2; pass++)
            (await RunnerSlotService.ReconcilePendingReleasesAsync(host.Directory, db, ct)).ShouldBeEmpty();
        (await db.AgentIncidents.CountAsync(ct)).ShouldBe(1);
        (await PruneAsync(schema.ConnectionString, new SupervisionSettings(), ct)).ShouldBe(0);
        var stuck = (await attention.GetAsync(ct)).Items.Single(i => i.Kind == AttentionKind.SessionStopStuck);

        var historyPath = $"/api/session-runners/slots/release-intents/{id:D}";
        var closePath = historyPath + "/close";
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        var body = new { Requester = "on-call operator", Evidence = "Verified session absent in runner records." };
        using var unauthorized = await host.PostOperatorAsync(closePath, body, null);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var missingEvidence = await host.PostOperatorAsync(closePath, body with { Evidence = " " }, token);
        missingEvidence.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var controlSessionId = Guid.NewGuid();
        Intent(db, controlSessionId, $"pending:kill-generation:{host.AllowedRunnerId}:{old.Ticks}", old.AddMinutes(-1));
        await db.SaveChangesAsync(ct);
        var allowReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.ReplyAsync = async frame =>
        {
            if (frame.Operation == PhoneHomeOperation.KillGeneration)
                await allowReply.Task.WaitAsync(ct);
            return null;
        };
        await using var reconcileDb = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var reconcile = RunnerSlotService.ReconcilePendingReleasesAsync(host.Directory, reconcileDb, ct);
        try
        {
            // Both rows are in the running pass's snapshot. Hold its first kill until the API closes the second.
            var first = await peer.WaitForAsync(PhoneHomeOperation.KillGeneration, TimeSpan.FromSeconds(10));
            first.Payload!.Value.GetProperty("sessionId").GetGuid().ShouldBe(controlSessionId);
            using var closed = await host.PostOperatorAsync(closePath, body, token);
            closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            allowReply.TrySetResult();
            await reconcile;
        }

        stuck.Evidence.ShouldContain(closePath);
        (await attention.GetAsync(ct)).Items.ShouldNotContain(i => i.Kind == AttentionKind.SessionStopStuck
            && i.SessionId == sessionId);
        using var repeated = await host.PostOperatorAsync(closePath, body with { Evidence = "Second closure." }, token);
        repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var historyRequest = new HttpRequestMessage(HttpMethod.Get, historyPath);
        historyRequest.Headers.Add(OperatorTokenFile.Header, token);
        using var history = await host.Http.SendAsync(historyRequest, ct);
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        var receipt = await history.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        receipt.GetProperty("intentId").GetGuid().ShouldBe(id);
        receipt.GetProperty("sessionId").GetGuid().ShouldBe(sessionId);
        var terminal = receipt.GetProperty("failureReason").GetString()!;
        terminal.ShouldStartWith("closed:");
        terminal.ShouldContain(body.Requester);
        terminal.ShouldContain(body.Evidence);

        (await reconcile).ShouldBe([controlSessionId]);
        (await RunnerSlotService.ReconcilePendingReleasesAsync(host.Directory, db, ct)).ShouldBeEmpty();
        peer.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(1, "only the open control intent was retried");
        (await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.Id == id, ct)).FailureReason.ShouldBe(terminal);
        (await PruneAsync(schema.ConnectionString, new SupervisionSettings(), ct)).ShouldBe(2);
        (await db.AgentIncidents.AnyAsync(i => i.Id == id, ct)).ShouldBeFalse();
    }

    [Test]
    [Arguments("pending:server2-temp")]
    [Arguments("pending:orphan:server2-temp")]
    [Arguments("pending:kill-generation:server2-temp:638997408000000000")]
    [Timeout(120_000)]
    public async Task C2078_Operator_can_find_and_close_each_kind_of_aged_intent(string marker, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var id = Intent(db, sessionId, marker, now.AddMinutes(-6));
        var young = Intent(db, sessionId, marker, now.AddMinutes(-1));
        Intent(db, sessionId, marker.Replace("server2-temp", "server2-temp2"), now.AddMinutes(-1));
        Intent(db, sessionId, "reconciled", now.AddMinutes(-10));
        await db.SaveChangesAsync(ct);

        const string path = "/api/session-runners/slots/release-intents";
        var closePath = $"{path}/{id:D}/close";
        var attention = AttentionServiceTests.BuildService(host.Local, db: db);
        var stuck = (await attention.GetAsync(ct)).Items.Where(i => i.SessionId == sessionId).ShouldHaveSingleItem();
        stuck.Evidence.ShouldContain(marker);
        stuck.Evidence.ShouldContain(closePath);
        stuck.Evidence.ShouldNotContain(young.ToString("D"));

        var listPath = $"{path}?runnerId=server2-temp&limit=1";
        using var unauthorized = await host.Http.GetAsync(listPath, ct);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        var first = await ReadPageAsync(listPath);
        var firstId = first.GetProperty("intents").EnumerateArray().Single().GetProperty("intentId").GetGuid();
        var cursor = first.GetProperty("nextAfter").GetGuid();
        cursor.ShouldBe(firstId);
        var second = await ReadPageAsync($"{listPath}&after={cursor:D}");
        var secondId = second.GetProperty("intents").EnumerateArray().Single().GetProperty("intentId").GetGuid();
        new[] { firstId, secondId }.ShouldBe([id, young], ignoreOrder: true);

        using var closed = await host.PostOperatorAsync(closePath,
            new { Requester = "on-call operator", Evidence = "Retirement record confirms this runner was decommissioned." },
            token);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await attention.GetAsync(ct)).Items.ShouldNotContain(i => i.SessionId == sessionId);
        var remaining = await ReadPageAsync($"{path}?runnerId=server2-temp&sessionId={sessionId:D}");
        remaining.GetProperty("intents").EnumerateArray().Single().GetProperty("intentId").GetGuid().ShouldBe(young);

        async Task<JsonElement> ReadPageAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add(OperatorTokenFile.Header, token);
            using var response = await host.Http.SendAsync(request, ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        }
    }

    private static async Task<int> PruneAsync(string connectionString, SupervisionSettings settings, CancellationToken ct)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"c2074-prune-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [], supervision: settings, connectionString: connectionString);
            return await harness.Supervisor().PruneIncidentsAsync(ct);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private static Guid Intent(AppDbContext db, Guid sessionId, string failureReason, DateTime createdAt)
    {
        var row = new AgentIncident
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Kind = AgentIncidentKind.RunnerSlotReleaseIntent,
            Severity = AlertSeverity.Warning,
            Message = "c2074 operator release",
            FailureReason = failureReason,
            CreatedAt = createdAt,
        };
        db.AgentIncidents.Add(row);
        return row.Id;
    }
}
