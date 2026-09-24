using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0653: the operator release route kills the runner seat and audits the desktop row.</summary>
[Category("Integration")]
public class RunnerSlotEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Release_stops_the_desktop_row_and_records_the_reason()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        peer.Sessions.Add(new RunnerSessionDto(sessionId, 4, now, "Running", null, "", 0));
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.ReleaseSlot)
                return null;
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(
                    new RunnerSessionDto(sessionId, 4, now, "Exited", 0, "KilledByRequest", 0),
                    PhoneHomeFraming.Json));
        };

        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using (var db = new AppDbContext(options))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = "/work",
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = host.AllowedRunnerId,
                RunnerStoreId = host.StoreId,
                RunnerCwd = "/work",
            });
            await db.SaveChangesAsync();
        }

        var listed = await host.Http.GetFromJsonAsync<RunnerSlotsDto>(
            $"/api/session-runners/{host.AllowedRunnerId}/slots", Json);
        listed.ShouldNotBeNull();
        listed.DeclaredCapacity.ShouldBe(1);
        listed.Occupied.ShouldBe(1);
        listed.Slots.Single().Orphan.ShouldBeTrue();
        listed.Slots.Single().OccupiesCapacity.ShouldBeTrue();

        using var response = await host.Http.PostAsJsonAsync(
            $"/api/session-runners/{host.AllowedRunnerId}/slots/{sessionId:D}/release",
            new RunnerSlotReleaseRequest("stuck after settlement"),
            Json);
        response.EnsureSuccessStatusCode();
        var released = await response.Content.ReadFromJsonAsync<RunnerSlotReleaseDto>(Json);
        released.ShouldNotBeNull();
        released.Released.ShouldBe(1);

        await using var verify = new AppDbContext(options);
        (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Stopped);
        var incident = await verify.AgentIncidents.SingleAsync();
        incident.Kind.ShouldBe(AgentIncidentKind.RunnerSlotForceReleased);
        incident.SessionId.ShouldBe(sessionId);
        incident.Message.ShouldContain("stuck after settlement");
        peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(1);
    }
}
