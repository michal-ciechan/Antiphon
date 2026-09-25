using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

// Owns only isolated test data and controlled transports. The directory's frozen clock is
// deliberately separate from the queue's real clock, whose confirmation timers must advance.
internal sealed class PhoneHomeOutageHarness : IAsyncDisposable
{
    public required IsolatedTestSchema Schema { get; init; }
    public required PhoneHomeTestHost Host { get; init; }
    public required BridgeQueueHarness Bridge { get; set; }
    public required PendingInventoryProbe Probe { get; init; }
    public required FakeTimeProvider Clock { get; init; }
    public Guid SourceId { get; private set; }
    public Guid CardId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid SessionId => Bridge.SessionId;
    public SessionMessageQueueService Queue => Bridge.Queue;
    public AgentSessionRuntime Runtime => Bridge.Runtime;
    public MentionRouteDiagnostics Diagnostics => Bridge.Provider.GetRequiredService<MentionRouteDiagnostics>();
    public AgentMentionRouter Router => Bridge.Provider.GetRequiredService<AgentMentionRouter>();
    private readonly CancellationTokenSource _stop = new();
    private OutageApiFactory? _api;
    public HttpClient Http => (_api ??= new OutageApiFactory(this)).CreateClient();
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public static async Task<PhoneHomeOutageHarness> CreateAsync(bool remote = true)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var probe = new PendingInventoryProbe();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString,
            configureDbContext: o => o.AddInterceptors(probe));
        var bridge = await CreateBridgeAsync(host, schema.ConnectionString);
        var h = new PhoneHomeOutageHarness { Schema = schema, Host = host, Bridge = bridge, Probe = probe, Clock = clock };
        if (remote)
        {
            bridge.Runtime.TryRemove(bridge.SessionId, out _).ShouldBeTrue();
            await using var db = h.Db();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.RunnerId, host.AllowedRunnerId)
                .SetProperty(s => s.RunnerStoreId, host.StoreId)
                .SetProperty(s => s.RunnerCwd, bridge.TempRoot));
        }
        await bridge.InsertTurnAsync("prior-turn", "prior-answer");
        (h.CardId, h.AttemptId) = await PhoneHomeStrandedQueueTests.ClaimCardAsync(bridge, schema.ConnectionString);
        h.SourceId = await PhoneHomeStrandedQueueTests.InsertMentionSourceAsync(schema.ConnectionString, h.CardId);
        return h;
    }

    private static Task<BridgeQueueHarness> CreateBridgeAsync(PhoneHomeTestHost host, string connectionString,
        Guid? sessionId = null, Guid? agentId = null) => BridgeQueueHarness.CreateAsync(new()
    {
        ConnectionString = connectionString, AlwaysOn = false, PreserveDatabaseOnDispose = true,
        AttachSessionId = sessionId, AttachAgentId = agentId,
        ConfigureServices = s =>
        {
            s.AddSingleton<ISessionRunnerDirectory>(host.Directory);
            s.AddSingleton<ISessionRunnerClient>(new Antiphon.Server.Infrastructure.Agents.SessionRunner.RoutingSessionRunnerClient(host.Directory));
            s.AddSingleton(Options.Create(new ScheduleSettings()));
            s.AddSingleton(Options.Create(new DigestSettings { TimeZone = "Europe/London" }));
            s.AddSingleton<ScheduleFireQueue>();
            s.AddSingleton<IScheduledCardActions>(new PhoneHomeStrandedQueueTests.UnusedScheduledCardActions());
            s.AddScoped<ScheduleService>();
            s.AddScoped<AgentChannelService>();
            s.AddSingleton<MentionScanner>();
            s.AddSingleton<MentionRouteDiagnostics>();
            s.AddSingleton<AgentMentionRouter>();
        }
    });

    public async Task RecreateGraphAsync()
    {
        var sessionId = SessionId;
        var agentId = Bridge.AgentId;
        if (_api is not null) { await _api.DisposeAsync(); _api = null; }
        await Bridge.DisposeAsync();
        Bridge = await CreateBridgeAsync(Host, Schema.ConnectionString, sessionId, agentId);
        Bridge.Runtime.TryRemove(sessionId, out _).ShouldBeTrue();
    }

    public async Task<PhoneHomeScriptedPeer> RecoverAsync(bool includeTarget = true)
    {
        var peer = await Host.ConnectPeerAsync();
        if (includeTarget) peer.Sessions.Add(PhoneHomeStrandedQueueTests.RunningOnRunner(SessionId));
        PhoneHomeStrandedQueueTests.EchoSubmittedPromptsToTranscript(peer, Bridge);
        (await PhoneHomeStrandedQueueTests.Pump(Host, Bridge).RunCycleAsync(_stop.Token)).ShouldBeTrue();
        return peer;
    }

    public async Task DisconnectAsync(PhoneHomeScriptedPeer peer)
    {
        var live = Host.Directory.SnapshotLive()!;
        peer.Socket.Abort();
        await UntilAsync(() => !live.SocketOpen);
        Host.Directory.Disconnect(live, "test_disconnect");
    }

    public async Task RouteAsync(string text)
    {
        var before = Diagnostics.Snapshot().Count(e => e.Stage is MentionRouteDiagnostics.RouteReturned or MentionRouteDiagnostics.RouteFailed);
        Router.ObserveDelta(SourceId, text);
        await UntilAsync(() => Diagnostics.Snapshot().Count(e => e.Stage is MentionRouteDiagnostics.RouteReturned or MentionRouteDiagnostics.RouteFailed) > before);
    }

    public async Task<List<SessionQueuedMessage>> RowsAsync()
    {
        await using var db = Db();
        return await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == SessionId).OrderBy(m => m.Sequence).ToListAsync();
    }

    public async Task AssertReceiptAsync(string body, int count = 1)
    {
        await using var db = Db();
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)).ShouldBe(count);
    }

    public async Task<SchedulePreviewDto> PreviewAsync(ScheduleWhenTargetDown policy = ScheduleWhenTargetDown.Skip)
    {
        await using var scope = Bridge.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduleService>().PreviewRequestAsync(new CreateScheduleRequest(
            "C696 preview", TimeZoneId: "Europe/London", Agent: Bridge.AgentId.ToString(), PromptText: "scheduled outage prompt",
            WhenTargetDown: policy, FireAt: DateTime.UtcNow.AddHours(1)), CancellationToken.None);
    }

    public async Task<string> DurableStateAsync()
    {
        await using var db = Db();
        return JsonSerializer.Serialize(new
        {
            messages = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == SessionId).OrderBy(m => m.Id).ToListAsync(),
            session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId),
            card = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == CardId),
            attempt = await db.RunAttempts.AsNoTracking().SingleAsync(a => a.Id == AttemptId),
            incidents = await db.AgentIncidents.AsNoTracking().Where(i => i.AgentId == Bridge.AgentId).OrderBy(i => i.Id).ToListAsync(),
            schedules = await db.Schedules.CountAsync(s => s.AgentId == Bridge.AgentId),
            fires = await db.ScheduleFires.CountAsync(f => f.Schedule.AgentId == Bridge.AgentId)
        });
    }

    public static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        condition().ShouldBeTrue("controlled operation reached its barrier within 10s");
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_api is not null) await _api.DisposeAsync();
        await Bridge.DisposeAsync();
        await Host.DisposeAsync();
        await Schema.DisposeAsync();
        _stop.Dispose();
    }

    private sealed class OutageApiFactory(PhoneHomeOutageHarness h) : AntiphonWebAppFactory
    {
        protected override string ConnectionString => h.Schema.ConnectionString;
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<SessionMessageQueueService>();
            services.AddSingleton(h.Queue);
            services.RemoveAll<AgentSessionRuntime>();
            services.AddSingleton(h.Runtime);
        }
    }
}

internal sealed class PendingInventoryProbe : DbCommandInterceptor
{
    private int _reads;
    public int Reads => Volatile.Read(ref _reads);
    public Action? OnRead { get; set; }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        // Count only the synchronous bound-ID projection, not local List or binding reads.
        if (command.CommandText.Contains("SELECT a.\"Id\"", StringComparison.Ordinal)
            && command.CommandText.Contains("a.\"RunnerId\" =", StringComparison.Ordinal)
            && command.CommandText.Contains("a.\"Status\"", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _reads);
            OnRead?.Invoke();
        }
        return result;
    }
}
