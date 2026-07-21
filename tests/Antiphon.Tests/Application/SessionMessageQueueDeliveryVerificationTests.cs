using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Delivery-time composer verification (the TUI-echo-probe replacement): a delivered body must
/// show up on the rendered screen BEFORE the submitting Enter goes out, and the Enter must produce
/// output. Failure paths: message reverts to Pending, a DeliveryVerificationFailed incident is
/// recorded, and (always-on agents only) the wedged session is killed for the supervisor to
/// restart; the stranded-queue watchdog then redelivers. The FakeAgentProtocolAdapter simulates
/// the composer: typed input echoes into the rendered screen, a lone CR clears it and emits an
/// ack (EchoTypedInputToScreen=false / SubmitAck="" simulate the two wedge modes).
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueDeliveryVerificationTests
{
    [Test]
    public async Task Verified_delivery_types_body_then_submits_and_leaves_no_incident()
    {
        var h = await CreateHarnessAsync(alwaysOn: true);
        try
        {
            var dto = await h.Queue.EnqueueAsync(
                h.SessionId, "verified hello", MessageSendMode.WhenIdle, CancellationToken.None);

            dto.Messages.ShouldBeEmpty("idle session: the message delivers straight away");
            h.Adapter.Inputs.ShouldBe(["verified hello", "\r"]);
            await using var db = CreateContext();
            (await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId)).ShouldBeFalse();
            h.Adapter.Killed.ShouldBeFalse();
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Wedged_composer_withholds_enter_reverts_message_and_restarts_always_on_agent()
    {
        var h = await CreateHarnessAsync(alwaysOn: true);
        try
        {
            h.Adapter.EchoTypedInputToScreen = false; // typed text never renders: wedged terminal

            var dto = await h.Queue.EnqueueAsync(
                h.SessionId, "into the void", MessageSendMode.WhenIdle, CancellationToken.None);

            // The body was typed but the submitting Enter must be withheld — the message is not
            // lost into a dead composer.
            h.Adapter.Inputs.ShouldBe(["into the void"]);

            dto.Messages.Count.ShouldBe(1);
            dto.Messages[0].Status.ShouldBe(nameof(QueuedMessageStatus.Pending));

            await using var db = CreateContext();
            var incident = await db.AgentIncidents.SingleOrDefaultAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
            incident.ShouldNotBeNull();
            incident.Message.ShouldContain("never appeared in the composer");

            h.Adapter.Killed.ShouldBeTrue("always-on agent: the wedged session is killed for the supervisor to restart");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Swallowed_submit_reverts_message_and_restarts_always_on_agent()
    {
        var h = await CreateHarnessAsync(alwaysOn: true);
        try
        {
            h.Adapter.SubmitAck = ""; // Enter lands but produces no output: submit swallowed

            await h.Queue.EnqueueAsync(
                h.SessionId, "swallowed submit", MessageSendMode.WhenIdle, CancellationToken.None);

            h.Adapter.Inputs.ShouldBe(["swallowed submit", "\r"]);

            await using var db = CreateContext();
            var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
            message.Status.ShouldBe(QueuedMessageStatus.Pending);
            message.SentAt.ShouldBeNull();

            var incident = await db.AgentIncidents.SingleOrDefaultAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
            incident.ShouldNotBeNull();
            incident.Message.ShouldContain("no output");
            h.Adapter.Killed.ShouldBeTrue();
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Non_always_on_agent_gets_incident_and_revert_but_no_kill()
    {
        var h = await CreateHarnessAsync(alwaysOn: false);
        try
        {
            h.Adapter.EchoTypedInputToScreen = false;

            await h.Queue.EnqueueAsync(
                h.SessionId, "manual agent message", MessageSendMode.WhenIdle, CancellationToken.None);

            await using var db = CreateContext();
            var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
            message.Status.ShouldBe(QueuedMessageStatus.Pending);
            (await db.AgentIncidents.AnyAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
                .ShouldBeTrue();
            h.Adapter.Killed.ShouldBeFalse("not always-on: never kill a human's session out from under them");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Send_now_throws_conflict_when_delivery_cannot_be_verified()
    {
        var h = await CreateHarnessAsync(alwaysOn: true);
        try
        {
            h.Adapter.EchoTypedInputToScreen = false;

            await Should.ThrowAsync<ConflictException>(() =>
                h.Queue.EnqueueAsync(h.SessionId, "send now please", MessageSendMode.Now, CancellationToken.None));

            h.Adapter.Inputs.ShouldBe(["send now please"], "Enter must be withheld on an unverified delivery");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Failed_turn_end_flush_does_not_broadcast_session_finished()
    {
        var h = await CreateHarnessAsync(alwaysOn: true);
        try
        {
            await MarkWorkingAsync(h.SessionId);
            await h.Queue.EnqueueAsync(h.SessionId, "held message", MessageSendMode.WhenIdle, CancellationToken.None);
            h.Adapter.EchoTypedInputToScreen = false;

            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

            h.EventBus.PublishedEvents.ShouldNotContain(
                e => e.EventName == "SessionFinished",
                "a failed delivery is not 'queue empty and agent finished'");
            h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "SessionQueueChanged");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Stranded_watchdog_redelivers_pending_messages_on_idle_always_on_sessions()
    {
        var h = await CreateHarnessAsync(alwaysOn: true);
        try
        {
            await SeedPendingMessageAsync(h.SessionId, "stranded message");

            var flushed = await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

            flushed.ShouldBe(1);
            h.Adapter.Inputs.ShouldBe(["stranded message", "\r"]);
            await using var db = CreateContext();
            (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
                .Status.ShouldBe(QueuedMessageStatus.Sent);
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Stranded_watchdog_skips_non_always_on_agents_and_working_sessions()
    {
        var h = await CreateHarnessAsync(alwaysOn: false);
        try
        {
            await SeedPendingMessageAsync(h.SessionId, "not mine to flush");
            (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
                .ShouldBe(0, "non-always-on agents are never auto-flushed");

            // Flip to always-on but make the session busy: still not flushed.
            await using (var db = CreateContext())
            {
                await db.Agents.Where(a => a.Id == h.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.AlwaysOn, true));
            }
            await MarkWorkingAsync(h.SessionId);
            (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
                .ShouldBe(0, "working sessions are never interrupted");

            h.Adapter.Inputs.ShouldBeEmpty();
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    [Test]
    public async Task Fresh_start_migrates_pending_messages_to_the_new_session()
    {
        var h = await CreateHarnessAsync(alwaysOn: true);
        try
        {
            await SeedPendingMessageAsync(h.SessionId, "survive the fresh fallback");

            // End the old session so StartAsync creates a NEW session row (fresh=true skips resume;
            // liveness is judged from the DB status).
            await using (var db = CreateContext())
            {
                await db.AgentSessions.Where(s => s.Id == h.SessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Failed));
            }

            // Fresh scope: the harness scope's DbContext still tracks the agent from creation
            // (identity resolution would hide the PersistentSessionId set via ExecuteUpdate).
            using var controlScope = h.Provider.CreateScope();
            var control = controlScope.ServiceProvider.GetRequiredService<AgentControlService>();
            var started = await control.StartAsync(
                h.AgentId, new StartAgentRequest(RemoteControl: false, Fresh: true), CancellationToken.None);

            var newSessionId = Guid.Parse(started.PersistentSessionId!);
            newSessionId.ShouldNotBe(h.SessionId);

            await using var verify = CreateContext();
            var message = await verify.SessionQueuedMessages.SingleAsync(m => m.Body == "survive the fresh fallback");
            message.AgentSessionId.ShouldBe(newSessionId, "pending messages must follow the agent to its new session");
        }
        finally
        {
            await h.DisposeAsync();
        }
    }

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static async Task MarkWorkingAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.AssistantText,
            Text = "working on it",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPendingMessageAsync(Guid sessionId, string body)
    {
        await using var db = CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = body,
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            CreatedAt = DateTime.UtcNow - TimeSpan.FromMinutes(5),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Harness> CreateHarnessAsync(bool alwaysOn)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-delivery-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System); // verification polls with real delays
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new DeliveryVerificationSettings
            {
                Enabled = true,
                EvidenceTimeoutSeconds = 1,   // fast wedge verdicts in tests
                PollIntervalMs = 50,
                PostSubmitAdvanceTimeoutSeconds = 1,
                StrandedAgeSeconds = 0,
            },
        }));
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
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        // A running ClaudeCode session owned by an agent (PersistentSessionId links them).
        var sessionId = Guid.NewGuid();
        var workspace = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspace);

        var agentDto = await scope.ServiceProvider.GetRequiredService<AgentService>()
            .CreateAsync(new CreateAgentRequest("DeliveryVerify", workspace), CancellationToken.None);

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
                .SetProperty(a => a.AlwaysOn, alwaysOn)
                .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
        }

        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);

        return new Harness(
            provider, scope, tempRoot, sessionId, agentDto.Id, adapter, eventBus, runtime,
            provider.GetRequiredService<SessionMessageQueueService>());
    }

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        string TempRoot,
        Guid SessionId,
        Guid AgentId,
        FakeAgentProtocolAdapter Adapter,
        MockEventBus EventBus,
        AgentSessionRuntime Runtime,
        SessionMessageQueueService Queue) : IAsyncDisposable
    {
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
    }

    // All sessions in these tests are runtime-registered fakes; the runner client only ever needs
    // to answer "no runner-hosted sessions" for ListLiveSessions and the supervisor's ctor.
    private sealed class EmptyRunnerClient : ISessionRunnerClient
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

    private sealed class ThrowingAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new NotSupportedException("Delivery verification tests never launch real processes.");
    }

    private sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
