namespace Antiphon.Server.Domain;

/// <summary>Full Git object IDs only. Abbreviations, revisions and branch names are never accepted.</summary>
public static class GitObjectId
{
    public static bool IsFull(string? value) =>
        value is { Length: 40 or 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (var c in value)
            if (c is '\r' or '\n' or '\t') return false;
        var trimmed = value.Trim().ToLowerInvariant();
        if (!IsFull(trimmed)) return false;
        normalized = trimmed;
        return true;
    }
}
