namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0665 D-1/D-2: the allowlist that classifies each git-ignored path in a source worktree
/// before guarded removal. A protected name refuses removal wherever it sits; otherwise evidence is
/// copied to the retained report root first, disposable content is deleted with the tree, and any
/// path matching neither list is protected and refuses removal. Patterns are worktree-relative globs with <c>/</c> separators (<c>**</c>, <c>*</c>,
/// <c>?</c>). A null list uses the code default; an empty list matches nothing.
/// </summary>
public sealed class WorktreeCleanupSettings
{
    public static IReadOnlyList<string> DefaultDisposableIgnored { get; } =
    [
        "**/bin/**", "**/obj/**", "**/bin-*/**", "**/node_modules/**", "**/dist/**",
        "**/storybook-static/**", "**/.tmp/**", "**/out/**", ".antiphon-cache/**",
        ".antiphon/inbox/*.md", ".antiphon/task-*-brief.md", ".antiphon/task-*-refinement-*.md",
    ];

    public static IReadOnlyList<string> DefaultRetainedIgnored { get; } =
    [
        ".antiphon/task-????????.md", ".antiphon/**/*.trx", ".antiphon/*checkpoints*/**",
    ];

    /// <summary>
    /// Secret or user-local names that refuse removal wherever they sit, including inside a
    /// disposable directory such as <c>server/bin-x/appsettings.Development.json</c>. They win over
    /// both other lists. Configuration can only add to these; it cannot remove one.
    /// </summary>
    public static IReadOnlyList<string> DefaultProtectedIgnored { get; } =
    [
        "**/.claude/**", "**/appsettings.*.json", "**/*.user", "**/*.local.json",
        "**/.antiphon/report.md", "**/.antiphon/deliverables/**",
        "logs/**", "backups/**", ".superpowers/**", ".memsearch/**", "tests/Antiphon.E2E/TestOutput/**",
    ];

    /// <summary>Operator additions to <see cref="DefaultProtectedIgnored"/>.</summary>
    public string[]? ProtectedIgnored { get; set; }

    /// <summary>Deleted with the tree by guarded removal's no-follow delete.</summary>
    public string[]? DisposableIgnored { get; set; }

    /// <summary>Evidence: byte-copied to the retained report root before the tree is removed.</summary>
    public string[]? RetainedIgnored { get; set; }

    /// <summary>Total evidence bytes one removal may retain. Default 64 MiB.</summary>
    public long MaxRetainedEvidenceBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Evidence files one removal may retain. Default 500.</summary>
    public int MaxRetainedEvidenceFiles { get; set; } = 500;

    public IReadOnlyList<string> EffectiveDisposableIgnored => DisposableIgnored ?? DefaultDisposableIgnored;

    public IReadOnlyList<string> EffectiveRetainedIgnored => RetainedIgnored ?? DefaultRetainedIgnored;

    /// <summary>The defaults plus any operator additions; an empty or null list keeps the defaults.</summary>
    public IReadOnlyList<string> EffectiveProtectedIgnored =>
        [.. DefaultProtectedIgnored, .. (ProtectedIgnored ?? []).Where(p => !DefaultProtectedIgnored.Contains(p, StringComparer.Ordinal))];
}
