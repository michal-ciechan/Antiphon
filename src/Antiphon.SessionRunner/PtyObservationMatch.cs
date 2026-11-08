using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>One pty buffer or snapshot read. A 304 carries no body; the caller already holds it.</summary>
public readonly record struct PtyRead<T>(bool NotModified, string? ETag, T? Body) where T : class;

/// <summary>
/// Opaque strong validators for pty buffer and snapshot reads.
/// LastSequence stays put when a chunk reuses a sequence, the ANSI tail is rewritten, or the live
/// buffer is cleared, so the token also carries the file stamp and the live-buffer epoch.
/// A match is this request's comparison: there is no time-to-live.
/// </summary>
internal static class PtyObservationMatch
{
    public static string Buffer(DateTime? generation, long sequence, long length, long writeTicks) =>
        string.Join('.',
            "b1",
            Ticks(generation),
            sequence.ToString(CultureInfo.InvariantCulture),
            length.ToString(CultureInfo.InvariantCulture),
            writeTicks.ToString(CultureInfo.InvariantCulture));

    public static string Snapshot(
        DateTime? generation, DateTime startedAt, long sequence, long inputRevision,
        long contentEpoch, ProviderDialogObservation dialog) =>
        string.Join('.',
            "s1",
            Ticks(generation),
            startedAt.Ticks.ToString(CultureInfo.InvariantCulture),
            sequence.ToString(CultureInfo.InvariantCulture),
            inputRevision.ToString(CultureInfo.InvariantCulture),
            contentEpoch.ToString(CultureInfo.InvariantCulture),
            DialogHash(dialog));

    public static string Quote(string token) => "\"" + token + "\"";

    public static bool Matches(string? ifNoneMatch, string token)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
            return false;
        foreach (var raw in ifNoneMatch.Split(','))
        {
            var entry = raw.Trim();
            if (entry.Length == 0 || entry == "*")
                continue;
            if (entry.StartsWith("W/", StringComparison.Ordinal))
                continue;
            if (entry.Length >= 2 && entry[0] == '"' && entry[^1] == '"')
                entry = entry[1..^1];
            if (entry.Contains('"', StringComparison.Ordinal))
                continue;
            if (string.Equals(entry, token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Ticks(DateTime? value) =>
        value is { } present ? present.Ticks.ToString(CultureInfo.InvariantCulture) : "none";

    private static string DialogHash(ProviderDialogObservation dialog)
    {
        var proof = dialog.SubmittedInput;
        var text = new StringBuilder();
        Field(text, dialog.SessionId);
        Field(text, dialog.RuntimeEpoch);
        Field(text, dialog.AcceptedStartedAt);
        Field(text, dialog.OutputSequence);
        Field(text, dialog.InputRevision);
        Field(text, dialog.State);
        Field(text, dialog.Kind);
        Field(text, dialog.CanCancel);
        Field(text, dialog.InputHistoryKnown);
        Field(text, dialog.Adopted);
        Field(text, dialog.InputUncertain);
        Field(text, dialog.ScreenState);
        if (proof is null)
        {
            text.Append("n;n;n;n;n;n;");
        }
        else
        {
            Field(text, proof.CaptureId);
            Field(text, proof.BindingIdentity);
            Field(text, proof.TranscriptFloor);
            Field(text, proof.PromptSequence);
            Field(text, proof.PromptUuid);
            Field(text, proof.SubmitRevision);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Field(StringBuilder text, object? value)
    {
        if (value is null)
        {
            text.Append("n;");
            return;
        }

        var rendered = value switch
        {
            DateTime stamp => stamp.Ticks.ToString(CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? "",
        };
        text.Append(rendered.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(rendered).Append(';');
    }
}
