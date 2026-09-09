using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Antiphon.Agents.Pty;

public enum ClaudeEffortOption { Unknown, Keep, Switch }

public sealed record ClaudeEffortIntent(string? Value = null, bool Invalid = false)
{
    public static ClaudeEffortIntent Read(IEnumerable<string> args)
    {
        var tokens = args.ToArray();
        string? value = null;
        var invalid = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "--") break;
            string? candidate;
            if (tokens[i] == "--effort")
                candidate = i + 1 < tokens.Length && !tokens[i + 1].StartsWith("--") ? tokens[++i] : "";
            else if (tokens[i].StartsWith("--effort=", StringComparison.Ordinal))
                candidate = tokens[i][9..];
            else continue;
            candidate = candidate.Trim().ToLowerInvariant();
            invalid |= !ClaudeEffortPrompt.IsEffort(candidate) || (value is not null && value != candidate);
            value = candidate;
        }
        return new(value, invalid);
    }
}

public sealed record ClaudeEffortMenu(string Model, string Current, string Suggested, ClaudeEffortOption Highlight)
{
    public bool SameIdentity(ClaudeEffortMenu other) => string.Equals(Model, other.Model, StringComparison.OrdinalIgnoreCase) && Current == other.Current && Suggested == other.Suggested;
    public ClaudeEffortOption Select(ClaudeEffortIntent intent)
    {
        if (intent.Invalid) return ClaudeEffortOption.Unknown;
        if (intent.Value is null || intent.Value == Current) return ClaudeEffortOption.Keep;
        return intent.Value == Suggested ? ClaudeEffortOption.Switch : ClaudeEffortOption.Unknown;
    }
}

public readonly record struct ClaudeEffortResolution(bool Cleared, string Detail);

/// <summary>The narrow startup-default picker, parsed as rows in one modal section.</summary>
public static class ClaudeEffortPrompt
{
    private const string Efforts = "low|medium|high|xhigh|max";
    public static bool IsEffort(string value) => value is "low" or "medium" or "high" or "xhigh" or "max";
    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();
    private static string Row(string text) => text.Trim(' ', '\t', '│', '┃', '║');

