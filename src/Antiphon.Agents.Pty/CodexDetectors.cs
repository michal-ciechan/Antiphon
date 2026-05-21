using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

/// <summary>
/// Waits until the Codex TUI has settled enough to accept input.
/// Codex does not currently expose a stable ready token through the PTY, so
/// readiness is a quiet terminal window after startup noise.
/// </summary>
public sealed class CodexReadyDetector
{
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(60);

    public async Task<bool> WaitAsync(
        PtyAgentRunner runner,
        Func<CancellationToken, Task>? observeStartupAsync = null,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + MaxWait;
        var lastLength = runner.SnapshotText().Length;
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            if (observeStartupAsync is not null)
                await observeStartupAsync(ct);

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }

            var currentLength = runner.SnapshotText().Length;
            if (currentLength != lastLength)
            {
                lastLength = currentLength;
                lastChange = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - lastChange >= QuietPeriod)
                return true;
        }

        return false;
    }
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
        => runner.WaitForQuietAsync(QuietPeriod, MaxWait, ct);
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
            var promptIndex = clean.IndexOf(prompt, StringComparison.Ordinal);
            if (promptIndex >= 0)
                clean = clean[(promptIndex + prompt.Length)..];
        }

        clean = BlankLineRun.Replace(clean, "\n\n");
        return clean.Trim();
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
