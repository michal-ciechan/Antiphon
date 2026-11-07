using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

public interface IHostLoadCapacityReader
{
    Task<HostLoadCapacityInput> ReadAsync(string runnerId, Guid? projectId, AgentTaskRole? role, CancellationToken ct);
}
