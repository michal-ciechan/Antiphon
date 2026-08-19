using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Waits until the Codex TUI has settled enough to accept input.
/// Codex does not currently expose a stable ready token through the PTY, so
/// readiness is visible child output followed by a quiet terminal window
/// (CARD-0052: empty or title-only snapshots are not ready).
/// </summary>
public sealed class CodexReadyDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);

    public Task<bool> WaitAsync(
        PtyAgentRunner runner,
        Func<CancellationToken, Task>? observeStartupAsync = null,
        CancellationToken ct = default)
        => runner.WaitForQuietAfterVisibleAsync(QuietPeriod, MaxWait, ct, observeStartupAsync);
}

/// <summary>
/// Codex turn completion detector. A completed turn or a Codex question both
/// settle into a quiet TUI, so the adapter treats terminal quiet as the turn
/// boundary and lets <see cref="CodexResponseAnalyzer"/> classify the text.
/// </summary>
public sealed class CodexDoneDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(5);

    public Task<bool> WaitAsync(PtyAgentRunner runner, CancellationToken ct = default)
        => runner.WaitForQuietAfterVisibleAsync(QuietPeriod, MaxWait, ct);
}

public static class CodexResponseAnalyzer
{
    private static readonly Regex BlankLineRun =
        new(@"\n{3,}", RegexOptions.Compiled);

    public static bool IsAskingQuestion(string? rawSnapshot, string? prompt = null) =>
        ExtractResponse(rawSnapshot, prompt).Contains('?');

    public static string ExtractResponse(string? rawSnapshot, string? prompt = null)
    {
        var clean = AnsiStripper.Clean(rawSnapshot) ?? "";
        clean = clean.Replace("\r\n", "\n").Replace('\r', '\n');

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            var echoEnd = FindPromptEchoEnd(clean, prompt);
            if (echoEnd >= 0)
                clean = clean[echoEnd..];
        }

        clean = BlankLineRun.Replace(clean, "\n\n");
        return clean.Trim();
    }

    /// <summary>
    /// Offset just past the echoed prompt, or -1 when no echo is present.
    /// <para>
    /// The terminal hard-wraps the echo at the window width, so a prompt whose echo crosses
    /// the right margin is NOT literally present in the snapshot — a newline sits inside it.
    /// Matching literally only, the echo survived into the response, which both leaked the
    /// user's own prompt into <c>ResponseText</c> and reported any '?' the prompt contained as
    /// the agent asking a question (which parks a channel-bound session waiting on a human).
    /// </para>
    /// <para>
    /// Wrapping is a function of the echo's column offset, so ordinary long prompts trip this
    /// in production. <c>CodexAdapterLocalShellTests</c> only saw it when cmd's prompt prefix —
    /// the cwd — pushed the echo past the margin, so it presented as depending on where the
    /// repo was checked out: green from C:\src\Antiphon, red from a deep worktree path.
    /// </para>
    /// </summary>
    private static int FindPromptEchoEnd(string clean, string prompt)
    {
        var normalized = prompt.Replace("\r\n", "\n").Replace('\r', '\n');

        var literal = clean.IndexOf(normalized, StringComparison.Ordinal);
        if (literal >= 0)
            return literal + normalized.Length;

        // Project the snapshot onto its newline-free form, remembering where each kept
        // character came from, so a match can be mapped back to an offset in the original.
        // Only newlines are dropped: ConPTY wraps by inserting a break at the margin and
        // does not pad or re-flow, so every other character of the echo survives verbatim.
        var flat = new StringBuilder(clean.Length);
        var origin = new int[clean.Length];
        for (var i = 0; i < clean.Length; i++)
        {
            if (clean[i] == '\n') continue;
            origin[flat.Length] = i;
            flat.Append(clean[i]);
        }

        var flatPrompt = normalized.Replace("\n", "");
        if (flatPrompt.Length == 0)
            return -1;

        var match = flat.ToString().IndexOf(flatPrompt, StringComparison.Ordinal);
        if (match < 0)
            return -1;

        return origin[match + flatPrompt.Length - 1] + 1;
    }
}

public static class CodexTrustPromptDetector
{
    public static bool IsVisible(string? rawSnapshot, string? renderedScreen = null)
    {
        var text = $"{AnsiStripper.Clean(rawSnapshot) ?? ""}\n{renderedScreen ?? ""}";
        var compact = Regex.Replace(text, @"\s+", "", RegexOptions.CultureInvariant)
            .ToLowerInvariant();

        return compact.Contains("doyoutrustthecontentsofthisdirectory")
            && compact.Contains("yes,continue");
    }
}
