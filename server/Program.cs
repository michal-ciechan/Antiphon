using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;

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

    // OpenTelemetry tracing (NFR20)
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("Antiphon"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter());

    var app = builder.Build();

    // Middleware pipeline order: CorrelationId → ExceptionHandler → routing → endpoints
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionMiddleware>();

    // Auto-migrate database on startup
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();
    }

    // Health check endpoint (replaces simple /api/health from Story 1.1)
    app.MapHealthChecks("/health");

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
