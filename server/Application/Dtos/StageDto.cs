using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public record StageDto(
    Guid Id,
    string Name,
    int StageOrder,
    StageStatus Status,
    bool GateRequired,
    int CurrentVersion);
