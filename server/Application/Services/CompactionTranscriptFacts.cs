using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

internal readonly record struct CompactionFactInput(
    long Sequence,
    string Kind,
    string? Text,
    DateTime? EventTime,
    DateTime CreatedAt,
    string? NativeId,
    string? ToolUseId);

/// <summary>
/// CARD-1754 fact builder. Check correlation is one pass over each prompt: GUID-shaped
/// substrings and the task-marker shape are looked up, which matches
/// <c>Contains</c> of the D form, the N form, and the task marker.
/// </summary>
internal static class CompactionTranscriptFacts
{
    internal static long OpenUserPromptStartExclusive(
        IReadOnlyList<CompactionFactInput> rows, DateTime acceptedStartedAt)
    {
        var facts = new CompactionTranscriptFact[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            facts[i] = ToFact(rows[i], correlated: false, human: false);
        return CompactionContinuationPolicy.OpenStartExclusive(facts, acceptedStartedAt);
    }

    internal static CompactionTranscriptFact[] Build(
        IReadOnlyList<CompactionFactInput> rows,
        IReadOnlyCollection<Guid> checkIds,
        IReadOnlyList<string> humanMatchable)
    {
        var tokens = CheckTokenSet.Create(checkIds);
        var facts = new CompactionTranscriptFact[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var text = row.Text;
            var prompt = row.Kind == TranscriptKinds.UserPrompt && text is not null;
            facts[i] = ToFact(
                row,
                correlated: prompt && tokens.Correlates(text!),
                human: prompt && MatchesHuman(text!, humanMatchable));
        }

        return facts;
    }

    /// <summary>
    /// Same flags and policy verdict as <see cref="Build"/> when every row's text is supplied,
    /// but text the policy never reads is dropped. Compact-boundary text, interrupt prefixes,
    /// and user-prompt text after the last effective end stay.
    /// </summary>
    internal static CompactionTranscriptFact[] BuildJudged(
        IReadOnlyList<CompactionFactInput> rows,
        DateTime acceptedStartedAt,
        IReadOnlyCollection<Guid> checkIds,
        IReadOnlyList<string> humanMatchable)
    {
        var start = OpenUserPromptStartExclusive(rows, acceptedStartedAt);
        var judged = new CompactionFactInput[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            judged[i] = KeepText(row, start) ? row : row with { Text = null };
        }

        return Build(judged, checkIds, humanMatchable);
    }

    private static bool KeepText(CompactionFactInput row, long start)
    {
        if (row.Kind == TranscriptKinds.CompactBoundary)
            return true;
        if (row.Kind != TranscriptKinds.UserPrompt || row.Text is null)
            return false;
        if (TranscriptKinds.IsInterruptPrompt(row.Kind, row.Text))
            return true;
        return row.Sequence > start;
    }

    private static bool MatchesHuman(string text, IReadOnlyList<string> humanMatchable)
    {
        for (var i = 0; i < humanMatchable.Count; i++)
        {
            var body = humanMatchable[i];
            // A body too short to identify makes IsCompleteIn vacuously true.
            if (!PromptSubmissionMatch.RequiresTextMatch(body))
                continue;
            if (PromptSubmissionMatch.IsCompleteIn(body, text))
                return true;
        }

        return false;
    }

    private static CompactionTranscriptFact ToFact(CompactionFactInput row, bool correlated, bool human) =>
        new(row.Sequence, row.Kind, row.Text, row.EventTime, row.CreatedAt, row.NativeId, row.ToolUseId,
            correlated, human);
}

internal sealed class CheckTokenSet
{
    private const string MarkerPrefix = "[antiphon-task:";

    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _d;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _n;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _short;

    private CheckTokenSet(
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> d,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> n,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> shortIds)
    {
        _d = d;
        _n = n;
        _short = shortIds;
    }

    internal static CheckTokenSet Create(IReadOnlyCollection<Guid> ids)
    {
        var d = new HashSet<string>(StringComparer.Ordinal);
        var n = new HashSet<string>(StringComparer.Ordinal);
        var shortIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var bare = id.ToString("N");
            d.Add(id.ToString("D"));
            n.Add(bare);
            shortIds.Add(bare[..8]);
        }

        return new CheckTokenSet(
            d.GetAlternateLookup<ReadOnlySpan<char>>(),
            n.GetAlternateLookup<ReadOnlySpan<char>>(),
            shortIds.GetAlternateLookup<ReadOnlySpan<char>>());
    }

    internal bool Correlates(string text) =>
        ContainsMarker(text) || ContainsD(text) || ContainsN(text);

    private bool ContainsMarker(string text)
    {
        var from = 0;
        while (from < text.Length)
        {
            var at = text.IndexOf(MarkerPrefix, from, StringComparison.Ordinal);
            if (at < 0)
                return false;
            var idAt = at + MarkerPrefix.Length;
            if (idAt + 8 < text.Length
                && text[idAt + 8] == ']'
                && IsHex8(text, idAt)
                && _short.Contains(text.AsSpan(idAt, 8)))
                return true;
            from = at + 1;
        }

        return false;
    }

    private bool ContainsD(string text)
    {
        for (var i = 0; i + 36 <= text.Length; i++)
        {
            if (text[i + 8] != '-' || text[i + 13] != '-' || text[i + 18] != '-' || text[i + 23] != '-')
                continue;
            if (!IsDHex(text, i))
                continue;
            if (_d.Contains(text.AsSpan(i, 36)))
                return true;
        }

        return false;
    }

    private bool ContainsN(string text)
    {
        var runStart = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (IsLowerHex(text[i]))
            {
                if (runStart < 0)
                    runStart = i;
                continue;
            }

            if (runStart >= 0 && ScanHexRun(text, runStart, i))
                return true;
            runStart = -1;
        }

        return runStart >= 0 && ScanHexRun(text, runStart, text.Length);
    }

    private bool ScanHexRun(string text, int start, int end)
    {
        var length = end - start;
        if (length < 32)
            return false;
        for (var offset = 0; offset <= length - 32; offset++)
        {
            if (_n.Contains(text.AsSpan(start + offset, 32)))
                return true;
        }

        return false;
    }

    private static bool IsDHex(string text, int i)
    {
        for (var k = 0; k < 8; k++)
            if (!IsLowerHex(text[i + k])) return false;
        for (var k = 9; k < 13; k++)
            if (!IsLowerHex(text[i + k])) return false;
        for (var k = 14; k < 18; k++)
            if (!IsLowerHex(text[i + k])) return false;
        for (var k = 19; k < 23; k++)
            if (!IsLowerHex(text[i + k])) return false;
        for (var k = 24; k < 36; k++)
            if (!IsLowerHex(text[i + k])) return false;
        return true;
    }

    private static bool IsHex8(string text, int i)
    {
        for (var k = 0; k < 8; k++)
            if (!IsLowerHex(text[i + k])) return false;
        return true;
    }

    private static bool IsLowerHex(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f';
}
