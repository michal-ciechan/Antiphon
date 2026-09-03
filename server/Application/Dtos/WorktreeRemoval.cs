namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Result of a best-effort worktree cleanup (CARD-0328). <see cref="IsClean"/> is true only when
/// nothing remains — residue names what is left and why.
/// </summary>
public sealed record WorktreeRemoval(
    bool Unregistered,
    bool DirectoryGone,
    bool BranchDeleted,
    string? Residue)
{
    public bool IsClean => Residue is null;

    public static WorktreeRemoval Clean { get; } = new(true, true, true, null);
}
