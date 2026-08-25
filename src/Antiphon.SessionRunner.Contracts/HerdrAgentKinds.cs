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
}
