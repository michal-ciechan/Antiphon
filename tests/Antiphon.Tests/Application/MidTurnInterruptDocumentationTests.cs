using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0491 V-7. The invariant paragraph names the tests that pin it and does not use
/// absolute words.
/// </summary>
[Category("Unit")]
public sealed class MidTurnInterruptDocumentationTests
{
    [Test]
    public async Task C0491_InvariantNamesTheTestsAndAvoidsAbsolutes()
    {
        var text = Read("docs/session-runtime-invariants.md").Replace("\r\n", "\n");
        var paragraphs = text.Split("\n\n", StringSplitOptions.None);
        var card = paragraphs.Single(p =>
            p.Contains("CARD-0491", StringComparison.Ordinal)
            && p.Contains("Pinned by", StringComparison.Ordinal));

        var pinned = card.IndexOf("Pinned by", StringComparison.Ordinal);
        var written = card.IndexOf(
            "AgentTaskMidTurnRefineTests.C0491_InterruptWritesOneConditionalCtrlCAfterTheMarkedRow",
            StringComparison.Ordinal);
        var refused = card.IndexOf(
            "C0491_RefusalKeepsTheRowPendingAndSendsNoKey", StringComparison.Ordinal);
        var cancelled = card.IndexOf(
            "C0491_CancelledBoundaryKeepsWorkingFlushesTheRowAndConfirmsFromTheUserPrompt",
            StringComparison.Ordinal);
        written.ShouldBeGreaterThan(pinned);
        refused.ShouldBeGreaterThan(written);
        cancelled.ShouldBeGreaterThan(refused);
        Regex.IsMatch(card, @"\b(always|never|every)\b", RegexOptions.IgnoreCase).ShouldBeFalse();
        await Task.CompletedTask;
    }

    private static string Read(string relative)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "docs", "orchestration-loop.md")))
                return File.ReadAllText(Path.Combine(directory, relative));
            directory = Path.GetDirectoryName(directory)!;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
