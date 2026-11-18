using System.Text.RegularExpressions;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// Label for one normalised <c>pg_stat_statements</c> query. Redaction runs before a tag
/// or the text is kept: utility statements are not normalised, so a password or a
/// connection string would otherwise leave the process in the label.
/// </summary>
public static partial class PgStatLabel
{
    public const int MaxLength = 120;
    public const string Redacted = "redacted-utility";

    private static readonly Regex Redaction = RedactionPattern();
    private static readonly Regex LeadingTag = LeadingTagPattern();
    private static readonly Regex Transaction = TransactionPattern();

    public static string FromQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) return "untagged";
        if (Redaction.IsMatch(query)) return Redacted;
        var tag = LeadingTag.Match(query);
        if (tag.Success) return tag.Groups[1].Value;
        var flat = Whitespace().Replace(query, " ").Trim();
        var tx = Transaction.Match(flat);
        if (tx.Success) return "tx." + tx.Groups[1].Value.ToLowerInvariant();
        if (flat.Length > MaxLength) return flat[..MaxLength];
        return flat.Length == 0 ? "untagged" : flat;
    }

    public static string Operation(string label) =>
        label == Redacted || IsTagToken(label) ? label : "untagged";

    public static bool IsTagToken(string label) =>
        label.Length is >= 1 and <= 64 && label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-');

    [GeneratedRegex(@"\b(password|alter\s+(role|user)|create\s+(role|user)|connection|secret|token)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RedactionPattern();

    [GeneratedRegex(@"^-- ([a-z0-9.\-]{1,64})(\r?\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTagPattern();

    [GeneratedRegex(@"^(begin|commit|rollback|release|savepoint)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TransactionPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
