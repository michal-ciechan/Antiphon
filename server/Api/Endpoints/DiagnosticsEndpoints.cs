using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;

namespace Antiphon.Server.Api.Endpoints;

public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        var diagnostics = app.MapGroup("/api/diagnostics").WithTags("Diagnostics");

        diagnostics.MapGet("/session-state", (SessionStateStore states, SessionStateCommandMetrics commands,
            AgentSessionRuntime runtime, AppDbContext db) =>
        {
            using var process = Process.GetCurrentProcess();
            // Only these sanitized flags leave the process; never serialize the connection string.
            var flags = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            return Results.Ok(new
            {
                recordedAt = DateTimeOffset.UtcNow, processId = process.Id, processStartedAt = process.StartTime.ToUniversalTime(),
                cpuSeconds = process.TotalProcessorTime.TotalSeconds, workingSetBytes = process.WorkingSet64,
                managedBytes = GC.GetTotalMemory(false), gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                liveSessions = runtime.ListLiveSessions().Count, unknownSessions = runtime.ListUnknownSessions().Count,
                cache = states.GetMetrics(), efReadAttempts = commands.Snapshot(),
                driverVersion = typeof(NpgsqlConnection).Assembly.GetName().Version?.ToString(),
                providerVersion = typeof(NpgsqlDbContextOptionsBuilderExtensions).Assembly.GetName().Version?.ToString(),
                pool = new { flags.Pooling, flags.NoResetOnClose, flags.MaxAutoPrepare, flags.Multiplexing }
            });
        });

        diagnostics.MapPost("/bundle", async (
            BugReportRequest request,
            DiagnosticsBundleService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var clientSha = http.Request.Headers[DiagnosticsBundleService.ClientShaHeader].FirstOrDefault();
            var stream = await service.BuildAsync(request, clientSha, ct);
            var name = $"antiphon-bug-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
            return Results.File(stream, "application/zip", name);
        });
    }
}
