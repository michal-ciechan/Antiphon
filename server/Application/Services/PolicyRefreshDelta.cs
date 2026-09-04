using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Stamp-line delta for a policy-refresh note (CARD-0334 S2). Keys and versions only —
/// never bundle text. A test scans the note against every <see cref="InstructionBundles"/> body.
/// </summary>
public static class PolicyRefreshDelta
{
    /// <summary>
    /// <c>orchestrator v26dea68f → v3c1f0a9e; board-api added v51981dbe; AGENTS.md changed</c>.
    /// Empty when nothing actually moved (the sweep should not have fired).
    /// </summary>
    public static string Format(
        string? launchedBundles,
        string currentBundles,
        string? launchedFiles,
        string currentFiles)
    {
        var parts = new List<string>();
        parts.AddRange(DescribeBundles(
            PolicyDrift.ParseStampLine(launchedBundles ?? ""),
            PolicyDrift.ParseStampLine(currentBundles ?? "")));

        var files = PolicyDrift.DiffKeys(launchedFiles, currentFiles ?? "");
        if (files.Count == 1)
            parts.Add($"{files[0]} changed");
        else if (files.Count > 1)
            parts.Add(string.Join(", ", files) + " changed");

        return string.Join("; ", parts);
    }

    private static IEnumerable<string> DescribeBundles(
        Dictionary<string, string> launched,
        Dictionary<string, string> current)
    {
        foreach (var key in current.Keys)
        {
            if (launched.TryGetValue(key, out var version))
            {
                if (!string.Equals(version, current[key], StringComparison.Ordinal))
                    yield return $"{key} v{version} → v{current[key]}";
            }
            else
            {
                yield return $"{key} added v{current[key]}";
            }
        }

        foreach (var key in launched.Keys)
        {
            if (!current.ContainsKey(key))
                yield return $"{key} removed";
        }
    }
}
