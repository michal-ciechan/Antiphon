using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0679 D-7. A failed remote launch whose clean-up kill cannot reach the runner used to log a
// Warning and leave a live orphan holding a seat. The kill is now recorded as a
// generation-conditional intent on the CARD-0653 kind and sent when the runner is back.
[Category("Integration")]
public class PhoneHomeDeferredKillTests
{
    [Test]
    public async Task Kill_with_no_eligible_connection_records_a_generation_kill_intent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        // The launch is acknowledged; its first ready read is never answered, and the socket drops
        // under it, so the clean-up kill finds no connection to send on.
        peer.SilentFor(PhoneHomeOperation.Snapshot);
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s =>
            {
                s.AddSingleton<IAgentProtocolAdapterFactory>(sp => new AgentProtocolAdapterFactory(
                    Options.Create(new AgentRegistrySettings()),
                    sp.GetRequiredService<ISessionRunnerClient>(),
                    directory: host.Directory));
                s.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
                {
                    KillGraceMs = 100,
                    SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-deferred-kill-{Guid.NewGuid():N}"),
                }));
            },
        });

        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.Raw,
                SessionBackend = SessionBackend.PtyHost,
                Status = SessionStatus.Starting,
                Cwd = h.TempRoot,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = host.AllowedRunnerId,
                RunnerStoreId = host.StoreId,
                RunnerCwd = "/work",
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(u => u
                .SetProperty(a => a.Status, AgentStatus.Running)
                .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
        }

        DateTime generation;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var startedAt = (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).StartedAt;
            generation = new DateTime(startedAt.Ticks - startedAt.Ticks % 10, startedAt.Kind);
        }

        using var scope = h.Provider.CreateScope();
        var launch = scope.ServiceProvider.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
            sessionId, h.AgentId,
            new AgentLaunchSpec("fake", AgentKind.Raw, "fake", [], new Dictionary<string, string>(), h.TempRoot, 120, 30),
            remoteControlName: null, resume: false, notes: null, CancellationToken.None);
        var ready = peer.WaitForAsync(PhoneHomeOperation.Snapshot, TimeSpan.FromSeconds(10));
        if (await Task.WhenAny(ready, launch) == launch)
            throw new InvalidOperationException(
                $"The launch ended before its first ready read: {launch.Exception?.GetBaseException()}");
        await ready;
        peer.RequestCount(PhoneHomeOperation.Launch).ShouldBe(1);
        peer.Socket.Abort();
        try
        {
            await launch.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            // The launch fails; what matters here is what its clean-up left behind.
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var intents = await verify.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent && i.SessionId == sessionId)
            .ToListAsync();
        intents.Count.ShouldBe(1, "the kill that could not be sent is recorded, not only logged");
        intents[0].FailureReason.ShouldBe($"pending:kill-generation:{host.AllowedRunnerId}:{generation.Ticks}");
        peer.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
    }

    [Test]
    public async Task Reconcile_finishes_the_intent_with_a_generation_conditional_kill_on_reconnect()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        var generation = new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);
        var killId = Guid.NewGuid();
        var replacedId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedIntentAsync(schema.ConnectionString, killId,
            $"pending:kill-generation:{host.AllowedRunnerId}:{generation.Ticks}", now.AddMinutes(-2));
        await SeedIntentAsync(schema.ConnectionString, replacedId,
            $"pending:kill-generation:{host.AllowedRunnerId}:{generation.Ticks}", now.AddMinutes(-1));

        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        // A session with the same id under another generation is a replacement, not ours to kill.
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.KillGeneration && SessionIdOf(frame) == replacedId
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(
                    new RunnerKillGenerationResult(replacedId, false, KillGenerationOutcomes.Mismatch, null),
                    PhoneHomeFraming.Json))
            : null;

        await using (var scheduled = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            await RunnerSlotService.ReconcilePendingReleasesAsync(host.Directory, scheduled, CancellationToken.None);

        var kills = peer.Incoming
            .Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == PhoneHomeOperation.KillGeneration)
            .ToList();
        kills.Count(f => SessionIdOf(f) == killId).ShouldBe(1, "the pending intent is a kill, sent once on reconnect");
        kills.Count(f => SessionIdOf(f) == replacedId).ShouldBe(1);
        foreach (var kill in kills)
            kill.Payload!.Value.GetProperty("expectedAcceptedStartedAt").GetDateTime().ToUniversalTime()
                .ShouldBe(generation, "the kill is conditional on the seeded generation");
        peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(0);

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await verify.AgentIncidents.SingleAsync(i => i.SessionId == killId && i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent))
            .FailureReason.ShouldBe("reconciled");
        (await verify.AgentIncidents.SingleAsync(i => i.SessionId == replacedId && i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent))
            .FailureReason!.ShouldStartWith("failed:");
        (await verify.AgentIncidents.SingleAsync(i => i.SessionId == killId && i.Kind == AgentIncidentKind.RunnerSlotForceReleased))
            .Message.ShouldContain(KillGenerationOutcomes.Killed);
        (await verify.AgentIncidents.CountAsync(i => i.SessionId == replacedId && i.Kind == AgentIncidentKind.RunnerSlotForceReleased))
            .ShouldBe(0);

        // A second pass finds nothing pending and sends nothing more.
        await using (var again = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            await RunnerSlotService.ReconcilePendingReleasesAsync(host.Directory, again, CancellationToken.None);
        peer.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(2);
    }

    private static Guid SessionIdOf(PhoneHomeFrame frame) =>
        frame.Payload is { ValueKind: JsonValueKind.Object } payload
        && payload.TryGetProperty("sessionId", out var id)
            ? id.GetGuid()
            : Guid.Empty;

    private static async Task SeedIntentAsync(string connectionString, Guid sessionId, string state, DateTime createdAt)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Kind = AgentIncidentKind.RunnerSlotReleaseIntent,
            Severity = AlertSeverity.Warning,
            Message = "failed remote launch clean-up",
            FailureReason = state,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
    }
}
