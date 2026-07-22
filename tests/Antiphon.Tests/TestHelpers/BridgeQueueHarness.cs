using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The shared DI harness for queue / bridge / supervision integration tests: a full service graph
/// (queue with delivery verification, supervisor + alerts, agent control/session services, channel
/// catalog + reply dispatcher on the in-memory messaging fake) around one Running ClaudeCode
/// session owned by one agent, with a <see cref="FakeAgentProtocolAdapter"/> registered as its
/// live adapter. Extracted from <c>SessionMessageQueueDeliveryVerificationTests</c> so every suite
/// in this family (delivery verification, launch notes, compaction recovery, batching) builds on
/// one setup instead of five drifting copies.
/// </summary>
internal sealed class BridgeQueueHarness : IAsyncDisposable
{
    public required ServiceProvider Provider { get; init; }
    public required IServiceScope Scope { get; init; }
    public required string TempRoot { get; init; }
    public required Guid SessionId { get; init; }
    public required Guid AgentId { get; init; }
    public required FakeAgentProtocolAdapter Adapter { get; init; }
    public required MockEventBus EventBus { get; init; }
    public required FakeAntiphonMessagingClient Messaging { get; init; }
    public required AgentSessionRuntime Runtime { get; init; }
    public required SessionMessageQueueService Queue { get; init; }
    public ChannelReplyDispatcher Dispatcher => Provider.GetRequiredService<ChannelReplyDispatcher>();

    public sealed record HarnessOptions
    {
        public bool AlwaysOn { get; init; } = true;
        public TimeProvider? TimeProvider { get; init; }
        public SupervisionSettings? Supervision { get; init; }
        public ChannelBridgeSettings? Bridge { get; init; }
        public Action<IServiceCollection>? ConfigureServices { get; init; }
    }

    public static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    public static async Task<BridgeQueueHarness> CreateAsync(HarnessOptions? options = null)
    {
        options ??= new HarnessOptions();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-bridge-queue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o =>
            o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        var messaging = new FakeAntiphonMessagingClient();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(messaging);
        services.AddSingleton<IAntiphonMessagingProducer>(messaging);
        services.AddSingleton<IAntiphonMessagingConsumer>(messaging);
        services.AddSingleton(options.TimeProvider ?? TimeProvider.System);
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(
            options.Supervision ?? new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings
                {
                    Enabled = true,
                    EvidenceTimeoutSeconds = 1, // fast wedge verdicts in tests
                    PollIntervalMs = 50,
                    PostSubmitAdvanceTimeoutSeconds = 1,
                    StrandedAgeSeconds = 0,
                },
            }));
        services.AddSingleton(Options.Create(options.Bridge ?? new ChannelBridgeSettings { Enabled = true }));
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            SessionLogPath = Path.Combine(tempRoot, "session-logs"),
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot,
        }));
        services.AddSingleton<ISessionRunnerClient>(new EmptyRunnerClient());
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<ChannelReplyDispatcher>();
        services.AddScoped<ChatChannelService>();
        services.AddScoped<AgentSupervisorService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IAlertRouter, NullAlertRouter>();
        services.AddScoped<AgentControlService>();
        services.AddScoped<AgentService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<BoardService>();
        services.AddScoped<CardService>();
        services.AddScoped<OrchestratorService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
            {
                DefaultDefinition = "fake",
                Definitions =
                {
                    ["fake"] = new AgentDefinition
                    {
                        Kind = "Raw",
                        Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    },
                },
            }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IAgentProtocolAdapterFactory>(new ThrowingAdapterFactory());
        services.AddSingleton<IWorktreeManager>(new NoWorktreeManager());
        services.AddSingleton<IWorkspaceHookRunner>(
            new Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner(
                NullLogger<Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddLogging();
        options.ConfigureServices?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        // A running ClaudeCode session owned by an agent (PersistentSessionId links them).
        var sessionId = Guid.NewGuid();
        var workspace = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspace);

        var agentDto = await scope.ServiceProvider.GetRequiredService<AgentService>()
            .CreateAsync(new CreateAgentRequest("BridgeQueue", workspace), CancellationToken.None);

        await using (var db = CreateContext())
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                CardId = null,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = workspace,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == agentDto.Id).ExecuteUpdateAsync(u => u
                .SetProperty(a => a.AlwaysOn, options.AlwaysOn)
                .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
        }

        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);

        return new BridgeQueueHarness
        {
            Provider = provider,
            Scope = scope,
            TempRoot = tempRoot,
            SessionId = sessionId,
            AgentId = agentDto.Id,
            Adapter = adapter,
            EventBus = eventBus,
            Messaging = messaging,
            Runtime = runtime,
            Queue = provider.GetRequiredService<SessionMessageQueueService>(),
        };
    }

    /// <summary>Inserts one transcript entry with the next sequence for the harness session.</summary>
    public async Task<long> InsertTranscriptEntryAsync(
        string kind, string? text = null, string? stopReason = null, Guid? sessionId = null)
    {
        var sid = sessionId ?? SessionId;
        await using var db = CreateContext();
        var seq = ((await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sid)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sid,
            Sequence = seq,
            Kind = kind,
            Text = text,
            StopReason = stopReason,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return seq;
    }

    /// <summary>A full turn (UserPrompt, AssistantText, TurnEnd/end_turn), transcript-driven like prod.</summary>
    public async Task InsertTurnAsync(string prompt, string response, Guid? sessionId = null)
    {
        await InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt, sessionId: sessionId);
        await InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, response, sessionId: sessionId);
        await InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", sessionId: sessionId);
    }

    /// <summary>Activity after the last TurnEnd — makes IsWorkingAsync read true.</summary>
    public Task MarkWorkingAsync(Guid? sessionId = null) =>
        InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "working on it", sessionId: sessionId);

    public async Task SeedPendingMessageAsync(string body, Guid? sessionId = null)
    {
        await using var db = CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId ?? SessionId,
            Body = body,
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            CreatedAt = DateTime.UtcNow - TimeSpan.FromMinutes(5),
        });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using (var db = CreateContext())
        {
            var sessionIds = await db.AgentSessions
                .Where(s => s.CardId == null && s.Cwd.StartsWith(TempRoot))
                .Select(s => s.Id)
                .ToListAsync();
            await db.SessionQueuedMessages
                .Where(m => sessionIds.Contains(m.AgentSessionId) || m.AgentSessionId == SessionId)
                .ExecuteDeleteAsync();
            await db.TranscriptEntries
                .Where(t => sessionIds.Contains(t.AgentSessionId) || t.AgentSessionId == SessionId)
                .ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.AgentId == AgentId).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(i => i.AgentId == AgentId).ExecuteDeleteAsync();
            await db.Alerts.Where(a => a.AgentId == AgentId).ExecuteDeleteAsync();
            await db.AgentSupervisionStates.Where(s => s.AgentId == AgentId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
            await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
        }

        Scope.Dispose();
        await Provider.DisposeAsync();
        try
        {
            if (Directory.Exists(TempRoot))
                Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }

    // All sessions in these tests are runtime-registered fakes; the runner client only ever needs
    // to answer "no runner-hosted sessions" for ListLiveSessions and the supervisor's ctor.
    internal sealed class EmptyRunnerClient : ISessionRunnerClient
    {
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    internal sealed class ThrowingAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new NotSupportedException("Bridge/queue harness tests never launch real processes.");
    }

    internal sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    internal sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
