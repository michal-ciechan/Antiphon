using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

/// <summary>Pure, single-message formatting for the human-facing digest and blocked ping.</summary>
public static class AwayDigestFormatter
{
    public const int MaxChars = 3500;
    public const int RowsPerSection = 5;
    public const int SentenceChars = 140;

    public static string? FormatDigest(AwayDigestDto digest, DigestSettings settings, DateTimeOffset localNow)
    {
        var lines = new List<string>
        {
            $"**While you were away** · {(digest.FirstWindow ? "last 24h" : $"since {digest.SinceUtc.ToLocalTime():HH:mm}")}" 
        };
        var rows = Math.Max(1, settings.RowsPerSection);
        AddTasks(lines, "❗ Needs you", digest.NeedsYou, "asked", rows);
        if (digest.Decisions.Count > 0)
            AddCards(lines, "❓ Decisions", digest.Decisions, rows, includeDetail: true);
        AddTasks(lines, "✗ Failed", digest.Failed, "failed", rows);
        AddTasks(lines, "✓ Finished", digest.Finished, "finished", rows);
        if (digest.Review.Count > 0)
            AddCards(lines, "◉ Ready for review", digest.Review, rows);
        if (digest.Running.Count > 0)
        {
            var elapsed = digest.Running.StartedAt is null ? string.Empty : $" · longest {Duration(localNow.UtcDateTime - digest.Running.StartedAt.Value)}";
            lines.Add($"▶ Running ({digest.Running.Count}){elapsed}{(digest.Running.Title is null ? string.Empty : $" ({Clean(digest.Running.Title, SentenceChars)})")}");
        }
        if (digest.Spend.SettledSpendUsd > 0)
            lines.Add($"Spend: ${digest.Spend.SettledSpendUsd:F2} settled · biggest root ${digest.Spend.BiggestRootUsd:F2}");
        foreach (var usage in digest.Subscription)
            lines.Add($"{usage.Provider} {usage.RemainingPercent:0}% left" + (usage.ResetsAt is null ? string.Empty : $", resets {usage.ResetsAt:ddd}"));

        if (lines.Count == 1 && digest.Running.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(settings.PublicBaseUrl)) lines.Add(settings.PublicBaseUrl.TrimEnd('/') + "/");
        return Cap(string.Join('\n', lines), Math.Min(MaxChars, Math.Max(1, settings.MaxChars)));
    }

    public static string FormatPing(AttentionItemDto blocked)
    {
        var id = blocked.TaskId?.ToString("N")[..8] ?? "unknown";
        return Cap($"❓ task {id} needs an answer — {blocked.Title}\n{Clean(blocked.Evidence, SentenceChars)} (blocked {blocked.SinceUtc?.ToLocalTime():HH:mm}, ${(blocked.SubtreeCostUsd ?? 0m):F2} so far)", MaxChars);
    }

    public static string FormatDecisionPing(AttentionItemDto decision, DigestSettings? settings = null)
    {
        var title = decision.Title.Split(" — ", 2);
        var identifier = title[0];
        var cardTitle = title.Length > 1 ? title[1] : decision.Title;
        var text = $"❓ {identifier} needs a decision — {cardTitle}\n{Clean(decision.Evidence, SentenceChars)} (parked {decision.SinceUtc?.ToLocalTime():HH:mm})";
        if (!string.IsNullOrWhiteSpace(settings?.PublicBaseUrl))
            text += "\n" + settings.PublicBaseUrl.TrimEnd('/') + "/orchestrator?tab=decisions";
        return Cap(text, MaxChars);
    }

    public static string FormatQuiet(AwayDigestDto digest, DateTimeOffset localNow) =>
        $"Quiet since {digest.SinceUtc.ToLocalTime():HH:mm} · {digest.Running.Count} running · nothing needs you";

    private static void AddTasks(List<string> lines, string heading, IReadOnlyList<AwayDigestTaskDto> tasks, string verb, int rows)
    {
        if (tasks.Count == 0) return;
        lines.Add($"{heading} ({tasks.Count})");
        foreach (var task in tasks.Take(rows))
            lines.Add($"• {task.ShortId} {Clean(task.Title, 80)} — {verb}: {Clean(task.Detail, SentenceChars)}{(task.CostUsd > 0 ? $" (${task.CostUsd:F2})" : string.Empty)}");
        if (tasks.Count > rows) lines.Add($"• +{tasks.Count - rows} more");
    }
    private static void AddCards(List<string> lines, string heading, IReadOnlyList<AwayDigestCardDto> cards, int rows, bool includeDetail = false)
    {
        lines.Add($"{heading} ({cards.Count})");
        foreach (var card in cards.Take(rows))
            lines.Add($"• {card.Identifier} {Clean(card.Title, SentenceChars)}" +
                (includeDetail && !string.IsNullOrWhiteSpace(card.Detail) ? $" — {Clean(card.Detail, SentenceChars)}" : string.Empty));
        if (cards.Count > rows) lines.Add($"• +{cards.Count - rows} more");
    }
    private static string Clean(string? text, int max)
    {
        var flat = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return flat.Length <= max ? flat : flat[..(max - 1)] + "…";
    }
    private static string Cap(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "…";
    private static string Duration(TimeSpan span) => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h{span.Minutes:00}m" : $"{Math.Max(0, (int)span.TotalMinutes)}m";
}
