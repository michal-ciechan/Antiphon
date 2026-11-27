namespace Antiphon.Server.Application.Dtos;

public sealed record VerifiedSessionStopPreconditions(
    DateTime Generation, Guid? OwnerId, Guid? LastTaskId, Guid? LastTaskRevision,
    Guid? OpenTaskId, long? TurnEndSequence, string Attachment, bool ProtectedOwner, string? RunnerStatus);

public sealed record VerifiedSessionStopRequest(
    DateTime ExpectedGeneration, Guid? ExpectedOwnerId, Guid? ExpectedLastTaskId,
    Guid? ExpectedLastTaskRevision, long ExpectedTurnEndSequence);

public sealed record VerifiedSessionStopReceipt(Guid SessionId, DateTime Generation, string Status, Guid? OpenTaskId);
