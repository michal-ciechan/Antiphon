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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0679 D-8. A phone-home drop inside a remote launch's Start-to-ready segment used to fail the
// row at once with a live session left on the runner. The segment now retries for a remote session
// only: re-attach after the runner acknowledged the Launch, re-launch before it, bounded by
// LaunchTransportRetries and LaunchReattachWaitSeconds, with the row Starting throughout.
[Category("Integration")]
public class PhoneHomeLaunchTransportTests
{
    private const int ReattachWaitSeconds = 90;

    [Test]
    public async Task Loss_after_the_launch_ack_reattaches_and_ends_Running()
    {
        await using var world = await LaunchWorld.CreateAsync();
        // The Launch is acknowledged; the first ready read is never answered and the socket drops
        // under it.
        world.PeerA.SilentFor(PhoneHomeOperation.Snapshot);

        var launch = world.Launch();
        await world.WaitForRequestOrEndAsync(world.PeerA, PhoneHomeOperation.Snapshot, launch);
        world.PeerA.Socket.Abort();

        await using var peerB = await world.ReconnectAsync(peer => peer.Sessions.Add(new RunnerSessionDto(
            world.SessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: world.Generation)));
        var error = await world.AdvanceUntilEndedAsync(launch);

        var session = await world.ReadSessionAsync();
        session.Status.ShouldBe(SessionStatus.Running,
            $"a post-ack loss re-attaches to the runner's session; the launch failed with: {session.FailureReason} {error}");
        error.ShouldBeNull();
        peerB.RequestCount(PhoneHomeOperation.Get).ShouldBeGreaterThanOrEqualTo(1, "the re-attach reads the runner's session");
        world.PeerA.Launches.Count.ShouldBe(1);
        peerB.Launches.Count.ShouldBe(0, "an acknowledged launch is never launched again");
        world.PeerA.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
        peerB.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
        var warnings = await world.ReadTaskWarningsAsync();
        warnings.Count.ShouldBe(1);
        warnings[0].ShouldContain("post-ack");
        warnings[0].ShouldContain("attempt 1");
        (await world.ReadKillIntentsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Loss_before_the_ack_requeues_the_launch_within_the_bound()
    {
        await using var world = await LaunchWorld.CreateAsync();
        // The Launch frame is written and never answered; the socket drops before the ack.
        world.PeerA.SilentFor(PhoneHomeOperation.Launch);

        var launch = world.Launch();
        await world.WaitForRequestOrEndAsync(world.PeerA, PhoneHomeOperation.Launch, launch);
        world.PeerA.Socket.Abort();

        await using var peerB = await world.ReconnectAsync();
        var error = await world.AdvanceUntilEndedAsync(launch);

        var session = await world.ReadSessionAsync();
        session.Status.ShouldBe(SessionStatus.Running,
            $"a pre-ack loss re-sends the Launch; the launch failed with: {session.FailureReason} {error}");
        error.ShouldBeNull();
        world.PeerA.Launches.Count.ShouldBe(1);
        peerB.Launches.Count.ShouldBe(1, "the lost Launch is sent once to the replacement connection");
        AcceptedStartedAtOf(peerB.Launches[0]).ShouldBe(AcceptedStartedAtOf(world.PeerA.Launches[0]),
            "the retried Launch carries the same generation fence");
        world.PeerA.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
        peerB.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
        var warnings = await world.ReadTaskWarningsAsync();
        warnings.Count.ShouldBe(1);
        warnings[0].ShouldContain("pre-ack");
    }

    [Test]
    public async Task Exhausted_retries_fail_with_a_transport_reason_and_a_deferred_kill()
    {
        await using var world = await LaunchWorld.CreateAsync(launchTransportRetries: 1);
        world.PeerA.SilentFor(PhoneHomeOperation.Snapshot);

        var launch = world.Launch();
        await world.WaitForRequestOrEndAsync(world.PeerA, PhoneHomeOperation.Snapshot, launch);
        world.PeerA.Socket.Abort();

        // No reconnect. Before the wait can have run out the row must still read Starting, so the
        // dead-session reconciler has nothing to act on while the launch waits for the runner.
        await world.WaitForLossRecordedOrEndAsync(launch);
        var statuses = new List<SessionStatus>();
        var steps = ReattachWaitSeconds * 2 - 1;
        for (var step = 0; step < steps && !launch.IsCompleted; step++)
        {
            world.Clock.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(2);
            statuses.Add((await world.ReadSessionAsync()).Status);
        }

        var endedBeforeTheWaitRanOut = launch.IsCompleted;
        var error = await world.AdvanceUntilEndedAsync(launch);

        var session = await world.ReadSessionAsync();
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldNotBeNull();
        session.FailureReason.ShouldContain(world.RunnerId);
        session.FailureReason.ShouldContain("post-ack");
        session.FailureReason.ShouldContain("1 retry");
        session.FailureReason.ShouldContain($"within {ReattachWaitSeconds} s");
        session.FailureReason.ShouldNotContain("A task was canceled.");
        session.RestartFailureKind.ShouldBe(RestartFailureKind.Infrastructure);
        error.ShouldNotBeNull();
        endedBeforeTheWaitRanOut.ShouldBeFalse("the launch waits for the runner before it fails");
        statuses.Count.ShouldBe(steps);
        statuses.ShouldAllBe(s => s == SessionStatus.Starting);
        var intents = await world.ReadKillIntentsAsync();
        intents.Count.ShouldBe(1, "the runner holds the acknowledged session, so its kill is deferred");
        intents[0].ShouldBe($"pending:kill-generation:{world.RunnerId}:{world.Generation.Ticks}");
        world.PeerA.RequestCount(PhoneHomeOperation.KillGeneration).ShouldBe(0);
    }

    [Test]
    public async Task Local_runner_launch_failures_are_unchanged()
    {
        var local = new TransportLosingLocalClient();
        await using var world = await LaunchWorld.CreateAsync(localClient: local, remote: false);

        var launch = world.Launch();
        // The fake clock is never advanced: a local launch must not wait for any runner.
        Exception? error = null;
        try
        {
            await launch.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            error = ex;
        }

        error.ShouldBeOfType<PhoneHomeTransportException>().Message.ShouldBe(TransportLosingLocalClient.Message);
        local.Starts.ShouldBe(1, "a local launch is never retried");
        var session = await world.ReadSessionAsync();
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldBe(TransportLosingLocalClient.Message);
        (await world.ReadTaskWarningsAsync()).ShouldBeEmpty();
        (await world.ReadKillIntentsAsync()).ShouldBeEmpty();
    }

    private static DateTime AcceptedStartedAtOf(PhoneHomeFrame launch) =>
        launch.Payload!.Value.Deserialize<RunnerLaunchRequest>(PhoneHomeFraming.Json)!.AcceptedStartedAt!.Value.ToUniversalTime();

    private sealed class LaunchWorld : IAsyncDisposable
    {
        private IsolatedTestSchema _schema = null!;
        private PhoneHomeTestHost _host = null!;
        private BridgeQueueHarness _harness = null!;
        private IServiceScope? _scope;

        public PhoneHomeScriptedPeer PeerA { get; private set; } = null!;
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid TaskId { get; } = Guid.NewGuid();
        public DateTime Generation { get; private set; }
        public string RunnerId => _host.AllowedRunnerId;

        public static async Task<LaunchWorld> CreateAsync(
            int? launchTransportRetries = null, ISessionRunnerClient? localClient = null, bool remote = true)
        {
            var world = new LaunchWorld();
            world._schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            world._host = await PhoneHomeTestHost.StartAsync(connectionString: world._schema.ConnectionString);
            world.PeerA = await world._host.ConnectPeerAsync();
            world._host.Directory.MarkRecovered(await world._host.WaitLiveAsync());
            var phoneHome = PhoneHomeSettings(world._host, launchTransportRetries);
            var host = world._host;
            world._harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                ConnectionString = world._schema.ConnectionString,
                TimeProvider = world.Clock,
                ConfigureServices = s =>
                {
                    s.AddSingleton<IAgentProtocolAdapterFactory>(sp => new AgentProtocolAdapterFactory(
                        Options.Create(new AgentRegistrySettings()),
                        localClient ?? sp.GetRequiredService<ISessionRunnerClient>(),
                        directory: host.Directory));
                    s.AddSingleton<ISessionRunnerDirectory>(host.Directory);
                    s.AddSingleton<IOptions<PhoneHomeRunnerSettings>>(Options.Create(phoneHome));
                    s.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
                    {
                        KillGraceMs = 100,
                        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-launch-transport-{Guid.NewGuid():N}"),
                    }));
                },
            });
            await world.SeedAsync(remote);
            return world;
        }

        /// <summary>
        /// Bound from configuration so the red tests compile before the settings exist: an unknown
        /// key is ignored by the binder, which is exactly today's behaviour.
        /// </summary>
        private static PhoneHomeRunnerSettings PhoneHomeSettings(PhoneHomeTestHost host, int? launchTransportRetries)
        {
            var values = new Dictionary<string, string?>
            {
                ["Enabled"] = "true",
                ["AllowedRunnerId"] = host.AllowedRunnerId,
                ["SharedSecret"] = host.Secret,
                ["LaunchReattachWaitSeconds"] = ReattachWaitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            if (launchTransportRetries is { } retries)
                values["LaunchTransportRetries"] = retries.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var settings = new PhoneHomeRunnerSettings();
            new ConfigurationBuilder().AddInMemoryCollection(values).Build().Bind(settings);
            return settings;
        }

        private async Task SeedAsync(bool remote)
        {
            var now = DateTime.UtcNow;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.Raw,
                SessionBackend = SessionBackend.PtyHost,
                Status = SessionStatus.Starting,
                Cwd = _harness.TempRoot,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                RunnerId = remote ? _host.AllowedRunnerId : null,
                RunnerStoreId = remote ? _host.StoreId : null,
                RunnerCwd = remote ? "/work" : null,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = TaskId,
                RootTaskId = TaskId,
                Title = "launch transport loss",
                Goal = "Do the thing.",
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.Raw,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _harness.TempRoot,
                AgentSessionId = SessionId,
                AgentId = _harness.AgentId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = now,
                DispatchedAt = now,
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == _harness.AgentId).ExecuteUpdateAsync(u => u
                .SetProperty(a => a.Status, AgentStatus.Running)
                .SetProperty(a => a.PersistentSessionId, SessionId.ToString("D")));
            var startedAt = (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId)).StartedAt;
            Generation = new DateTime(startedAt.Ticks - startedAt.Ticks % 10, DateTimeKind.Utc);
        }

        public Task Launch()
        {
            _scope = _harness.Provider.CreateScope();
            return _scope.ServiceProvider.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
                SessionId, _harness.AgentId,
                new AgentLaunchSpec("fake", AgentKind.Raw, "fake", [], new Dictionary<string, string>(), _harness.TempRoot, 120, 30),
                remoteControlName: null, resume: false, notes: null, CancellationToken.None);
        }

        public async Task WaitForRequestOrEndAsync(PhoneHomeScriptedPeer peer, PhoneHomeOperation operation, Task launch)
        {
            var seen = peer.WaitForAsync(operation, TimeSpan.FromSeconds(10));
            if (await Task.WhenAny(seen, launch) == launch)
                throw new InvalidOperationException(
                    $"The launch ended before its {operation} request: {launch.Exception?.GetBaseException()}");
            await seen;
        }

        /// <summary>The launch recorded its transport loss (task event) or has already ended.</summary>
        public async Task WaitForLossRecordedOrEndAsync(Task launch)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!launch.IsCompleted && (await ReadTaskWarningsAsync()).Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);
        }

        public async Task<PhoneHomeScriptedPeer> ReconnectAsync(Action<PhoneHomeScriptedPeer>? script = null)
        {
            var liveA = _host.Directory.SnapshotLive();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (liveA is { SocketOpen: true } && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            var peer = await _host.ConnectPeerAsync();
            script?.Invoke(peer);
            deadline = DateTime.UtcNow.AddSeconds(2);
            while (ReferenceEquals(_host.Directory.SnapshotLive(), liveA) && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            var liveB = _host.Directory.SnapshotLive();
            liveB.ShouldNotBeNull();
            liveB.ShouldNotBeSameAs(liveA);
            peer.Epoch = liveB.Epoch;
            _host.Directory.MarkRecovered(liveB);
            return peer;
        }

        /// <summary>
        /// Moves the service's clock in eligibility-poll steps until the launch ends; returns the
        /// launch's exception, if any. The real-time bound is the only wall-clock wait.
        /// </summary>
        public async Task<Exception?> AdvanceUntilEndedAsync(Task launch)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!launch.IsCompleted && DateTime.UtcNow < deadline)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(500));
                await Task.Delay(20);
            }

            launch.IsCompleted.ShouldBeTrue("the launch ended within the real-time bound");
            try
            {
                await launch;
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public async Task<AgentSession> ReadSessionAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
            return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
        }

        public async Task<List<string>> ReadTaskWarningsAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == TaskId && e.Type == AgentTaskEventType.Warning)
                .OrderBy(e => e.At)
                .Select(e => e.Detail ?? "")
                .ToListAsync();
        }

        public async Task<List<string>> ReadKillIntentsAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
            return await db.AgentIncidents.AsNoTracking()
                .Where(i => i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent && i.SessionId == SessionId)
                .Select(i => i.FailureReason ?? "")
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _scope?.Dispose();
            if (_harness is not null)
                await _harness.DisposeAsync();
            if (PeerA is not null)
                await PeerA.DisposeAsync();
            if (_host is not null)
                await _host.DisposeAsync();
            if (_schema is not null)
                await _schema.DisposeAsync();
        }
    }

    /// <summary>
    /// A local runner whose Start fails with the transport-loss error a phone-home launch retries
    /// on. A local session must not be retried or wait for any runner: the failure is today's.
    /// </summary>
    private sealed class TransportLosingLocalClient : ISessionRunnerClient
    {
        public const string Message = "local runner transport lost during launch";
        private int _starts;

        public int Starts => Volatile.Read(ref _starts);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        {
            Interlocked.Increment(ref _starts);
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.ConnectionClosedInFlight, Message);
        }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
