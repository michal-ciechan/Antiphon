using System.Net;
using System.Net.Http.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Application;

/// <summary>Migrated isolated PostgreSQL, real queue/state/DI and serialized fake runner I/O.</summary>
internal sealed class RunnerSeatReleaseFixture : IAsyncDisposable
{
    public required IsolatedTestSchema Schema { get; init; }
    public required BridgeQueueHarness Harness { get; init; }
    public required FakeTimeProvider Clock { get; init; }
    public required SeatWire Wire { get; init; }
    public required SeatDirectory Directory { get; init; }
    public LaunchRecorder Launches => Harness.Provider.GetRequiredService<LaunchRecorder>();
    public Guid TaskId { get; } = Guid.NewGuid();
    public Guid SessionId => Harness.SessionId;
    public Guid AgentId => Harness.AgentId;
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;
    public TerminalSeatObservationRequest Observation => new(Directory.StoreId, Now.AddHours(-1), "binding", 10);
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public static async Task<RunnerSeatReleaseFixture> CreateAsync(
        AgentTaskStatus status = AgentTaskStatus.Succeeded, bool sourced = false,
        Action<DbContextOptionsBuilder>? configureDb = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var wire = new SeatWire();
        var http = new HttpClient(wire);
        var client = new SessionRunnerHttpClient(http, new ClientFactory(http),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://seat.test" }));
        var directory = new SeatDirectory(client);
        BridgeQueueHarness harness;
        try
        {
            harness = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString, TimeProvider = clock,
                AlwaysOn = false, PreserveDatabaseOnDispose = true,
                ConfigureDbContext = configureDb,
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerDirectory>(directory);
                    services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
                    services.AddSingleton<SessionStateStore>();
                    services.AddSingleton(Options.Create(new SessionStateSettings()));
                    services.AddSingleton(Options.Create(new TerminalRunnerSeatReleaseOptions { AutomaticEnabled = true }));
                    services.AddSingleton<TerminalRunnerSeatReleasePolicy>();
                    services.AddScoped<TerminalRunnerSeatReleaseService>();
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddDelegationWorktreeGraph(new GitSettings());
                    services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                    services.AddScoped<AgentTaskService>();
                    services.AddSingleton<AgentTaskReplyService>();
                    services.AddScoped<SubscriptionUsageReader>();
                    services.AddScoped<SubscriptionQuotaGate>();
                    services.AddSingleton(Options.Create(new SubscriptionQuotaGateSettings()));
                    services.AddSingleton<LaunchRecorder>();
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(
                        new AgentRegistrySettings { DefaultDefinition = "fake", Definitions =
                            { ["fake"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "fixture-provider" } } }));
                    services.AddScoped<RemoteWorkspaceService>();
                    services.AddScoped(sp => new AgentTaskDispatcher(
                        sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<AgentRegistry>(),
                        sp.GetRequiredService<AgentSessionLaunchQueue>(), sp.GetRequiredService<SessionMessageQueueService>(),
                        sp.GetRequiredService<DelegationWorktreeService>(), sp.GetRequiredService<AgentTaskService>(),
                        sp.GetRequiredService<IDelegateSessionStopper>(), sp.GetRequiredService<IOptions<DelegationSettings>>(),
                        sp.GetRequiredService<IEventBus>(), clock, sp.GetRequiredService<ILogger<AgentTaskDispatcher>>(),
                        modelAvailability: sp.GetRequiredService<ModelAvailability>(),
                        dispatchWarnings: sp.GetRequiredService<DispatchBaseWarningIntentService>(),
                        workspaceUse: sp.GetRequiredService<WorkspaceUseAdmission>(),
                        remoteWorkspace: sp.GetRequiredService<RemoteWorkspaceService>(), runners: directory,
                        taskLaunchSink: sp.GetRequiredService<LaunchRecorder>()));
                }
            });
        }
        catch { http.Dispose(); await schema.DisposeAsync(); throw; }
        var f = new RunnerSeatReleaseFixture { Schema = schema, Harness = harness, Clock = clock, Wire = wire, Directory = directory };
        try
        {
            await using var db = f.Db();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
            session.RunnerId = "fixture"; session.RunnerStoreId = directory.StoreId;
            session.RunnerCwd = "/fixture";
            session.StartedAt = f.Observation.ExpectedAcceptedStartedAt;
            var agent = await db.Agents.SingleAsync(a => a.Id == f.AgentId);
            agent.IsPoolDelegate = true; agent.AlwaysOn = false; agent.BoardId = null;
            Guid? landingId = null;
            if (sourced)
            {
                landingId = Guid.NewGuid();
                var ownerId = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask { Id = ownerId, RootTaskId = ownerId, Status = AgentTaskStatus.Succeeded,
                    CompletedAt = f.Now, CreatedAt = f.Now });
                await db.SaveChangesAsync();
                db.AgentTaskLandings.Add(new AgentTaskLanding
                {
                    Id = landingId.Value, TaskId = ownerId, CreatedAt = f.Now, UpdatedAt = f.Now
                });
                await db.SaveChangesAsync();
            }
            db.AgentTasks.Add(new AgentTask
            {
                Id = f.TaskId, RootTaskId = f.TaskId, AgentId = f.AgentId, AgentSessionId = f.SessionId,
                RunnerId = "fixture", Workspace = WorkspaceMode.Worktree, Attempt = 1,
                Status = status, CompletedAt = f.Now.AddMinutes(-3), CreatedAt = f.Now.AddHours(-1),
                Ephemeral = true, Goal = "Continue the seat transaction", WorkingDirectory = harness.TempRoot,
                Result = "completed report", ReportEvidence = AgentTaskReportEvidence.Marked,
                SourceLandingOperationId = landingId,
            });
            await db.SaveChangesAsync();
            if (status == AgentTaskStatus.Blocked)
            {
                db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = f.TaskId,
                    Type = AgentTaskEventType.Blocked, At = f.Now.AddMinutes(-3), Detail = "Which answer?" });
                await db.SaveChangesAsync();
            }
            await f.IngestAsync(TranscriptKinds.UserPrompt, "task", f.Now.AddMinutes(-4));
            await f.IngestAsync(TranscriptKinds.TurnEnd, null, f.Now.AddMinutes(-3));
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    public async Task<TerminalRunnerSeatReservation> RunAsync(Guid? taskId = null)
    {
        using var scope = Harness.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
            .RegisterAndReserveAsync(taskId ?? TaskId, Observation, CancellationToken.None);
    }

    public async Task<Guid?> ReleaseAsync(Func<string, CancellationToken, Task>? boundary = null)
    {
        using var scope = Harness.Provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>();
        service.BoundaryAsync = boundary;
        return await service.RegisterAndReleaseAsync(TaskId, Observation, CancellationToken.None);
    }

    public async Task EditAsync(Action<AgentTask, Agent> edit)
    {
        await using var db = Db();
        edit(await db.AgentTasks.SingleAsync(t => t.Id == TaskId), await db.Agents.SingleOrDefaultAsync(a => a.Id == AgentId) ?? new Agent());
        await db.SaveChangesAsync();
    }

    public async Task<AgentTask> TaskAsync()
    {
        await using var db = Db();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
    }

    public Task<AgentTaskSummaryDto> AnswerAsync(string text, int? round = 1) =>
        Harness.Provider.GetRequiredService<AgentTaskReplyService>()
            .AnswerAsync(TaskId, text, AnswerOrigin.Web, round, CancellationToken.None);

    public async Task<Exception?> TryAnswerAsync(string text, int? round = 1)
    {
        try { await AnswerAsync(text, round); return null; }
        catch (Exception ex) { return ex; }
    }

    public async Task RetryAsync()
    {
        using var scope = Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(TaskId, default);
    }

    public async Task<AgentTaskDispatcher.TickResult> DispatchAsync()
    {
        using var scope = Harness.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
    }

    public Task PrepareContinuationAsync() => EditAsync((t, _) =>
    {
        t.WorktreePath = Harness.TempRoot; t.WorktreeBranch = "feat/retained";
        t.WorktreeBaseRef = "master"; t.WorktreeBaseSha = new string('a', 40);
        t.RemoteWorktreePath = "/fixture/retained";
    });

    public RecordingSessionStopper RecordedStops => (RecordingSessionStopper)Harness.Provider.GetRequiredService<IDelegateSessionStopper>();

    internal sealed class LaunchRecorder : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Calls { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) => Calls.Add(spec);
    }

    public async Task IngestAsync(string kind, string? text, DateTime timestamp)
    {
        await using var db = Db();
        var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
        var result = await Harness.Runtime.PersistTranscriptAsync(SessionId,
            [new SessionRunnerTranscriptEvent(SessionId, sequence, kind, Guid.NewGuid().ToString("D"), null,
                new DateTimeOffset(timestamp), kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                text, null, null, null, null, kind == TranscriptKinds.TurnEnd ? "end_turn" : null)]);
        if (result.LastStoredSeq is null) throw new InvalidOperationException("Fixture transcript ingestion did not commit.");
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
        Wire.Dispose();
        await Schema.DisposeAsync();
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }

    internal sealed class SeatWire : HttpMessageHandler
    {
        public bool Unsupported { get; set; }
        public bool DropReply { get; set; }
        public TerminalSeatReleaseOutcome Outcome { get; set; } = TerminalSeatReleaseOutcome.Released;
        public Func<TerminalSeatReleaseRequest, Task>? AtCommand { get; set; }
        public Exception? CallbackFailure { get; private set; }
        public Func<TerminalSeatReleaseResult, TerminalSeatReleaseResult>? RewriteReply { get; set; }
        public List<TerminalSeatReleaseRequest> Requests { get; } = [];
        public List<string> Calls { get; } = [];
        public int ConditionalCommands => Calls.Count(p => p.EndsWith("/release-terminal-seat"));
        public int ForceCommands => Calls.Count(p => p.EndsWith("/kill") || p.EndsWith("/kill-generation") || p.EndsWith("/release"));
        public TerminalSeatObservation Qualified { get; } = new(TerminalSeatQualificationStatus.Qualified,
            new(TerminalTranscriptReadStatus.Success, TerminalTranscriptVerdict.Idle,
                "binding", "file", 100, 12, 12, 11), "issued-token", TimeSpan.FromSeconds(120),
            new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero)); // Runner clock is +24h.
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("/release-terminal-seat"))
            {
                var command = (await request.Content!.ReadFromJsonAsync<TerminalSeatReleaseRequest>(ct))!;
                Requests.Add(command);
                if (AtCommand is not null)
                {
                    try { await AtCommand(command); }
                    catch (Exception ex) { CallbackFailure = ex; throw; }
                }
                if (DropReply) throw new HttpRequestException("fixture dropped the reply after execution");
                var sessionId = Guid.Parse(request.RequestUri.AbsolutePath.Split('/')[2]);
                var result = new TerminalSeatReleaseResult(sessionId, command.ActionId, Outcome,
                    command.Observation.ExpectedAcceptedStartedAt);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(RewriteReply?.Invoke(result) ?? result) };
            }
            return Unsupported
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Qualified) };
        }
    }

    internal sealed class SeatDirectory(ISessionRunnerClient client) : ISessionRunnerDirectory
    {
        public Guid StoreId { get; } = Guid.NewGuid();
        public bool Available { get; set; } = true;
        public int Capacity { get; set; } = 10;
        public bool RefuseNewWork { get; set; }
        public bool Stale { get; set; }
        public bool Recovered { get; set; } = true;
        public Guid? LiveStoreOverride { get; set; }
        public int InventoryCalls { get; private set; }
        public Func<Task<RunnerInventory>>? Inventory { get; set; }
        public ISessionRunnerClient Client { get; set; } = client;
        public ISessionRunnerClient Local => Client;
        public ISessionRunnerClient Resolve(string? runnerId) => Client;
        public ISessionRunnerClient ResolveForNewWork(string? runnerId) => RefuseNewWork
            ? throw new ServiceUnavailableException("fixture runner unavailable", "runner_unavailable") : Client;
        public int? DeclaredCapacity(string runnerId) => Capacity;
        public Guid? GetLiveStoreId(string? runnerId) => LiveStoreOverride ?? StoreId;
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerDescriptor?>(new("fixture", "fixture", "linux", null, Available, Recovered, Stale, 1,
                new RunnerCapabilitiesDto("fake", "fake", "fixture", false,
                    Features: [RunnerCapabilityFeatures.TerminalSeatReleaseV1], RunnerStoreId: StoreId)));
        public IReadOnlyList<string> KnownRunnerIds => ["fixture"];
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(new("fixture", StoreId, "/fixture"));
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(new SessionRunnerBinding.Remote(new("fixture", StoreId, "/fixture")));
        public async Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct)
        {
            InventoryCalls++;
            return Inventory is null ? new RunnerInventory.Unavailable("fixture inventory not supplied") : await Inventory();
        }
        public bool RemoteInventoryPending(string? runnerId) => !Recovered;
    }
}
