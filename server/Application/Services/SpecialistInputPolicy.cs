using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// A capability-bound, full-inline Check envelope. This is transport metadata, never a
/// qualification certificate. Only the capability owner may supply a measured byte limit;
/// there is deliberately no production default or provider-wide ceiling override.
/// </summary>
public sealed record SpecialistInputPolicy(
    int Version,
    Guid TaskId,
    Guid SessionId,
    DateTime SessionStartedAt,
    AgentKind Kind,
    DeliveryBackend Backend,
    int MaxUtf8Bytes,
    string CapabilityFingerprint)
{
    public string Serialize() => JsonSerializer.Serialize(this);

    public static SpecialistInputPolicy? Read(string? json)
    {
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<SpecialistInputPolicy>(json)
                ?? throw new SpecialistInputUnsupportedException("Missing specialist input policy.");
        }
        catch (JsonException)
        {
            throw new SpecialistInputUnsupportedException("Invalid specialist input policy.");
        }
    }

    public string Fit(string body, AgentKind kind, DeliveryBackend backend)
    {
        if (Version != 1 || TaskId == Guid.Empty || SessionId == Guid.Empty
            || SessionStartedAt == default || MaxUtf8Bytes <= 0
            || string.IsNullOrWhiteSpace(CapabilityFingerprint)
            || Kind != kind || Backend != backend || Backend != DeliveryBackend.ModernConPty
            || Kind is not (AgentKind.ClaudeCode or AgentKind.Codex))
            throw new SpecialistInputUnsupportedException("Unsupported or mismatched specialist input capability.");

        var normalized = body.ReplaceLineEndings("\n").Trim();
        if (Encoding.UTF8.GetByteCount(normalized) > MaxUtf8Bytes)
            throw new SpecialistInputUnsupportedException("Complete specialist input exceeds its certified UTF-8 envelope.");
        if (!normalized.Contains(DelegationReportFormatter.TaskMarker(TaskId), StringComparison.Ordinal))
            throw new SpecialistInputUnsupportedException("Specialist input does not carry its current Check identity.");
        return normalized;
    }

    public void RequireSession(AgentSession session)
    {
        if (session.Id != SessionId || session.StartedAt != SessionStartedAt || session.AgentKind != Kind
            || session.SessionBackend != SessionBackend.PtyHost)
            throw new SpecialistInputUnsupportedException("Specialist input capability belongs to a different session generation.");
    }

    public static bool CompletePromptEquals(string body, string? recordText) =>
        recordText is not null
        && string.Equals(body.ReplaceLineEndings("\n").Trim(),
            recordText.ReplaceLineEndings("\n").Trim(), StringComparison.Ordinal);
}

public sealed class SpecialistInputUnsupportedException(string message)
    : ConflictException(message, "specialist_input_unsupported")
{
    public SpecialistAttemptOutcome Outcome => SpecialistAttemptOutcome.InputUnsupported;
}
