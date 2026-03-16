using Microsoft.EntityFrameworkCore;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
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

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// SPA fallback for production (serves React build from wwwroot)
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
