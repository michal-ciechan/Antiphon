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
            DbCommandMetrics dbCommands, AgentSessionRuntime runtime, AppDbContext db) =>
        {
            using var process = Process.GetCurrentProcess();
            // Only these sanitized flags leave the process; never serialize the connection string.
            var flags = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            var prompt = TranscriptPromptSpan.PromptCache.Shared.ReadOutcomes();
            var promptCache = TranscriptPromptSpan.PromptCache.Shared.Snapshot();
            return Results.Ok(new
            {
                recordedAt = DateTimeOffset.UtcNow, processId = process.Id, processStartedAt = process.StartTime.ToUniversalTime(),
                cpuSeconds = process.TotalProcessorTime.TotalSeconds, workingSetBytes = process.WorkingSet64,
                managedBytes = GC.GetTotalMemory(false), gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                liveSessions = runtime.ListLiveSessions().Count, unknownSessions = runtime.ListUnknownSessions().Count,
                cache = states.GetMetrics(), efReadAttempts = commands.Snapshot(),
                // CARD-1403 S0: every command by allowlisted call-site tag; never SQL or parameters.
                dbCommands = dbCommands.Read(),
                promptSpan = new
                {
                    prompt.Bypass, prompt.Cold, prompt.Hit, prompt.Tail,
                    prompt.FullAfterProof, prompt.TooLargeToStore,
                    entries = promptCache.Entries, chars = promptCache.Chars
                },
                driverVersion = typeof(NpgsqlConnection).Assembly.GetName().Version?.ToString(),
                providerVersion = typeof(NpgsqlDbContextOptionsBuilderExtensions).Assembly.GetName().Version?.ToString(),
                pool = new { flags.Pooling, flags.NoResetOnClose, flags.MaxAutoPrepare, flags.Multiplexing }
            });
        });

        diagnostics.MapGet("/db-rate", DbRate);

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

    private static IResult DbRate(int? minutes, DbRateWindow window)
    {
        var n = minutes ?? 60;
        if (n is < 1 or > DbRateWindow.RingMinutes)
            return Results.BadRequest(new { error = "minutes must be from 1 to 1440" });
        var report = window.Report(n, DateTimeOffset.UtcNow);
        return Results.Ok(new
        {
            minutes = n,
            rollups = report.Rollups.Select(r => new
            {
                minutes = r.Minutes, qps = r.Qps, dbMsPerSecond = r.DbMsPerSecond, rowsPerSecond = r.RowsPerSecond
            }),
            series = report.Series.Select(m => new
            {
                at = m.At, qps = m.Qps, dbMsPerSecond = m.DbMsPerSecond, rowsPerSecond = m.RowsPerSecond
            }),
            top = new
            {
                byCalls = report.Top.ByCalls.Select(Statement),
                byTime = report.Top.ByTime.Select(Statement)
            },
            byTag = report.ByTag.Select(o => new
            {
                operation = o.Operation, qps = o.Qps, dbMsPerSecond = o.DbMsPerSecond,
                rowsPerSecond = o.RowsPerSecond, calls = o.Calls, meanMs = o.MeanMs
            }),
            gaps = report.Gaps.Select(g => new { at = g.At, reason = g.Reason }),
            dbCommands = new { totalPerSecond = report.DbCommands.TotalPerSecond, untaggedPerSecond = report.DbCommands.UntaggedPerSecond },
            callSites = new
            {
                unknownShare = report.CallSites.UnknownShare,
                top = report.CallSites.Top.Select(s => new { site = s.Site, samples = s.Samples, share = s.Share })
            },
            transactions = new
            {
                beginPerSecond = report.Transactions.BeginPerSecond,
                commitPerSecond = report.Transactions.CommitPerSecond,
                rollbackPerSecond = report.Transactions.RollbackPerSecond,
                savepointPerSecond = report.Transactions.SavepointPerSecond,
                releasePerSecond = report.Transactions.ReleasePerSecond
            }
        });

        static object Statement(StatementRate row) => new
        {
            label = row.Label, calls = row.Calls, qps = row.Qps, dbMsPerSecond = row.DbMsPerSecond,
            rowsPerSecond = row.RowsPerSecond, meanMs = row.MeanMs
        };
    }
}
