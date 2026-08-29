namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Herdr agent-manifest kind strings (CARD-0160 P4, CARD-0187 K1/K5). The wire, the runner
/// launch poll, and the server Kind map all share this list — no second table.
/// </summary>
public static class HerdrAgentKinds
{
    /// <summary>Claude Code (CARD-0160 P4).</summary>
    public const string Claude = "claude";

    /// <summary>Grok Build (CARD-0187 K1).</summary>
    public const string Grok = "grok";

    /// <summary>Codex (CARD-0187 K5 — launched via <c>codex.cmd</c>, never <c>agent.start</c>).</summary>
    public const string Codex = "codex";

    public static IReadOnlyList<string> Supported { get; } = [Claude, Grok, Codex];

    /// <summary>
    /// <see langword="null"/> is supported: it keeps the pre-CARD-0187 wire meaning of Claude
    /// so a new runner in front of an old server behaves exactly as today.
    /// </summary>
    public static bool IsSupported(string? kind) =>
        kind is null || Supported.Contains(kind, StringComparer.Ordinal);

    /// <summary>
    /// CARD-0213: executable names that may occupy a pane of this kind (process_info <c>name</c>).
    /// A <c>pwsh</c> wrapper is deliberately absent (CARD-0187 K6). Codex includes <c>cmd</c>
    /// because the interactive launcher is <c>codex.cmd</c>.
    /// </summary>
    public static IReadOnlyList<string> ExecutableFamily(string? kind)
    {
        var resolved = string.IsNullOrEmpty(kind) ? Claude : kind;
        return resolved switch
        {
            Grok => ["grok", "grok.exe"],
            Codex => ["codex", "codex.exe", "cmd", "cmd.exe"],
            _ => ["claude", "claude.exe", "node", "node.exe"],
        };
    }

    /// <summary>True when <paramref name="processName"/> belongs to <see cref="ExecutableFamily"/>.</summary>
    public static bool IsFamilyMember(string? kind, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;
        foreach (var candidate in ExecutableFamily(kind))
        {
            if (string.Equals(candidate, processName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
