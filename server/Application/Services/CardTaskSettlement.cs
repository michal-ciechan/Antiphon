using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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
    private const int ReasonLimit = 4000;

    private static readonly AgentTaskStatus[] OpenStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
        AgentTaskStatus.Blocked,
    ];

    private readonly AppDbContext _db;
    private readonly AgentTaskService _tasks;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _time;
    private readonly ILogger<CardTaskSettlement> _logger;

    public CardTaskSettlement(
        AppDbContext db,
        AgentTaskService tasks,
        IEventBus eventBus,
        TimeProvider time,
        ILogger<CardTaskSettlement> logger)
    {
        _db = db;
        _tasks = tasks;
        _eventBus = eventBus;
        _time = time;
        _logger = logger;
    }

    public async Task<CardTaskSettlementResult> SettleClosedCardAsync(CardClosure closure, CancellationToken ct)
    {
        var tasks = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.CardId == closure.CardId)
            .Where(AgentTaskRoles.NotSpecialist)
            .Where(t => OpenStatuses.Contains(t.Status))
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);

        var reason = ComposeReason(closure);
        var canceled = new List<SettledTask>();
        var leftOpen = new List<SettledTask>();
        foreach (var task in tasks)
        {
            try
            {
                if (await HasStartedAsync(task, ct))
                {
                    var note = WarningDetail(closure);
                    await LeaveRunningAsync(task, note, ct);
                    leftOpen.Add(new SettledTask(task.Id, Short(task.Id), task.Status, note));
                }
                else
                {
                    await _tasks.CancelAsync(task.Id, ct, reason);
                    canceled.Add(new SettledTask(task.Id, Short(task.Id), task.Status, reason));
                }
            }
            catch (ConflictException)
            {
                // A concurrent writer settled it between the read and the cancel.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Card {Identifier} could not settle task {ShortId}",
                    closure.Identifier,
                    Short(task.Id));
                leftOpen.Add(new SettledTask(task.Id, Short(task.Id), task.Status, ex.Message));
            }
        }

        var verb = closure.Kind == CardClosureKind.Archived ? "archived" : "closed";
        _logger.LogInformation(
            "Card {Identifier} {Verb}: cancelled {CanceledCount} bound task(s) [{CanceledIds}]; left {LeftCount} started [{LeftIds}]",
            closure.Identifier,
            verb,
            canceled.Count,
            string.Join(", ", canceled.Select(t => t.ShortId)),
            leftOpen.Count,
            string.Join(", ", leftOpen.Select(t => t.ShortId)));

        return new CardTaskSettlementResult(canceled, leftOpen);
    }

    /// <summary>
    /// The dispatcher's own started verdict (CARD-0117 D7): a Sent brief outranks the transcript,
    /// a Pending brief means not started, and a missing brief falls through to turn prompts.
    /// Working is always started. Queued and Blocked are never consulted.
    /// </summary>
    private async Task<bool> HasStartedAsync(AgentTask task, CancellationToken ct)
    {
        if (task.Status == AgentTaskStatus.Working)
            return true;
        if (task.Status is AgentTaskStatus.Queued or AgentTaskStatus.Blocked)
            return false;
        if (task.AgentSessionId is not Guid sessionId)
            return false;

        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var brief = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && m.Origin == QueuedMessageOrigin.Delegation
                && (task.DispatchedAt == null || m.CreatedAt >= task.DispatchedAt)
                && m.Body.Contains(marker))
            .OrderBy(m => m.Sequence)
            .Select(m => new { m.Status })
            .FirstOrDefaultAsync(ct);
        if (brief is not null)
            return brief.Status == QueuedMessageStatus.Sent;

        var span = await TranscriptPromptSpan.LoadAsync(_db, sessionId, task.DispatchedAt, ct);
        return span.TurnPrompts.Count > 0;
    }

    private async Task LeaveRunningAsync(AgentTask task, string detail, CancellationToken ct)
    {
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Warning,
            Detail = Clamp(detail, ReasonLimit),
            At = _time.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
    }

    private static string ComposeReason(CardClosure closure)
    {
        var text = closure.Kind == CardClosureKind.Archived
            ? $"Card {closure.Identifier} archived: {closure.Reason}"
            : $"Card {closure.Identifier} closed ({closure.Status}): {closure.Reason}";
        return Clamp(text, ReasonLimit);
    }

    private static string WarningDetail(CardClosure closure)
    {
        var text = closure.Kind == CardClosureKind.Archived
            ? $"Card {closure.Identifier} archived: {closure.Reason} while this task was still working; it was not stopped. Cancel it, or reopen the card."
            : $"Card {closure.Identifier} closed ({closure.Status}: {closure.Reason}) while this task was still working; it was not stopped. Cancel it, or reopen the card.";
        return Clamp(text, ReasonLimit);
    }

    private static string Short(Guid id) => DelegationReportFormatter.Short(id);

    private static string Clamp(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
