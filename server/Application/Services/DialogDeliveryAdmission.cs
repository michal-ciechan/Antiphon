using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>One named withhold. <see cref="Code"/> is the guarded-input outcome; <see cref="Text"/> is the queue reason.</summary>
internal readonly record struct DialogWithhold(string Text, string Code, bool ChargeAttempt);

/// <summary>
/// Ordinary automated Claude input. Maintenance intents stay on
/// <see cref="ProviderDialogObservationService.Request"/>, which still requires known input history.
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

    public static DialogWithhold? Decide(
        ProviderDialogObservation? observation, Guid sessionId, DateTime generation,
        bool transcriptIdle, SessionBackend backend, string? input = null)
    {
        if (observation is null || observation.SessionId != sessionId || observation.RuntimeEpoch == Guid.Empty)
            return new(SnapshotUnavailable, ConditionalInputOutcomes.ObservationUnknown, false);
        if (!SessionGeneration.Equal(observation.AcceptedStartedAt, generation))
            return new(GenerationMismatch, ConditionalInputOutcomes.ObservationUnknown, true);
        if (observation.State == ProviderDialogState.Blocked || observation.ScreenState == ProviderDialogState.Blocked)
            return new(BlocksInput, ConditionalInputOutcomes.ModalBlocked, false);
        if (observation.InputUncertain)
            return new(InputUncertain, ConditionalInputOutcomes.ObservationUnknown, true);
        if (observation.InputHistoryKnown && observation.State == ProviderDialogState.Clear)
            return null;
        if (observation.ScreenState == ProviderDialogState.Unknown)
            return new(ScreenUnknown, ConditionalInputOutcomes.ObservationUnknown, true);
        if (observation.Adopted && !observation.InputUncertain
            && observation.ScreenState == ProviderDialogState.Clear
            && !observation.ScreenWorking
            && transcriptIdle && backend == SessionBackend.PtyHost)
        {
            var submit = input is "\r" or "\n";
            var eligible = input is null
                ? observation.CurrentIdleComposer || observation.ComposerHoldsAutomatedBody
                : submit ? observation.ComposerHoldsAutomatedBody : observation.CurrentIdleComposer;
            if (eligible) return null;
        }
        if (observation.Adopted)
            return new(AdoptedSession, ConditionalInputOutcomes.ObservationUnknown, true);
        return new(ObservationUnavailable, ConditionalInputOutcomes.ObservationUnknown, true);
    }

    public static RunnerConditionalInputRequest ConditionalRequest(
        ProviderDialogObservation observation, DateTime generation, string input) =>
        new(generation, observation.OutputSequence, input, true,
            observation.InputRevision, observation.RuntimeEpoch);
}

internal static class TranscriptIdleQuery
{
    public static async Task<bool> IsConfirmedIdleAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var last = await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId)
            .OrderByDescending(e => e.Sequence)
            .Select(e => new { e.Kind, e.StopReason })
            .FirstOrDefaultAsync(ct);
        if (last is null) return false;
        var tools = await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId
                && (e.Kind == TranscriptKinds.ToolCall || e.Kind == TranscriptKinds.ToolResult))
            .OrderBy(e => e.Sequence)
            .Select(e => new { e.Sequence, e.Kind, e.ToolUseId, e.ToolName })
            .ToListAsync(ct);
        return TranscriptIdleEvidence.IsConfirmedIdle(last.Kind, last.StopReason,
            tools.Select(t => new TranscriptIdleEvidence.ToolRow(t.Sequence, t.Kind, t.ToolUseId, t.ToolName)).ToList());
    }
}
