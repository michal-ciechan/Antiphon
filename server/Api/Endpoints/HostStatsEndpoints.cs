using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public static class HostStatsEndpoints
{
    public static void MapHostStatsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/hosts/stats", () => Results.StatusCode(501));
        app.MapGet("/api/hosts/{hostId}/stats/series", () => Results.StatusCode(501));
        return;
        var hosts = app.MapGroup("/api/hosts").WithTags("Hosts");
        hosts.MapGet("/stats", (HostStatsCache cache) => Results.Ok(cache.Project()));
        hosts.MapGet("/{hostId}/stats/series", async (
            string hostId, string? metric, string? window, PhoneHomeRunnerDirectory directory,
            IOptions<HostStatsSettings> settings, TimeProvider time, CancellationToken ct) =>
        {
            if (metric is not ("cpu" or "load" or "memory" or "tasks")
                || window is not ("1m" or "5m" or "15m" or "30m"))
                return Results.BadRequest(new { code = "invalid_host_stats_query" });
            if (!directory.KnownRunnerIds.Contains(hostId, StringComparer.Ordinal))
                return Results.NotFound();
            using var deadline = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(settings.Value.SeriesTimeoutMs), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            try
            {
                var answer = await directory.Resolve(hostId).GetHostSeriesAsync(metric, window, linked.Token);
                if (answer is null)
                    throw Unavailable();
                return Results.Ok(new HostSeriesDto(hostId, answer.Metric, answer.Window,
                    answer.IntervalSeconds, answer.Points));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw Unavailable();
            }
        });
    }

    private static ConflictException Unavailable() =>
        new("Host stats series is unavailable from this runner.", PhoneHomeProblemTypes.Unavailable);
}
