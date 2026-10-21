using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.StateMachine;

public static class RunAttemptStateMachine
{
    private static readonly IReadOnlyDictionary<RunPhase, RunPhase[]> AllowedTransitions =
        new Dictionary<RunPhase, RunPhase[]>
        {
            [RunPhase.PreparingWorkspace] = [RunPhase.BuildingPrompt, RunPhase.Failed, RunPhase.TimedOut, RunPhase.Canceled],
            [RunPhase.BuildingPrompt] = [RunPhase.LaunchingAgent, RunPhase.Failed, RunPhase.TimedOut, RunPhase.Canceled],
            [RunPhase.LaunchingAgent] = [RunPhase.InitializingSession, RunPhase.Failed, RunPhase.TimedOut, RunPhase.Canceled],
            [RunPhase.InitializingSession] = [RunPhase.StreamingTurn, RunPhase.Failed, RunPhase.TimedOut, RunPhase.Canceled],
            [RunPhase.StreamingTurn] = [RunPhase.AwaitingInput, RunPhase.Finishing, RunPhase.Stalled, RunPhase.Failed, RunPhase.TimedOut, RunPhase.Canceled],
            [RunPhase.AwaitingInput] = [RunPhase.StreamingTurn, RunPhase.Failed, RunPhase.Canceled],
            [RunPhase.Finishing] = [RunPhase.Succeeded, RunPhase.Failed, RunPhase.TimedOut, RunPhase.Canceled],
            [RunPhase.Succeeded] = [],
            [RunPhase.Failed] = [],
            [RunPhase.TimedOut] = [],
            [RunPhase.Stalled] = [],
            [RunPhase.Canceled] = []
        };

    public static bool CanTransition(RunPhase current, RunPhase next) =>
        AllowedTransitions.TryGetValue(current, out var allowed) && allowed.Contains(next);

    public static void Transition(RunAttempt attempt, RunPhase nextPhase, DateTime utcNow)
    {
        if (!CanTransition(attempt.Phase, nextPhase))
        {
            throw new InvalidOperationException(
                $"Invalid run attempt phase transition from {attempt.Phase} to {nextPhase}.");
        }

        var leaving = attempt.Phase;
        RecordDuration(attempt, utcNow);
        attempt.Phase = nextPhase;
        attempt.PhaseStartedAt = utcNow;
        attempt.LastEventAt = utcNow;
        if (nextPhase == RunPhase.AwaitingInput)
            SetOpenInputWait(attempt);
        else if (leaving == RunPhase.AwaitingInput)
            ClearOpenInputWait(attempt);

        if (IsTerminal(nextPhase))
            attempt.CompletedAt = utcNow;
    }

    public static bool IsTerminal(RunPhase phase) =>
        phase is RunPhase.Succeeded
            or RunPhase.Failed
            or RunPhase.TimedOut
            or RunPhase.Stalled
            or RunPhase.Canceled;

    // One open input wait. A real Transition out of AwaitingInput removes it, so a later
    // wait can arm provisionally after an earlier wait already recorded its duration.
    internal const string OpenInputWaitKey = "OpenInputWait";

    // Sequence of the prompt that entered the current real StreamingTurn.
    // A later TurnEnd or interrupt belongs to this turn only when its sequence is higher.
    internal const string RearmPromptSequenceKey = "RearmPromptSequence";

    // Highest transcript sequence already stored when a cancelled launch entered
    // AwaitingInput. Prompts at or below it are the launch turn, not a follow-up.
    internal const string LaunchPromptCeilingKey = "LaunchPromptCeiling";

    public static bool HasAwaitingInputDuration(RunAttempt attempt) =>
        ReadDurations(attempt.PhaseDurationsJson).ContainsKey(RunPhase.AwaitingInput.ToString());

    public static bool IsProvisionalStreamingTurn(RunAttempt attempt) =>
        attempt.Phase == RunPhase.StreamingTurn && HasOpenInputWait(attempt);

