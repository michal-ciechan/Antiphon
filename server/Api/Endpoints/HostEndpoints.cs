using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

public static class HostEndpoints
{
    public static void MapHostEndpoints(this WebApplication app)
    {
        var hosts = app.MapGroup("/api/hosts").WithTags("Hosts");
        hosts.MapGet("", async (HostBudgetService budgets, PhoneHomeRunnerDirectory directory,
            AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var limits = await budgets.ListAsync(ct);
            var prep = http.RequestServices.GetService<RemoteWorkspacePreparer>();
            var rows = new List<HostBudgetDto>(limits.Count);
            foreach (var limit in limits)
                rows.Add(await ProjectAsync(limit, directory, db, prep, ct));
            return Results.Ok(rows);
        });

        hosts.MapPut("/{hostId}/budget", async (string hostId, PutHostBudgetRequest body,
            HostBudgetService budgets, PhoneHomeRunnerDirectory directory, AppDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var limit = await budgets.UpsertAsync(hostId, body.MaxInFlight, body.Reason, ct);
            var prep = http.RequestServices.GetService<RemoteWorkspacePreparer>();
            return Results.Ok(await ProjectAsync(limit, directory, db, prep, ct));
        });

        // CARD-1079 D-12: read-only audit. No write route. Unknown hosts 404 through the budget lookup.
        hosts.MapGet("/{hostId}/occupancy-samples", GetOccupancySamplesAsync);
    }

    private const int DefaultOccupancySampleLimit = 500;
    private const int MaxOccupancySampleLimit = 2000;

    private static async Task<IResult> GetOccupancySamplesAsync(
        string hostId, DateTime? from, DateTime? to, int? limit,
        HostBudgetService budgets, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await budgets.EffectiveAsync(hostId, ct);
        if (limit is < 1)
            return Results.BadRequest(new { error = "limit_invalid" });

        var applied = Math.Min(limit ?? DefaultOccupancySampleLimit, MaxOccupancySampleLimit);
        var windowTo = to is { } rawTo ? AsUtc(rawTo) : clock.GetUtcNow().UtcDateTime;
        DateTime windowFrom;
        if (from is { } rawFrom)
            windowFrom = AsUtc(rawFrom);
        else if (windowTo < DateTime.MinValue.AddHours(24))
            return Results.BadRequest(new { error = "window_invalid" });
        else
            windowFrom = windowTo.AddHours(-24);
        if (windowFrom > windowTo)
            return Results.Ok(new HostOccupancySamplesResponse(applied, windowFrom, windowTo, []));

        var rows = await db.HostOccupancySamples.AsNoTracking()
            .Where(row => row.HostId == hostId && row.SampledAt >= windowFrom && row.SampledAt <= windowTo)
            .OrderByDescending(row => row.SampledAt)
            .ThenByDescending(row => row.Id)
            .Take(applied)
            .ToListAsync(ct);
        return Results.Ok(new HostOccupancySamplesResponse(
            applied, windowFrom, windowTo, rows.Select(ToDto).ToArray()));
    }

    private static HostOccupancySampleDto ToDto(Domain.Entities.HostOccupancySample row) =>
        new(row.Id, row.HostId, row.SampledAt, row.InventoryState, row.InventoryReason,
            row.InFlight, row.DispatchedWorking, row.Sessions, row.PendingLaunch, row.InFlightMirrors,
            row.IdleSeats, row.PooledWarmSeats, row.OrphanSlots, row.EffectiveLimit, row.DeclaredCapacity,
            row.OldestIdleSince);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static async Task<HostBudgetDto> ProjectAsync(
        HostLimit limit, PhoneHomeRunnerDirectory directory, AppDbContext db,
        RemoteWorkspacePreparer? prep, CancellationToken ct)
    {
        if (limit.HostId == "local")
        {
            var active = await db.AgentTasks.AsNoTracking().Where(AgentTaskRoles.NotSpecialist)
                .CountAsync(t => (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                    && !t.CapacityWaitRetained && (t.RunnerId == null || t.RunnerId == ""), ct);
            return new HostBudgetDto(limit.HostId, "local", limit.Configured, null, limit.Effective,
                active, new HostOccupiedBreakdownDto(0, 0, 0), true, true,
                limit.Source, limit.Reason, limit.UpdatedAt, limit.Revision);
        }

        var status = directory.Status(limit.HostId);
        var sessions = await db.AgentSessions.AsNoTracking().CountAsync(s => s.RunnerId == limit.HostId
            && (s.Status == SessionStatus.Created || s.Status == SessionStatus.Starting
                || s.Status == SessionStatus.Running || s.Status == SessionStatus.Stopping), ct);
        var pending = await db.AgentTasks.AsNoTracking().CountAsync(t => t.RunnerId == limit.HostId
            && t.Status == AgentTaskStatus.Queued && t.AgentSessionId == null
            && t.RemoteWorktreePath != null, ct);
        var mirrors = prep?.InFlightCount(limit.HostId, Guid.Empty) ?? 0;
        return new HostBudgetDto(limit.HostId, "runner", limit.Configured, limit.Declared,
            limit.Effective, sessions + pending + mirrors,
            new HostOccupiedBreakdownDto(sessions, pending, mirrors),
            status.Available, status.DispatchEligible && status.AcceptingNewWork,
            limit.Source, limit.Reason, limit.UpdatedAt, limit.Revision);
    }
}
