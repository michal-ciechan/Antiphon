using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Antiphon.Messaging.Client;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Data.Seeding;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.ExternalChanges;
using Antiphon.Server.Infrastructure.FileSystem;
using Antiphon.Server.Infrastructure.GitHub;
using Antiphon.Server.Infrastructure.IssueTrackers;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Server.Infrastructure.Realtime;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Server.Infrastructure.WorkflowDefinitions;

// Bootstrap Serilog for startup logging (before host is built)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog — structured logging with correlation enrichment (NFR19)
    builder.Host.UseSerilog((ctx, lc) =>
    {
        var logPath = ctx.Configuration["Serilog:LogPath"] ?? "logs";
        // Console verbosity is separable from the file's. Test hosts turn this down to Warning so a
        // failing assertion isn't buried under the run's own log, while the file keeps everything.
        var consoleLevel =
            Enum.TryParse<Serilog.Events.LogEventLevel>(
                ctx.Configuration["Serilog:ConsoleMinimumLevel"], ignoreCase: true, out var parsed)
                ? parsed
                : Serilog.Events.LogEventLevel.Verbose;
        var retention =
            Antiphon.Server.Infrastructure.Logging.FileLogRetentionPolicy.FromConfiguration(
                ctx.Configuration);
        // Source levels (including the security-motivated Hosting.Diagnostics turn-down — it logs
        // full query strings) live in Serilog:MinimumLevel:Override in configuration, never here:
        // a debugging session re-arms a source with a config edit, not a rebuild. Pinned by
        // LogRetentionTests.
        lc
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                restrictedToMinimumLevel: consoleLevel,
                outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logPath, "antiphon-.log"),
                rollingInterval: RollingInterval.Day,
                // Cap each day's file and roll within the day if exceeded. Retention is by TIME
                // (5 days) — the file COUNT cap is only a disk backstop, because counting files is
                // not counting days: before CARD-0043 turned the noisy sources down, 14 files was
                // between 5 and 45 hours of history, not 14 days. See FileLogRetentionPolicy.
                fileSizeLimitBytes: retention.FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: retention.RetainedFileCountLimit,
                retainedFileTimeLimit: retention.RetainedFileTimeLimit,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            )
            // Alert log tap (armed after build via AlertingLogSink.Attach; disabled by default).
            .WriteTo.Sink(Antiphon.Server.Infrastructure.Supervision.AlertingLogSink.Instance);
    });

    // Database
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly("Antiphon.Server");
                // Transaction isolation to prevent concurrent writes to the same workflow stage (NFR15)
                npgsqlOptions.SetPostgresVersion(16, 0);
            }));

    // Typed settings — IOptions<T> pattern (never inject IConfiguration into services)
    builder.Services.Configure<GitSettings>(builder.Configuration.GetSection("Git"));
    builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection("Llm"));
    builder.Services.Configure<SignalRSettings>(builder.Configuration.GetSection("SignalR"));
    builder.Services.Configure<AuditSettings>(builder.Configuration.GetSection("Audit"));
    builder.Services.Configure<GithubSettings>(builder.Configuration.GetSection("GitHub"));
    builder.Services.Configure<SessionRunnerSettings>(builder.Configuration.GetSection("SessionRunner"));
    builder.Services.AddSingleton<IValidateOptions<AgentSessionSettings>, AgentSessionSettingsValidator>();
    builder.Services.AddOptions<AgentSessionSettings>()
        .Bind(builder.Configuration.GetSection("AgentSessions"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<OrchestratorSettings>, OrchestratorSettingsValidator>();
    builder.Services.AddOptions<OrchestratorSettings>()
        .Bind(builder.Configuration.GetSection("Orchestrator"))
        .ValidateOnStart();
    builder.Services.Configure<DelegationSettings>(builder.Configuration.GetSection("Delegation"));

    // The pty backend switch (CARD-0037), read from the SAME config key the session runner uses and
    // exported into this process's environment — the server spawns in-proc ptys of its own
    // (ClaudeAdapter/CodexAdapter/RawPtyAdapter), and PtyDeliveryProfile sizes every typed body
    // against whatever this resolves to. An env var already set wins, so one machine can be flipped
    // without editing config. Setting it here does NOT make the runner's ptys modern — that is the
    // runner's own config, and PtyDeliveryProfile verifies the two agree before using the raised
    // ceilings.
    {
        var configuredBackend = builder.Configuration[Antiphon.Agents.Pty.PtyBackendPolicy.ConfigKey];
        if (!string.IsNullOrWhiteSpace(configuredBackend)
            && string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable(Antiphon.Agents.Pty.PtyBackendPolicy.EnvVar)))
        {
            Environment.SetEnvironmentVariable(
                Antiphon.Agents.Pty.PtyBackendPolicy.EnvVar, configuredBackend);
        }
    }
    builder.Services.Configure<WatchdogSettings>(builder.Configuration.GetSection("Watchdog"));
    builder.Services.Configure<SessionReconciliationSettings>(builder.Configuration.GetSection("SessionReconciliation"));
    builder.Services.Configure<SupervisionSettings>(builder.Configuration.GetSection("Supervision"));
    builder.Services.Configure<ContextWindowSettings>(builder.Configuration.GetSection("ContextWindow"));
    builder.Services.Configure<AlertsSettings>(builder.Configuration.GetSection("Alerts"));
    builder.Services.Configure<RetentionSettings>(builder.Configuration.GetSection("Retention"));
    builder.Services.AddSingleton<IValidateOptions<AgentTuiSettings>, AgentTuiSettingsValidator>();
    builder.Services.AddOptions<AgentTuiSettings>()
        .Bind(builder.Configuration.GetSection("AgentTui"))
        .ValidateOnStart();

    var agentTuiSettings = builder.Configuration.GetSection("AgentTui").Get<AgentTuiSettings>()
        ?? new AgentTuiSettings();
    var agentTuiPlatform = OperatingSystem.IsWindows()
        ? AgentTuiPlatform.Windows
        : OperatingSystem.IsLinux()
            ? AgentTuiPlatform.Linux
            : OperatingSystem.IsMacOS()
                ? AgentTuiPlatform.MacOS
                : AgentTuiPlatform.Other;
    var agentTuiPathEnvironment = new AgentTuiPathEnvironment(
        agentTuiPlatform,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    string? agentTuiKeyRingPath = null;
    try
    {
        agentTuiKeyRingPath = agentTuiSettings.ResolveKeyRingPath(agentTuiPathEnvironment);
    }
    catch (Exception exception) when (exception is InvalidOperationException
                                      or PlatformNotSupportedException)
    {
        // Managed-secret operations fail closed through the readiness guard. Wrapper-managed
        // profiles do not use the protector and remain available without a supported key path.
    }
    AgentTuiDataProtectionSetup.Configure(
        builder.Services,
        agentTuiSettings,
        agentTuiPlatform,
        agentTuiKeyRingPath,
        builder.Environment.ContentRootPath);

    // Agent registry (E02) — typed config + fail-fast validator + adapter factory
    builder.Services.AddSingleton<IValidateOptions<AgentRegistrySettings>, AgentRegistrySettingsValidator>();
    builder.Services.AddOptions<AgentRegistrySettings>()
        .Bind(builder.Configuration.GetSection("Agents"))
        .ValidateOnStart();
    builder.Services.AddSingleton<AgentRegistry>();
    builder.Services.AddSingleton<IAgentProtocolAdapterFactory, AgentProtocolAdapterFactory>();
    builder.Services.AddHttpClient<ISessionRunnerClient, SessionRunnerHttpClient>((sp, client) =>
    {
        var runnerSettings = sp.GetRequiredService<IOptions<SessionRunnerSettings>>().Value;
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, runnerSettings.RequestTimeoutSeconds));
    });
    // The /events SSE stream must never hit HttpClient.Timeout (a long-lived response is not a
    // slow request) — liveness is handled by runner keepalives + the client-side idle watchdog.
    builder.Services.AddHttpClient(SessionRunnerHttpClient.EventStreamClientName, client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    });

    // JSON serialization — serialize enums as strings for API responses
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

    // Health checks (NFR21)
    var healthChecks = builder.Services.AddHealthChecks();
    if (!string.IsNullOrEmpty(connectionString))
    {
        healthChecks.AddNpgSql(connectionString, name: "postgresql");
    }

    // ICurrentUser — scoped, resolved by CurrentUserMiddleware per request
    builder.Services.AddScoped<ICurrentUser>(sp =>
    {
        var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
        var currentUser = httpContextAccessor.HttpContext?.Items["CurrentUser"] as ICurrentUser;
        return currentUser ?? throw new InvalidOperationException(
            "ICurrentUser not available. Ensure CurrentUserMiddleware is registered in the pipeline.");
    });
    builder.Services.AddHttpContextAccessor();

    // Application services
    builder.Services.AddScoped<WorkflowTemplateService>();
    builder.Services.AddScoped<WorkspaceHookService>();
    builder.Services.AddScoped<AgentSessionService>();
    // Cancel/escalate on a delegated task must actually stop the delegate, not just relabel the row.
    builder.Services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
    builder.Services.AddScoped<RunAttemptStallDetector>();
    builder.Services.AddScoped<OrchestratorService>();
    builder.Services.AddScoped<ExternalTrackerSyncService>();
    // Delegated agent tasks (feature 007). The reply service is a SINGLETON because the runtime's
    // transcript observer (itself a singleton) calls it on every turn-end; it opens its own scope.
    builder.Services.AddSingleton<DelegationWorkspaceResolver>();
    builder.Services.AddScoped<DelegationWorktreeService>();
    builder.Services.AddScoped<AgentTaskService>();
    builder.Services.AddScoped<AgentTaskDispatcher>();
    builder.Services.AddSingleton<AgentTaskReplyService>();
    // Scheduled check-ins on a running delegate (CARD-0047). The probe is read-only by
    // construction — see its constructor; the queue is the hand-off that keeps the dispatcher's
    // 5 s tick from ever waiting on a check.
    builder.Services.AddScoped<DelegateCheckProbe>();
    builder.Services.AddSingleton<AgentTaskCheckQueue>();
    builder.Services.AddScoped<AgentTaskCheckService>();
    // The standing specialist that interprets a check's bundle (CARD-0047 slice 4). Provisioning is
    // idempotent and self-healing, so it is safe to call at startup and again from any check.
    builder.Services.AddScoped<CheckInterpreterProvisioner>();
    // The "what is stuck" projection (CARD-0035). Read-only — every verb it names is an endpoint
    // that already exists, and it is scoped because it is one query burst per request.
    builder.Services.AddScoped<AttentionService>();
    // The read-only projection over the plan files in the repo (mobile-thread spec §D1). A
    // singleton because its 30s catalog cache is the whole point — a phone polling a thread must
    // not stat two dozen files per tap.
    builder.Services.AddSingleton<PlanCatalogService>();
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<PlanCatalogService>());
    builder.Services.AddScoped<RetryScheduler>();
    builder.Services.AddSingleton<OrchestratorControlState>();
    builder.Services.AddSingleton<AgentSessionLaunchQueue>();
    builder.Services.AddScoped<LlmProviderService>();
    builder.Services.AddScoped<ProjectService>();
    builder.Services.AddScoped<BoardService>();
    builder.Services.AddScoped<CardService>();
    // One card's work gathered from the four places it is recorded, correlated by the identifier
    // everything already cites. Read-only; scoped because it is one query burst per request.
    builder.Services.AddScoped<CardThreadService>();
    builder.Services.AddScoped<CardWorkflowRunFactory>();
    builder.Services.AddScoped<AgentService>();
    builder.Services.AddScoped<AgentControlService>();
    // The CLAUDE.md floor every agent's working directory carries (CARD-0059). Singleton: it holds no
    // state, touches no database and its only dependency is a logger. Idempotent and never-clobbering,
    // so calling it on every create and every launch costs a comparison when nothing has changed.
    builder.Services.AddSingleton<AgentWorkspaceProvisioner>();
    builder.Services.AddScoped<AgentDraftService>();
    builder.Services.AddScoped<CardReviewService>();
    builder.Services.AddSingleton<MentionScanner>();
    builder.Services.AddScoped<AgentChannelService>();
    builder.Services.AddSingleton<AgentMentionRouter>();
    builder.Services.AddSingleton<WatchdogMatcher>();
    builder.Services.AddSingleton<WatchdogCooldownStore>();
    builder.Services.AddScoped<WatchdogService>();
    builder.Services.AddScoped<SessionReconciliationService>();
    builder.Services.AddScoped<AgentSupervisorService>();
    builder.Services.AddScoped<DataRetentionService>();
    builder.Services.AddScoped<SessionHealthService>();
    builder.Services.AddScoped<Antiphon.Server.Application.Interfaces.ISessionHealthActions,
        Antiphon.Server.Infrastructure.Supervision.SessionHealthActions>();
    builder.Services.AddSingleton<Antiphon.Server.Application.Interfaces.IRcBridgeProbe,
        Antiphon.Server.Infrastructure.Supervision.WindowsRcBridgeProbe>();
    builder.Services.AddSingleton<SessionHealthStateStore>();
    builder.Services.AddScoped<Antiphon.Server.Application.Interfaces.IAlertService, AlertService>();
    builder.Services.AddScoped<Antiphon.Server.Application.Interfaces.IAlertRouter, ChannelAlertRouter>();
    builder.Services.AddScoped<AlertDigestFlusher>();
    builder.Services.AddSingleton<AlertThrottle>();
    builder.Services.AddSingleton<RunnerReachabilityState>();
    // Singleton because SessionReconciliationService is scoped — a per-sweep flap counter would
    // reset every 15s and bound nothing (CARD-0056).
    builder.Services.AddSingleton<SessionReAdoptionState>();
    // Same reason, for the dead-session sweep's grace clock: AgentTaskDispatcher is scoped, so a
    // per-tick map would restart the window every 5s and never fire (CARD-0021).
    builder.Services.AddSingleton<DeadSessionFirstSeenState>();
    builder.Services.AddSingleton<WorkflowDefinitionVersionGate>();
    builder.Services.AddScoped<WorkflowDefinitionLoader>();
    builder.Services.AddScoped<WorkflowEngine>();
    builder.Services.AddScoped<CascadeService>();
    // Agent execution — AgentExecutor is the real IStageExecutor; MockExecutor is available for testing.
    // To use MockExecutor instead, change the registration below.
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<LlmClientFactory>();
    builder.Services.AddScoped<IAgentDraftGenerator, AgentDraftGenerator>();
    builder.Services.AddScoped<IStageExecutor, AgentExecutor>();
    builder.Services.AddScoped<IGitService, GitService>();
    builder.Services.AddSingleton(TimeProvider.System);
    // Working-directory autocomplete (directory browsing + atomic dir creation).
    builder.Services.AddSingleton<System.IO.Abstractions.IFileSystem>(new System.IO.Abstractions.FileSystem());
    builder.Services.AddSingleton<IDirectoryLister, FileSystemDirectoryLister>();
    builder.Services.AddSingleton<IDriveProvider, DriveProvider>();
    builder.Services.AddSingleton<IDirectoryWriter, FileSystemDirectoryWriter>();
    builder.Services.AddSingleton<DirectoryBrowseService>();
    // Expose the browse cache for test reset (shared WebApplicationFactory keeps it alive across tests).
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<DirectoryBrowseService>());
    // Slash-command / skill autocomplete catalog (built-ins + ~/.claude + project .claude).
    builder.Services.AddSingleton<IClaudeConfigDirProvider, ClaudeConfigDirProvider>();
    builder.Services.AddSingleton<SlashCommandCatalogService>();
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<SlashCommandCatalogService>());
    builder.Services.AddSingleton<IWorktreeManager, WorktreeManager>();
    builder.Services.AddSingleton<IWorkspaceHookRunner, WorkspaceHookRunner>();
    builder.Services.AddScoped<IWorkflowFileStore, WorkflowFileStore>();
    builder.Services.AddSingleton<IFileSystemWatcher, WorkflowFileSystemWatcher>();
    builder.Services.AddSingleton<AgentSessionRuntime>();
    // Which delivery ceilings are in force, from the pseudoconsole actually serving the ptys
    // (CARD-0037). Must be registered before anything that types into a terminal.
    builder.Services.AddSingleton<PtyDeliveryProfile>();
    builder.Services.AddSingleton<SessionMessageQueueService>();
    // Compaction recovery (incident + workspace re-read note); dispatched lazily from the runtime
    // on CompactBoundary transcript entries.
    builder.Services.AddSingleton<CompactionRecoveryService>();
    builder.Services.AddSingleton<TranscriptBindingIncidentService>();
    // Same-sender inbound debounce for the channel bridge (host-constructed service — an
    // unregistered dependency here fails at startup, not at first message).
    builder.Services.AddSingleton<ChannelInboundDebouncer>();

    // Channel bridge: external chats (Telegram via the messaging gateway; more providers later) mapped
    // to agents. The Kafka client + reply dispatcher are always registered (construction is lazy and
    // connection-free); the consuming hosted service only runs when ChannelBridge:Enabled is true.
    builder.Services.Configure<ChannelBridgeSettings>(
        builder.Configuration.GetSection(ChannelBridgeSettings.SectionName));
    builder.Services.AddAntiphonMessaging(builder.Configuration);
    builder.Services.AddScoped<ChatChannelService>();
    builder.Services.AddSingleton<ChannelReplyDispatcher>();
    builder.Services.AddSingleton<GitWorkspaceService>();
    // Workspace switcher lookups (repo root / branch / worktree list), TTL-cached.
    builder.Services.AddSingleton<WorkspaceInfoService>();
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<WorkspaceInfoService>());
    builder.Services.AddScoped<AgentFilesService>();
    builder.Services.AddScoped<AgentReviewCheckpointService>();
    builder.Services.AddScoped<ReviewThreadService>();
    builder.Services.AddSingleton<ReviewReplyDispatcher>();
    if (builder.Configuration.GetValue<bool>($"{ChannelBridgeSettings.SectionName}:Enabled"))
        builder.Services.AddHostedService<ChannelBridgeService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddSingleton<AgentTuiRunnerCatalog>();
    builder.Services.AddSingleton<AgentTuiMetrics>();
    builder.Services.AddSingleton<AgentTuiSecretIdempotencyCache>();
    builder.Services.AddSingleton<RunnerProcessReaper>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<RunnerProcessReaper>());
    builder.Services.AddSingleton<IRunnerProcessProbe, RunnerProcessProbe>();
    builder.Services.AddSingleton<AgentTuiOperationCoordinator>();
    builder.Services.AddScoped<AgentTuiProfileService>();
    builder.Services.AddScoped<AgentTuiProfileImporter>();
    builder.Services.AddScoped<AgentTuiLaunchResolver>();
    builder.Services.AddScoped<CostTrackingService>();
    builder.Services.AddScoped<FeatureStatusService>();

    // GitHub integration (FR59-FR64) — feature-flagged per project
    builder.Services.AddHttpClient<IGitHubService, GitHubService>();
    builder.Services.AddHttpClient<GitHubIssuesTracker>();
    builder.Services.AddHttpClient<LinearTracker>();
    builder.Services.AddHttpClient<JiraTracker>();
    builder.Services.AddScoped<IIssueTracker>(sp => sp.GetRequiredService<GitHubIssuesTracker>());
    builder.Services.AddScoped<IIssueTracker>(sp => sp.GetRequiredService<LinearTracker>());
    builder.Services.AddScoped<IIssueTracker>(sp => sp.GetRequiredService<JiraTracker>());
    builder.Services.AddSingleton<GitHubRepoCache>();
    builder.Services.AddHostedService<GitHubRepoCacheWarmupService>();
    // Background services for GitHub PR monitoring and external change detection
    builder.Services.AddHostedService<GitHubMonitorService>();
    builder.Services.AddHostedService<ChangeDetectionService>();
    builder.Services.AddHostedService<WorktreeJanitorHostedService>();
    builder.Services.AddHostedService<RunAttemptStallHostedService>();
    builder.Services.AddHostedService<WatchdogHostedService>();
    builder.Services.AddHostedService<SessionReconciliationHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.AgentSupervisorHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.SessionHealthHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.AlertDigestFlushHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.DataRetentionHostedService>();
    builder.Services.AddHostedService<OrchestratorTickHostedService>();
    builder.Services.AddHostedService<AgentTaskDispatcherHostedService>();
    builder.Services.AddHostedService<AgentTaskCheckHostedService>();
    // One-shot: re-prices tasks costed before CARD-0023, so the per-root ceiling stops reading
    // ~10x-inflated history. No-ops once every row carries the current pricing version.
    builder.Services.AddHostedService<DelegationCostBackfillService>();
    builder.Services.AddHostedService<WorkflowFileWatcherHostedService>();
    builder.Services.AddHostedService<SessionRunnerEventPump>();

    // HttpClient for provider connectivity testing
    builder.Services.AddHttpClient();

    // SignalR — real-time communication (NFR4: sub-1s push)
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IEventBus, EventBus>();

    // OpenTelemetry tracing (NFR20)
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("Antiphon"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter());

    var app = builder.Build();

    // Arm the alert log tap (no-op unless Alerts:LogTap:Enabled).
    {
        var logTap = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<AlertsSettings>>().Value.LogTap;
        Antiphon.Server.Infrastructure.Supervision.AlertingLogSink.Instance.Attach(
            app.Services, logTap.Enabled, logTap.MinLevel);
    }

    // Fail-fast on agent DI graph (E02): resolves AgentRegistry + IAgentProtocolAdapterFactory
    // and runs ValidateOnStart for AgentRegistrySettings. Throws here rather than at first use.
    _ = app.Services.GetRequiredService<AgentRegistry>();
    _ = app.Services.GetRequiredService<IAgentProtocolAdapterFactory>();

    // Settle the delivery ceilings BEFORE anything can type into a terminal (CARD-0037). Resolved
    // lazily this would leave a window where the first deliveries used this process's own guess at
    // the backend while the runner's contradicting answer was still in flight — and that guess
    // being wrong is a 43 KB body typed into a pty that clips at 1 KB. Best-effort: an unreachable
    // runner is not evidence, so the local decision stands and the profile re-probes on its own.
    await app.Services.GetRequiredService<PtyDeliveryProfile>().RefreshAsync(CancellationToken.None);

    // Middleware pipeline order: CorrelationId → CurrentUser → ExceptionHandler → routing → endpoints
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<CurrentUserMiddleware>();
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseMiddleware<AuditMiddleware>();

    // Create database if it doesn't exist, then migrate and seed.
    // Wrapped in try-catch: in managed environments (k8s, shared postgres) the app user
    // may not have access to the postgres admin DB or CREATEDB privilege — that's fine if
    // the database already exists.
    var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    try
    {
        var masterConnStr = new NpgsqlConnectionStringBuilder(rawConnectionString) { Database = "postgres" }.ConnectionString;
        var targetDb = new NpgsqlConnectionStringBuilder(rawConnectionString).Database;
        await using var adminConn = new NpgsqlConnection(masterConnStr);
        await adminConn.OpenAsync();
        await using var checkCmd = new NpgsqlCommand(
            $"SELECT 1 FROM pg_database WHERE datname = '{targetDb}'", adminConn);
        var exists = await checkCmd.ExecuteScalarAsync() is not null;
        if (!exists)
        {
            Log.Information("Creating database {Database}", targetDb);
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{targetDb}\"", adminConn);
            await createCmd.ExecuteNonQueryAsync();
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not auto-create database (may already exist or user lacks CREATEDB permission) — continuing");
    }

    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var llmSettings = scope.ServiceProvider.GetRequiredService<IOptions<LlmSettings>>().Value;
        dbContext.Database.Migrate();
        await DatabaseSeeder.SeedAsync(dbContext, llmSettings, CancellationToken.None);
        var agentTuiMetrics = scope.ServiceProvider.GetRequiredService<AgentTuiMetrics>();
        Antiphon.Server.Application.Dtos.AgentTuiImportResultDto profileImport;
        try
        {
            profileImport = await scope.ServiceProvider.GetRequiredService<AgentTuiProfileImporter>()
                .ImportAsync(CancellationToken.None);
            agentTuiMetrics.RecordImport(profileImport);
        }
        catch
        {
            agentTuiMetrics.RecordImportFailure();
            throw;
        }
        if (profileImport.ProfilesCreated > 0 || profileImport.AgentsAssigned > 0)
        {
            Log.Information(
                "Imported {ProfileCount} agent TUI profile(s) and assigned {AgentCount} legacy agent(s)",
                profileImport.ProfilesCreated,
                profileImport.AgentsAssigned);
        }
        // Every agent must have a default board (Add-Work and card routing rely on it) — create
        // boards for any agent that predates the rule or lost its link to the old update path.
        var backfilled = await scope.ServiceProvider.GetRequiredService<AgentService>()
            .EnsureAgentBoardsAsync(CancellationToken.None);
        if (backfilled > 0)
            Log.Information("Backfilled default boards for {Count} agent(s)", backfilled);
    }

    // Health check endpoint (replaces simple /api/health from Story 1.1)
    app.MapHealthChecks("/health");

    // API endpoints
    app.MapSettingsEndpoints();
    app.MapProjectEndpoints();
    app.MapBoardEndpoints();
    app.MapCardEndpoints();
    app.MapAgentEndpoints();
    app.MapAgentTuiEndpoints();
    app.MapChannelEndpoints();
    app.MapWorkflowEndpoints();
    app.MapGateEndpoints();
    app.MapCascadeEndpoints();
    app.MapArtifactEndpoints();
    app.MapAuditEndpoints();
    app.MapGitHubEndpoints();
    app.MapSessionEndpoints();
    app.MapOrchestratorEndpoints();
    app.MapAgentTaskEndpoints();
    app.MapAttentionEndpoints();
    app.MapPlanEndpoints();
    app.MapFileSystemEndpoints();
    app.MapReviewEndpoints();

    // SignalR hub
    app.MapHub<AntiphonHub>("/hubs/antiphon");

    // SPA fallback for production (serves React build from wwwroot)
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");

    Log.Information("Antiphon server starting");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Antiphon server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed as public so test projects can use WebApplicationFactory<Program> with public
// subclasses (e.g. the shared AntiphonWebAppFactory). The top-level program otherwise emits
// an internal Program class.
public partial class Program { }
