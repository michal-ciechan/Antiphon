namespace Antiphon.SessionRunner.Contracts;

/// <summary>Shared producer/recipient task-report recognition for relative inventory paths.</summary>
public static class CompletedCardWorkspaceReport
{
    public static bool IsTaskReportPath(string raw, bool isDirectory = false)
    {
        if (isDirectory) return false;
        var path = raw.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        // The previous runner regex's $ anchor also admitted one terminal LF.
        // Keep that evidence protected by the same producer/recipient predicate.
        if (path.EndsWith('\n')) path = path[..^1];
        const string prefix = ".antiphon/task-";
        const string suffix = ".md";
        // Preserve the runner's conservative eight-character glob, including nonhex
        // identifiers. Do not trim dot/space or reinterpret ADS/Unicode spellings.
        return path.Length == prefix.Length + 8 + suffix.Length
            && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && !path.AsSpan(prefix.Length, 8).Contains('/');
    }
}
