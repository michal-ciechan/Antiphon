using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

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

public sealed class HostStatsAntiphonCounters(AppDbContext db) : IHostStatsAntiphonCounters
{
    public Task<IReadOnlyDictionary<string, HostStatsAntiphonDto>> ReadAsync(CancellationToken ct) =>
        throw new NotImplementedException();

    public static IReadOnlyDictionary<string, HostStatsAntiphonDto> Group(IEnumerable<HostStatsTaskRow> rows) =>
        throw new NotImplementedException();
}
