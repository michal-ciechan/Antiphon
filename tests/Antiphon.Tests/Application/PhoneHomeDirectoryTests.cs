using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-14. The runner declares a capacity and the server bounds it, so a misconfigured or
// tampered runner cannot enlarge its own seat count; and registration carries the capabilities DTO
// so a deploy row can read the runner's platform and build without a second round trip.
[Category("Unit")]
public sealed class PhoneHomeDirectoryTests
{
    [Test]
    public void Capacity_above_bound_is_refused()
    {
        var directory = Directory(maxCapacity: 2);

        var refused = Should.Throw<ConflictException>(() => directory.Register(Registration(capacity: 3)));
        refused.Code.ShouldBe(PhoneHomeProblemTypes.Capacity);

        var zero = Should.Throw<ConflictException>(() => directory.Register(Registration(capacity: 0)));
        zero.Code.ShouldBe(PhoneHomeProblemTypes.Capacity);
    }

    [Test]
    public void Capacity_within_bound_is_admitted()
    {
        // The whole point of D-14: two is now a legal seat count, where CARD-0490 allowed only one.
        var response = Directory(maxCapacity: 8).Register(Registration(capacity: 2));
        response.Ticket.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Registration_carries_capabilities()
    {
        var directory = Directory(maxCapacity: 8);
        var capabilities = new RunnerCapabilitiesDto(
            "PortaPty", "PortaPty", "linux", false,
            Version: "cafebabe",
            VerificationCustodyBackend: null,
            RunnerStoreId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var response = directory.Register(Registration(capacity: 2, platform: "linux", capabilities: capabilities));

        // The registration's own store identity is what the ticket and the status are pinned to,
        // and it survives the capabilities payload rather than being replaced by it.
        response.RunnerStoreId.ShouldBe(StoreId);
        var status = directory.Status("server2");
        status.RunnerStoreId.ShouldBe(StoreId);
        status.Available.ShouldBeFalse("no socket has connected yet");
    }

    [Test]
    public void Foreign_runner_id_is_refused()
    {
        var refused = Should.Throw<ConflictException>(
            () => Directory(maxCapacity: 8).Register(Registration(capacity: 1) with { RunnerId = "someone-else" }));
        refused.Code.ShouldBe(PhoneHomeProblemTypes.RunnerMismatch);
    }

    [Test]
    public async Task Provider_auth_endpoint_routes_to_the_live_runner_and_409s_when_absent()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var path = $"/api/session-runners/{host.AllowedRunnerId}/provider-auth/claude";
        using (var absent = await host.Http.GetAsync(path))
            absent.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var local = await host.Http.GetAsync("/api/session-runners/local/provider-auth/claude"))
            local.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        peer.Reply = request => request.Operation == PhoneHomeOperation.ProviderAuth
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId,
                request.Operation, JsonSerializer.SerializeToElement(
                    new RunnerProviderAuthDto("claude", true, "claude.ai", "max", DateTimeOffset.UtcNow, null),
                    PhoneHomeFraming.Json))
            : null;

        using var response = await host.Http.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<RunnerProviderAuthDto>(PhoneHomeFraming.Json);
        auth.ShouldNotBeNull();
        auth.Provider.ShouldBe("claude");
        auth.LoggedIn.ShouldBe(true);
        auth.SubscriptionType.ShouldBe("max");
        (await peer.WaitForAsync(PhoneHomeOperation.ProviderAuth)).Payload!.Value.GetProperty("provider")
            .GetString().ShouldBe("claude");
    }

    // CARD-0679 D-6: a remote adapter held the one PhoneHomeRunnerClient that Resolve returned when
    // it was created, so after a reconnect every call (including the clean-up kill) went to the
    // dead connection and failed its dispatch gate.
    [Test]
    public async Task Remote_adapter_reaches_the_replacement_connection_after_a_reconnect()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        var sessionId = Guid.NewGuid();
        var generation = new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);
        var held = new RunnerSessionDto(sessionId, 4242, generation, "Running", null, "", 0, AcceptedStartedAt: generation);
        await using var peerA = await host.ConnectPeerAsync();
        peerA.Sessions.Add(held);
        var liveA = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(liveA);
        var factory = new AgentProtocolAdapterFactory(
            Options.Create(new AgentRegistrySettings()), host.Local, directory: host.Directory);
        var adapter = factory.Create(AgentKind.Raw, host.AllowedRunnerId);
        await ((IAttachableProtocolAdapter)adapter).AttachAsync(sessionId, CancellationToken.None);

        peerA.Socket.Abort();
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while ((liveA.SocketOpen || ReferenceEquals(host.Directory.SnapshotLive(), liveA)) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await using var peerB = await host.ConnectPeerAsync();
        peerB.Sessions.Add(held);
        var liveB = await host.WaitLiveAsync();
        liveB.ShouldNotBeSameAs(liveA);
        host.Directory.MarkRecovered(liveB);

        Exception? thrown = null;
        try
        {
            await adapter.KillGenerationAsync(generation, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        peerB.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(1, $"the kill threw {thrown}");
        thrown.ShouldBeNull();
    }

    // CARD-0679 D-6 (card ask 2): between the socket closing and the connect route recording the
    // end, Resolve handed out a client bound to a closed socket because it checked only the flag.
    [Test]
    public void Resolve_refuses_a_recovered_connection_whose_socket_is_closed()
    {
        var directory = Directory(maxCapacity: 8);
        var ticket = directory.Register(Registration(capacity: 1));
        var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        var live = directory.AcceptConnect("server2", ticket.Ticket, socket);
        directory.MarkRecovered(live);
        directory.Resolve("server2").ShouldNotBeNull();

        socket.Abort();
        live.SocketOpen.ShouldBeFalse();
        live.IsLeaseExpired(TimeSpan.FromSeconds(90)).ShouldBeFalse("the lease is still fresh");

        Exception? thrown = null;
        try
        {
            directory.Resolve("server2");
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var refused = thrown.ShouldBeOfType<ServiceUnavailableException>();
        refused.Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
    }

    private static readonly Guid StoreId =Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static PhoneHomeRegistrationRequest Registration(
        int capacity, string platform = "linux", RunnerCapabilitiesDto? capabilities = null) =>
        new(
            PhoneHomeProtocol.Version,
            "server2",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            StoreId,
            platform,
            capacity,
            capabilities);

    private static PhoneHomeRunnerDirectory Directory(int maxCapacity) =>
        new(
            new RefusingSessionRunnerClient(),
            Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true,
                AllowedRunnerId = "server2",
                AllowDelegatedTasks = true,
                MaxCapacity = maxCapacity,
                HostWorkspaceRoot = @"C:\src\Antiphon",
                CallbackOrigin = "https://antiphon.desktop.codeperf.net",
                SharedSecret = "x",
            }),
            new NoScopeFactory(),
            TimeProvider.System);

    // Registration never opens a scope: it is a pure in-memory admission decision.
    private sealed class NoScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("registration takes no scope");
    }
}
