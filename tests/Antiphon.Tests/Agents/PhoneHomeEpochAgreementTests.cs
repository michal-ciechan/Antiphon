using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0604 CP-6a. The phone-home epoch is stamped on every frame and BOTH receive loops discard
/// a frame whose epoch does not match their own. The server used to mint its epoch at
/// <c>AcceptConnect</c> while the runner numbered its own successful connects, so the two counters
/// agreed only when each process happened to have made the same number of connections since its
/// own boot - a server restart or a runner restart alone desynchronised them. Nothing then logs an
/// error: the socket opens and stays open while every heartbeat, request and reply is dropped in
/// silence, the lease decays to `available:false`, and `dispatchEligible` never turns true.
///
/// The existing phone-home tests could not see this because their scripted peer takes its epoch
/// from <c>live.Epoch</c> - an in-process back door the real runner does not have. These tests
/// drive the REAL <see cref="PhoneHomeConnectionService"/> over a real socket, against a server
/// whose counter has deliberately been advanced past a fresh runner's.
/// </summary>
[Category("Integration")]
public class PhoneHomeEpochAgreementTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Registration_advertises_the_epoch_the_accepted_connection_runs_at(CancellationToken ct)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);

        // Burn epochs the way a reconnecting runner does, so the server's counter is no longer 1.
        await host.RegisterAsync();
        await host.RegisterAsync();
        var third = await host.RegisterAsync();

        third.Epoch.ShouldBe(3, "the server mints the epoch at registration, so the third one is 3");

        using var socket = new System.Net.WebSockets.ClientWebSocket();
        socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, third.Ticket);
        await socket.ConnectAsync(host.ConnectUri, ct);
        var live = await host.WaitLiveAsync();

        live.Epoch.ShouldBe(third.Epoch, "the connection must run at the epoch its ticket advertised");
    }

    /// <summary>
    /// The end-to-end guard: a real runner, whose own connect counter is 1, against a server whose
    /// counter is already past 1. Before the fix the runner stamped 1, the server dropped every
    /// frame, and LastHeartbeatUtc stayed frozen at the accept instant forever.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    public async Task A_real_runner_heartbeat_lands_when_the_server_counter_is_ahead(CancellationToken ct)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(clock);

        var root = Path.Combine(Path.GetTempPath(), "c604-epoch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // The runner's store id is bound by the first registration, so the warm-up registrations
            // must carry the same one - otherwise the runner is refused with phone_home_store_mismatch
            // and never reaches the epoch question at all.
            var storeIdPath = Path.Combine(root, "runner-store-id");
            var storeId = Guid.NewGuid();
            await File.WriteAllTextAsync(storeIdPath, storeId.ToString("D"), ct);
            var secretPath = Path.Combine(root, "secret");
            await File.WriteAllTextAsync(secretPath, host.Secret, ct);

            // Advance the server's epoch past 1 under ONE warm-up boot id (a second boot id would be
            // refused as a competing boot while the first lease is unexpired).
            var warmBoot = Guid.NewGuid();
            await host.RegisterAsync(bootId: warmBoot, storeId: storeId);
            await host.RegisterAsync(bootId: warmBoot, storeId: storeId);
            await host.RegisterAsync(bootId: warmBoot, storeId: storeId);

            // Let the warm-up lease expire so the runner's own boot id is allowed to take over.
            clock.Advance(TimeSpan.FromSeconds(180));

            var settings = Options.Create(new PhoneHomeSettings
            {
                Enabled = true,
                RunnerId = host.AllowedRunnerId,
                ServerOrigin = host.Http.BaseAddress!.ToString(),
                SecretPath = secretPath,
                StoreIdPath = storeIdPath,
                AllowedCwd = "/work",
                Capacity = 1,
                HeartbeatSeconds = 1,
            });
            var runtime = new SessionRunnerRuntime(
                Options.Create(new SessionRunnerSettings { SessionLogPath = Path.Combine(root, "sessions") }),
                NullLogger<SessionRunnerRuntime>.Instance);
            var gate = new PhoneHomeAdoptionGate();
            var service = new PhoneHomeConnectionService(
                settings,
                gate,
                new PhoneHomeCommandDispatcher(
                    new PhoneHomeRuntimeAdapter(
                        runtime,
                        new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow)),
                    settings.Value),
                runtime,
                new DefaultHttpClientFactory(),
                TimeProvider.System,
                NullLogger<PhoneHomeConnectionService>.Instance);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var running = service.StartAsync(cts.Token);
            gate.SignalReady();
            try
            {
                var live = await host.WaitLiveAsync(TimeSpan.FromSeconds(30));

                // Non-vacuity: if the two counters happened to agree, this test would prove nothing.
                live.Epoch.ShouldBeGreaterThan(
                    1, "the scenario only bites when the server's counter is ahead of a fresh runner's");

                var atAccept = live.LastHeartbeatUtc;
                clock.Advance(TimeSpan.FromSeconds(5));

                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline && live.LastHeartbeatUtc == atAccept)
                    await Task.Delay(100, ct);

                live.LastHeartbeatUtc.ShouldBeGreaterThan(
                    atAccept,
                    "the runner must stamp the epoch the server advertised, or the server silently "
                    + "discards every heartbeat and the connection decays while looking connected");
                live.IsLeaseExpired(TimeSpan.FromSeconds(90)).ShouldBeFalse();
            }
            finally
            {
                cts.Cancel();
                try { await running; } catch (OperationCanceledException) { /* expected on shutdown */ }
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
