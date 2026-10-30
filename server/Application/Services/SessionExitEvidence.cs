using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Copies actual evidence only. Lifecycle policy belongs to the exit consumers.</summary>
public static class SessionExitEvidence
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool Apply(AgentSession session, int? code, int? signal, ProcessStopIntentDto? intent = null)
    {
        // Keep the first complete observation. Missing-field replay cannot erase known evidence.
        if (session.ExitSignal is not null && signal is not null && session.ExitSignal != signal)
            return false;
        // A different actual code cannot supply a missing signal for the previous observation.
        if (session.ExitCode is not null && code is not null && session.ExitCode != code)
            return false;
        session.ExitCode ??= code;
        session.ExitSignal ??= signal is >= 0 ? signal : null;
        if (session.ExitStopIntentJson is null && Bound(intent, session.Id, session.StartedAt) is { } bounded)
            session.ExitStopIntentJson = JsonSerializer.Serialize(bounded, Json);
        return true;
    }

    public static ProcessStopIntentDto? Bound(ProcessStopIntentDto? intent, Guid sessionId, DateTime generation) =>
        intent is not null && intent.SessionId == sessionId && SessionGeneration.Equal(intent.AcceptedStartedAt, generation)
        && intent.RequestId != Guid.Empty && intent.IssuerBootId != Guid.Empty
        && intent.AttemptedSignal is > 0 && (intent.CustodyNamespace?.Length ?? 0) <= 128
        && (intent.CustodyContainer?.Length ?? 0) <= 128
        && Enum.IsDefined(intent.Target) && Enum.IsDefined(intent.Path) && Enum.IsDefined(intent.Outcome)
        && Enum.IsDefined(intent.Provenance) ? intent : null;

    public static ProcessStopIntentDto? ReadIntent(string? json)
    {
        if (json is null || json.Length > 4096) return null;
        try { return JsonSerializer.Deserialize<ProcessStopIntentDto>(json, Json); }
        catch (JsonException) { return null; }
    }
}
