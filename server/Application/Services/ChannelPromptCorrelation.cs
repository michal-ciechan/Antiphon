using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Persisted channel wire identity, independent of the queue's weaker submission probe and of
/// machine/specialist note identity. A marker identifies transport, never an outbound address.
/// </summary>
internal static class ChannelPromptCorrelation
{
    private const string Prefix = "[antiphon-channel:";
    private const int MarkerLength = 51; // prefix + a complete GUID in N format + ']'

    public static string Mark(Guid id, string body) => $"{Prefix}{id:N}] {body}";

    public static string? OpeningMarker(string? body)
    {
        if (body is null || body.Length < MarkerLength
            || !body.StartsWith(Prefix, StringComparison.Ordinal)
            || body[MarkerLength - 1] != ']'
            || !Guid.TryParseExact(body.AsSpan(Prefix.Length, 32), "N", out var id))
            return null;
        var marker = body[..MarkerLength];
        return marker == $"{Prefix}{id:N}]" ? marker : null;
    }

    public static string WithoutOuterMarker(string body) =>
        OpeningMarker(body) is { } marker ? body[marker.Length..].TrimStart() : body;

    public static bool IsSpillPointer(string body) => OpeningMarker(body) is { } marker
        && Guid.TryParseExact(marker.AsSpan(Prefix.Length, 32), "N", out var id)
        && body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal)
        && body.Contains(TypedBodySpill.InboxRelativePath(id.ToString("D")), StringComparison.Ordinal);

    public static string RemoveMarkers(string text)
    {
        var start = 0;
        while ((start = text.IndexOf(Prefix, start, StringComparison.Ordinal)) >= 0)
        {
            if (OpeningMarker(text[start..]) is { } marker)
                text = text.Remove(start, marker.Length);
            else
                start += Prefix.Length;
        }
        return text.Trim();
    }

    /// <summary>Only an untouched legacy Pending row can acquire a new wire identity.</summary>
    public static void PrepareFirstAttempt(SessionQueuedMessage row)
    {
        if (row.Origin == QueuedMessageOrigin.Channel && row.Status == QueuedMessageStatus.Pending
            && row.DeliveryAttempts == 0 && row.LastDeliveryStartedAt is null
            && row.LastDeliveryBaselineSequence is null && row.LastDeliveryGeneration is null
            && row.SentAt is null && row.DeliveryVerdict is null
            && row.ChannelReplySettledAt is null && OpeningMarker(row.Body) != $"{Prefix}{row.Id:N}]")
            row.Body = Mark(row.Id, row.Body);
    }

    public static bool Matches(SessionQueuedMessage row, TranscriptEntry prompt,
        TimeSpan tolerance, out string reason)
    {
        reason = "not-prompt";
        if (prompt.Kind is not (TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt))
            return false;
        if (TranscriptKinds.IsLocalCommandRecord(prompt.Kind, prompt.Text)
            || TranscriptKinds.IsCompactionContinuationPrompt(prompt.Kind, prompt.Text))
            return false;
        reason = "wrong-session";
        if (row.AgentSessionId != prompt.AgentSessionId || row.Origin != QueuedMessageOrigin.Channel)
            return false;
        reason = "empty-body";
        if (string.IsNullOrWhiteSpace(row.Body) || string.IsNullOrWhiteSpace(prompt.Text))
            return false;
        reason = "no-attempt";
        if (row.DeliveryAttempts <= 0)
            return false;

        var marker = OpeningMarker(row.Body);
        reason = "before-generation";
        if (row.LastDeliveryGeneration is { } generation && prompt.Timestamp is { } native
            && native < generation)
            return false;
        reason = "before-attempt";
        if (row.LastDeliveryBaselineSequence is { } floor)
        {
            if (prompt.Sequence <= floor) return false;
        }
        else
        {
            // SentAt is a compatibility floor only for an old unmarked row. Late-confirm can
            // replace it; native time (never ingestion CreatedAt) must use the original attempt.
            var started = row.LastDeliveryStartedAt ?? (marker is null ? row.SentAt : null);
            if (started is null || prompt.Timestamp is not { } timestamp
                || timestamp < started.Value - tolerance)
                return false;
        }

        if (marker is not null)
        {
            reason = "empty-marked-body";
            if (string.IsNullOrWhiteSpace(WithoutOuterMarker(row.Body))) return false;
            reason = "missing-marker";
            if (!prompt.Text.Contains(marker, StringComparison.Ordinal)) return false;
            reason = "incomplete-marked-body";
            if (!PromptSubmissionMatch.RequiresTextMatch(row.Body)
                || !PromptSubmissionMatch.IsCompleteIn(row.Body, prompt.Text))
                return false;
        }
        else
        {
            reason = "legacy-content-mismatch";
            var expected = row.Body.ReplaceLineEndings("\n").Trim();
            var actual = prompt.Text.ReplaceLineEndings("\n").Trim();
            // A short raw legacy 'done' cannot claim a task header. No weak arm and no lossy
            // whitespace fallback for legacy traffic, including old Grok newline-elided turns.
            if (!PromptSubmissionMatch.RequiresTextMatch(expected)
                ? !string.Equals(expected, actual, StringComparison.Ordinal)
                : !actual.Contains(expected, StringComparison.Ordinal))
                return false;
        }
        reason = "matched";
        return true;
    }

    public static bool SameDeliveredBatch(SessionQueuedMessage a, SessionQueuedMessage b) =>
        a.AgentSessionId == b.AgentSessionId && a.ConversationKey == b.ConversationKey
        && a.LastDeliveryStartedAt is not null && a.LastDeliveryStartedAt == b.LastDeliveryStartedAt
        && a.LastDeliveryBaselineSequence == b.LastDeliveryBaselineSequence
        && a.LastDeliveryGeneration == b.LastDeliveryGeneration;
}
