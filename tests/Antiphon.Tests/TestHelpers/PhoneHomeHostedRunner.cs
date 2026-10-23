using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Owns the real sender, hosted recovery pump, Kestrel socket and isolated runtime graph.
/// Only command inventory and the external publication dependency are controlled.</summary>
internal sealed class PhoneHomeHostedRunner : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _connection;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "c1523-" + Guid.NewGuid().ToString("N"));
    private IsolatedTestSchema? _schema;
    public BridgeQueueHarness Bridge { get; private set; } = null!;
    public PhoneHomeTestHost Host { get; private set; } = null!;
    public PhoneHomeLiveConnection Live { get; private set; } = null!;
    public SessionRunnerRuntime Runtime { get; private set; } = null!;
    public PhoneHomeConnectionService Service { get; private set; } = null!;
    public CommandSurface Commands { get; private set; } = null!;
    public HeldPublication Bus { get; } = new();
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public CapturingLoggerProvider Logs { get; } = new();
    private ILoggerFactory? _loggers;
    private Guid? _sourceSession;
    public Guid SessionId => _sourceSession ?? Bridge.SessionId;
    public HeldActivity Activity { get; } = new();
    public TaskCompletionSource RecoveryHold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Connection => _connection!;
    public DateTime Generation { get; private set; }
    public PhoneHomeReceiveControl Completion { get; } = new(PhoneHomeReceiveMode.ObserveOnly);
    public PhoneHomeRecoveryPump Pump { get; private set; } = null!;
    public AppDbContext Db() => Bridge.CreateObserverContext();

    public static async Task<PhoneHomeHostedRunner> CreateAsync(int count = 4, int bytes = 32768, int runnerCount = 8, bool holdCatchup = false, bool observeCompletion = true, bool legacyServer = false)
    {
        var h = new PhoneHomeHostedRunner();
        try
        {
            Directory.CreateDirectory(h._root);
            h._schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            h.Bridge = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = h._schema.ConnectionString,
                AlwaysOn = false,
                ConfigureDbContext = options => options.AddInterceptors(h.Activity),
                ConfigureServices = services =>
                {
                    services.AddSingleton<IEventBus>(h.Bus);
                    services.AddSingleton(Options.Create(new ScheduleSettings()));
                    services.AddSingleton(Options.Create(new DigestSettings { TimeZone = "Europe/London" }));
                    services.AddSingleton<ScheduleFireQueue>();
                    services.AddSingleton<IScheduledCardActions>(new PhoneHomeStrandedQueueTests.UnusedScheduledCardActions());
                    services.AddScoped<ScheduleService>();
                    services.AddScoped<AgentChannelService>();
                    services.AddSingleton<MentionScanner>();
                    services.AddSingleton<MentionRouteDiagnostics>();
                    services.AddSingleton<AgentMentionRouter>();
                },
            });
            h.Bus.HeldSession = h.SessionId;
            h._loggers = LoggerFactory.Create(b => b.AddProvider(h.Logs));
            h.Host = await PhoneHomeTestHost.StartAsync(h.Clock, h._schema.ConnectionString,
                limits: new PhoneHomeLimits(MaxPendingEvents: count, MaxPendingEventBytes: bytes),
                receiveControl: observeCompletion ? h.Completion : null,
                configureRunnerSettings: s => s.InventoryRefreshSeconds = 0,
                configureServices: services => services.AddHostedService<PhoneHomeRecoveryPump>(sp =>
                    h.Pump = new PhoneHomeRecoveryPump(sp.GetRequiredService<PhoneHomeRunnerDirectory>(),
                        sp.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>(),
                        h.Bridge.Provider.GetRequiredService<IServiceScopeFactory>(),
                        h._loggers.CreateLogger<PhoneHomeRecoveryPump>()) { CatchUpHold = holdCatchup ? h.RecoveryHold : null }));
            await using (var db = h.Db())
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
                session.RunnerId = h.Host.AllowedRunnerId;
                session.RunnerStoreId = h.Host.StoreId;
                session.RunnerCwd = "/work";
                h.Generation = session.StartedAt;
                session.StartedAt = h.Generation;
                await db.SaveChangesAsync();
            }
            var settings = new PhoneHomeSettings
            {
                Enabled = true, RunnerId = h.Host.AllowedRunnerId,
                ServerOrigin = h.Host.Http.BaseAddress!.ToString(),
                SecretPath = Path.Combine(h._root, "secret"), StoreIdPath = Path.Combine(h._root, "store"),
                CapacityStatePath = Path.Combine(h._root, "capacity"),
                LaunchGenerationsPath = Path.Combine(h._root, "generations"),
                AllowedCwd = "/work", HeartbeatSeconds = 1,
                Limits = new PhoneHomeLimits(MaxPendingEvents: runnerCount, MaxPendingEventBytes: 65536),
            };
            await File.WriteAllTextAsync(settings.SecretPath, h.Host.Secret);
            await File.WriteAllTextAsync(settings.StoreIdPath, h.Host.StoreId.ToString());
            h.Runtime = new SessionRunnerRuntime(Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings
                { SessionLogPath = Path.Combine(h._root, "sessions") }), NullLogger<SessionRunnerRuntime>.Instance);
            h.Commands = new CommandSurface(new PhoneHomeRuntimeAdapter(h.Runtime,
                new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow)));
            h.Commands.Sessions.Add(new RunnerSessionDto(h.SessionId, 1, h.Generation, "Running", null, "", 0,
                AcceptedStartedAt: h.Generation));
            h.Service = new PhoneHomeConnectionService(Options.Create(settings), new PhoneHomeAdoptionGate(),
                new PhoneHomeCommandDispatcher(h.Commands, settings), h.Runtime, new HttpFactory(legacyServer),
                TimeProvider.System, h._loggers.CreateLogger<PhoneHomeConnectionService>());
            h._connection = h.Service.RunConnectionAsync(h._stop.Token);
            h.Live = await h.Host.WaitLiveAsync(TimeSpan.FromSeconds(10));
            if (!holdCatchup) await UntilAsync(() => h.Live.DispatchEligible, "hosted catch-up establishes eligibility");
            return h;
        }
        catch { await h.DisposeAsync(); throw; }
    }

    public void Output(Guid session, long sequence, string text) =>
        Runtime.EventHubForTest.Publish(SessionRunnerEventNames.SessionOutput, new RunnerOutputEvent(session, sequence, text));
    public void Transcript(Guid session, long sequence, string uuid, string text, string kind = TranscriptKinds.UserPrompt)
    {
        var entry = new RunnerTranscriptEvent(session, sequence, kind, uuid, null, DateTimeOffset.UtcNow,
            "user", text, null, null, null, null, null);
        Commands.Transcripts.AddOrUpdate(session, new RunnerTranscriptDto(session, [entry], sequence),
            (_, old) => new RunnerTranscriptDto(session, [.. old.Entries, entry], sequence));
        Runtime.EventHubForTest.Publish(SessionRunnerEventNames.SessionTranscript, entry);
    }
    public void Exit(Guid session, long sequence, DateTime generation) =>
        Runtime.EventHubForTest.Publish(SessionRunnerEventNames.SessionExited,
            new RunnerSessionExitedEvent(session, 0, "Normal", sequence, generation));

    public async Task PrepareMentionAsync()
    {
        var (card, _) = await PhoneHomeStrandedQueueTests.ClaimCardAsync(Bridge, Bridge.ConnectionString);
        _sourceSession = await PhoneHomeStrandedQueueTests.InsertMentionSourceAsync(Bridge.ConnectionString, card);
        await using var db = Db();
        var source = await db.AgentSessions.SingleAsync(s => s.Id == SessionId);
        source.RunnerId = Host.AllowedRunnerId;
        source.RunnerStoreId = Host.StoreId;
        source.RunnerCwd = "/work";
        Generation = source.StartedAt;
        await db.SaveChangesAsync();
        Bus.HeldSession = SessionId;
        Commands.Sessions.Clear();
        Commands.Sessions.Add(new RunnerSessionDto(SessionId, 1, Generation, "Running", null, "", 0, AcceptedStartedAt: Generation));
        await Bridge.MarkWorkingAsync();
    }

    public async Task ReconnectAsync()
    {
        try { await _connection!; } catch (PhoneHomeTransportException) { }
        _connection = Service.RunConnectionAsync(_stop.Token);
        await UntilAsync(() => Host.Directory.SnapshotLive() is { } next && !ReferenceEquals(next, Live), "replacement connection accepted");
        Live = Host.Directory.SnapshotLive()!;
        await UntilAsync(() => Live.DispatchEligible, "replacement becomes eligible after catch-up");
    }

    public async Task<Guid> AddSessionAsync()
    {
        await using var db = Db();
        var session = new AgentSession { Id = Guid.NewGuid(), DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, Cwd = "/work", Cols = 80, Rows = 24, CreatedAt = DateTime.UtcNow,
            StartedAt = Generation, LastSeenAt = DateTime.UtcNow, RunnerId = Host.AllowedRunnerId,
            RunnerStoreId = Host.StoreId, RunnerCwd = "/work" };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    public static async Task UntilAsync(Func<bool> condition, string because)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition() && !timeout.IsCancellationRequested)
            try { await Task.Delay(10, timeout.Token); } catch (OperationCanceledException) { }
        condition().ShouldBeTrue(because);
    }
    public static async Task UntilAsync(Func<Task<bool>> condition, string because)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition() && !timeout.IsCancellationRequested)
            try { await Task.Delay(10, timeout.Token); } catch (OperationCanceledException) { }
        (await condition()).ShouldBeTrue(because);
    }

    public async ValueTask DisposeAsync()
    {
        Bus.Release.TrySetResult();
        Activity.Release.TrySetResult();
        RecoveryHold.TrySetResult();
        Commands?.ReleaseList.Set();
        _stop.Cancel();
        if (_connection is not null)
            try { await _connection.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            catch (PhoneHomeTransportException) { }
        if (Host is not null) await Host.DisposeAsync();
        if (Pump is not null) await Pump.EndPumpForTest();
        if (Bridge is not null) await Bridge.DisposeAsync();
        if (_schema is not null) await _schema.DisposeAsync();
        Service?.Dispose();
        _loggers?.Dispose();
        _stop.Dispose();
        Commands?.ReleaseList.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    public sealed class HeldActivity : SaveChangesInterceptor
    {
        public volatile bool Hold;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Hold && data.Context!.ChangeTracker.Entries<AgentSession>().Any(e => e.State == EntityState.Modified
                && e.Property(s => s.LastSeenAt).IsModified))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class HttpFactory(bool legacy) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => legacy ? new(new LegacyRegistrationHandler()) : new();
    }
    // Old-server wire contract: ignore the new capability and omit the response member.
    // The real runner registration and event sender remain unchanged.
    private sealed class LegacyRegistrationHandler : DelegatingHandler
    {
        public LegacyRegistrationHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = System.Text.Json.Nodes.JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            body["capabilities"] = null;
            request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            var response = await base.SendAsync(request, ct);
            var answer = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!.AsObject();
            answer.Remove("eventWindow");
            response.Content = new StringContent(answer.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            return response;
        }
    }

    public sealed class HeldPublication : IEventBus
    {
        public Guid HeldSession { get; set; }
        public bool IgnoreCancellation { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MockEventBus Recipient { get; } = new();
        public async Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            if (eventName == "AgentTextDelta" && group == AgentSessionGroups.Session(HeldSession))
            {
                Entered.TrySetResult();
                if (IgnoreCancellation) await Release.Task; else await Release.Task.WaitAsync(ct);
            }
            await Recipient.PublishToGroupAsync(group, eventName, payload, ct);
        }
        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) =>
            Recipient.PublishToAllAsync(eventName, payload, ct);
    }

    public sealed class CommandSurface(PhoneHomeRuntimeAdapter adapter) : IPhoneHomeRuntimeSurface
    {
        public List<RunnerSessionDto> Sessions { get; } = [];
        public System.Collections.Concurrent.ConcurrentDictionary<Guid, RunnerTranscriptDto> Transcripts { get; } = new();
        public volatile bool HoldList;
        public TaskCompletionSource ListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseList { get; } = new();
        public RunnerCapabilitiesDto Capabilities() => adapter.Capabilities();
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List()
        {
            if (HoldList) { ListEntered.TrySetResult(); ReleaseList.Wait(); }
            return Sessions.ToArray();
        }
        public RunnerTranscriptDto GetTranscript(Guid id) => Transcripts.GetValueOrDefault(id) ?? new(id, [], 0);
        public int OwnedSessionCount => Sessions.Count;
        public Task<RunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Sessions.Single(s => s.SessionId == id));
        public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public RunnerBufferDto GetBuffer(Guid id) => new(id, "", 0);
        public Task<RunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => Task.FromResult(new RunnerSnapshotDto(id, "", "", 0, DateTime.UtcNow));
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid id, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid id, DateTime started, CancellationToken ct) => throw new NotSupportedException();
    }
}
