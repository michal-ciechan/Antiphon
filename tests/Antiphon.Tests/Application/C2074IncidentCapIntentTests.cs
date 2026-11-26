using System.Net;
using System.Net.Http.Json;
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
/// rows must never cap it away. Ordinary age retention is unchanged.
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
            .ShouldBe([kill, release], ignoreOrder: true, "only a pending release obligation survives the age sweep");
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
        var stuck = (await attention.GetAsync(ct)).Items.Single(i => i.Kind == AttentionKind.SessionStopStuck);

        var historyPath = $"/api/session-runners/slots/release-intents/{id:D}";
        var closePath = historyPath + "/close";
        var token = OperatorTokenFile.ReadOrCreate(host.OperatorTokenPath);
        var body = new { Requester = "on-call operator", Evidence = "Verified session absent in runner records." };
        using var closed = await host.PostOperatorAsync(closePath, body, token);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        stuck.Evidence.ShouldContain(closePath);
        (await attention.GetAsync(ct)).Items.ShouldNotContain(i => i.Kind == AttentionKind.SessionStopStuck);

        using var historyRequest = new HttpRequestMessage(HttpMethod.Get, historyPath);
        historyRequest.Headers.Add(OperatorTokenFile.Header, token);
        using var history = await host.Http.SendAsync(historyRequest, ct);
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        var receipt = await history.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
        receipt.GetProperty("intentId").GetGuid().ShouldBe(id);
        receipt.GetProperty("sessionId").GetGuid().ShouldBe(sessionId);
        var terminal = receipt.GetProperty("failureReason").GetString()!;
        terminal.ShouldStartWith("closed:");
        terminal.ShouldContain(body.Requester);
        terminal.ShouldContain(body.Evidence);

        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        (await RunnerSlotService.ReconcilePendingReleasesAsync(host.Directory, db, ct)).ShouldBeEmpty();
        peer.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
        (await db.AgentIncidents.AsNoTracking().SingleAsync(ct)).FailureReason.ShouldBe(terminal);
        (await PruneAsync(schema.ConnectionString, new SupervisionSettings(), ct)).ShouldBe(1);
        (await db.AgentIncidents.AnyAsync(ct)).ShouldBeFalse();
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
