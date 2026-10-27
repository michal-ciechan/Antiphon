namespace Antiphon.SessionRunner.Contracts;

public enum ProviderDialogState { Unknown, Clear, Blocked }

/// <summary>Volatile evidence tied to one runner/session generation. Contains no input or tool text.</summary>
public sealed record ProviderDialogObservation(
    Guid SessionId, Guid RuntimeEpoch, DateTime? AcceptedStartedAt,
    long OutputSequence, long InputRevision, ProviderDialogState State,
    string? Kind = null, bool? CanCancel = null, bool InputHistoryKnown = false,
    SubmittedInputProof? SubmittedInput = null,
    bool Adopted = false, bool InputUncertain = false,
    ProviderDialogState ScreenState = ProviderDialogState.Unknown,
    bool CurrentIdleComposer = false, bool ScreenWorking = false,
    bool ComposerHoldsAutomatedBody = false)
{
    public const string Capability = "claudeDialogGuardV1";
}

/// <summary>Matched full prompt after two settled empty composers; grants no key authority.</summary>
public sealed record SubmittedInputProof(Guid CaptureId, string BindingIdentity,
    long TranscriptFloor, long PromptSequence, string PromptUuid, long SubmitRevision);
