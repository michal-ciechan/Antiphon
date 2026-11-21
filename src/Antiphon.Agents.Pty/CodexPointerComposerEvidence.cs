namespace Antiphon.Agents.Pty;

/// <summary>Whole pointer evidence from one current, bottom Codex composer. History is excluded.</summary>
public static class CodexPointerComposerEvidence
{
    public static bool IsComplete(string screen, string body) =>
        TryReadComposer(screen, out var content) && ComposerDeliveryEvidence.WholeBodyEquals(body, content);

    public static int MatchingPrefixLength(string screen, string body)
    {
        if (!TryReadComposer(screen, out var content)) return 0;
        var expected = ComposerDeliveryEvidence.Normalize(body);
        var actual = ComposerDeliveryEvidence.Normalize(content);
        var i = 0;
        while (i < expected.Length && i < actual.Length && expected[i] == actual[i]) i++;
        return i;
    }

    private static bool TryReadComposer(string screen, out string content)
    {
        content = "";
        var classification = CodexStartupScreen.Classify(screen);
        if (classification.Reason is not (CodexStartupReason.Unknown or CodexStartupReason.Ready))
            return false;
        var lines = CodexStartupScreen.NormalizeLines(screen);
        var footer = lines.Length - 1;
        while (footer >= 0 && string.IsNullOrWhiteSpace(lines[footer])) footer--;
        if (footer < 0)
            return false;
        // Codex 0.170.0 draws an agents/shortcuts row under the model footer while
        // the composer is empty. With text in the composer that row keeps only the
        // right-aligned warning chip. Step back to the footer. A 0.160 footer keeps
        // the chip on the GPT-* line, which fails IsWarningChipRow and stays put.
        if (CodexStartupScreen.IsAgentShortcutsRow(lines[footer])
            || IsWarningChipRow(lines[footer]))
        {
            footer--;
            while (footer >= 0 && string.IsNullOrWhiteSpace(lines[footer])) footer--;
        }
        if (footer < 1 || !CodexStartupScreen.IsModelEffortCwdFooter(lines[footer])
            || !CodexStartupScreen.TryReadFooterOnlyModel(lines[footer].Trim(), out _)) return false;
        var bottom = footer - 1;
        while (bottom >= 0 && string.IsNullOrWhiteSpace(lines[bottom])) bottom--;
        for (var row = bottom; row >= 0; row--)
        {
            if (CodexStartupScreen.TryParseComposer(lines[row], out _, out var first))
            {
                content = first + string.Concat(lines.Skip(row + 1).Take(bottom - row));
                return true;
            }
            // Wrapped input rows are indented. A blank separator or unknown layout cannot
            // borrow an older prompt glyph from the conversation above this composer.
            if (string.IsNullOrWhiteSpace(lines[row]) || !lines[row].StartsWith("  ", StringComparison.Ordinal))
                return false;
        }
        return false;
    }

    private static bool IsWarningChipRow(string line)
    {
        var t = line.Trim();
        return t.Contains("f2 to view", StringComparison.Ordinal)
            && t.Contains("warning", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("GPT-", StringComparison.Ordinal);
    }
}
