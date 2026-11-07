using System.Collections;
using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1760. <see cref="TaskReportTurnSelector.LastBefore"/> must keep the last
/// strictly-earlier prompt. The probe bound fails while that method still walks
/// every row from the start.
/// </summary>
[Category("Unit")]
public class TaskReportTurnSelectorTests
{
    private static readonly DateTime Stamp = new(2026, 11, 7, 12, 16, 35, DateTimeKind.Utc);

    [Test]
    public async Task LastBefore_matches_the_linear_oracle()
    {
        foreach (var (rows, sequence) in SeededTranscripts())
        {
            TaskReportTurnSelector.LastBefore(rows, sequence)
                .ShouldBe(LinearOracle(rows, sequence));
        }

        var random = new Random(1760);
        for (var trial = 0; trial < 200; trial++)
        {
            var rows = RandomTranscript(random);
            foreach (var sequence in Queries(random, rows))
            {
                TaskReportTurnSelector.LastBefore(rows, sequence)
                    .ShouldBe(LinearOracle(rows, sequence));
            }
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task LastBefore_bounds_the_probe_count()
    {
        const int count = 4096;
        var rows = new TranscriptPromptSpan.PromptRow[count];
        for (var i = 0; i < count; i++)
        {
            rows[i] = new TranscriptPromptSpan.PromptRow(
                i * 2L, "p" + i, Stamp, TranscriptKinds.UserPrompt);
        }

        var probe = new ProbeList(rows);
        var sequence = rows[count - 1].Sequence + 1;
        var found = TaskReportTurnSelector.LastBefore(probe, sequence);
        found.ShouldBe(LinearOracle(rows, sequence));

        // A forward scan enumerates this list or reads thousands of slots.
        // A binary search over 4096 ordered rows stays within a few probes.
        var bound = (2 * (int)Math.Ceiling(Math.Log2(count))) + 2;
        probe.Enumerated.ShouldBe(0);
        probe.IndexReads.ShouldBeGreaterThan(0);
        probe.IndexReads.ShouldBeLessThanOrEqualTo(bound);
        await Task.CompletedTask;
    }

    /// <summary>The pre-CARD-1760 scan. The production method must agree with it.</summary>
    private static TranscriptPromptSpan.PromptRow? LinearOracle(
        IReadOnlyList<TranscriptPromptSpan.PromptRow> rows, long sequence)
    {
        TranscriptPromptSpan.PromptRow? owner = null;
        foreach (var row in rows)
        {
            if (row.Sequence >= sequence)
                break;
            owner = row;
        }

        return owner;
    }

    private static IEnumerable<(IReadOnlyList<TranscriptPromptSpan.PromptRow> Rows, long Sequence)> SeededTranscripts()
    {
        var equalClock = Stamp;
        var laterSequenceEarlierClock = Stamp.AddHours(-2);

        yield return ([], 0);
        yield return ([], 40);

        var only = new[] { Row(10, "only", null) };
        yield return (only, 10);
        yield return (only, 9);
        yield return (only, 11);

        var sameTimestamp = new[]
        {
            Row(10, "older", equalClock),
            Row(20, "same-clock", equalClock),
            Row(30, "still-same-clock", equalClock),
        };
        yield return (sameTimestamp, 10);
        yield return (sameTimestamp, 20);
        yield return (sameTimestamp, 25);
        yield return (sameTimestamp, 31);

        var duplicateSequence = new[]
        {
            Row(10, "before", equalClock),
            Row(20, "dup-first", equalClock),
            Row(20, "dup-last", equalClock.AddMinutes(1)),
            Row(30, "after", null),
        };
        yield return (duplicateSequence, 20);
        yield return (duplicateSequence, 21);
        yield return (duplicateSequence, 30);

        var clockDisagreesWithSequence = new[]
        {
            Row(10, "earlier-sequence", Stamp),
            Row(20, "later-sequence", laterSequenceEarlierClock),
        };
        yield return (clockDisagreesWithSequence, 25);
        yield return (clockDisagreesWithSequence, 15);

        var nullText = new[] { Row(5, null, null), Row(8, null, equalClock) };
        yield return (nullText, 8);
        yield return (nullText, 9);
    }

    private static List<TranscriptPromptSpan.PromptRow> RandomTranscript(Random random)
    {
        var count = random.Next(0, 48);
        var rows = new List<TranscriptPromptSpan.PromptRow>(count);
        var sequence = (long)random.Next(0, 8);
        for (var i = 0; i < count; i++)
        {
            sequence += random.Next(0, 4);
            DateTime? timestamp = random.Next(5) switch
            {
                0 => null,
                1 => rows.Count == 0 ? Stamp : rows[rows.Count - 1].Timestamp,
                _ => Stamp.AddSeconds(random.Next(0, 20_000)),
            };
            var text = random.Next(4) == 0 ? null : "row-" + i;
            var kind = random.Next(2) == 0
                ? TranscriptKinds.UserPrompt
                : TranscriptKinds.QueuedUserPrompt;
            rows.Add(new TranscriptPromptSpan.PromptRow(sequence, text, timestamp, kind));
        }

        return rows;
    }

    private static IEnumerable<long> Queries(
        Random random, IReadOnlyList<TranscriptPromptSpan.PromptRow> rows)
    {
        if (rows.Count == 0)
        {
            yield return 0;
            yield return 1;
            yield break;
        }

        var first = rows[0].Sequence;
        var last = rows[rows.Count - 1].Sequence;
        var mid = rows[rows.Count / 2].Sequence;
        yield return first - 1;
        yield return first;
        yield return mid;
        yield return mid + 1;
        yield return last;
        yield return last + 1;
        for (var i = 0; i < 4; i++)
            yield return random.Next((int)(first - 1), (int)last + 2);
    }

    private static TranscriptPromptSpan.PromptRow Row(long sequence, string? text, DateTime? timestamp) =>
        new(sequence, text, timestamp, TranscriptKinds.UserPrompt);

    private sealed class ProbeList : IReadOnlyList<TranscriptPromptSpan.PromptRow>
    {
        private readonly IReadOnlyList<TranscriptPromptSpan.PromptRow> _rows;

        public ProbeList(IReadOnlyList<TranscriptPromptSpan.PromptRow> rows) => _rows = rows;

        public int IndexReads { get; private set; }

        public int Enumerated { get; private set; }

        public int Count => _rows.Count;

        public TranscriptPromptSpan.PromptRow this[int index]
        {
            get
            {
                IndexReads++;
                return _rows[index];
            }
        }

        public IEnumerator<TranscriptPromptSpan.PromptRow> GetEnumerator()
        {
            Enumerated++;
            return _rows.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
