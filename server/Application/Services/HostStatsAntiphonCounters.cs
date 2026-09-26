using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed record HostStatsTaskRow(
    AgentTaskRole Role,
    AgentKind AgentKind,
    string? RunnerId,
    AgentTaskStatus Status,
    bool CapacityWaitRetained,
    bool HasLand);

public interface IHostStatsAntiphonCounters
{
    Task<IReadOnlyDictionary<string, HostStatsAntiphonDto>> ReadAsync(CancellationToken ct);
}

public sealed class HostStatsAntiphonCounters(IServiceScopeFactory scopes) : IHostStatsAntiphonCounters
{
    public async Task<IReadOnlyDictionary<string, HostStatsAntiphonDto>> ReadAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // One materialization over the small open-task set. Never load cards or session history.
        var rows = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked)
            .Select(t => new HostStatsTaskRow(t.Role, t.AgentKind, t.RunnerId, t.Status,
                t.CapacityWaitRetained, t.LandRequestedAt != null))
            .ToListAsync(ct);
        return Group(rows);
    }

    public static IReadOnlyDictionary<string, HostStatsAntiphonDto> Group(IEnumerable<HostStatsTaskRow> rows) =>
        rows.GroupBy(r => string.IsNullOrWhiteSpace(r.RunnerId) ? "desktop" : r.RunnerId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g =>
            {
                var active = g.Where(r => r.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working).ToArray();
                return new HostStatsAntiphonDto(active.Length,
                    active.GroupBy(r => r.Role.ToString()).ToDictionary(x => x.Key, x => x.Count()),
                    active.GroupBy(r => r.AgentKind.ToString()).ToDictionary(x => x.Key, x => x.Count()),
                    g.Count(r => r.Status == AgentTaskStatus.Queued),
                    g.Count(r => r.CapacityWaitRetained),
                    g.Count(r => r.HasLand), null, null, null);
            }, StringComparer.Ordinal);
}
