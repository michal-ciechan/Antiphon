using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.InMemory;
using Microsoft.AspNetCore.ResponseCompression;
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

// Startup writes PROCESS-GLOBAL state: Serilog's static Log.Logger. WebApplicationFactory runs
// this entry point once per factory, and the test assembly holds several of them (the shared
// AntiphonWebAppFactory, its per-suite subclasses, and SmokeTests' own bare factory), so two
// invocations can be inside startup at the same time. The bootstrap logger is a ReloadableLogger
// parked on Log.Logger and builder.Build() FREEZES it: interleaved, the second invocation
// overwrites Log.Logger, the first freezes what the second parked there, and the second's Build()
// throws "The logger is already frozen." The gate covers assignment-through-Build only - it is
// released before app.Run() blocks - and is a no-op for the single invocation a real server makes.
//
// It covers the whole of startup, not just Build(), because the same overlap breaks the seeder:
// DatabaseSeeder is check-then-insert against a database both invocations share, so a second one
// arriving mid-seed inserts a row the first already inserted and Postgres answers 23505 on
// PK_TemplateGroups. Cross-PROCESS seeding is still unguarded - that wants a pg advisory lock
// around migrate+seed, which this gate is not.
Program.StartupGate.Wait();
var startupGateHeld = true;

try
{
    // Bootstrap Serilog for startup logging (before host is built)
    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateBootstrapLogger();

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
    builder.Services.Configure<ProjectsSettings>(builder.Configuration.GetSection("Projects"));
    builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection("Llm"));
    builder.Services.Configure<SignalRSettings>(builder.Configuration.GetSection("SignalR"));
    builder.Services.Configure<AuditSettings>(builder.Configuration.GetSection("Audit"));
    builder.Services.Configure<GithubSettings>(builder.Configuration.GetSection("GitHub"));
    builder.Services.Configure<SessionRunnerSettings>(builder.Configuration.GetSection("SessionRunner"));
    builder.Services.Configure<DiagnosticsSettings>(builder.Configuration.GetSection("Diagnostics"));
    builder.Services.Configure<DeliverablesSettings>(
        builder.Configuration.GetSection(DeliverablesSettings.SectionName));
    builder.Services.AddSingleton<IValidateOptions<AgentSessionSettings>, AgentSessionSettingsValidator>();
    builder.Services.AddOptions<AgentSessionSettings>()
        .Bind(builder.Configuration.GetSection("AgentSessions"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<OrchestratorSettings>, OrchestratorSettingsValidator>();
    builder.Services.AddOptions<OrchestratorSettings>()
        .Bind(builder.Configuration.GetSection("Orchestrator"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<DelegationSettings>, DelegationSettingsValidator>();
    builder.Services.AddOptions<DelegationSettings>()
        .Bind(builder.Configuration.GetSection("Delegation"))
        .ValidateOnStart();

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
    builder.Services.AddSingleton<IValidateOptions<HangfireSettings>, HangfireSettingsValidator>();
    builder.Services.AddOptions<HangfireSettings>()
        .Bind(builder.Configuration.GetSection("Hangfire"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<ZombieCensusSettings>, ZombieCensusSettingsValidator>();
    builder.Services.AddOptions<ZombieCensusSettings>()
        .Bind(builder.Configuration.GetSection("ZombieCensus"))
        .ValidateOnStart();
    // CARD-0040: cards move themselves from the delegated work bound to them.
    builder.Services.Configure<CardWorkTransitionSettings>(builder.Configuration.GetSection("CardTransitions"));
    builder.Services.AddSingleton<IValidateOptions<ScheduleSettings>, ScheduleSettingsValidator>();
    builder.Services.AddOptions<ScheduleSettings>()
        .Bind(builder.Configuration.GetSection(ScheduleSettings.SectionName))
        .ValidateOnStart();
    // CARD-0004: card → docs/cards/<slug>/ files. AutoCommit defaults false — do not flip it;
    // a human dryRun then a manual sync must land before anyone turns committing on.
    builder.Services.Configure<CardFileSyncSettings>(builder.Configuration.GetSection(CardFileSyncSettings.SectionName));
    builder.Services.Configure<ParkedMessageSweepSettings>(builder.Configuration.GetSection("ParkedMessages"));
    builder.Services.AddSingleton<IValidateOptions<SupervisionSettings>, SupervisionSettingsValidator>();
    builder.Services.AddOptions<SupervisionSettings>()
        .Bind(builder.Configuration.GetSection("Supervision"))
        .ValidateOnStart();
    builder.Services.Configure<SubscriptionUsageMonitoringSettings>(
        builder.Configuration.GetSection("SubscriptionUsageMonitoring"));
    builder.Services.AddSingleton<IValidateOptions<SubscriptionQuotaGateSettings>, SubscriptionQuotaGateSettingsValidator>();
    builder.Services.AddOptions<SubscriptionQuotaGateSettings>()
        .Bind(builder.Configuration.GetSection("SubscriptionQuotaGate"))
        .ValidateOnStart();
    builder.Services.Configure<TranscriptBindingSettings>(builder.Configuration.GetSection("TranscriptBinding"));
    builder.Services.Configure<ContextWindowSettings>(builder.Configuration.GetSection("ContextWindow"));
    builder.Services.Configure<CardsSettings>(builder.Configuration.GetSection("Cards"));
    builder.Services.AddSingleton<IValidateOptions<ContextCompactionSettings>, ContextCompactionSettingsValidator>();
    builder.Services.AddOptions<ContextCompactionSettings>()
        .Bind(builder.Configuration.GetSection("ContextCompaction"))
        .ValidateOnStart();
    builder.Services.Configure<AlertsSettings>(builder.Configuration.GetSection("Alerts"));
    builder.Services.AddSingleton<IValidateOptions<DigestSettings>, DigestSettingsValidator>();
    builder.Services.AddOptions<DigestSettings>()
        .Bind(builder.Configuration.GetSection("Digest"))
        .ValidateOnStart();
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

    // JSON serialization — serialize enums as strings for API responses.
    // Integer tokens are rejected: a numeric modelLevel used to bind as the enum ordinal
    // (0 → Frontier, 99 → an undefined value that round-tripped as a number).
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
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
    builder.Services.AddScoped<AgentSessionLaunchComposer>();
    // Cancel/escalate on a delegated task must actually stop the delegate, not just relabel the row.
    builder.Services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
    builder.Services.AddScoped<RunAttemptStallDetector>();
    builder.Services.AddScoped<OrchestratorService>();
    builder.Services.AddScoped<ExternalTrackerSyncService>();
    // Delegated agent tasks (feature 007). The reply service is a SINGLETON because the runtime's
    // transcript observer (itself a singleton) calls it on every turn-end; it opens its own scope.
    builder.Services.AddSingleton<DelegationWorkspaceResolver>();
    // CARD-0063: each repo's antiphon.areas.json. A SINGLETON so its per-path, mtime-keyed cache
    // survives between the dispatcher's 5 s scopes; it can never fail a dispatch (a missing or
    // malformed map degrades to "no names known").
    builder.Services.AddSingleton<AreaMapLoader>();
    builder.Services.AddScoped<DelegationWorktreeService>();
    builder.Services.AddScoped<DelegationOpenGate>();
    builder.Services.AddScoped<WorktreeHealthService>();
    builder.Services.AddScoped<AgentTaskService>();
    builder.Services.AddScoped<AgentTaskPipelineStatusService>();
    builder.Services.AddSingleton<AgentTaskLandQueue>();
    builder.Services.AddScoped<AgentTaskLandService>();
    builder.Services.AddScoped<StageOutcomeService>();
    // CARD-0140 S3: AgentTuiLaunchResolver is already AddScoped below; the dispatcher's optional
    // constructor parameter picks it up so a pinned standing agent launches from its own profile.
    builder.Services.AddScoped<AgentTaskDispatcher>();
    builder.Services.AddSingleton<AgentTaskReplyService>();
    // Scheduled check-ins on a running delegate (CARD-0047). The probe is read-only by
    // construction — see its constructor; the queue is the hand-off that keeps the dispatcher's
    // 5 s tick from ever waiting on a check.
    builder.Services.AddScoped<DelegateCheckProbe>();
    builder.Services.AddSingleton<AgentTaskCheckQueue>();
    builder.Services.AddScoped<AgentTaskCheckService>();
    builder.Services.AddSingleton<ScheduleFireQueue>();
    builder.Services.AddScoped<ScheduleService>();
    // The standing specialist that interprets a check's bundle (CARD-0047 slice 4). Provisioning is
    // idempotent and self-healing, so it is safe to call at startup and again from any check.
    builder.Services.AddScoped<CheckInterpreterProvisioner>();
    // The standing specialist that titles untitled tasks and labels unlabelled cards (CARD-0352).
    // Same substrate as the check interpreter. Job 1 (auto-title) drains DiagnoseQueue; job 2
    // (auto-label) lands in S4 on the same queue.
    builder.Services.AddScoped<DiagnoseProvisioner>();
    builder.Services.AddSingleton<DiagnoseQueue>();
    builder.Services.AddScoped<DiagnoseService>();
    // The "what is stuck" projection (CARD-0035). Read-only — every verb it names is an endpoint
    // that already exists, and it is scoped because it is one query burst per request.
    builder.Services.AddScoped<AttentionService>();
    builder.Services.AddSingleton<AttentionSummaryCache>();
    // The home-rail projection over cards and unbound delegations (CARD-0002). Read-only —
    // bound tasks nest as a card's worker line; stuckness stays AttentionService.
    builder.Services.AddScoped<HomeTaskService>();
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<AttentionSummaryCache>());
    builder.Services.AddScoped<DiagnosticsBundleService>();
    // The read-only projection over the plan files in the repo (mobile-thread spec §D1). A
    // singleton because its 30s catalog cache is the whole point — a phone polling a thread must
    // not stat two dozen files per tap.
    builder.Services.AddSingleton<PlanCatalogService>();
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<PlanCatalogService>());
    builder.Services.AddScoped<RetryScheduler>();
    builder.Services.AddSingleton<OrchestratorControlState>();
    builder.Services.AddSingleton<AgentSessionLaunchQueue>();
    builder.Services.AddSingleton<ILaunchOwnership>(sp => sp.GetRequiredService<AgentSessionLaunchQueue>());
    builder.Services.AddScoped<LlmProviderService>();
    builder.Services.AddScoped<ProjectService>();
    builder.Services.AddScoped<ProjectSetupService>();
    builder.Services.AddScoped<BoardService>();
    builder.Services.AddScoped<CardService>();
    builder.Services.AddScoped<IScheduledCardActions>(sp => sp.GetRequiredService<CardService>());
    builder.Services.AddSingleton<CardTaskFileSyncGate>();
    builder.Services.AddScoped<CardTaskFileService>();
    builder.Services.AddScoped<CardCommentService>();
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
    builder.Services.AddScoped<CardWorkTransitionService>();
    builder.Services.AddScoped<ParkedMessageSweepService>();
    builder.Services.AddSingleton<IZombieProcessCensus, WindowsZombieProcessCensus>();
    builder.Services.AddScoped<ZombieCensusService>();
    builder.Services.AddScoped<ZombieCensusJob>();
    builder.Services.AddScoped<AgentSupervisorService>();
    builder.Services.AddScoped<IAgentIncidentRecorder>(sp => sp.GetRequiredService<AgentSupervisorService>());
    builder.Services.AddScoped<AppHostWatchdogStateAttentionService>();
    builder.Services.AddScoped<ChannelIngressIncidentService>();
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
    builder.Services.AddSingleton<HerdrPendingAlertState>();
    // CARD-0102 / coverage plan P0-3: the pty-host census the reconciliation sweep reports on.
    // Singletons for the same reason SessionReAdoptionState is one - the dedup window is per
    // server uptime, and the probe holds no state at all.
    builder.Services.AddSingleton<IPtyHostCensusProbe, PtyHostCensusProbe>();
    builder.Services.AddSingleton<PtyHostCensusAlertState>();
    // Same reason, for the dead-session sweep's grace clock: AgentTaskDispatcher is scoped, so a
    // per-tick map would restart the window every 5s and never fire (CARD-0021).
    builder.Services.AddSingleton<DeadSessionFirstSeenState>();
    // CARD-0299 S2: skip FailDeadSessionTasksAsync while a boot-wedge relaunch is in flight.
    builder.Services.AddSingleton<BootWedgeRelaunchState>();
    // CARD-0248: the deferred-report sweep's re-hand watermark. Same reason as the dead-session
    // clock — AgentTaskDispatcher is scoped, so an instance map would never suppress a tick.
    builder.Services.AddSingleton<DeferredReportSweepMarks>();
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
    // CARD-0161: per-session ceilings (herdr vs pty). Composes PtyDeliveryProfile.
    builder.Services.AddSingleton<SessionDeliveryProfile>();
    builder.Services.AddSingleton<SessionMessageQueueService>();
    // CARD-0082 S3: idle auto-compact sweep. Singleton because the in-memory per-session attempt
    // stamp has to survive the supervisor hosted service's per-tick scope.
    builder.Services.AddSingleton<ContextCompactionService>();
    builder.Services.AddSingleton<SubscriptionUsageMonitorService>();
    builder.Services.AddScoped<SubscriptionUsageReader>();
    builder.Services.AddScoped<SubscriptionQuotaGate>();
    builder.Services.AddScoped<ModelAvailability>();
    builder.Services.AddScoped<IModelAvailability>(sp => sp.GetRequiredService<ModelAvailability>());
    // CARD-0305: per-card/stage routing pins. Scoped like the availability reader it hands off to.
    builder.Services.AddScoped<RoutingPinService>();
    // CARD-0090: complexity chains. Scoped like the pin/availability readers the walker consumes.
    builder.Services.AddScoped<ComplexityRoutingService>();
    builder.Services.AddScoped<ComplexityChainService>();
    // CARD-0072 S5a: durable API-error retry. Singleton for the same reason as compaction —
    // the supervisor hosted service is a singleton and this is the action it calls.
    builder.Services.AddSingleton<ApiErrorRecoveryService>();
    // CARD-0162: herdr status corroboration (Warning-only; never kills/retypes).
    builder.Services.AddSingleton<HerdrStatusCorroborationService>();
    // CARD-0247 S3: orchestrator investigation detection (Warning-only; never kills/retypes).
    builder.Services.AddSingleton<OrchestratorInvestigationSweepService>();
    // CARD-0292 S4: swallowed-input watchdog (detection only; never kills/types/Escs).
    builder.Services.AddSingleton<QueuedInputWatchdogService>();
    // CARD-0312 S3: the boot-reply watch's sweep (rung 5 of the delivery evidence ladder). Sends
    // nothing — it resolves a watch a launch already armed. Never a periodic probe.
    builder.Services.AddSingleton<BootReplyWatchdogService>();
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
    builder.Services.AddSingleton<IValidateOptions<ChannelBridgeSettings>, ChannelBridgeSettingsValidator>();
    builder.Services.AddOptions<ChannelBridgeSettings>()
        .Bind(builder.Configuration.GetSection(ChannelBridgeSettings.SectionName))
        .ValidateOnStart();
    builder.Services.AddAntiphonMessaging(builder.Configuration);
    builder.Services.AddScoped<ChatChannelService>();
    builder.Services.AddSingleton<ChannelReplyDispatcher>();
    builder.Services.AddSingleton<GitProcessGate>(sp =>
        new GitProcessGate(Math.Max(1, sp.GetRequiredService<IOptions<GitSettings>>().Value.MaxConcurrentProcesses)));
    builder.Services.AddSingleton<GitWorkspaceService>();
    builder.Services.AddSingleton<MarkdownPdfRenderer>();
    builder.Services.AddSingleton<DeliverableBundleService>();
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<ProjectReadinessCache>();
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<ProjectReadinessCache>());
    // CARD-0085: positive-evidence gate before a zero-transcript Failed. Singleton — no DB of
    // its own; the dispatcher (scoped) calls it with the task + session it already loaded.
    builder.Services.AddOptions<DelegateBindRefusalRecoverySettings>();
    builder.Services.AddSingleton<DelegateBindRefusalRecovery>();
    // Workspace switcher lookups (repo root / branch / worktree list), TTL-cached.
    builder.Services.AddSingleton<WorkspaceInfoService>();
    builder.Services.AddSingleton<IResettableCache>(sp => sp.GetRequiredService<WorkspaceInfoService>());
    builder.Services.AddScoped<AgentFilesService>();
    builder.Services.AddScoped<IWorkspaceProgressProbe>(sp => sp.GetRequiredService<AgentFilesService>());
    builder.Services.AddScoped<AgentReviewCheckpointService>();
    builder.Services.AddScoped<ReviewThreadService>();
    builder.Services.AddSingleton<ReviewReplyDispatcher>();
    if (builder.Configuration.GetValue<bool>($"{ChannelBridgeSettings.SectionName}:Enabled"))
    {
        builder.Services.AddHostedService<ChannelBridgeService>();
        builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Messaging.InboundUnconsumedEventConsumer>();
    }
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
    builder.Services.AddScoped<AgentTuiSecretMigrator>();
    builder.Services.AddScoped<AgentTuiLaunchResolver>();
    // CARD-0106: API key store CRUD. The protector itself is registered by
    // AgentTuiDataProtectionSetup.Configure, alongside the key ring both stores share.
    builder.Services.AddScoped<ApiKeyService>();
    // CARD-0106 S2: launch-time placeholder resolution. Scoped, because it reads the key store.
    builder.Services.AddScoped<ApiKeyEnvResolver>();
    // CARD-0166 S2: tracker token_key -> ApiKeys resolution (project then global), env-var fallback.
    builder.Services.AddScoped<TrackerTokenResolver>();
    // CARD-0166 S4+: bidirectional sync (never on the orchestrator tick — trigger endpoints in S7).
    builder.Services.AddScoped<TrackerBidirectionalSyncService>();
    // CARD-0171: opt-in change summary to the board's tracker.notify_channel, after a sync commits.
    builder.Services.AddScoped<TrackerSyncNotifier>();
    builder.Services.AddScoped<AwayDigestProjection>();
    builder.Services.AddScoped<AwayDigestNotifier>();
    builder.Services.AddScoped<BlockedTaskNotifier>();
    builder.Services.AddScoped<DecisionCardNotifier>();
    builder.Services.AddScoped<IncidentPageNotifier>();
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
    builder.Services.AddHostedService<WorktreeHealthHostedService>();
    builder.Services.AddHostedService<RunAttemptStallHostedService>();
    builder.Services.AddHostedService<WatchdogHostedService>();
    builder.Services.AddHostedService<SessionReconciliationHostedService>();
    builder.Services.AddHostedService<
        Antiphon.Server.Infrastructure.Orchestration.CardWorkTransitionHostedService>();
    builder.Services.AddHostedService<
        Antiphon.Server.Infrastructure.Orchestration.CardTaskFileSyncHostedService>();
    builder.Services.AddHostedService<ParkedMessageSweepHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.AgentSupervisorHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.AppHostWatchdogStateHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.SubscriptionUsageMonitorHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.SessionHealthHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.AlertDigestFlushHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.AwayDigestHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Supervision.DataRetentionHostedService>();
    builder.Services.AddHostedService<OrchestratorTickHostedService>();
    builder.Services.AddHostedService<AgentTaskDispatcherHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Orchestration.AgentTaskLandHostedService>();
    builder.Services.AddHostedService<Antiphon.Server.Infrastructure.Orchestration.AgentTaskLandSweepHostedService>();
    builder.Services.AddHostedService<AgentTaskCheckHostedService>();
    builder.Services.AddHostedService<DiagnoseHostedService>();
    builder.Services.AddHostedService<ScheduleSweepHostedService>();
    builder.Services.AddHostedService<ScheduleFireHostedService>();
    // One-shot: re-prices tasks costed before CARD-0023, so the per-root ceiling stops reading
    // ~10x-inflated history. No-ops once every row carries the current pricing version.
    builder.Services.AddHostedService<DelegationCostBackfillService>();
    builder.Services.AddHostedService<StageOutcomeBackfillService>();
    builder.Services.AddHostedService<WorkflowFileWatcherHostedService>();
    builder.Services.AddHostedService<SessionRunnerEventPump>();

    // CARD-0298: Hangfire storage is always registered (dashboard + job serialization). The worker
    // is the dangerous bit — it must not WMI-scan or call the runner from a test Program boot.
    var hangfireSettings = builder.Configuration.GetSection("Hangfire").Get<HangfireSettings>()
        ?? new HangfireSettings();
    builder.Services.AddHangfire(config =>
        config.UseInMemoryStorage(HangfireConfiguration.CreateStorageOptions(hangfireSettings)));
    if (hangfireSettings.ServerEnabled)
    {
        builder.Services.AddHangfireServer(options => options.WorkerCount = 1);
    }

    // HttpClient for provider connectivity testing
    builder.Services.AddHttpClient();

    // SignalR — real-time communication (NFR4: sub-1s push)
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IEventBus, EventBus>();

    // API payloads can be large over the phone connection, but streaming paths must never be
    // buffered. Keep the MIME allowlist deliberately narrow; static assets are served by Vite/Caddy.
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
        options.MimeTypes = ["application/json", "application/problem+json"];
    });

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
    // SignalR includes long-lived streaming transports. Exclude the whole hub route, including
    // negotiate, rather than depending on its current content type to keep it unbuffered.
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/hubs"),
        branch => branch.UseResponseCompression());

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
        var profileImport = new Antiphon.Server.Application.Dtos.AgentTuiImportResultDto(0, 0);
        if (scope.ServiceProvider.GetRequiredService<IOptions<AgentTuiSettings>>()
            .Value.ImportProfilesOnStartup)
        {
            try
            {
                await scope.ServiceProvider
                    .GetRequiredService<AgentTuiSecretMigrator>()
                    .MigrateAsync(CancellationToken.None);
                profileImport = await scope.ServiceProvider
                    .GetRequiredService<AgentTuiProfileImporter>()
                    .ImportAsync(CancellationToken.None);
                agentTuiMetrics.RecordImport(profileImport);
            }
            catch
            {
                agentTuiMetrics.RecordImportFailure();
                throw;
            }
        }
        if (profileImport.ProfilesCreated > 0 || profileImport.AgentsAssigned > 0)
        {
            Log.Information(
                "Imported {ProfileCount} agent TUI profile(s) and assigned {AgentCount} legacy agent(s)",
                profileImport.ProfilesCreated,
                profileImport.AgentsAssigned);
        }
        // Every standing agent must have a default board (Add-Work and card routing rely on it); pool
        // delegates are boardless by design (CARD-0210). Repair standing agents that lost their link.
        var backfilled = await scope.ServiceProvider.GetRequiredService<AgentService>()
            .EnsureAgentBoardsAsync(CancellationToken.None);
        if (backfilled > 0)
            Log.Information("Backfilled default boards for {Count} agent(s)", backfilled);

        var hangfire = scope.ServiceProvider.GetRequiredService<IOptions<HangfireSettings>>().Value;
        var census = scope.ServiceProvider.GetRequiredService<IOptions<ZombieCensusSettings>>().Value;
        if (hangfire.ServerEnabled && census.Enabled)
        {
            var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
            HangfireConfiguration.AddOrUpdateCensusJob(recurringJobManager, census);
        }
    }

    // Health check endpoint (replaces simple /api/health from Story 1.1)
    app.MapHealthChecks("/health");
    // CARD-0179 R3: git SHA identity. Kept off /health because SmokeTests pins that body as
    // the literal "Healthy".
    app.MapVersionEndpoints();

    // API endpoints
    app.MapSettingsEndpoints();
    app.MapProjectEndpoints();
    app.MapApiKeyEndpoints();
    app.MapBoardEndpoints();
    app.MapTrackerSyncEndpoints();
    app.MapCardFileSyncEndpoints();
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
    app.MapModelAvailabilityEndpoints();
    app.MapRoutingPinEndpoints();
    app.MapStageOutcomeEndpoints();
    app.MapComplexityChainEndpoints();
    app.MapScheduleEndpoints();
    app.MapAttentionEndpoints();
    app.MapHomeEndpoints();
    app.MapDigestEndpoints();
    app.MapDiagnosticsEndpoints();
    app.MapPlanEndpoints();
    app.MapFileSystemEndpoints();
    app.MapReviewEndpoints();

    // SignalR hub
    app.MapHub<AntiphonHub>("/hubs/antiphon");

    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new LocalRequestsOnlyAuthorizationFilter()]
    });

    // SPA fallback for production (serves React build from wwwroot)
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");

    Log.Information("Antiphon server starting");

    // Startup is done: the static logger is frozen, migrations are applied and the seed rows
    // exist. Release before app.Run(), which blocks until shutdown.
    Program.StartupGate.Release();
    startupGateHeld = false;

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // RETHROW. A swallowed startup exception exits 0, which lies to every supervisor that watches
    // this process, and under WebApplicationFactory it is worse than silent: the entry point
    // returns without ever starting the host, the factory caches the unstarted TestServer, and
    // every later test on that shared factory fails with "The server has not been started or no
    // web application was configured" - naming nothing, forever. HostAbortedException is excluded
    // so `dotnet ef` design-time host resolution still unwinds cleanly.
    Log.Fatal(ex, "Antiphon server terminated unexpectedly");
    throw;
}
finally
{
    if (startupGateHeld)
        Program.StartupGate.Release();

    Log.CloseAndFlush();
}

// Exposed as public so test projects can use WebApplicationFactory<Program> with public
// subclasses (e.g. the shared AntiphonWebAppFactory). The top-level program otherwise emits
// an internal Program class.
public partial class Program
{
    /// <summary>
    /// Serialises the process-global part of startup (see the comment at the top of this file).
    /// Held from the bootstrap-logger assignment until <c>builder.Build()</c> returns, never across
    /// <c>app.Run()</c>. Uncontended - and therefore free - for a real server, which invokes this
    /// entry point once.
    /// </summary>
    internal static readonly SemaphoreSlim StartupGate = new(1, 1);
}
