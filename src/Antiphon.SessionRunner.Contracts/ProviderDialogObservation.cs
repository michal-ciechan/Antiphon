namespace Antiphon.SessionRunner.Contracts;

public enum ProviderDialogState { Unknown, Clear, Blocked }

/// <summary>Volatile evidence tied to one runner/session generation. Contains no input or tool text.</summary>
public sealed record ProviderDialogObservation(
    Guid SessionId, Guid RuntimeEpoch, DateTime? AcceptedStartedAt,
    long OutputSequence, long InputRevision, ProviderDialogState State,
    string? Kind = null, bool? CanCancel = null, bool InputHistoryKnown = false);
