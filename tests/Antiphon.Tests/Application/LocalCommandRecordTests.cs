using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class LocalCommandRecordTests
{
    private const int Iterations = 1_000;

    private static readonly string LongTail = new('x', 8_000);

    private static readonly string[] LeadingPrefixes = BuildPrefixes();

    private static readonly string[] NotificationShort =
    [
        " \t" + TranscriptKinds.TaskNotificationPrefix,
        "\u2003" + TranscriptKinds.TaskNotificationPrefix + "body",
        "\nordinary",
        "\uFEFF" + TranscriptKinds.TaskNotificationPrefix,
        "",
        " " + TranscriptKinds.TaskNotificationPrefix.ToUpperInvariant(),
    ];

    private static readonly string NotificationLong =
        "\u00a0" + TranscriptKinds.TaskNotificationPrefix + LongTail;

    private static readonly string[] CompactionShort =
    [
        " \t" + TranscriptKinds.CompactionContinuationPromptPrefix,
        "\u2003" + TranscriptKinds.CompactionContinuationPromptPrefix + " tail",
        "\nordinary",
        "\uFEFF" + TranscriptKinds.CompactionContinuationPromptPrefix,
        "",
        " " + TranscriptKinds.CompactionContinuationPromptPrefix.ToUpperInvariant(),
    ];

    private static readonly string CompactionLong =
        "\u3000" + TranscriptKinds.CompactionContinuationPromptPrefix + LongTail;

    private static readonly string NameMiss = "\u1680" + LongTail;

    private static readonly string NameHit =
        "\u2003<command-name>/compact</command-name>" + LongTail;

    private static readonly string[] EchoShort =
    [
        "\t/compact",
        " /compact args",
        "\n/compacting",
        "\u2003ordinary",
        "",
        "\uFEFF/compact",
    ];

    private static readonly string EchoLong = "\t/compact " + LongTail;

    private static readonly HashSet<string> EchoHash = new(StringComparer.Ordinal) { "/compact" };

    private static readonly List<string> EchoList = ["/compact"];

    private int _sink;

    [Test]
    public void Record_check_matches_original_over_corpus()
    {
        var texts = new string?[]
        {
            null, "", " ", "<command-name>", "<local-command-stdout>",
            "<COMMAND-NAME>", "<Local-command-stdout>", "<command-name", "<command-names>",
            "x<command-name>", "/status", "<local-command-stdoutx>", "<command-name>λ🙂</command-name>",
            new string('x', 100_000), "<local-command-stdout>" + new string('λ', 100_000),
        };
        foreach (var kind in new string?[] { null, "", "userprompt", TranscriptKinds.UserPrompt, TranscriptKinds.QueuedUserPrompt })
        foreach (var prefix in LeadingPrefixes)
        foreach (var text in texts)
        {
            var input = text is null ? null : prefix + text;
            TranscriptKinds.IsLocalCommandRecord(kind, input).ShouldBe(OriginalRecord(kind, input));
        }
    }

    [Test]
    public void Record_check_allocates_zero_bytes()
    {
        var inputs = new[]
        {
            " \t<command-name>/status</command-name>", "\u2003<local-command-stdout>ok",
            " \nordinary prompt", " <COMMAND-NAME>", "", "<command-name>",
        };
        var matches = 0;
        for (var warmup = 0; warmup < 10_000; warmup++)
            foreach (var input in inputs)
                if (TranscriptKinds.IsLocalCommandRecord(TranscriptKinds.UserPrompt, input)) matches++;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 10_000; iteration++)
            foreach (var input in inputs)
                if (TranscriptKinds.IsLocalCommandRecord(TranscriptKinds.UserPrompt, input)) matches++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        matches.ShouldBe(60_000);
        allocated.ShouldBe(0);
    }

    [Test]
    public void Notification_check_matches_original_over_corpus()
    {
        var bodies = new[]
        {
            TranscriptKinds.TaskNotificationPrefix,
            TranscriptKinds.TaskNotificationPrefix + "\n<summary>done</summary>\n</task-notification>",
            TranscriptKinds.TaskNotificationPrefix.ToUpperInvariant(),
            "<Task-Notification>",
            "<task-notification",
            "<task-notifications>",
            "x" + TranscriptKinds.TaskNotificationPrefix,
            "",
            " \t",
            TranscriptKinds.TaskNotificationPrefix + "\uD83D\uDE00",
            "\uD83D\uDE00" + TranscriptKinds.TaskNotificationPrefix,
            new string('λ', 4_000),
            TranscriptKinds.TaskNotificationPrefix + new string('x', 100_000),
        };
        ForEach(bodies, (kind, input) =>
            TranscriptKinds.IsTaskNotificationPrompt(kind, input).ShouldBe(OriginalNotification(kind, input)));
    }

    [Test]
    public void Compaction_check_matches_original_over_corpus()
    {
        var prefix = TranscriptKinds.CompactionContinuationPromptPrefix;
        var bodies = new[]
        {
            prefix,
            prefix + " that ran out of context.",
            prefix.ToUpperInvariant(),
            prefix[..^1],
            "This session is being continued from a previous conversations",
            "",
            " \t",
            prefix + "\uD83D\uDE00",
            "\uD83D\uDE00" + prefix,
            new string('λ', 4_000),
            prefix + new string('x', 100_000),
        };
        ForEach(bodies, (kind, input) =>
            TranscriptKinds.IsCompactionContinuationPrompt(kind, input).ShouldBe(OriginalCompaction(kind, input)));
    }

    [Test]
    public void Command_name_matches_original_over_corpus()
    {
        var bodies = new[]
        {
            "<command-name>/compact</command-name>",
            "<command-name>/compact </command-name>",
            "<command-name>  /compact  </command-name>",
            "<command-name></command-name>",
            "<command-name> </command-name>",
            "<command-name>/compact",
            "<command-name></command-name>/compact",
            "<COMMAND-NAME>/compact</command-name>",
            "<local-command-stdout>/compact</local-command-stdout>",
            "<command-name>/\uD83D\uDE00</command-name>",
            "<command-name>\uFEFF/compact</command-name>",
            "<command-name>\uFEFF</command-name>",
            "<command-name>/compact</command-name>" + new string('x', 4_000),
            "<command-name>" + new string('a', 2_000) + "</command-name>",
            "",
            "/compact",
            " \t",
        };
        ForEach(bodies, (kind, input) =>
            TranscriptKinds.TryReadLocalCommandName(kind, input).ShouldBe(OriginalName(kind, input)));
        TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, null).ShouldBeNull();
    }

    [Test]
    public void Raw_echo_matches_original_over_corpus()
    {
        var bodies = new[]
        {
            "/compact",
            "/compact args",
            "/compact\targs",
            "/compacting is broken",
            "/compaction is the topic",
            "/status",
            "/Compact",
            "<command-name>/compact</command-name>",
            "",
            " ",
            TranscriptKinds.CompactionContinuationPromptPrefix,
            "/\uD83D\uDE00",
            "/\uD83D\uDE00 tail",
            "/compact " + new string('x', 4_000),
            new string('x', 1_000),
        };
        IReadOnlyCollection<string>[] sets =
        [
            Array.Empty<string>(),
            new[] { "/compact" },
            new[] { "/compact", "/status" },
            new[] { "" },
            new[] { "", "/compact" },
            new List<string> { "/compact" },
            new HashSet<string>(StringComparer.Ordinal) { "/compact", "/status" },
            new HashSet<string> { "/\uD83D\uDE00" },
            new Queue<string>(new[] { "/compact" }),
        ];
        foreach (var invoked in sets)
        ForEach(bodies, (kind, input) =>
            TranscriptKinds.IsRawLocalCommandEcho(kind, input, invoked)
                .ShouldBe(OriginalEcho(kind, input, invoked)));
    }

    [Test]
    public void Notification_check_allocates_zero_bytes()
    {
        var lines = new List<string>();
        var failed = false;
        var shortHits = 0;
        RequireNetZero("notification-short", () =>
        {
            foreach (var input in NotificationShort)
                if (TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, input)) shortHits++;
        }, NotificationShort.Length, lines, ref failed);
        var longHits = 0;
        RequireNetZero("notification-long", () =>
        {
            if (TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, NotificationLong)) longHits++;
        }, 1, lines, ref failed);
        Finish(lines, failed, shortHits, Iterations * 6 * 2, longHits, Iterations * 6);
    }

    [Test]
    public void Compaction_check_allocates_zero_bytes()
    {
        var lines = new List<string>();
        var failed = false;
        var shortHits = 0;
        RequireNetZero("compaction-short", () =>
        {
            foreach (var input in CompactionShort)
                if (TranscriptKinds.IsCompactionContinuationPrompt(TranscriptKinds.UserPrompt, input)) shortHits++;
        }, CompactionShort.Length, lines, ref failed);
        var longHits = 0;
        RequireNetZero("compaction-long", () =>
        {
            if (TranscriptKinds.IsCompactionContinuationPrompt(TranscriptKinds.UserPrompt, CompactionLong)) longHits++;
        }, 1, lines, ref failed);
        Finish(lines, failed, shortHits, Iterations * 6 * 2, longHits, Iterations * 6);
    }

    [Test]
    public void Command_name_miss_allocates_zero_bytes()
    {
        var lines = new List<string>();
        var failed = false;
        var hits = 0;
        RequireNetZero("command-name-miss", () =>
        {
            if (TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, NameMiss) is null) hits++;
        }, 1, lines, ref failed);
        Finish(lines, failed, hits, Iterations * 6, 0, 0);
    }

    [Test]
    public void Command_name_return_allocates_only_the_name()
    {
        const string expected = "/compact";
        var lines = new List<string>();
        var failed = false;
        var hits = 0;
        var baseline = Samples(static () => { });
        var names = Samples(() =>
        {
            var copy = expected.AsSpan().ToString();
            _sink += copy.Length;
        });
        var measured = Samples(() =>
        {
            var name = TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, NameHit);
            if (name == expected) hits++;
            _sink += name?.Length ?? 0;
        });
        for (var i = 0; i < measured.Length; i++)
        {
            var nameNet = names[i] - baseline[i];
            var productionNet = measured[i] - baseline[i];
            var extra = productionNet - nameNet;
            lines.Add(
                $"command-name-return sample={i} productionNet={productionNet} nameNet={nameNet} baseline={baseline[i]} extra={extra} calls={Iterations} extraBytesPerCall={(double)extra / Iterations:F2} productionBytesPerCall={(double)productionNet / Iterations:F2}");
            if (extra != 0 || nameNet <= 0) failed = true;
        }

        Finish(lines, failed, hits, Iterations * 6, 0, 0);
    }

    [Test]
    public void Raw_echo_check_allocates_zero_bytes()
    {
        var lines = new List<string>();
        var failed = false;
        var hashHits = 0;
        RequireNetZero("raw-echo-hashset-long", () =>
        {
            if (TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, EchoLong, EchoHash)) hashHits++;
        }, 1, lines, ref failed);
        var listHits = 0;
        RequireNetZero("raw-echo-list-long", () =>
        {
            if (TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, EchoLong, EchoList)) listHits++;
        }, 1, lines, ref failed);
        var shortHits = 0;
        RequireNetZero("raw-echo-hashset-short", () =>
        {
            foreach (var input in EchoShort)
                if (TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, input, EchoHash)) shortHits++;
        }, EchoShort.Length, lines, ref failed);
        Finish(lines, failed, hashHits, Iterations * 6, listHits, Iterations * 6);
        shortHits.ShouldBe(Iterations * 6 * 2);
    }

    private void RequireNetZero(string label, Action body, int callsPerIteration, List<string> lines, ref bool failed)
    {
        var baseline = Samples(static () => { });
        var measured = Samples(body);
        var calls = Iterations * callsPerIteration;
        for (var i = 0; i < measured.Length; i++)
        {
            var net = measured[i] - baseline[i];
            lines.Add($"{label} sample={i} net={net} baseline={baseline[i]} calls={calls} bytesPerCall={(double)net / calls:F2}");
            if (net != 0) failed = true;
        }
    }

    private static long[] Samples(Action body)
    {
        for (var warmup = 0; warmup < Iterations; warmup++)
            body();
        var samples = new long[5];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var n = 0; n < Iterations; n++)
                body();
            samples[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return samples;
    }

    private static void Finish(List<string> lines, bool failed, int hits, int expectedHits, int secondHits, int expectedSecond)
    {
        if (hits != expectedHits)
        {
            failed = true;
            lines.Add($"hits={hits} expected={expectedHits}");
        }

        if (expectedSecond != 0 && secondHits != expectedSecond)
        {
            failed = true;
            lines.Add($"secondHits={secondHits} expected={expectedSecond}");
        }

        var report = string.Join('\n', lines);
        Console.WriteLine(report);
        failed.ShouldBeFalse(report);
    }

    private static void ForEach(string[] bodies, Action<string?, string?> check)
    {
        var kinds = new string?[]
        {
            null, "", "userprompt", TranscriptKinds.UserPrompt, TranscriptKinds.QueuedUserPrompt, TranscriptKinds.AssistantText,
        };
        foreach (var kind in kinds)
        foreach (var prefix in LeadingPrefixes)
        foreach (var body in bodies)
            check(kind, prefix + body);
        check(TranscriptKinds.UserPrompt, null);
    }

    private static string[] BuildPrefixes()
    {
        var scalars = new[]
        {
            "\u0009", "\u000a", "\u000b", "\u000c", "\u000d", "\u0020",
            "\u0085", "\u00a0", "\u1680",
            "\u2000", "\u2001", "\u2002", "\u2003", "\u2004", "\u2005",
            "\u2006", "\u2007", "\u2008", "\u2009", "\u200a",
            "\u2028", "\u2029", "\u202f", "\u205f", "\u3000",
        };
        var all = string.Concat(scalars);
        var prefixes = new List<string>
        {
            "",
            " ",
            "\t\r\n",
            "\u00a0\u2003\u202f\u3000",
            "\u200b",
            "\ufeff",
            "\ufeff" + all,
            all + "\ufeff",
            all,
            "\uD83D\uDE00",
            new string(' ', 64),
            new string(' ', 10_000),
        };
        prefixes.AddRange(scalars);
        return prefixes.ToArray();
    }

    private static bool OriginalRecord(string? kind, string? text)
    {
        if (kind != TranscriptKinds.UserPrompt || text is null) return false;
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("<command-name>", StringComparison.Ordinal)
            || trimmed.StartsWith("<local-command-stdout>", StringComparison.Ordinal);
    }

    private static bool OriginalNotification(string? kind, string? text) =>
        kind == TranscriptKinds.UserPrompt
        && text is not null
        && text.TrimStart().StartsWith(TranscriptKinds.TaskNotificationPrefix, StringComparison.Ordinal);

    private static bool OriginalCompaction(string? kind, string? text) =>
        kind == TranscriptKinds.UserPrompt
        && text is not null
        && text.TrimStart().StartsWith(TranscriptKinds.CompactionContinuationPromptPrefix, StringComparison.Ordinal);

    private static string? OriginalName(string? kind, string? text)
    {
        if (kind != TranscriptKinds.UserPrompt || text is null)
            return null;
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith(TranscriptKinds.LocalCommandPrefix, StringComparison.Ordinal))
            return null;
        var close = trimmed.IndexOf("</command-name>", StringComparison.Ordinal);
        if (close <= TranscriptKinds.LocalCommandPrefix.Length)
            return null;
        var name = trimmed[TranscriptKinds.LocalCommandPrefix.Length..close].Trim();
        return name.Length == 0 ? null : name;
    }

    private static bool OriginalEcho(string? kind, string? text, IReadOnlyCollection<string> invoked)
    {
        if (kind != TranscriptKinds.UserPrompt || text is null || invoked.Count == 0)
            return false;
        if (OriginalRecord(kind, text))
            return false;
        var trimmed = text.TrimStart();
        foreach (var name in invoked)
        {
            if (name.Length == 0 || !trimmed.StartsWith(name, StringComparison.Ordinal))
                continue;
            if (trimmed.Length == name.Length || char.IsWhiteSpace(trimmed[name.Length]))
                return true;
        }

        return false;
    }
}