    public static ClaudeEffortMenu? Parse(string screen)
    {
        var rows = screen.ReplaceLineEndings("\n").Split('\n').Select(Row).ToArray();
        var fenced = false;
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i].StartsWith("```")) { fenced = !fenced; continue; }
            if (fenced || !rows[i].StartsWith("Use ", StringComparison.OrdinalIgnoreCase)) continue;
            var title = rows[i];
            var end = i;
            while (!title.EndsWith('?') && end + 1 < rows.Length && end < i + 3)
                title += " " + rows[++end];
            var question = Regex.Match(Normalize(title), $@"^use (.+) at ({Efforts}) effort by default\?$", RegexOptions.CultureInvariant);
            if (!question.Success) continue;
            Match? keep = null, change = null;
            var markers = 0;
            var highlight = ClaudeEffortOption.Unknown;
            for (var j = end + 1; j < rows.Length; j++)
            {
                var row = rows[j];
                if (row.Contains("```") || row.Any(c => c is '─' or '━' or '═') || row.StartsWith("Use ", StringComparison.OrdinalIgnoreCase)) break;
                var selected = row.StartsWith('>') || row.StartsWith('❯');
                var label = Normalize(selected ? row[1..] : row);
                var k = Regex.Match(label, $@"^keep ({Efforts})$");
                if (k.Success)
                {
                    if (keep is not null) return null;
                    keep = k;
                    if (selected) { markers++; highlight = ClaudeEffortOption.Keep; }
                }
                if (label.StartsWith("switch "))
                {
                    if (!label.EndsWith(" effort") && j + 1 < rows.Length)
                        label += " " + Normalize(rows[++j]);
                    var s = Regex.Match(label, $@"^switch (.+) to ({Efforts}) effort$");
                    if (!s.Success || change is not null) return null;
                    change = s;
                    if (selected) { markers++; highlight = ClaudeEffortOption.Switch; }
                }
            }
            if (keep is null || change is null
                || question.Groups[1].Value != change.Groups[1].Value
                || question.Groups[2].Value != change.Groups[2].Value) continue;
            var displayModel = Regex.Match(Regex.Replace(title.Trim(), @"\s+", " "), $@"^use (.+) at ({Efforts}) effort by default\?$", RegexOptions.IgnoreCase).Groups[1].Value;
            return new(displayModel, keep.Groups[1].Value, question.Groups[2].Value,
                markers == 1 ? highlight : ClaudeEffortOption.Unknown);
        }
        return null;
    }

    public static bool HasRemnant(string screen) => Regex.IsMatch(screen,
        @"(?im)^\s*[│┃║]?\s*(?:Use .+ effort by default\?|[>❯]?\s*Keep (?:low|medium|high|xhigh|max)\s*$|[>❯]?\s*Switch .+ effort\s*$)");

    public static string? CurrentEffort(string screen)
    {
        // Current-session banner only: descriptive recommendation text is not applied state.
        var match = Regex.Match(screen, $@"(?im)^.*\bwith ({Efforts}) effort(?:\s*[·│]|\s*$)");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static async Task<ClaudeEffortResolution> ResolveAsync(
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, CancellationToken, Task> write,
        ClaudeEffortIntent intent,
        TimeSpan budget,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(budget > TimeSpan.Zero ? budget : TimeSpan.FromMilliseconds(1));
        var token = bounded.Token;
        ClaudeEffortMenu? original = null;
        var target = ClaudeEffortOption.Unknown;
        var enters = 0;
        ClaudeEffortResolution Result(bool cleared, string cause) => new(cleared,
            $"requested={ (intent.Invalid ? "invalid" : intent.Value ?? "absent (Keep option)") }; current={original?.Current ?? "unknown"}; "
            + $"suggested={original?.Suggested ?? "unknown"}; selected={target}; Enter={enters}; {cause}. "
            + (cleared ? "" : "Inspect the effort picker and relaunch with a supported explicit effort."));
        try
        {
            original = Parse(await snapshotScreen(token));
            if (original is null) return Result(false, "no complete effort dialog");
            target = original.Select(intent);
            if (target == ClaudeEffortOption.Unknown || original.Highlight == ClaudeEffortOption.Unknown)
                return Result(false, "ambiguous intent or highlight; input withheld");
            await Task.Delay(ClaudeTrustDialogKeys.HighlightSettle, token);
            var nextEnter = TimeSpan.Zero;
            var navigation = 0;
            var clearFrames = 0;
            while (clock.Elapsed < budget)
            {
                var screen = await snapshotScreen(token);
                var menu = Parse(screen);
                if (menu is null)
                {
                    var nextModal = ClaudeBlockingPromptDetector.Detect(screen);
                    if (enters > 0 && !HasRemnant(screen)
                        && (ClaudeScreen.ComposerIsLive(screen) || nextModal is not null))
                    {
                        var observed = CurrentEffort(screen);
                        var desired = target == ClaudeEffortOption.Keep ? original.Current : original.Suggested;
                        if (observed is not null && observed != desired)
                            return Result(false, $"visible current effort contradicts selection ({observed})");
                        if (++clearFrames >= 2) return Result(true, "two clear observations");
                    }
                    else clearFrames = 0;
                    await Task.Delay(50, token);
                    continue;
                }
                clearFrames = 0;
                if (!original.SameIdentity(menu) || menu.Highlight == ClaudeEffortOption.Unknown)
                    return Result(false, "dialog identity or highlight changed; input withheld");
                string? key = null;
                if (menu.Highlight != target && enters == 0 && navigation < ClaudeTrustDialogKeys.HighlightNextCandidates.Length)
                    key = ClaudeTrustDialogKeys.HighlightNextCandidates[navigation++];
                else if (enters < 3 && clock.Elapsed >= nextEnter)
                {
                    if (menu.Highlight != target) return Result(false, "intended highlight not reached; Enter withheld");
                    key = "\r";
                }
                if (key is not null)
                {
                    // Mandatory fresh validation immediately before every key, including retries.
                    var fresh = Parse(await snapshotScreen(token));
                    if (fresh is null || !original.SameIdentity(fresh) || fresh.Highlight != menu.Highlight)
                        return Result(false, "fresh dialog identity or highlight changed; input withheld");
                    token.ThrowIfCancellationRequested();
                    await write(key, token);
                    if (key == "\r") { enters++; nextEnter = clock.Elapsed + ClaudeTrustDialogKeys.HighlightSettle; }
                    else await Task.Delay(ClaudeTrustDialogKeys.HighlightSettle, token);
                }
                await Task.Delay(50, token);
            }
            return Result(false, "settle deadline exhausted");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result(false, "settle deadline exhausted");
        }
    }
}
