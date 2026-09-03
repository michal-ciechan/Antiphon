using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Read side of <see cref="Services.ModelAvailability"/> (CARD-0352 S3). Diagnose checks a
/// hold before creating a seat row so a held haiku alias cannot pile Diagnose tasks the way
/// CARD-0079 piled interpreter rows.
/// </summary>
public interface IModelAvailability
{
    Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct);
}
