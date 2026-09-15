using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal static class WallRerouteFixture
{
    internal static async Task SeedHardChainAsync(IsolatedTestSchema schema) =>
        await SeedChainAsync(
            schema,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.High),
            (AgentKind.Grok, AgentModelLevel.Frontier));

    internal static async Task SeedChainAsync(
        IsolatedTestSchema schema,
        params (AgentKind Kind, AgentModelLevel Level)[] pairs)
    {
        await using var db = CreateContext(schema);
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = null,
            Complexity = TaskComplexity.Hard,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                pairs.Select(p => new ComplexityCandidatePair(p.Kind, p.Level)).ToList()),
            Provenance = RoutingPinProvenance.Human,
            Reason = "test Hard chain",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    internal static async Task SeedHoldAsync(IsolatedTestSchema schema, AgentKind kind, string alias)
    {
        await using var db = CreateContext(schema);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = null,
            HitAt = DateTime.UtcNow,
            Reason = "manual hold",
        });
        await db.SaveChangesAsync();
    }

    internal static async Task<(AgentTask Task, Guid SessionId, Guid AgentId)> SeedWorkingChainTaskAsync(
        IsolatedTestSchema schema,
        string directory,
        Guid? parentSessionId = null,
        Guid? cardId = null,
        TaskComplexity? complexity = TaskComplexity.Hard)
    {
        var (sessionId, agentId) = await SeedSessionAndAgentAsync(schema, directory);
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Title = "hard chain wall",
            Goal = "plan it",
            Role = AgentTaskRole.Plan,
            CardId = cardId,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Complexity = complexity,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Working,
            AgentSessionId = sessionId,
            AgentId = agentId,
            Ephemeral = true,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId, agentId);
    }

    internal static async Task<(Guid SessionId, Guid AgentId)> SeedSessionAndAgentAsync(
        IsolatedTestSchema schema, string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var name = $"wall-{agentId:N}"[..16];
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            EffectiveModelId = "fable",
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = directory,
            Details = "Ephemeral pool delegate.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (sessionId, agentId);
    }

    internal static async Task<Guid> SeedSessionAsync(IsolatedTestSchema schema, string directory)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    internal static async Task StampSessionModelAsync(
        IsolatedTestSchema schema, Guid sessionId, string alias)
    {
        await using var db = CreateContext(schema);
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EffectiveModelId, alias));
    }

    internal static async Task SeedApiErrorStubTurnAsync(
        IsolatedTestSchema schema, Guid sessionId, Guid taskId, string errorText)
    {
        await using var db = CreateContext(schema);
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(
            sessionId, ++seq, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing."));

        var stubText = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, errorText);
        stubText.IsApiError = true;
        stubText.ApiErrorClass = "rate_limit";
        stubText.ApiErrorStatus = 429;
        db.TranscriptEntries.Add(stubText);

        var stubEnd = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        stubEnd.StopReason = "stop_sequence";
        stubEnd.IsApiError = true;
        stubEnd.ApiErrorClass = "rate_limit";
        stubEnd.ApiErrorStatus = 429;
        db.TranscriptEntries.Add(stubEnd);
        await db.SaveChangesAsync();
    }

    internal static async Task SeedInFlightTurnAfterStubAsync(
        IsolatedTestSchema schema, Guid sessionId, string promptKind)
    {
        await using var db = CreateContext(schema);
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
        db.TranscriptEntries.Add(NewEntry(
            sessionId, ++seq, promptKind, "continue the work"));
        db.TranscriptEntries.Add(NewEntry(
            sessionId, ++seq, TranscriptKinds.AssistantText, "I'll keep going on the same session."));
        await db.SaveChangesAsync();
    }

    internal static async Task ClearHoldsAsync(IsolatedTestSchema schema)
    {
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        await db.ModelAvailabilityHolds
            .Where(h => h.ClearedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(h => h.ClearedAt, now));
    }

    internal static AgentTaskDispatcher CreateDispatcher(IsolatedTestSchema schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-wall-reroute-tick"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
    }

    internal static TranscriptEntry NewEntry(Guid sessionId, long sequence, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };

    internal static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    internal sealed class WallRerouteHarness : IServiceScopeFactory, IDisposable
    {
        private readonly ServiceProvider _provider;

        public RecordingSessionStopper Stopper { get; } = new();
        public AgentTaskReplyService Reply { get; }

        public WallRerouteHarness(string connectionString, string workspace)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings { AllowedRoots = [workspace] }));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<ApiErrorRecoveryService>();
            services.AddSingleton<CapacityRecoveryService>();
            services.AddSingleton<IDelegateSessionStopper>(Stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-wall-reroute-wt"),
            });
            services.AddScoped<ModelAvailability>();
            services.AddScoped<RoutingPinService>();
            services.AddScoped<ComplexityRoutingService>();
            services.AddScoped<AgentTaskService>();
            _provider = services.BuildServiceProvider();
            Reply = new AgentTaskReplyService(
                this,
                Options.Create(new DelegationSettings { ReplyInlineMaxChars = 20_000 }),
                new MockEventBus(),
                TimeProvider.System,
                NullLogger<AgentTaskReplyService>.Instance);
        }

        public IServiceScope CreateScope() => _provider.CreateScope();

        public void Dispose() => _provider.Dispose();
    }

    internal sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-wall-reroute").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
