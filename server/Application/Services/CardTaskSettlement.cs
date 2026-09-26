using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>Why a card left play. Archive keeps <see cref="CardStatus"/> and still takes the card out.</summary>
public enum CardClosureKind
{
    Closed,
    Archived,
}

/// <summary>The close or archive that just committed, as the settlement reads it.</summary>
public sealed record CardClosure(
    Guid CardId,
    string Identifier,
    Guid BoardId,
    CardClosureKind Kind,
    CardStatus Status,
    string Reason,
    DateTime At);

public sealed record SettledTask(
    Guid TaskId,
    string ShortId,
    AgentTaskStatus StatusBefore,
    string Note);

public sealed record CardTaskSettlementResult(
    IReadOnlyList<SettledTask> Canceled,
    IReadOnlyList<SettledTask> LeftOpen);

/// <summary>
/// CARD-0738. Which bound tasks a card close touches, and whether each one has started.
/// The only card-path caller of <see cref="AgentTaskService.CancelAsync"/>.
/// </summary>
public sealed class CardTaskSettlement
{
    public CardTaskSettlement(
        AppDbContext db,
        AgentTaskService tasks,
        IEventBus eventBus,
        TimeProvider time,
        ILogger<CardTaskSettlement> logger)
    {
        _ = db;
        _ = tasks;
        _ = eventBus;
        _ = time;
        _ = logger;
    }

    public Task<CardTaskSettlementResult> SettleClosedCardAsync(CardClosure closure, CancellationToken ct)
    {
        _ = closure;
        _ = ct;
        return Task.FromResult(new CardTaskSettlementResult([], []));
    }
}
