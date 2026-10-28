using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>One named withhold. <see cref="Code"/> is the guarded-input outcome; <see cref="Text"/> is the queue reason.</summary>
internal readonly record struct DialogWithhold(string Text, string Code, bool ChargeAttempt);

/// <summary>
/// Names a Claude withhold for the queue. The allow set is
/// <see cref="ProviderDialogObservationService.Refusal"/>: this method never grants a write.
/// </summary>
internal static class DialogDeliveryAdmission
{
    public const string SnapshotUnavailable = "Claude dialog snapshot unavailable";
    public const string GenerationMismatch = "Claude dialog accepted generation mismatch";
    public const string BlocksInput = "Claude dialog blocks input";
    public const string InputUncertain = "Claude dialog input uncertain";
    public const string AdoptedSession = "Claude dialog observation unavailable: adopted session";
    public const string ScreenUnknown = "Claude dialog screen unknown";
    public const string ObservationUnavailable = "Claude dialog observation unavailable";

    public static DialogWithhold? Describe(
        ProviderDialogObservation? observation, Guid sessionId, DateTime generation)
    {
        var code = ProviderDialogObservationService.Refusal(observation, sessionId, generation);
        if (code is null) return null;
        if (observation is null || observation.SessionId != sessionId || observation.RuntimeEpoch == Guid.Empty)
            return new(SnapshotUnavailable, code, false);
        if (!SessionGeneration.Equal(observation.AcceptedStartedAt, generation))
            return new(GenerationMismatch, code, true);
        if (code == ConditionalInputOutcomes.ModalBlocked
            || observation.State == ProviderDialogState.Blocked
            || observation.ScreenState == ProviderDialogState.Blocked)
            return new(BlocksInput, ConditionalInputOutcomes.ModalBlocked, false);
        if (observation.InputUncertain)
            return new(InputUncertain, code, true);
        if (observation.ScreenState == ProviderDialogState.Unknown)
            return new(ScreenUnknown, code, true);
        if (observation.Adopted)
            return new(AdoptedSession, code, true);
        return new(ObservationUnavailable, code, true);
    }
}
