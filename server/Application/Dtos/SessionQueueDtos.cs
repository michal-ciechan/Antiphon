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

/// <summary>CARD-0650: the operator's reason for releasing an expectation-watchdog composer hold. Required.</summary>
public sealed record ReleaseExpectationHoldRequest(string? Reason);

/// <summary>The watchdog nudges whose composer hold was released. Empty when the session held nothing.</summary>
public sealed record ExpectationHoldReleaseResult(Guid SessionId, IReadOnlyList<Guid> ReleasedNudgeIds);

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
/// <param name="Origin">Who enqueued it — Ui, Channel, Check, Delegation, Supervision, Scheduled, Mention (<c>QueuedMessageOrigin</c>).</param>
/// <param name="Parked">Pending AND out of attempts: no automatic path will ever type it again.</param>
public sealed record QueuedMessageDto(
    Guid Id,
    long Sequence,
    string Body,
    string Status,
    DateTime CreatedAt,
    int DeliveryAttempts = 0,
    string Origin = "Ui",
    bool Parked = false,
    string? NoteHeader = null,
    bool ModalBlocked = false,
    string? WithholdReason = null);

/// <summary>
/// How a Mode:Now delivery was confirmed. Values of <see cref="DeliveryReceiptDto.ConfirmedBy"/>.
/// </summary>
public static class DeliveryConfirmedBy
{
    public const string Transcript = "transcript";
    public const string Screen = "screen";
    public const string None = "none";
}

/// <summary>
/// CARD-0180 S3: what Mode:Now actually proved about the delivery. Populated only on the Mode:Now
/// response (WhenIdle stays a queue row with no receipt). <see cref="ConfirmedBy"/> is
/// <c>transcript</c>, <c>screen</c>, or <c>none</c>.
/// </summary>
public sealed record DeliveryReceiptDto(
    string Verdict,
    string ConfirmedBy,
    bool Degraded,
    string? Reason,
    DateTime At);

/// <summary>
/// The pending messages waiting to be delivered to a session, plus whether the agent is currently
/// working. "Finished" in the UI = <see cref="Working"/> is false and <see cref="Messages"/> is empty.
/// </summary>
public sealed record SessionQueueDto(
    Guid SessionId,
    IReadOnlyList<QueuedMessageDto> Messages,
    bool Working,
    DeliveryReceiptDto? LastDelivery = null,
    bool ModalBlocked = false,
    string? ModalBlockedReason = null);

/// <summary>One pending note on the read-only poll. <see cref="WithholdReason"/> is the current queue withhold, when one is active.</summary>
public sealed record PendingNoteDto(
    Guid Id,
    long Sequence,
    string Body,
    DateTime CreatedAt,
    int DeliveryAttempts,
    string Origin,
    string? NoteHeader,
    string? WithholdReason,
    int WithholdCount = 0,
    DateTime? FirstWithheldAt = null,
    DateTime? LastWithheldAt = null,
    string? LastWithholdReason = null);

/// <summary>
/// A bounded page of pending notes after <see cref="Since"/> (exclusive sequence).
/// <see cref="NextSince"/> is the last returned sequence, or <see cref="Since"/> when the page is empty.
/// </summary>
public sealed record PendingNotePageDto(
    Guid SessionId,
    long Since,
    long NextSince,
    bool ModalBlocked,
    string? WithholdReason,
    IReadOnlyList<PendingNoteDto> Notes);
