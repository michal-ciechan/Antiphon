using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner.Contracts;

public sealed record ParsedDelegateBranchName(string ShortId, string? CardNumber, string? Stage, string Branch);

public static class DelegateBranchName
{
    private static readonly Regex Pattern = new(
        @"^(?:refs/heads/)?feat/card-(?:task|(?<card>[0-9]{1,8})-(?<stage>[a-z][a-z0-9]{0,15}))-(?<id>[0-9a-fA-F]{8})$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CardPattern = new(@"\ACARD-([0-9]{1,8})\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex StagePattern = new(@"\A[a-z][a-z0-9]{0,15}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string Legacy(Guid taskId) => "feat/card-task-" + taskId.ToString("N")[..8];

    public static string Build(Guid taskId, string? cardIdentifier, string? stage)
    {
        if (cardIdentifier is null || cardIdentifier.Length > 13 || stage is null || stage.Length > 16)
            return Legacy(taskId);
        var card = CardPattern.Match(cardIdentifier);
        if (!card.Success || !StagePattern.IsMatch(stage)) return Legacy(taskId);
        return "feat/card-" + card.Groups[1].Value + "-" + stage + "-" + taskId.ToString("N")[..8];
    }

    public static bool TryParse(string? value, out ParsedDelegateBranchName parsed)
    {
        parsed = null!;
        if (value is null || value.Length > 200) return false;
        var match = Pattern.Match(value);
        // In .NET '$' alone also matches before a final LF. Consume the entire input.
        if (!match.Success || match.Length != value.Length) return false;
        var card = match.Groups["card"];
        parsed = new(match.Groups["id"].Value.ToLowerInvariant(), card.Success ? card.Value : null,
            card.Success ? match.Groups["stage"].Value : null,
            value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value[11..] : value);
        return true;
    }

    public static bool IsOwned(string? branch, Guid taskId) =>
        TryParse(branch, out var parsed) && parsed.ShortId == taskId.ToString("N")[..8];
}
