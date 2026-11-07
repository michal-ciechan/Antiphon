using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.HostLoad;

public sealed class HostLoadCapacityReader(AppDbContext db, HostBudgetService budgets,
    ISessionRunnerDirectory directory, TimeProvider time, DispatchConcurrencySettingsService? concurrency = null) : IHostLoadCapacityReader
{
    public async Task<HostLoadCapacityInput> ReadAsync(string runnerId, Guid? projectId, AgentTaskRole? role, CancellationToken ct)
    {
        var canonical = HostLoadCounterCache.Canonical(runnerId);
        var open = await db.AgentTasks.AsNoTracking().Where(t => t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
            .Select(t => new { t.RunnerId, t.ProjectId, t.Role }).ToListAsync(ct);
        var host = await budgets.EffectiveAsync(canonical == "desktop" ? "local" : canonical, ct);
        var hostOccupied = open.Count(t => HostLoadCounterCache.Canonical(t.RunnerId ?? "desktop") == canonical);
        var descriptor = await directory.DescribeAsync(canonical, ct);
        var runnerRemaining = descriptor is { Available: true, DispatchEligible: true, Stale: false, Capacity: { } capacity }
            ? Math.Max(0, capacity - hostOccupied) : (int?)null;
        int? projectRemaining = null, stageRemaining = null;
        if (projectId is { } project && role is { } selected && concurrency is not null)
        {
            var policy = await concurrency.ReadEffectiveAsync(project, ct);
            projectRemaining = Math.Max(0, policy.MaxParallel - open.Count(t => t.ProjectId == project));
            var roleLimit = policy.CombinedParallel(selected);
            stageRemaining = roleLimit is null ? projectRemaining
                : Math.Max(0, roleLimit.Value - open.Count(t => t.ProjectId == project && t.Role == selected));
        }
        return new(time.GetUtcNow(), host.Effective is { } limit ? Math.Max(0, limit - hostOccupied) : null,
            runnerRemaining, projectRemaining, stageRemaining);
    }
}
