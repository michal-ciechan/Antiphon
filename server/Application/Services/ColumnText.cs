namespace Antiphon.Server.Application.Services;

/// <summary>
/// Fits diagnostic text to the column that has to hold it.
///
/// <para>Every user of this is a REPORT — an alert's detail, an incident's message. The whole point
/// of those rows is to survive the thing they describe, so the one failure mode they may never have
/// is dying of their own size. CARD-0195 fixed that once for
/// <see cref="TranscriptBindingIncidentService"/>; CARD-0205 found it a second time, where a
/// reconciler alert listing 190 orphaned session ids overflowed <c>Alerts.Detail</c>
/// (<c>varchar(4000)</c>) on every sweep for four days.</para>
///
/// <para>Clipping is right here and wrong almost everywhere else: a truncated report still names
/// the problem, where a rejected one names nothing. It is deliberately NOT applied as a global EF
/// value converter — silent truncation of arbitrary columns would hide real defects rather than
/// keep a backstop alive.</para>
/// </summary>
internal static class ColumnText
{
    /// <summary>
    /// Clips to at most <paramref name="max"/> CHARS, marking the cut with an ellipsis so a reader
    /// can tell a clipped report from a short one. Never splits a surrogate pair — half a pair is
    /// not a valid string, and handing one to Npgsql trades an oversize failure for an encoding
    /// failure, which is the same bug wearing a different message.
    /// </summary>
    public static string Clip(string text, int max)
    {
        if (max <= 0)
            return string.Empty;
        if (text.Length <= max)
            return text;

        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;

        return string.Concat(text.AsSpan(0, cut), "…");
    }

    /// <summary>Null in, null out — an absent detail is not the same as an empty one.</summary>
    public static string? ClipOrNull(string? text, int max) =>
        text is null ? null : Clip(text, max);
}
