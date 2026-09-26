using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0738. <c>apply</c> defaults to false: a bare POST lists and writes nothing.</summary>
public sealed record ClosedCardSweepRequest(bool Apply = false);

public sealed record ClosedCardSweepDto(
    DateTime AsOf,
    bool Applied,
    IReadOnlyList<ClosedCardSweepRowDto> Rows,
    int CanceledCount,
    int LeftOpenCount);

public sealed record ClosedCardSweepRowDto(
    Guid TaskId,
    string ShortId,
    string Title,
    AgentTaskStatus Status,
    AgentTaskRole Role,
    Guid CardId,
    string CardIdentifier,
    Guid BoardId,
    CardStatus CardStatus,
    bool Archived,
    DateTime CardClosedAt,
    string? CardReason,
    bool CreatedAfterClose,
    bool Started,
    ClosedCardSweepAction Action,
    string? Note = null);

public enum ClosedCardSweepAction
{
    Listed,
    Canceled,
    LeftOpenStarted,
    LeftOpenCreatedAfterClose,
    CancelFailed,
}
