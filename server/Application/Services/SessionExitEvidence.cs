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
        signal = signal is >= 0 ? signal : null;
        // Keep the first complete observation. Missing-field replay cannot erase known evidence.
        if (session.ExitSignal is not null && signal is not null && session.ExitSignal != signal)
            return false;
        if (session.ExitCode is not null && code is not null && session.ExitCode != code)
        {
            // A legacy integer without signal evidence is incomplete. The first complete
            // native pair takes precedence; later conflicting complete pairs stay refused.
            if (session.ExitSignal is not null || signal is null) return false;
            session.ExitCode = code;
        }
        session.ExitCode ??= code;
        session.ExitSignal ??= signal;
        if (Bound(intent, session.Id, session.StartedAt) is { } bounded)
        {
            var current = ReadIntent(session.ExitStopIntentJson);
            // A diagnostic replay cannot erase stronger server-accepted live owner evidence.
            // Later owner-memory/durability enrichment preserves the actual first exit pair.
            var stronger = current is null || bounded.Provenance == ProcessStopProvenance.LiveIssuer
                && (current.Provenance != ProcessStopProvenance.LiveIssuer
                    || bounded.Outcome == ProcessStopOutcome.Issued && current.Outcome != ProcessStopOutcome.Issued
                    || SessionExitPolicy.CanAttribute(bounded, session.ExitSignal)
                        && !SessionExitPolicy.CanAttribute(current, session.ExitSignal)
                    || bounded.RequestId == current.RequestId && bounded.IssuerBootId == current.IssuerBootId
                    && bounded.HostInstanceId == current.HostInstanceId
                    && (bounded.Outcome == ProcessStopOutcome.Issued && current.Outcome != ProcessStopOutcome.Issued
                        || bounded.Outcome == current.Outcome && bounded.Durable && !current.Durable));
            if (stronger) session.ExitStopIntentJson = JsonSerializer.Serialize(bounded, Json);
        }
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
