using Microsoft.EntityFrameworkCore;
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
using Antiphon.Server.Infrastructure.Realtime;

// Bootstrap Serilog for startup logging (before host is built)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog — structured logging with correlation enrichment (NFR19)
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        .WriteTo.File("logs/antiphon-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

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
    builder.Services.AddScoped<IStageExecutor, MockExecutor>();
    builder.Services.AddScoped<IGitService, GitService>();

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

    // Auto-migrate database on startup and seed data
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();
        await DatabaseSeeder.SeedAsync(dbContext, CancellationToken.None);
    }

    // Health check endpoint (replaces simple /api/health from Story 1.1)
    app.MapHealthChecks("/health");

    // API endpoints
    app.MapSettingsEndpoints();
    app.MapProjectEndpoints();
    app.MapWorkflowEndpoints();

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
