namespace Antiphon.Server.Application.Dtos;

/// <summary>When a message addressed to an agent session should be delivered.</summary>
public enum MessageSendMode
{
    /// <summary>Deliver immediately, even if the agent is mid-task.</summary>
    Now = 0,

    /// <summary>Hold until the agent finishes its current turn (reaches an end_turn), then deliver.</summary>
    WhenIdle = 1,
}

public sealed record EnqueueMessageRequest(string Body, MessageSendMode Mode = MessageSendMode.WhenIdle);

/// <summary>
/// A message waiting in a session's queue.
///
/// <para><see cref="DeliveryAttempts"/>, <see cref="Origin"/> and <see cref="Parked"/> are additive
/// (CARD-0035 slice 4) and carry no behaviour: they exist because CARD-0055 shipped PARKING with no
/// way to see it. Parking is not a status — a message is parked when it is still
/// <c>Pending</c> and has spent <c>DeliveryVerification.MaxDeliveryAttempts</c> — so a queue UI
/// reading <see cref="Status"/> alone shows a parked message as an ordinary pending one and gives no
/// hint that nothing will ever type it again. The server decides <see cref="Parked"/> against the
/// same setting the attention projection reads, so the two surfaces cannot disagree about what
/// parked means.</para>
/// </summary>
/// <param name="DeliveryAttempts">How many times this has been typed into a terminal (CARD-0055).</param>
/// <param name="Origin">Who enqueued it — Ui, Channel, Check, Delegation (<c>QueuedMessageOrigin</c>).</param>
/// <param name="Parked">Pending AND out of attempts: no automatic path will ever type it again.</param>
public sealed record QueuedMessageDto(
    Guid Id,
    long Sequence,
    string Body,
    string Status,
    DateTime CreatedAt,
    int DeliveryAttempts = 0,
    string Origin = "Ui",
    bool Parked = false);

/// <summary>
/// The pending messages waiting to be delivered to a session, plus whether the agent is currently
/// working. "Finished" in the UI = <see cref="Working"/> is false and <see cref="Messages"/> is empty.
/// </summary>
public sealed record SessionQueueDto(
    Guid SessionId,
    IReadOnlyList<QueuedMessageDto> Messages,
    bool Working);
