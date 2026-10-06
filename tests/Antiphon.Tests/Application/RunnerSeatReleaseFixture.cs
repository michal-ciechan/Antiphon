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
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Antiphon.Tests.Agents;
using System.Text.Json;
using System.Text;
using System.Net.WebSockets;
using System.Threading.Channels;
using Antiphon.SessionRunner;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Antiphon.Server.Api.Endpoints;
using System.Security.Claims;
using SessionRunnerSettings = Antiphon.Server.Application.Settings.SessionRunnerSettings;

namespace Antiphon.Tests.Application;

/// <summary>Migrated isolated PostgreSQL, real queue/state/DI and serialized fake runner I/O.</summary>
internal sealed partial class RunnerSeatReleaseFixture : IAsyncDisposable
{
    public required IsolatedTestSchema Schema { get; init; }
    public required BridgeQueueHarness Harness { get; set; }
    private BridgeQueueHarness.HarnessOptions? _harnessOptions;
    private readonly List<string> _roots = [];
    public required FakeTimeProvider Clock { get; init; }
    public required SeatWire Wire { get; init; }
    public required SeatDirectory Directory { get; init; }
    public LaunchRecorder Launches => Harness.Provider.GetRequiredService<LaunchRecorder>();
    public Guid TaskId { get; } = Guid.NewGuid();
    public Guid SessionId { get; private set; }
    public Guid AgentId { get; private set; }
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;
    public LiveSeat? Live { get; private set; }
    public Guid CandidateId => Live?.SessionId ?? SessionId;
    public TerminalSeatObservationRequest Observation => new(Directory.StoreId, Now.AddHours(-1), "binding", 10);
    public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public CapturingLoggerProvider AttentionLogs => Wire.Logs;

