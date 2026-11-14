using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class LocalCommandRecordTests
{
    [Test]
    public void Record_check_matches_original_over_corpus()
    {
        var texts = new string?[] { null, "", " ", "<command-name>", "<local-command-stdout>",
            "<COMMAND-NAME>", "<Local-command-stdout>", "<command-name", "<command-names>",
            "x<command-name>", "/status", "<local-command-stdoutx>", "<command-name>λ🙂</command-name>",
            new string('x', 100_000), "<local-command-stdout>" + new string('λ', 100_000) };
        foreach (var kind in new string?[] { null, "", "userprompt", TranscriptKinds.UserPrompt, TranscriptKinds.QueuedUserPrompt })
        foreach (var prefix in new[] { "", " ", "\t\r\n", "\u00a0\u2003\u202f\u3000", "\u200b", new string(' ', 10_000) })
        foreach (var text in texts)
        {
            var input = text is null ? null : prefix + text;
            TranscriptKinds.IsLocalCommandRecord(kind, input).ShouldBe(Original(kind, input));
        }
    }

    [Test]
    public void Record_check_allocates_zero_bytes()
    {
        var inputs = new[] { " \t<command-name>/status</command-name>", "\u2003<local-command-stdout>ok",
            " \nordinary prompt", " <COMMAND-NAME>", "", "<command-name>" };
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

    private static bool Original(string? kind, string? text)
    {
        if (kind != TranscriptKinds.UserPrompt || text is null) return false;
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("<command-name>", StringComparison.Ordinal)
            || trimmed.StartsWith("<local-command-stdout>", StringComparison.Ordinal);
    }
}
