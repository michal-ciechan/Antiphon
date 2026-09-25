using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-7. Recovery, inventory and disconnect stay on the runner that owns them.
/// </summary>
[Category("Integration")]
public sealed class MultiRunnerRecoveryTests
{
    [Test]
    public async Task Slow_A_does_not_block_B_recovery()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        var pump = Pump(host);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.HoldCatchUp("runner-a", hold);
        var cycle = pump.RunCycleAsync(CancellationToken.None);

        var ready = await WaitUntilAsync(() => host.Directory.SnapshotLive("runner-b") is { DispatchEligible: true });
        ready.ShouldBeTrue("runner B becomes dispatch-eligible while runner A is still held");
        host.Directory.SnapshotLive("runner-a")!.DispatchEligible.ShouldBeFalse();
        hold.TrySetResult();
        (await cycle).ShouldBeTrue();
        host.Directory.SnapshotLive("runner-a")!.DispatchEligible.ShouldBeTrue();
        peerA.Epoch.ShouldBeGreaterThan(0);
        peerB.Epoch.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Disconnect_A_preserves_B_events()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        var pump = Pump(host);
        (await pump.RunCycleAsync(CancellationToken.None)).ShouldBeTrue();
        var liveB = host.Directory.SnapshotLive("runner-b");
        liveB.ShouldNotBeNull();
        peerA.Socket.Abort();
        await WaitUntilAsync(() => host.Directory.SnapshotLive("runner-a") is null || !host.Directory.SnapshotLive("runner-a")!.SocketOpen);
        host.Directory.SnapshotLive("runner-b").ShouldBeSameAs(liveB);
        liveB.DispatchEligible.ShouldBeTrue();
        var before = peerB.RequestCount(PhoneHomeOperation.List);
        await host.Directory.GetInventoryAsync("runner-b", CancellationToken.None);
        peerB.RequestCount(PhoneHomeOperation.List).ShouldBeGreaterThan(before);
        peerA.RequestCount(PhoneHomeOperation.List).ShouldBeLessThanOrEqualTo(peerB.RequestCount(PhoneHomeOperation.List));
    }

    [Test]
    public async Task Pending_inventory_and_failures_are_per_runner()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString, configured: Pair(secretA, secretB));
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(Session(sessionA, "runner-a", now));
            db.AgentSessions.Add(Session(sessionB, "runner-b", now));
            await db.SaveChangesAsync();
        }

        var inventory = new PendingRunnerSessionInventory(
            host.App.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger.Instance);
        inventory.Read("runner-a").ShouldBe([sessionA]);
        inventory.Read("runner-b").ShouldBe([sessionB]);
    }

    [Test]
    public async Task Reconnect_replays_only_owned_transcripts()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        peerA.Sessions.Add(new RunnerSessionDto(sessionA, 1, DateTime.UtcNow, "Running", null, "", 0));
        peerB.Sessions.Add(new RunnerSessionDto(sessionB, 1, DateTime.UtcNow, "Running", null, "", 0));
        var pump = Pump(host);
        (await pump.RunCycleAsync(CancellationToken.None)).ShouldBeTrue();
        peerA.Socket.Abort();
        await using var nextA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        nextA.Sessions.Add(new RunnerSessionDto(sessionA, 1, DateTime.UtcNow, "Running", null, "", 0));
        (await pump.RunCycleAsync(CancellationToken.None)).ShouldBeTrue();
        nextA.RequestCount(PhoneHomeOperation.Transcript).ShouldBeGreaterThan(0);
        var foreign = nextA.Incoming.Any(frame =>
            frame.Operation == PhoneHomeOperation.Transcript
            && frame.Payload?.ToString().Contains(sessionB.ToString(), StringComparison.Ordinal) == true);
        foreign.ShouldBeFalse("runner A's reconnect does not ask for runner B's session");
        peerB.Incoming.Any(frame =>
            frame.Operation == PhoneHomeOperation.Transcript
            && frame.Payload?.ToString().Contains(sessionA.ToString(), StringComparison.Ordinal) == true)
            .ShouldBeFalse();
    }

    [Test]
    public async Task Deferred_kill_and_slot_release_stay_on_owner()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-a"));
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-b"));
        var session = Guid.NewGuid();
        await host.Directory.Resolve("runner-a").KillAsync(session, CancellationToken.None);
        peerA.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBeGreaterThan(0);
        peerB.RequestCount(PhoneHomeOperation.ReleaseSlot).ShouldBe(0);
    }

    [Test]
    public async Task Queued_delivery_survives_one_runner_restart()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA, autoReply: false);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", secret: secretB);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-b"));
        var prompt = "CARD-0710 unique prompt " + Guid.NewGuid().ToString("N");
        var sessionB = Guid.NewGuid();
        await host.Directory.Resolve("runner-b").SendInputAsync(sessionB, prompt, CancellationToken.None);
        peerB.Inputs.Any(frame => frame.Payload?.ToString().Contains(prompt, StringComparison.Ordinal) == true).ShouldBeTrue();
        peerA.Inputs.ShouldBeEmpty();
        peerA.Socket.Abort();
        await using var nextA = await host.ConnectPeerAsync(runnerId: "runner-a", secret: secretA);
        host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: "runner-a"));
        nextA.Inputs.Any(frame => frame.Payload?.ToString().Contains(prompt, StringComparison.Ordinal) == true).ShouldBeFalse();
        peerB.Inputs.Count(frame => frame.Payload?.ToString().Contains(prompt, StringComparison.Ordinal) == true).ShouldBe(1);
    }

    [Test]
    public async Task Removed_runner_keeps_bound_sessions_unknown()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString, configured: Pair(secretA, "secret-b"));
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(Session(sessionId, "runner-a", now));
            await db.SaveChangesAsync();
        }

        var absent = new PhoneHomeRunnerDirectory(
            new PhoneHomeTestHost.RecordingLocalClient(),
            Options.Create(new PhoneHomeRunnerSettings { Enabled = true, Runners = [] }),
            host.App.Services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
        (await absent.GetInventoryAsync("runner-a", CancellationToken.None))
            .ShouldBeOfType<RunnerInventory.Unavailable>();
        await using var dbAfter = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await dbAfter.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        session.RunnerId.ShouldBe("runner-a");
    }

    private static PhoneHomeRecoveryPump Pump(PhoneHomeTestHost host) => new(
        host.Directory,
        Options.Create(new PhoneHomeRunnerSettings { Enabled = true, CatchUpRetrySeconds = 30 }),
        host.App.Services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<PhoneHomeRecoveryPump>.Instance);

    private static async Task<bool> WaitUntilAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (ready())
                return true;
            await Task.Delay(20);
        }

        return ready();
    }

    private static AgentSession Session(Guid id, string runnerId, DateTime now) => new()
    {
        Id = id,
        DefinitionName = "grok",
        AgentKind = AgentKind.Grok,
        Status = SessionStatus.Running,
        Cwd = "/work",
        Cols = 80,
        Rows = 24,
        CreatedAt = now,
        StartedAt = now,
        LastSeenAt = now,
        RunnerId = runnerId,
    };

    private static PhoneHomeRunnerSettings Pair(string secretA, string secretB) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = Entry("runner-a", secretA, "/work/a"),
            ["runner-b"] = Entry("runner-b", secretB, "/work/b"),
        },
    };

    private static PhoneHomeRunnerEntry Entry(string name, string secret, string workspace) => new()
    {
        Enabled = true,
        DisplayName = name,
        AllowDelegatedTasks = true,
        HostWorkspaceRoot = "/work",
        RunnerWorkspace = workspace,
        RunnerRepository = workspace + "/repo",
        CallbackOrigin = "https://" + name + ".test",
        SharedSecret = secret,
        MaxCapacity = 2,
        ChildGrokHome = "/state/" + name + "/grok",
    };
}
