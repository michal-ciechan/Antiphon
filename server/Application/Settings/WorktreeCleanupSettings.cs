namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0665 D-1/D-2: the allowlist that classifies each git-ignored path in a source worktree
/// before guarded removal. Evidence is copied to the retained report root first, disposable
/// content is deleted with the tree, and any path matching neither list is protected and refuses
/// removal. Patterns are worktree-relative globs with <c>/</c> separators (<c>**</c>, <c>*</c>,
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

    /// <summary>Deleted with the tree by non-forcing <c>git worktree remove</c>.</summary>
    public string[]? DisposableIgnored { get; set; }

    /// <summary>Evidence: byte-copied to the retained report root before the tree is removed.</summary>
    public string[]? RetainedIgnored { get; set; }

    /// <summary>Total evidence bytes one removal may retain. Default 64 MiB.</summary>
    public long MaxRetainedEvidenceBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Evidence files one removal may retain. Default 500.</summary>
    public int MaxRetainedEvidenceFiles { get; set; } = 500;

    public IReadOnlyList<string> EffectiveDisposableIgnored => DisposableIgnored ?? DefaultDisposableIgnored;

    public IReadOnlyList<string> EffectiveRetainedIgnored => RetainedIgnored ?? DefaultRetainedIgnored;
}
