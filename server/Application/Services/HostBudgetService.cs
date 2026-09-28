using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class HostBudgetService(
    AppDbContext db, ISessionRunnerDirectory runners, IOptions<DelegationSettings> settings,
    TimeProvider clock)
{
    public Task<HostLimit> EffectiveAsync(string hostId, CancellationToken ct) =>
        Task.FromResult(new HostLimit(hostId, null, null, settings.Value.MaxConcurrentTasks, "config"));

    public Task<HostLimit> UpsertAsync(string hostId, int? maxInFlight, string? reason, CancellationToken ct) =>
        EffectiveAsync(hostId, ct);
}
