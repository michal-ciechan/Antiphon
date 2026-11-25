using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
                    CreatedAt = now.AddMinutes(-60 + i),
                };
                db.AgentIncidents.Add(row);
                noise.Add(row.Id);
            }
            await db.SaveChangesAsync(ct);
            (await db.AgentIncidents.CountAsync(i => i.AgentId == null, ct)).ShouldBe(8, "control: one null bucket");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"c2074-prune-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        int removed;
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [], supervision: new SupervisionSettings { IncidentCapPerAgent = 3 },
                connectionString: schema.ConnectionString);
            removed = await harness.Supervisor().PruneIncidentsAsync(ct);
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

        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var remaining = await db.AgentIncidents.AsNoTracking().Select(i => i.Id).ToListAsync(ct);
            remaining.ShouldContain(kill, "a pending deferred kill is owed until the runner resolves");
            remaining.ShouldContain(direct, "a pending release audit is owed until the runner stops listing the seat");
            remaining.ShouldNotContain(finished, "a reconciled intent is ordinary history and is capped");
            remaining.ShouldNotContain(noise[0]);
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
