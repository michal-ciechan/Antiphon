using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Demand-driven, bounded reads. No cached Clear grants input authority.</summary>
public sealed class ProviderDialogObservationService(ISessionRunnerClient runner)
{
    public Task<ProviderDialogObservation?> ReadAsync(Guid id, CancellationToken ct) =>
        ReadBoundedAsync(async token => (await runner.GetSnapshotAsync(id, token)).Dialog, ct);

    internal static async Task<ProviderDialogObservation?> ReadBoundedAsync(
        Func<CancellationToken, Task<ProviderDialogObservation?>> read, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { return await read(deadline.Token).WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    internal static string? Refusal(ProviderDialogObservation? observation, Guid id, DateTime generation)
    {
        if (observation is null || observation.SessionId != id || observation.RuntimeEpoch == Guid.Empty || !observation.InputHistoryKnown
            || !SessionGeneration.Equal(observation.AcceptedStartedAt, generation))
            return ConditionalInputOutcomes.ObservationUnknown;
        return observation.State switch
        {
            ProviderDialogState.Clear => null,
            ProviderDialogState.Blocked => ConditionalInputOutcomes.ModalBlocked,
            _ => ConditionalInputOutcomes.ObservationUnknown
        };
    }

    internal static RunnerConditionalInputRequest Request(ProviderDialogObservation? observation,
        Guid id, DateTime generation, string input, ClaudeDialogInputIntent intent = ClaudeDialogInputIntent.None)
    {
        var refusal = Refusal(observation, id, generation);
        if (intent == ClaudeDialogInputIntent.None && refusal is not null)
            throw new ProviderDialogInputRefusedException(refusal);
        if (observation is null || observation.SessionId != id || observation.RuntimeEpoch == Guid.Empty
            || !observation.InputHistoryKnown || !SessionGeneration.Equal(observation.AcceptedStartedAt, generation))
            throw new ProviderDialogInputRefusedException(ConditionalInputOutcomes.ObservationUnknown);
        return new(generation, observation.OutputSequence, input, true,
            observation.InputRevision, observation.RuntimeEpoch, intent);
    }

    internal static void RequireWritten(RunnerConditionalInputResult result)
    {
        if (result.Outcome == ConditionalInputOutcomes.Written) return;
        if (result.Outcome == ConditionalInputOutcomes.Unknown)
            throw new IOException("Guarded input acknowledgement unavailable; submission is uncertain.");
        throw new ProviderDialogInputRefusedException(result.Outcome);
    }
}

/// <summary>A definitive refusal before this write's first backend byte.</summary>
public sealed class ProviderDialogInputRefusedException(string reason)
    : ConflictException("Automated Claude input withheld: " + reason, "provider_dialog_" + reason)
{
    public string Reason { get; } = reason;
}