    public async Task<AttentionDto> AttentionAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(AttentionLogs);
        builder.Services.AddScoped(_ => Db());
        builder.Services.AddScoped(sp => new AttentionService(sp.GetRequiredService<AppDbContext>(),
            new AttentionServiceTests.FakeRunnerClient(), Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), Clock, sp.GetRequiredService<ILogger<AttentionService>>()));
        builder.Services.AddSingleton<AttentionSummaryCache>();
        builder.Services.AddMemoryCache();
        await using var app = builder.Build();
        var credential = Guid.NewGuid().ToString("N");
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Test-Attention-Token"] != credential)
            { context.Response.StatusCode = 401; return; }
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "fixture-operator")], "fixture"));
            await next(context);
        });
        app.MapAttentionEndpoints();
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using (var refused = await http.GetAsync("/api/attention/"))
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Add("X-Test-Attention-Token", credential);
        return (await http.GetFromJsonAsync<AttentionDto>("/api/attention/"))!;
    }

    public async Task RecoverAttentionAsync()
    {
        using var scope = Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
            .ReconcileAttentionAsync(default);
    }

    public async Task<RunnerSeatDiscoveryResult> DiscoverAsync(int budget = 3, int pageSize = 2,
        RunnerSeatDiscoveryCursor? cursor = null, Func<string, CancellationToken, Task>? boundary = null)
    {
        using var scope = Harness.Provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>();
        service.BoundaryAsync = boundary;
        return await service.DiscoverAsync(budget, pageSize, cursor, default);
    }

    public async Task<TerminalRunnerSeatEvidence> AcquireAsync()
    {
        using var scope = Harness.Provider.CreateScope();
        var inventory = await Directory.GetInventoryAsync("fixture", default);
        var seat = ((RunnerInventory.Available)inventory).Sessions.Single(s => s.SessionId == CandidateId);
        return await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
            .ObserveCapturedCandidateAsync("fixture", Directory.StoreId, seat.SessionId, seat.AcceptedStartedAt, default);
    }

    public async Task<TerminalRunnerSeatReservation> ReserveAsync(TerminalSeatObservationRequest request)
    {
        using var scope = Harness.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
            .RegisterAndReserveAsync(TaskId, request, default);
    }

    public async Task AdvanceAsync(Guid releaseId, TerminalSeatObservationRequest request)
    {
        using var scope = Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
            .AdvanceAsync(releaseId, request, default);
    }

    public static async Task<RunnerSeatReleaseFixture> CreateAsync(
        AgentTaskStatus status = AgentTaskStatus.Succeeded, bool sourced = false,
        Action<DbContextOptionsBuilder>? configureDb = null,
        string? provider = null, bool phoneHome = false, bool rowless = false,
        bool productionDefaults = false, bool parking = false)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var wire = new SeatWire();
        var http = new HttpClient(wire);
        var client = new SessionRunnerHttpClient(http, new ClientFactory(wire),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://seat.test" }));
        var directory = new SeatDirectory(client);
        BridgeQueueHarness harness;
        try
        {
            var harnessOptions = new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString, TimeProvider = clock,
                AlwaysOn = false, PreserveDatabaseOnDispose = true,
                ConfigureDbContext = configureDb,
                ConfigureServices = services =>
                {
                    services.AddLogging(b => b.AddProvider(wire.Logs));
                    services.AddSingleton<RemoteSpillCourier>();
                    services.AddSingleton<ISessionRunnerDirectory>(directory);
                    services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
                    services.AddSingleton<SessionStateStore>();
                    services.AddSingleton(Options.Create(new SessionStateSettings()));
                    var releaseOptions = new TerminalRunnerSeatReleaseOptions();
                    if (!productionDefaults) releaseOptions.AutomaticEnabled = wire.AutomaticEnabled;
                    services.AddSingleton(Options.Create(releaseOptions));
                    services.AddSingleton<TerminalRunnerSeatReleasePolicy>();
                    services.AddSingleton<TerminalRunnerSeatDiscoveryState>();
                    services.AddScoped<TerminalRunnerSeatReleaseService>();
                    services.AddSingleton(Options.Create(new BlockedTaskParkingOptions { Enabled = parking }));
                    services.AddScoped<BlockedTaskParkingService>();
                    services.AddSingleton<ITaskProgressGit, TaskParkPublicationTests.ParkGit>();
                    services.AddSingleton<IRepositoryMutationLease>(sp => new RepositoryMutationLease(
                        (TaskParkPublicationTests.ParkGit)sp.GetRequiredService<ITaskProgressGit>()));
                    services.AddSingleton<IWorkspaceReservationJournal, WorkspaceReservationJournal>();
                    services.AddScoped<LocalTaskParkPublisher>();
                    services.AddScoped<TaskParkPublicationService>();
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddDelegationWorktreeGraph(new GitSettings());
                    services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                    services.AddScoped<AgentTaskService>();
                    services.AddSingleton<AgentTaskReplyService>();
                    services.AddSingleton<CompletionNoteFlushQueue>();
                    services.AddScoped<AgentTaskLandNotificationService>();
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
                        taskLaunchSink: sp.GetRequiredService<LaunchRecorder>(),
                        terminalSeatRelease: sp.GetRequiredService<TerminalRunnerSeatReleaseService>()));
                }
            };
            harness = await BridgeQueueHarness.CreateAsync(harnessOptions);
            wire.HarnessOptions = harnessOptions;
        }
        catch { http.Dispose(); await schema.DisposeAsync(); throw; }
        var f = new RunnerSeatReleaseFixture { Schema = schema, Harness = harness, Clock = clock, Wire = wire, Directory = directory };
        f._harnessOptions = wire.HarnessOptions;
        f.SessionId = harness.SessionId;
        f.AgentId = harness.AgentId;
        f._roots.Add(harness.TempRoot);
        try
        {
            if (provider is not null)
            {
                wire.ForbidFixedEvidence = true;
                f.Live = await LiveSeat.CreateAsync(rowless ? Guid.NewGuid() : f.SessionId,
                    f.Now.AddHours(-1), provider, phoneHome);
                directory.StoreId = f.Live.Runtime.RunnerStoreId;
                directory.Client = f.Live.Client;
                directory.Capabilities = () => f.Live.Client.GetCapabilitiesAsync(default);
                directory.Inventory = async () => new RunnerInventory.Available(await f.Live.Client.ListAsync(default));
            }
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

    public async Task ReleaseFromSettlementAsync()
    {
        using var scope = Harness.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
        var method = typeof(AgentTaskReplyService).GetMethod("ReleaseDelegateAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(scope.ServiceProvider.GetRequiredService<AgentTaskReplyService>(),
            [scope.ServiceProvider, db, task, Now, CancellationToken.None, true])!;
        await db.SaveChangesAsync();
    }

    public async Task SweepAsync(bool janitor = false)
    {
        using var scope = Harness.Provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        if (janitor) await dispatcher.RetireIdleWarmAgentsAsync(default);
        else await dispatcher.ReleaseUnownedPoolDelegatesAsync(default);
    }

    public async Task<int> JobAsync(PhoneHomeRunnerDirectory? legacyDirectory = null)
    {
        using var scope = Harness.Provider.CreateScope();
        var directory = legacyDirectory ?? new PhoneHomeRunnerDirectory(Directory.Client,
            Options.Create(new PhoneHomeRunnerSettings()), Harness.Provider.GetRequiredService<IServiceScopeFactory>(), Clock);
        return await new RunnerSlotReconcileJob(directory, scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            NullLogger<RunnerSlotReconcileJob>.Instance,
            scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()).ExecuteAsync(default);
    }

    public async Task<Guid> AddParentAsync(bool busy)
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, StartedAt = Now.AddHours(-1), Cwd = Harness.TempRoot });
        await db.SaveChangesAsync();
        await AttachRecipientAsync(id, busy);
        await EditAsync((t, _) => { t.ParentSessionId = id; t.ReplyTo = AgentTaskReplyTo.Session;
            t.Role = AgentTaskRole.Code; t.VerificationProfileVersion = 1; t.VerificationRound = VerificationRound.Final; });
        return id;
    }

    public async Task SettleAsync(string verdict)
    {
        await EditAsync((t, _) => { t.Status = AgentTaskStatus.Working; t.CompletedAt = null;
            t.DispatchedAt = Now.AddMinutes(-1); t.Result = null; t.ReportEvidence = default; });
        await IngestAsync(TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(TaskId), Now);
        await IngestAsync(TranscriptKinds.AssistantText,
            $"Complete seat report canary.\n[antiphon-report:{DelegationReportFormatter.Short(TaskId)} {verdict}]", Now);
        await IngestAsync(TranscriptKinds.TurnEnd, null, Now);
        await Harness.Provider.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(SessionId, default);
    }

    public async Task ReconcileParentAsync()
    {
        using var scope = Harness.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.AgentTaskLandNotifications.Where(n => n.TaskId == TaskId).Select(n => n.Id).SingleAsync();
        await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(id, default);
    }

    public void ConfigureReleaseEndpointServices(IServiceCollection services)
    {
        services.AddScoped(sp => new TerminalRunnerSeatReleaseService(sp.GetRequiredService<AppDbContext>(),
            new TerminalRunnerSeatReleasePolicy(), Harness.Queue, Harness.Provider.GetRequiredService<SessionStateStore>(),
            Directory, Clock, Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>(),
            Harness.EventBus, sp.GetRequiredService<ILogger<TerminalRunnerSeatReleaseService>>(),
            Harness.Provider.GetRequiredService<TerminalRunnerSeatDiscoveryState>()));
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
        var work = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
        await DriveAsync(work);
        return await work;
    }

    private async Task DriveAsync(Task work)
    {
        while (!work.IsCompleted)
        {
            await Task.WhenAny(work, Task.Delay(10));
            Clock.Advance(TimeSpan.FromMilliseconds(20));
        }
        await work;
    }

    public Task FlushAsync(Guid sessionId) => DriveAsync(Harness.Queue.FlushSessionAsync(sessionId, default));

    public Task PrepareContinuationAsync() => EditAsync((t, _) =>
    {
        t.WorktreePath = Harness.TempRoot; t.WorktreeBranch = "feat/retained";
        t.WorktreeBaseRef = "master"; t.WorktreeBaseSha = new string('a', 40);
        t.RemoteWorktreePath = "/fixture/retained";
    });

    // Historical CARD-0667 data, created before CARD-1065 required a park. New
    // Blocked releases must never use this setup as authority to send a command.
    // Keep legacy answer recovery independent of the S7/S8 parked continuation.
    public async Task SeedLegacyBlockedReleaseAsync(bool unresolved = false)
    {
        Harness.Provider.GetRequiredService<IOptions<BlockedTaskParkingOptions>>().Value.Enabled.ShouldBeFalse();
        await PrepareContinuationAsync();
        await using var db = Db();
        (await db.AgentTaskParks.CountAsync()).ShouldBe(0);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == TaskId);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == SessionId);
        var block = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == TaskId && e.Type == AgentTaskEventType.Blocked);
        db.RunnerSeatReleases.Add(new RunnerSeatRelease
        {
            Id = Guid.NewGuid(), RunnerId = task.RunnerId!, RunnerStoreId = session.RunnerStoreId!.Value,
            SessionId = SessionId, AcceptedStartedAt = session.StartedAt, TaskId = TaskId,
            Attempt = task.Attempt, AgentId = task.AgentId, SettlementEventId = block.Id,
            SettlementRevision = task.ConcurrencyToken, SettledAt = task.CompletedAt,
            State = unresolved ? RunnerSeatReleaseState.Unresolved : RunnerSeatReleaseState.Confirmed,
            Revision = 2, ActionId = Guid.NewGuid(), ReasonCode = "Reserved:Blocked",
            OutcomeCode = unresolved ? "TransportUnknown" : nameof(TerminalSeatReleaseOutcome.Released),
            ConfirmedAt = unresolved ? null : Now, CreatedAt = Now, UpdatedAt = Now
        });
        if (!unresolved) { session.Status = SessionStatus.Stopped; session.EndedAt = Now; }
        await db.SaveChangesAsync();
    }

    public RecordingSessionStopper RecordedStops => (RecordingSessionStopper)Harness.Provider.GetRequiredService<IDelegateSessionStopper>();

    internal sealed class LaunchRecorder : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Calls { get; } = [];
        public Action<Guid>? OnLaunch { get; set; }
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        { Calls.Add(spec); OnLaunch?.Invoke(sessionId); }
    }

    public async Task RestartAsync()
    {
        var task = await TaskAsync();
        var sessionId = task.AgentSessionId ?? SessionId;
        var agentId = task.AgentId ?? AgentId;
        await Harness.DisposeAsync();
        if (Live is not null)
        {
            await Live.RestartServerTransportAsync();
            Directory.Client = Live.Client;
        }
        Harness = await BridgeQueueHarness.CreateAsync(_harnessOptions! with
        { AttachSessionId = sessionId, AttachAgentId = agentId });
        _roots.Add(Harness.TempRoot);
    }

    public FakeAgentProtocolAdapter? Recipient { get; private set; }
    public List<string> Submitted { get; } = [];

    public async Task AttachRecipientAsync(Guid sessionId, bool busy = false)
    {
        var adapter = sessionId == Harness.SessionId ? Harness.Adapter : new FakeAgentProtocolAdapter();
        Recipient = adapter;
        adapter.OnSubmitted = async body =>
        {
            Submitted.Add(body);
            var spill = await Harness.Provider.GetRequiredService<RemoteSpillCourier>()
                .FindDurableAsync(sessionId, body, default);
            if (spill is not null)
            {
                var file = Path.Combine(Harness.TempRoot, spill.Spill.RelativePath);
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, spill.Spill.Body);
            }
            await NativePromptAsync(sessionId, body);
        };
        if (sessionId != Harness.SessionId) Harness.Runtime.Register(sessionId, adapter);
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        if (busy) await NativePromptAsync(sessionId, "existing turn");
    }

    public async Task NativePromptAsync(Guid sessionId, string body)
    {
        var line = JsonSerializer.Serialize(new { type = "user", uuid = Guid.NewGuid().ToString("D"),
            timestamp = Clock.GetUtcNow(), message = new { role = "user", content = body } });
        await File.AppendAllTextAsync(Path.Combine(Harness.TempRoot, $"{sessionId}.jsonl"), line + "\n");
        await using var db = Db();
        var sequence = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).MaxAsync(t => (long?)t.Sequence) ?? 0;
        var parts = Antiphon.SessionRunner.TranscriptNormalizer.Normalize(line);
        if (parts.Count == 0) throw new InvalidOperationException("Native prompt did not normalize.");
        await Harness.Runtime.PersistTranscriptAsync(sessionId, parts.Select(p => new SessionRunnerTranscriptEvent(
            sessionId, ++sequence, p.Kind, p.Uuid, p.ParentUuid, p.Timestamp, p.Role, p.Text,
            p.ToolName, p.ToolInput, p.ToolUseId, p.ToolIsError, p.StopReason)).ToArray());
    }

    public async Task EndTurnAsync(Guid sessionId)
    {
        await using var db = Db();
        var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
        await Harness.Runtime.PersistTranscriptAsync(sessionId,
            [new SessionRunnerTranscriptEvent(sessionId, sequence, TranscriptKinds.TurnEnd, Guid.NewGuid().ToString("D"),
                null, Clock.GetUtcNow(), "assistant", null, null, null, null, null, "end_turn")]);
    }

    public async Task<SessionQueuedMessage?> AnswerQueueAsync()
    {
        var task = await TaskAsync();
        await using var db = Db();
        return await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(m =>
            m.AgentSessionId == task.AgentSessionId && m.ExecutionTaskId == TaskId);
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
        if (Live is not null) await Live.DisposeAsync();
        Wire.Dispose();
        await Schema.DisposeAsync();
        foreach (var root in _roots)
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
    }

    /// <summary>Only I/O is faked: the child writes the bytes it actually received into a
    /// private native transcript. Runtime, tailer, protocol and server clients are production.</summary>
    internal sealed class LiveSeat : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "c667-server-seat-" + Guid.NewGuid().ToString("N"));
        private readonly bool _phoneHome;
        private readonly string _provider;
        public string? Checkout { get; set; }
        public SessionRunnerRuntime.RunnerSession Session { get; private set; } = null!;
        private readonly DateTime _generation;
        private readonly SemaphoreSlim _pollPermit = new(0);
        private readonly Channel<bool> _pollArrived = Channel.CreateUnbounded<bool>();
        private readonly StringBuilder _composer = new();
        private ITranscriptTailer _tailer = null!;
        private WebApplication? _app;
        private HttpClient? _http;
        private PhoneHomeTestHost? _phoneHost;
        private ClientWebSocket? _socket;
        private CancellationTokenSource? _socketLifetime;
        private Task? _socketLoop;
        public Guid SessionId { get; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
        public SessionRunnerRuntime Runtime { get; private set; } = null!;
        public ISessionRunnerClient Client { get; private set; } = null!;
        public NativeChild Child { get; } = new();
        public List<TerminalSeatObservationRequest> Observations { get; } = [];
        public int ConditionalCommands { get; private set; }
        public int ForceCommands { get; private set; }
        public string? ResponseFault { get; set; }
        public bool SwallowNextEnter { get; set; }
        public List<string> NativeSubmissions { get; } = [];
        public string TranscriptPath => Path.Combine(_root, "native.jsonl");
        private Antiphon.SessionRunner.SessionRunnerSettings Settings => new() { SessionLogPath = _root };

        private LiveSeat(Guid id, DateTime generation, string provider, bool phoneHome)
        { SessionId = id; _generation = generation; _provider = provider; _phoneHome = phoneHome; }

        public static async Task<LiveSeat> CreateAsync(Guid id, DateTime generation, string provider, bool phoneHome)
        {
            var live = new LiveSeat(id, generation, provider, phoneHome);
            try { await live.StartAsync(); return live; }
            catch { await live.DisposeAsync(); throw; }
        }

        private async Task StartAsync()
        {
            System.IO.Directory.CreateDirectory(_root);
            await File.WriteAllTextAsync(TranscriptPath, Prompt("previous generation", "old") + End("old"));
            var hub = new SessionRunnerEventHub();
            var claims = new TranscriptClaimRegistry();
            _tailer = _provider switch
            {
                "Claude" => new TranscriptTailer(SessionId, _root, hub, NullLogger.Instance,
                    knownTranscriptPath: TranscriptPath, claims: claims, forkScanInterval: TimeSpan.FromDays(1)),
                "Grok" => new GrokTranscriptTailer(SessionId, TranscriptPath, hub, NullLogger.Instance,
                    pollInterval: TimeSpan.FromMilliseconds(1)),
                _ => new CodexTranscriptTailer(SessionId, _root, hub, NullLogger.Instance,
                    knownTranscriptPath: TranscriptPath, sessionsRoot: _root, claims: claims,
                    pollInterval: TimeSpan.FromMilliseconds(1))
            };
            var observer = _tailer switch
            {
                TranscriptTailer t => t.TerminalObservation,
                GrokTranscriptTailer t => t.TerminalObservation,
                CodexTranscriptTailer t => t.TerminalObservation,
                _ => throw new InvalidOperationException()
            };
            observer.BeforePoll = async ct =>
            {
                await _pollArrived.Writer.WriteAsync(true, ct);
                await _pollPermit.WaitAsync(ct);
            };
            _tailer.Start();
            await _pollArrived.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            _pollPermit.Release();
            await _pollArrived.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            (await _tailer.ObserveTerminalSeatAsync(default)).Status.ShouldBe(TerminalTranscriptReadStatus.Success);
            Child.Write = async input =>
            {
                if (input != "\r") { _composer.Append(input); return; }
                if (SwallowNextEnter) { SwallowNextEnter = false; return; }
                if (_composer.Length == 0) return;
                var body = _composer.ToString().Replace("\u001b[200~", "").Replace("\u001b[201~", "");
                _composer.Clear();
                NativeSubmissions.Add(body);
                var id = Guid.NewGuid().ToString("N");
                await File.AppendAllTextAsync(TranscriptPath, Prompt(body, id) + End(id), new UTF8Encoding(false));
            };
            BindRuntime();
            await StartTransportAsync();
        }

        private void BindRuntime()
        {
            Runtime = new(Options.Create(Settings), NullLogger<SessionRunnerRuntime>.Instance, timeProvider: Clock);
            var session = new SessionRunnerRuntime.RunnerSession(SessionId, Settings,
                new SessionRunnerEventHub(), NullLogger.Instance);
            session.BindChildForTest(Child, _tailer, _generation);
            if (Checkout is not null) session.RetainCheckout(Checkout);
            Session = session;
            Runtime.Track(session);
        }

        public async Task SubmitAsync(string body)
        {
            await Client.SendInputAsync(SessionId, "\u001b[200~" + body + "\u001b[201~", default);
            await Client.SendInputAsync(SessionId, "\r", default);
        }

        public async Task RestartRunnerAsync()
        {
            await StopTransportAsync();
            Runtime.DetachTerminalTailerForTest(SessionId);
            await Runtime.DisposeAsync();
            BindRuntime(); // Same child, store, generation and tailer; no volatile capture.
            await StartTransportAsync();
        }

        public async Task RestartServerTransportAsync()
        {
            await StopTransportAsync();
            await StartTransportAsync(); // Recreate clients and phone-home server, keep runner alive.
        }

        private async Task StartTransportAsync()
        {
            var adapter = new PhoneHomeRuntimeAdapter(Runtime, RunnerBuildIdentity.Resolve());
            if (_phoneHome)
            {
                _phoneHost = await PhoneHomeTestHost.StartAsync();
                var ticket = await _phoneHost.RegisterAsync(storeId: Runtime.RunnerStoreId, capabilities: adapter.Capabilities());
                _socket = new ClientWebSocket();
                _socket.Options.SetRequestHeader(PhoneHomeProtocol.TicketHeader, ticket.Ticket);
                await _socket.ConnectAsync(_phoneHost.ConnectUri, default);
                _socketLifetime = new CancellationTokenSource();
                var dispatcher = new PhoneHomeCommandDispatcher(adapter,
                    new PhoneHomeSettings { LaunchGenerationsPath = Path.Combine(_root, "generations") });
                _socketLoop = PumpAsync(dispatcher, _socketLifetime.Token);
                var connection = await _phoneHost.WaitLiveAsync();
                Client = new PhoneHomeRunnerClient(connection);
                // The test host has no recovery pump. Complete its real inventory catch-up
                // before admitting input, including after either transport restart.
                connection.DispatchEligible.ShouldBeFalse();
                var inventory = await Client.ListAsync(default);
                if (Child.Kills == 0)
                {
                    var seat = inventory.ShouldHaveSingleItem();
                    seat.SessionId.ShouldBe(SessionId);
                    seat.AcceptedStartedAt.ShouldBe(_generation);
                }
                else inventory.ShouldBeEmpty("confirmed release remains absent after server restart");
                _phoneHost.Directory.SnapshotLive().ShouldBeSameAs(connection);
                _phoneHost.Directory.MarkRecovered(connection);
                connection.DispatchEligible.ShouldBeTrue();
                return;
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Testing" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(Runtime);
            builder.Services.AddSingleton<IPhoneHomeRuntimeSurface>(adapter);
            builder.Services.Configure<HerdrSettings>(_ => { });
            builder.Services.Configure<Antiphon.SessionRunner.HostStatsSettings>(_ => { });
            _app = builder.Build();
            _app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value!;
                if (path.EndsWith("/terminal-seat-observation"))
                {
                    context.Request.EnableBuffering();
                    Observations.Add((await context.Request.ReadFromJsonAsync<TerminalSeatObservationRequest>())!);
                    context.Request.Body.Position = 0;
                    if (ResponseFault == "lost") { context.Abort(); return; }
                    if (ResponseFault == "malformed") { await context.Response.WriteAsync("{}"); return; }
                }
                if (path.EndsWith("/release-terminal-seat")) ConditionalCommands++;
                if (path.EndsWith("/kill") || path.EndsWith("/kill-generation") || path.EndsWith("/release"))
                { ForceCommands++; throw new InvalidOperationException("Unexpected destructive fallback"); }
                await next(context);
            });
            _app.MapRunnerCapabilitiesRoute(RunnerBuildIdentity.Resolve());
            _app.MapTerminalSeatReleaseRoutes();
            _app.MapGet("/sessions", () => Runtime.List());
            _app.MapPost("/sessions/{id:guid}/input", async (Guid id, RunnerInputRequest request, CancellationToken ct) =>
            { await Runtime.SendInputAsync(id, request.Input, ct); return Results.Ok(); });
            await _app.StartAsync();
            var uri = new Uri(_app.Urls.Single());
            uri.IsLoopback.ShouldBeTrue(); uri.Port.ShouldNotBe(17204);
            _http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(10) };
            Client = new SessionRunnerHttpClient(_http, new LoopbackClientFactory(uri),
                Options.Create(new SessionRunnerSettings { BaseUrl = uri.ToString() }));
        }

        private async Task PumpAsync(PhoneHomeCommandDispatcher dispatcher, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await PhoneHomeFraming.ReadFrameAsync(_socket!, 16 * 1024 * 1024, ct);
                    if (frame is null) break;
                    if (frame.Kind != PhoneHomeFrameKind.Request) continue;
                    if (frame.Operation == PhoneHomeOperation.ObserveTerminalSeat)
                    {
                        Observations.Add(frame.Payload!.Value.Deserialize<PhoneHomeTerminalSeatObservationRequest>(PhoneHomeFraming.Json)!.Observation);
                        if (ResponseFault == "lost") { _socket!.Abort(); return; }
                    }
                    if (frame.Operation == PhoneHomeOperation.ReleaseTerminalSeat) ConditionalCommands++;
                    if (frame.Operation is PhoneHomeOperation.ReleaseSlot or PhoneHomeOperation.KillGeneration)
                    { ForceCommands++; throw new InvalidOperationException("Unexpected destructive fallback"); }
                    var reply = await dispatcher.DispatchAsync(frame, ct);
                    if (frame.Operation == PhoneHomeOperation.ObserveTerminalSeat && ResponseFault == "malformed")
                        reply = reply with { Payload = JsonSerializer.SerializeToElement(new { }) };
                    await PhoneHomeFraming.WriteFrameAsync(_socket!, reply, 16 * 1024 * 1024, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (WebSocketException) when (ct.IsCancellationRequested) { }
        }

        private async Task StopTransportAsync()
        {
            if (_socketLifetime is not null)
            {
                await _socketLifetime.CancelAsync();
                _socket?.Abort();
                if (_socketLoop is not null) await _socketLoop;
                _socket?.Dispose(); _socketLifetime.Dispose(); _socketLifetime = null;
            }
            if (_phoneHost is not null) { await _phoneHost.DisposeAsync(); _phoneHost = null; }
            _http?.Dispose(); _http = null;
            if (_app is not null) { await _app.DisposeAsync(); _app = null; }
        }

        private string Prompt(string body, string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "user", uuid = id, message = new { role = "user", content = body } }) + "\n",
            "Grok" => Grok(new { sessionUpdate = "user_message_chunk", content = new { type = "text", text = body } }, id),
            _ => Codex(new { type = "user_message", message = body }, id)
        };
        private string End(string id) => _provider switch
        {
            "Claude" => JsonSerializer.Serialize(new { type = "assistant", uuid = id + "-end", message = new { role = "assistant", content = Array.Empty<object>(), stop_reason = "end_turn" } }) + "\n",
            "Grok" => Grok(new { sessionUpdate = "turn_completed", prompt_id = id, stop_reason = "end_turn" }, id),
            _ => Codex(new { type = "task_complete", turn_id = id }, id)
        };
        private static string Grok(object update, string id) => JsonSerializer.Serialize(new
        { method = "session/update", @params = new { update, _meta = new { eventId = Guid.NewGuid().ToString(), promptId = id } } }) + "\n";
        private static string Codex(object payload, string id) => JsonSerializer.Serialize(new
        { type = "event_msg", timestamp = "2026-10-05T00:00:00Z", id, payload }) + "\n";

        public async ValueTask DisposeAsync()
        {
            await StopTransportAsync();
            if (Runtime is not null)
            {
                if (Runtime.List().Any(s => s.SessionId == SessionId)) Runtime.DetachTerminalTailerForTest(SessionId);
                await Runtime.DisposeAsync();
            }
            if (_tailer is not null) await _tailer.DisposeAsync();
            _pollPermit.Dispose();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, true);
        }

        internal sealed class NativeChild : ISessionChild
        {
            public Func<string, Task>? Write { get; set; }
            public int Kills { get; private set; }
            public event Action<ChildExit>? Exited;
            public Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
            public Task WriteAsync(string input, CancellationToken ct) => Write!(input);
            public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
            public Task<bool> KillAsync(CancellationToken ct)
            { Kills++; Exited?.Invoke(new(0, "KilledByRequest")); return Task.FromResult(true); }
            public Task<ChildScreen?> ReadScreenAsync(CancellationToken ct) => Task.FromResult<ChildScreen?>(null);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        // The production read client disposes each factory-created HttpClient. Mutation
        // requests keep their separate long-lived client, as in the real DI registration.
        private sealed class LoopbackClientFactory(Uri address) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new() { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }

    internal sealed class SeatWire : HttpMessageHandler
    {
        public CapturingLoggerProvider Logs { get; } = new();
        public bool ForbidFixedEvidence { get; set; }
        public bool AutomaticEnabled { get; set; } = true;
        public BridgeQueueHarness.HarnessOptions? HarnessOptions { get; set; }
        public bool Unsupported { get; set; }
        public HttpStatusCode UnsupportedStatusCode { get; set; } = HttpStatusCode.NotFound;
        public bool DropReply { get; set; }
        public TerminalSeatReleaseOutcome Outcome { get; set; } = TerminalSeatReleaseOutcome.Released;
        public Func<TerminalSeatReleaseRequest, Task>? AtCommand { get; set; }
        public Exception? CallbackFailure { get; private set; }
        public Func<TerminalSeatReleaseResult, TerminalSeatReleaseResult>? RewriteReply { get; set; }
        public Func<TerminalSeatReleaseRequest, Task<bool>>? VerifySource { get; set; }
        public List<TerminalSeatReleaseRequest> Requests { get; } = [];
        public List<string> Calls { get; } = [];
        public int ConditionalCommands => Calls.Count(p => p.EndsWith("/release-terminal-seat"));
        public int ForceCommands => Calls.Count(p => p.EndsWith("/kill") || p.EndsWith("/kill-generation") || p.EndsWith("/release"));
        public TerminalSeatObservation Qualified { get; set; } = new(TerminalSeatQualificationStatus.Qualified,
            new(TerminalTranscriptReadStatus.Success, TerminalTranscriptVerdict.Idle,
                "binding", "file", 100, 12, 12, 11), "issued-token", TimeSpan.FromSeconds(120),
            new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero)); // Runner clock is +24h.
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (ForbidFixedEvidence) throw new InvalidOperationException("Real-runtime mode reached fixed evidence wire.");
            Calls.Add(request.RequestUri!.AbsolutePath);
            // An old transport rejects both observation and release. Do not synthesize a
            // successful release receipt before applying the configured unsupported response.
            if (Unsupported) return new HttpResponseMessage(UnsupportedStatusCode);
            if (request.RequestUri.AbsolutePath == "/capabilities")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new RunnerCapabilitiesDto("fixture", "fixture", "fixture", false,
                    Features: [RunnerCapabilityFeatures.TerminalSeatReleaseV1, RunnerCapabilityFeatures.WorkspaceParkSourceModesV1])) };
            if (request.RequestUri.AbsolutePath.EndsWith("/release-terminal-seat"))
            {
                var command = (await request.Content!.ReadFromJsonAsync<TerminalSeatReleaseRequest>(ct))!;
                Requests.Add(command);
                if (AtCommand is not null)
                {
                    try { await AtCommand(command); }
                    catch (Exception ex) { CallbackFailure = ex; throw; }
                }
                var sourceValid = VerifySource is null || await VerifySource(command);
                if (DropReply) throw new HttpRequestException("fixture dropped the reply after execution");
                var sessionId = Guid.Parse(request.RequestUri.AbsolutePath.Split('/')[2]);
                var result = new TerminalSeatReleaseResult(sessionId, command.ActionId, sourceValid ? Outcome : TerminalSeatReleaseOutcome.Unknown,
                    command.Observation.ExpectedAcceptedStartedAt);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(RewriteReply?.Invoke(result) ?? result) };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Qualified) };
        }
    }

    internal sealed class SeatDirectory(ISessionRunnerClient client) : ISessionRunnerDirectory
    {
        public string RunnerId { get; set; } = "fixture";
        public bool IsLocal { get; set; }
        public bool LocalBinding { get; set; }
        public IReadOnlyList<string>? RunnerIds { get; set; }
        public Func<string?, Task<RunnerInventory>>? InventoryByRunner { get; set; }
        public Func<string?, Task<RunnerDescriptor?>>? DescriptorByRunner { get; set; }
        public Guid StoreId { get; set; } = Guid.NewGuid();
        public Func<Task<RunnerCapabilitiesDto?>>? Capabilities { get; set; }
        public IReadOnlyList<string>? FeaturesOverride { get; set; }
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
        public ISessionRunnerClient Resolve(string? runnerId) => IsLocal
            ? throw new InvalidOperationException("Local discovery must use the local client") : Client;
        public ISessionRunnerClient ResolveForNewWork(string? runnerId) => RefuseNewWork
            ? throw new ServiceUnavailableException("fixture runner unavailable", "runner_unavailable") : Client;
        public int? DeclaredCapacity(string runnerId) => Capacity;
        public Guid? GetLiveStoreId(string? runnerId) => IsLocal ? null : LiveStoreOverride ?? StoreId;
        public async Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            if (DescriptorByRunner is not null) return await DescriptorByRunner(runnerId);
            var caps = Capabilities is null
                ? new RunnerCapabilitiesDto("fake", "fake", "fixture", false,
                    Features: [RunnerCapabilityFeatures.TerminalSeatReleaseV1,
                        RunnerCapabilityFeatures.TerminalSeatDeliveryEvidenceV1], RunnerStoreId: StoreId)
                : await Capabilities();
            if (FeaturesOverride is not null && caps is not null) caps = caps with { Features = FeaturesOverride };
            return new(RunnerId, RunnerId, "linux", null, Available, Recovered, Stale, 1, caps);
        }
        public IReadOnlyList<string> KnownRunnerIds => RunnerIds ?? [IsLocal ? PhoneHomeProtocol.LocalRunnerId : RunnerId];
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(new("fixture", StoreId, "/fixture"));
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(
            LocalBinding ? SessionRunnerBinding.Local.Instance : new SessionRunnerBinding.Remote(new("fixture", StoreId, "/fixture")));
        public async Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct)
        {
            InventoryCalls++;
            if (IsLocal) throw new InvalidOperationException("Local inventory is not a phone-home directory lookup");
            if (InventoryByRunner is not null) return await InventoryByRunner(id);
            return Inventory is null ? new RunnerInventory.Unavailable("fixture inventory not supplied") : await Inventory();
        }
        public bool RemoteInventoryPending(string? runnerId) => !Recovered;
    }
}
