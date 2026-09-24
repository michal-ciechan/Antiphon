namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Result of a best-effort worktree cleanup (CARD-0328). <see cref="IsClean"/> is true only when
/// nothing remains — residue names what is left and why.
/// </summary>
public sealed record WorktreeRemoval(
    bool Unregistered,
    bool DirectoryGone,
    bool BranchDeleted,
    string? Residue,
    WorktreeCleanupReference? Diagnostics = null)
{
    public bool IsClean => Residue is null;

    /// <summary>
    /// CARD-0665 D-8: operator-facing detail beside the bare <see cref="Residue"/> code, such as
    /// the protected ignored paths that refused removal or the retained evidence count.
    /// </summary>
    public string? Detail { get; init; }

    public static WorktreeRemoval Clean { get; } = new(true, true, true, null);
}
