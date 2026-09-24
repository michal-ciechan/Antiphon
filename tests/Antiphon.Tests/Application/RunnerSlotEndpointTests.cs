using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        var incident = await verify.AgentIncidents.SingleAsync(i => i.Kind == AgentIncidentKind.RunnerSlotForceReleased);
        (await verify.AgentIncidents.SingleAsync(i => i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent))
            .FailureReason.ShouldBe("reconciled");
        incident.SessionId.ShouldBe(sessionId);
        incident.Message.ShouldContain("stuck after settlement");
        peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(1);
    }

    [Test]
    public async Task Remote_force_release_is_forbidden()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var sessionId = Guid.NewGuid();
        peer.Sessions.Add(new RunnerSessionDto(sessionId, 4, DateTime.UtcNow, "Running", null, "", 0));
        host.ClientAddress = IPAddress.Parse("203.0.113.9");

        using var response = await host.Http.PostAsJsonAsync(
            $"/api/session-runners/{host.AllowedRunnerId}/slots/{sessionId:D}/release",
            new RunnerSlotReleaseRequest("not from here"),
            Json);
        ((int)response.StatusCode).ShouldBe(403);
        using var orphans = await host.Http.PostAsJsonAsync(
            $"/api/session-runners/{host.AllowedRunnerId}/slots/release-orphans",
            new RunnerSlotReleaseRequest("not from here"),
            Json);
        ((int)orphans.StatusCode).ShouldBe(403);
        peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(0);
    }

    [Test]
    public async Task Release_orphans_skips_a_seat_claimed_after_the_list()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        peer.Sessions.Add(new RunnerSessionDto(sessionId, 4, now, "Running", null, "", 0));
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.ReleaseSlot)
                return null;
            peer.Sessions.RemoveAll(session => session.SessionId == sessionId);
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(
                    new RunnerSessionDto(sessionId, 4, now, "Exited", 0, "KilledByRequest", 0),
                    PhoneHomeFraming.Json));
        };
        await SeedRunningSessionAsync(schema.ConnectionString, sessionId, now, host.StoreId);

        await using var db = new AppDbContext(OptionsWith(
            schema.ConnectionString, new ClaimOnFirstTaskRead(schema.ConnectionString, sessionId)));
        var released = await RunnerSlotService.ReleaseOrphansAsync(
            host.Directory, db, host.AllowedRunnerId, "sweep", CancellationToken.None);

        released.Released.ShouldBe(0);
        peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(0);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await verify.AgentTasks.CountAsync(task =>
            task.AgentSessionId == sessionId && task.Status == AgentTaskStatus.Dispatched)).ShouldBe(1);
        (await verify.AgentSessions.SingleAsync(session => session.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task Failed_audit_save_is_reconciled_without_a_second_release()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        peer.Sessions.Add(new RunnerSessionDto(sessionId, 4, now, "Running", null, "", 0));
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.ReleaseSlot)
                return null;
            peer.Sessions.RemoveAll(session => session.SessionId == sessionId);
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(
                    new RunnerSessionDto(sessionId, 4, now, "Exited", 0, "KilledByRequest", 0),
                    PhoneHomeFraming.Json));
        };
        await SeedRunningSessionAsync(schema.ConnectionString, sessionId, now, host.StoreId);

        await using (var db = new AppDbContext(OptionsWith(schema.ConnectionString, new FailAuditSave())))
        {
            await Should.ThrowAsync<IOException>(() => RunnerSlotService.ReleaseAsync(
                host.Directory, db, host.AllowedRunnerId, sessionId, "stuck after settlement", CancellationToken.None));
        }

        await using (var gap = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            (await gap.AgentSessions.SingleAsync(session => session.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
            (await gap.AgentIncidents.SingleAsync()).Kind.ShouldBe(AgentIncidentKind.RunnerSlotReleaseIntent);
        }

        peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(1);
        await using var reconcile = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var finished = await RunnerSlotService.ReconcilePendingReleasesAsync(
            host.Directory, reconcile, CancellationToken.None);
        finished.ShouldBe([sessionId]);
        peer.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(1);

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await verify.AgentSessions.SingleAsync(session => session.Id == sessionId)).Status.ShouldBe(SessionStatus.Stopped);
        (await verify.AgentIncidents.SingleAsync(incident => incident.Kind == AgentIncidentKind.RunnerSlotForceReleased))
            .Message.ShouldContain("stuck after settlement");
        (await verify.AgentIncidents.SingleAsync(incident => incident.Kind == AgentIncidentKind.RunnerSlotReleaseIntent))
            .FailureReason.ShouldBe("reconciled");
    }

    private static DbContextOptions<AppDbContext> OptionsWith(string connectionString, IInterceptor interceptor) =>
        new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(connectionString))
            .AddInterceptors(interceptor)
            .Options;

    private static async Task SeedRunningSessionAsync(string connectionString, Guid sessionId, DateTime now, Guid storeId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
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
            RunnerId = "grok-linux",
            RunnerStoreId = storeId,
            RunnerCwd = "/work",
        });
        await db.SaveChangesAsync();
    }

    private sealed class FailAuditSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
        {
            var finishing = eventData.Context!.ChangeTracker.Entries<AgentIncident>().Any(entry =>
                entry.State == EntityState.Added && entry.Entity.Kind == AgentIncidentKind.RunnerSlotForceReleased);
            if (finishing)
                throw new IOException("audit save failed");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ClaimOnFirstTaskRead(string connectionString, Guid sessionId) : DbCommandInterceptor
    {
        private int _reads;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken)
        {
            if (command.CommandText.Contains("AgentTasks", StringComparison.Ordinal)
                && Interlocked.Increment(ref _reads) == 1)
                await InsertClaimAsync(cancellationToken);
            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        private async Task InsertClaimAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var id = Guid.NewGuid();
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "claimed",
                Goal = "claim the seat",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = "/work",
                Status = AgentTaskStatus.Dispatched,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
                AgentSessionId = sessionId,
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