    // The launch StreamingTurn has neither marker. A provisional arm still has the marker.
    // A real re-arm has closed AwaitingInput and cleared the marker.
    public static bool IsRearmedStreamingTurn(RunAttempt attempt)
    {
        if (attempt.Phase != RunPhase.StreamingTurn)
            return false;

        var durations = ReadDurations(attempt.PhaseDurationsJson);
        return durations.ContainsKey(OpenInputWaitKey)
            || durations.ContainsKey(RunPhase.AwaitingInput.ToString());
    }

    public static void ArmProvisionalStreamingTurn(RunAttempt attempt, DateTime utcNow)
    {
        if (attempt.Phase != RunPhase.AwaitingInput)
        {
            throw new InvalidOperationException(
                $"Provisional streaming arm requires {RunPhase.AwaitingInput}, not {attempt.Phase}.");
        }

        // A wait already sitting in AwaitingInput before this marker existed has none.
        // Without it the slash prompt is not provisional, the wrapper cannot restore,
        // and the stall scanner kills the session.
        if (!HasOpenInputWait(attempt))
            SetOpenInputWait(attempt);

        attempt.Phase = RunPhase.StreamingTurn;
        attempt.LastEventAt = utcNow;
    }

    public static long RearmPromptSequence(RunAttempt attempt)
    {
        var durations = ReadDurations(attempt.PhaseDurationsJson);
        return durations.TryGetValue(RearmPromptSequenceKey, out var sequence)
            ? (long)sequence
            : 0;
    }

    public static void RememberRearmPrompt(RunAttempt attempt, long sequence) =>
        WriteDurations(attempt, durations => { durations[RearmPromptSequenceKey] = sequence; });

    public static bool TryGetLaunchPromptCeiling(RunAttempt attempt, out long sequence)
    {
        var durations = ReadDurations(attempt.PhaseDurationsJson);
        if (!durations.TryGetValue(LaunchPromptCeilingKey, out var stored))
        {
            sequence = 0;
            return false;
        }

        sequence = (long)stored;
        return true;
    }

    public static void RememberLaunchPromptCeiling(RunAttempt attempt, long sequence) =>
        WriteDurations(attempt, durations => { durations[LaunchPromptCeilingKey] = sequence; });

    public static void RestoreProvisionalAwaitingInput(RunAttempt attempt)
    {
        if (!IsProvisionalStreamingTurn(attempt))
        {
            throw new InvalidOperationException(
                "Provisional input-wait restore requires the open input-wait marker.");
        }

        attempt.Phase = RunPhase.AwaitingInput;
        attempt.LastEventAt = attempt.PhaseStartedAt;
    }

    private static bool HasOpenInputWait(RunAttempt attempt) =>
        ReadDurations(attempt.PhaseDurationsJson).ContainsKey(OpenInputWaitKey);

    private static void SetOpenInputWait(RunAttempt attempt) =>
        WriteDurations(attempt, durations => { durations[OpenInputWaitKey] = 1; });

    private static void ClearOpenInputWait(RunAttempt attempt) =>
        WriteDurations(attempt, durations => { durations.Remove(OpenInputWaitKey); });

    private static void RecordDuration(RunAttempt attempt, DateTime utcNow)
    {
        var elapsedMs = Math.Max(0, (utcNow - attempt.PhaseStartedAt).TotalMilliseconds);
        var durations = ReadDurations(attempt.PhaseDurationsJson);
        var key = attempt.Phase.ToString();

        durations[key] = durations.TryGetValue(key, out var existing)
            ? existing + elapsedMs
            : elapsedMs;

        attempt.PhaseDurationsJson = JsonSerializer.Serialize(durations);
    }

    private static void WriteDurations(RunAttempt attempt, Action<Dictionary<string, double>> edit)
    {
        var durations = ReadDurations(attempt.PhaseDurationsJson);
        edit(durations);
        attempt.PhaseDurationsJson = JsonSerializer.Serialize(durations);
    }

    private static Dictionary<string, double> ReadDurations(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, double>(StringComparer.Ordinal);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json)
                ?? new Dictionary<string, double>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }
    }
}
