using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The two diagnosis label families (CARD-0352 D4). Antiphon-local routing metadata, not
/// tracker-managed: they sit at the end of the card's label list (the face shows the first two,
/// which are the topic tags a human scans) and survive import by union, never export.
/// </summary>
public static class CardDiagnosisLabels
{
    public const string ComplexityPrefix = "complexity:";
    public const string UiPrefix = "ui:";
    public const string DiagnoseActor = "antiphon-diagnose";

    public static bool IsDiagnosisLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;
        var normalized = label.Trim();
        return normalized.StartsWith(ComplexityPrefix, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(UiPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasComplexity(IEnumerable<string> labels) =>
        labels.Any(l => l.Trim().StartsWith(ComplexityPrefix, StringComparison.OrdinalIgnoreCase));

    public static bool HasUi(IEnumerable<string> labels) =>
        labels.Any(l => l.Trim().StartsWith(UiPrefix, StringComparison.OrdinalIgnoreCase));

    public static bool HasBothFamilies(IEnumerable<string> labels)
    {
        var list = labels as ICollection<string> ?? labels.ToList();
        return HasComplexity(list) && HasUi(list);
    }

    public static TaskComplexity? Complexity(IEnumerable<string> labels)
    {
        foreach (var label in labels)
        {
            var trimmed = label.Trim();
            if (!trimmed.StartsWith(ComplexityPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = trimmed[ComplexityPrefix.Length..].Trim().ToLowerInvariant();
            return value switch
            {
                "hard" => TaskComplexity.Hard,
                "medium" => TaskComplexity.Medium,
                "easy" => TaskComplexity.Easy,
                _ => null,
            };
        }

        return null;
    }

    public static bool? Ui(IEnumerable<string> labels)
    {
        foreach (var label in labels)
        {
            var trimmed = label.Trim();
            if (!trimmed.StartsWith(UiPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = trimmed[UiPrefix.Length..].Trim().ToLowerInvariant();
            return value switch
            {
                "yes" => true,
                "no" => false,
                _ => null,
            };
        }

        return null;
    }

    public static string ComplexityLabel(TaskComplexity complexity) =>
        ComplexityPrefix + complexity.ToString().ToLowerInvariant();

    public static string UiLabel(bool ui) => UiPrefix + (ui ? "yes" : "no");

    public static string AppliedText(TaskComplexity complexity, bool ui) =>
        $"complexity={complexity.ToString().ToLowerInvariant()} ui={(ui ? "yes" : "no")}";

    /// <summary>
    /// Merge a diagnosis onto an existing label list. Topic tags stay; diagnosis labels go at
    /// the end. Non-forced keeps a family a human already set and only adds the missing one.
    /// Forced replaces diagnosis-prefixed labels and keeps everything else.
    /// </summary>
    public static CardDiagnosisMerge Merge(
        IReadOnlyList<string> existing,
        TaskComplexity complexity,
        bool ui,
        bool forced)
    {
        var currentComplexity = Complexity(existing);
        var currentUi = Ui(existing);

        if (!forced && currentComplexity is not null && currentUi is not null)
        {
            return new CardDiagnosisMerge(
                Wrote: false,
                AlreadyLabelled: true,
                Labels: existing,
                Applied: AppliedText(currentComplexity.Value, currentUi.Value));
        }

        List<string> next;
        TaskComplexity nextComplexity;
        bool nextUi;
        if (forced)
        {
            nextComplexity = complexity;
            nextUi = ui;
            next = existing.Where(l => !IsDiagnosisLabel(l)).ToList();
            next.Add(ComplexityLabel(nextComplexity));
            next.Add(UiLabel(nextUi));
        }
        else
        {
            next = existing.ToList();
            nextComplexity = currentComplexity ?? complexity;
            nextUi = currentUi ?? ui;
            if (currentComplexity is null)
                next.Add(ComplexityLabel(nextComplexity));
            if (currentUi is null)
                next.Add(UiLabel(nextUi));
        }

        return new CardDiagnosisMerge(
            Wrote: true,
            AlreadyLabelled: false,
            Labels: next,
            Applied: AppliedText(nextComplexity, nextUi));
    }
}

/// <summary>Result of <see cref="CardDiagnosisLabels.Merge"/>.</summary>
public readonly record struct CardDiagnosisMerge(
    bool Wrote,
    bool AlreadyLabelled,
    IReadOnlyList<string> Labels,
    string Applied);

/// <summary>Result of <see cref="CardService.ApplyDiagnosisAsync"/>.</summary>
public readonly record struct CardDiagnosisApplyResult(
    bool Wrote,
    bool AlreadyLabelled,
    string Applied,
    string LabelsJson);
