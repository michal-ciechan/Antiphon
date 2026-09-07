using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0412 evidence-time facts for wall parsing. Never sweep-time now.</summary>
public static class CapacityEvidence
{
    public readonly record struct Facts(
        DateTime At,
        CapacityEvidenceTimestampSource Source,
        DateTime? TurnEndTimestamp,
        DateTime? AssistantTimestamp,
        DateTime? TurnEndCreatedAt);

    public static Facts Resolve(
        DateTime? turnEndTimestamp,
        DateTime? assistantTimestamp,
        DateTime? turnEndCreatedAt,
        DateTime detectedAt)
    {
        if (IsUsable(turnEndTimestamp))
            return new Facts(turnEndTimestamp!.Value, CapacityEvidenceTimestampSource.TurnEndTimestamp,
                turnEndTimestamp, assistantTimestamp, turnEndCreatedAt);
        if (IsUsable(assistantTimestamp))
            return new Facts(assistantTimestamp!.Value, CapacityEvidenceTimestampSource.AssistantTextTimestamp,
                turnEndTimestamp, assistantTimestamp, turnEndCreatedAt);
        if (IsUsable(turnEndCreatedAt))
            return new Facts(turnEndCreatedAt!.Value, CapacityEvidenceTimestampSource.TurnEndCreatedAt,
                turnEndTimestamp, assistantTimestamp, turnEndCreatedAt);
        return new Facts(detectedAt, CapacityEvidenceTimestampSource.DetectedAt,
            turnEndTimestamp, assistantTimestamp, turnEndCreatedAt);
    }

    /// <summary>
    /// Enrichment may improve a DetectedAt/CreatedAt fallback to a real provider timestamp once.
    /// Later sweeps never substitute their current time.
    /// </summary>
    public static Facts? TryImprove(Facts current, Facts incoming)
    {
        if (IsProvider(current.Source))
            return null;
        if (!IsProvider(incoming.Source))
            return null;
        if (incoming.At == current.At && incoming.Source == current.Source)
            return null;
        return incoming;
    }

    public static string Digest(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "empty";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text)));
    }

    public static async Task<(string? Text, DateTime? Timestamp, DateTime? CreatedAt)> LoadTurnEndAsync(
        AppDbContext db, Guid sessionId, long stubSequence, CancellationToken ct)
    {
        var row = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Sequence == stubSequence
                && t.Kind == TranscriptKinds.TurnEnd)
            .Select(t => new { t.Text, t.Timestamp, t.CreatedAt, t.Uuid })
            .FirstOrDefaultAsync(ct);
        return row is null ? (null, null, null) : (row.Text, row.Timestamp, row.CreatedAt);
    }

    public static async Task<(string? Text, DateTime? Timestamp)> LoadSiblingAsync(
        AppDbContext db, Guid sessionId, string? stubUuid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stubUuid))
            return (null, null);
        var row = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Uuid == stubUuid
                && t.Kind == TranscriptKinds.AssistantText
                && t.IsApiError == true)
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Text, t.Timestamp })
            .FirstOrDefaultAsync(ct);
        return row is null ? (null, null) : (row.Text, row.Timestamp);
    }

    private static bool IsUsable(DateTime? value) =>
        value is { } at && at.Year > 1 && at != DateTime.MinValue && at != default;

    private static bool IsProvider(CapacityEvidenceTimestampSource source) =>
        source is CapacityEvidenceTimestampSource.TurnEndTimestamp
            or CapacityEvidenceTimestampSource.AssistantTextTimestamp;
}
