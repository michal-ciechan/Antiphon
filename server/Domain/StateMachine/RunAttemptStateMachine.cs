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
            [RunPhase.StreamingTurn] = [RunPhase.Finishing, RunPhase.Stalled, RunPhase.Failed, RunPhase.TimedOut, RunPhase.Canceled],
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

        RecordDuration(attempt, utcNow);
        attempt.Phase = nextPhase;
        attempt.PhaseStartedAt = utcNow;
        attempt.LastEventAt = utcNow;

        if (IsTerminal(nextPhase))
            attempt.CompletedAt = utcNow;
    }

    public static bool IsTerminal(RunPhase phase) =>
        phase is RunPhase.Succeeded
            or RunPhase.Failed
            or RunPhase.TimedOut
            or RunPhase.Stalled
            or RunPhase.Canceled;

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
