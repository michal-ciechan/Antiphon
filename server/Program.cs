using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Data.Seeding;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.ExternalChanges;
using Antiphon.Server.Infrastructure.GitHub;
using Antiphon.Server.Infrastructure.Realtime;

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
        lc
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logPath, "antiphon-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            );
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
    builder.Services.AddScoped<LlmProviderService>();
    builder.Services.AddScoped<ProjectService>();
    builder.Services.AddScoped<WorkflowEngine>();
    builder.Services.AddScoped<CascadeService>();
    // Agent execution — AgentExecutor is the real IStageExecutor; MockExecutor is available for testing.
    // To use MockExecutor instead, change the registration below.
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<LlmClientFactory>();
    builder.Services.AddScoped<IStageExecutor, AgentExecutor>();
    builder.Services.AddScoped<IGitService, GitService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddScoped<CostTrackingService>();
    builder.Services.AddScoped<FeatureStatusService>();

    // GitHub integration (FR59-FR64) — feature-flagged per project
    builder.Services.AddHttpClient<IGitHubService, GitHubService>();
    builder.Services.AddSingleton<GitHubRepoCache>();
    builder.Services.AddHostedService<GitHubRepoCacheWarmupService>();
    // Background services for GitHub PR monitoring and external change detection
    builder.Services.AddHostedService<GitHubMonitorService>();
    builder.Services.AddHostedService<ChangeDetectionService>();

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
    }

    // Health check endpoint (replaces simple /api/health from Story 1.1)
    app.MapHealthChecks("/health");

    // API endpoints
    app.MapSettingsEndpoints();
    app.MapProjectEndpoints();
    app.MapWorkflowEndpoints();
    app.MapGateEndpoints();
    app.MapCascadeEndpoints();
    app.MapArtifactEndpoints();
    app.MapAuditEndpoints();
    app.MapGitHubEndpoints();

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
