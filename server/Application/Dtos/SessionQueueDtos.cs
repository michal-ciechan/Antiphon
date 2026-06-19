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

public sealed record QueuedMessageDto(
    Guid Id,
    long Sequence,
    string Body,
    string Status,
    DateTime CreatedAt);

/// <summary>
/// The pending messages waiting to be delivered to a session, plus whether the agent is currently
/// working. "Finished" in the UI = <see cref="Working"/> is false and <see cref="Messages"/> is empty.
/// </summary>
public sealed record SessionQueueDto(
    Guid SessionId,
    IReadOnlyList<QueuedMessageDto> Messages,
    bool Working);
