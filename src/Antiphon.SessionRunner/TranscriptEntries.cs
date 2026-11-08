using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1764. The rows of a tailer's sequence-ordered entry list after a cursor.</summary>
internal static class TranscriptEntries
{
    /// <summary>
    /// Entries whose sequence is greater than <paramref name="since"/>, in order. Tailers append with a
    /// strictly increasing sequence, so the first such entry is found by binary search. A cursor at or
    /// below zero returns every entry.
    /// </summary>
    public static RunnerTranscriptEvent[] After(IReadOnlyList<RunnerTranscriptEvent> entries, long since)
    {
        var low = 0;
        if (since > 0)
        {
            var high = entries.Count;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (entries[mid].Sequence <= since)
                    low = mid + 1;
                else
                    high = mid;
            }
        }

        var result = new RunnerTranscriptEvent[entries.Count - low];
        for (var i = 0; i < result.Length; i++)
            result[i] = entries[low + i];
        return result;
    }
}
