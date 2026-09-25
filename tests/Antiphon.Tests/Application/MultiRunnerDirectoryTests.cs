using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-6. Two configured runners keep separate secrets, tickets, stores and inventory.
/// A legacy singleton settings block still admits exactly one runner.
/// </summary>
[Category("Integration")]
public sealed class MultiRunnerDirectoryTests
{
    [Test]
    public async Task Two_connections_keep_distinct_owners()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;

        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", storeId: storeB, secret: secretB);
        var liveA = host.Directory.SnapshotLive("runner-a");
        var liveB = host.Directory.SnapshotLive("runner-b");
        liveA.ShouldNotBeNull();
        liveB.ShouldNotBeNull();
        liveA.RunnerStoreId.ShouldBe(storeA);
        liveB.RunnerStoreId.ShouldBe(storeB);
        liveA.ShouldNotBeSameAs(liveB);
        host.Directory.GetLiveStoreId("runner-a").ShouldBe(storeA);
        host.Directory.GetLiveStoreId("runner-b").ShouldBe(storeB);
        host.Directory.Status("runner-a").RunnerStoreId.ShouldBe(storeA);
        host.Directory.Status("runner-b").RunnerStoreId.ShouldBe(storeB);
        peerA.Epoch.ShouldBeGreaterThan(0);
        peerB.Epoch.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Secret_and_ticket_cannot_cross_runners()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));

        using var crossed = await PostRegister(host, "runner-b", secretA, Guid.NewGuid(), "linux");
        crossed.IsSuccessStatusCode.ShouldBeFalse("runner B rejects runner A's secret");

        using var registered = await PostRegister(host, "runner-a", secretA, Guid.NewGuid(), "linux");
        registered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ticket = await registered.Content.ReadFromJsonAsync<PhoneHomeRegistrationResponse>(PhoneHomeFraming.Json);
        ticket.ShouldNotBeNull();
        var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        var refused = Should.Throw<ConflictException>(() => host.Directory.AcceptConnect("runner-b", ticket.Ticket, socket));
        refused.Code.ShouldBe(PhoneHomeProblemTypes.InvalidTicket);
        host.Directory.SnapshotLive("runner-b").ShouldBeNull();
    }

    [Test]
    public async Task Replacement_and_lease_expiry_affect_only_owner()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", storeId: storeB, secret: secretB);
        var firstA = host.Directory.SnapshotLive("runner-a");
        var firstB = host.Directory.SnapshotLive("runner-b");
        firstA.ShouldNotBeNull();
        firstB.ShouldNotBeNull();

        peerA.Socket.Abort();
        await using var replacement = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        var nextA = host.Directory.SnapshotLive("runner-a");
        nextA.ShouldNotBeNull();
        nextA.ShouldNotBeSameAs(firstA);
        host.Directory.SnapshotLive("runner-b").ShouldBeSameAs(firstB);
        host.Directory.GetLiveStoreId("runner-b").ShouldBe(storeB);
        replacement.Epoch.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Inventory_status_capacity_and_store_are_keyed()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        var secretB = "secret-b-" + Guid.NewGuid().ToString("N");
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Pair(secretA, secretB));
        host.Capacity = 2;
        await using var peerA = await host.ConnectPeerAsync(runnerId: "runner-a", storeId: storeA, secret: secretA);
        await using var peerB = await host.ConnectPeerAsync(runnerId: "runner-b", storeId: storeB, secret: secretB);
        var liveA = host.Directory.SnapshotLive("runner-a")!;
        var liveB = host.Directory.SnapshotLive("runner-b")!;
        host.Directory.MarkRecovered(liveA);
        host.Directory.MarkRecovered(liveB);
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        peerA.Sessions.Add(new RunnerSessionDto(sessionA, 1, DateTime.UtcNow, "Running", null, "", 0));
        peerB.Sessions.Add(new RunnerSessionDto(sessionB, 1, DateTime.UtcNow, "Running", null, "", 0));

        var inventoryA = await host.Directory.GetInventoryAsync("runner-a", CancellationToken.None);
        var inventoryB = await host.Directory.GetInventoryAsync("runner-b", CancellationToken.None);
        var availableA = inventoryA.ShouldBeOfType<RunnerInventory.Available>();
        var availableB = inventoryB.ShouldBeOfType<RunnerInventory.Available>();
        availableA.Sessions.Select(s => s.SessionId).ShouldBe([sessionA]);
        availableB.Sessions.Select(s => s.SessionId).ShouldBe([sessionB]);
        host.Directory.DeclaredCapacity("runner-a").ShouldBe(2);
        host.Directory.DeclaredCapacity("runner-b").ShouldBe(2);
        host.Directory.GetLiveStoreId("runner-a").ShouldBe(storeA);
        host.Directory.GetLiveStoreId("runner-b").ShouldBe(storeB);
        Should.Throw<NotFoundException>(() => host.Directory.Status("runner-c"));
        (await host.Directory.GetInventoryAsync("runner-c", CancellationToken.None))
            .ShouldBeOfType<RunnerInventory.Unavailable>();
    }

    [Test]
    public async Task Legacy_settings_normalize_once()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        host.Directory.KnownRunnerIds.ShouldContain(host.AllowedRunnerId);
        host.Directory.KnownRunnerIds.Count(id => !RunnerRequestIntent.IsDesktopAlias(id)).ShouldBe(1);

        using var ok = await PostRegister(host, host.AllowedRunnerId, host.Secret, host.StoreId, "linux");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var foreign = await PostRegister(host, "someone-else", host.Secret, Guid.NewGuid(), "linux");
        foreign.IsSuccessStatusCode.ShouldBeFalse();
        host.Directory.KnownRunnerIds.Count(id => !RunnerRequestIntent.IsDesktopAlias(id)).ShouldBe(1);
    }

    [Test]
    public void Invalid_ids_and_duplicate_pins_are_rejected()
    {
        var pin = Guid.NewGuid();
        var settings = new PhoneHomeRunnerSettings
        {
            Enabled = true,
            Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["desktop"] = Entry("desktop", "s", pin),
                ["runner-a"] = Entry("runner-a", "a", pin),
                ["runner-b"] = Entry("runner-b", "b", pin),
            },
        };

        var failures = PhoneHomeRunnerSettingsRules.Validate(settings);
        failures.ShouldContain(f => f.Contains("desktop", StringComparison.OrdinalIgnoreCase));
        failures.ShouldContain(f => f.Contains("standing agent", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public void Registration_disagreement_is_refused()
    {
        var directory = new PhoneHomeRunnerDirectory(
            new PhoneHomeTestHost.RecordingLocalClient(),
            Options.Create(Pair("secret-a", "secret-b")),
            new NoScope(),
            TimeProvider.System);
        var disagreed = new RunnerCapabilitiesDto(
            "InboxConhost", "inbox", "test", false, Platform: "windows", Features: [RunnerPlatformWire.Feature]);
        var refused = Should.Throw<ConflictException>(() => directory.Register(new PhoneHomeRegistrationRequest(
            PhoneHomeProtocol.Version, "runner-a", Guid.NewGuid(), Guid.NewGuid(), "linux", 2, disagreed)));
        refused.Code.ShouldBe(RunnerPlatformProblems.Conflict);
        directory.GetLiveStoreId("runner-a").ShouldBeNull();
    }

    private static async Task<HttpResponseMessage> PostRegister(
        PhoneHomeTestHost host, string runnerId, string secret, Guid storeId, string platform)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PhoneHomeProtocol.RegisterPath);
        request.Headers.TryAddWithoutValidation(PhoneHomeProtocol.SecretHeader, secret);
        request.Content = JsonContent.Create(new PhoneHomeRegistrationRequest(
            PhoneHomeProtocol.Version,
            runnerId,
            Guid.NewGuid(),
            storeId,
            platform,
            2,
            new RunnerCapabilitiesDto(
                "InboxConhost", "inbox", "test", false,
                Features: [RunnerPlatformWire.Feature],
                Platform: platform)), options: PhoneHomeFraming.Json);
        return await host.Http.SendAsync(request);
    }

    private static PhoneHomeRunnerSettings Pair(string secretA, string secretB) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = Entry("Runner A", secretA, Guid.Empty),
            ["runner-b"] = Entry("Runner B", secretB, Guid.Empty),
        },
    };

    private static PhoneHomeRunnerEntry Entry(string display, string secret, Guid pin) => new()
    {
        Enabled = true,
        DisplayName = display,
        AllowDelegatedTasks = pin == Guid.Empty,
        StandingAgentId = pin,
        HostWorkspaceRoot = @"C:\work",
        RunnerWorkspace = "/work",
        RunnerRepository = "/work/repos/antiphon",
        CallbackOrigin = "https://antiphon.test",
        SharedSecret = secret,
        MaxCapacity = 4,
        ChildGrokHome = "/state/grok",
        ChildClaudeHome = "/state/claude",
        ChildCodexHome = "/state/codex",
    };

    private sealed class NoScope : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("registration takes no scope");
    }
}
