namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// What deleting a project would destroy or detach. The delete dialog reads this before it lets
/// anyone confirm, so "this action cannot be undone" comes with a list of what is being undone.
/// </summary>
/// <param name="OpenCardCount">Cards that are not Done or Canceled — the "outstanding items" warning.</param>
/// <param name="DetachedAgentCount">Agents unpinned from the board/card. They are never deleted.</param>
/// <param name="Blockers">Reasons the delete cannot proceed at all, even forced. Empty means it can.</param>
public record ProjectDeletionImpactDto(
    Guid ProjectId,
    string ProjectName,
    int BoardCount,
    int CardCount,
    int OpenCardCount,
    int RunningSessionCount,
    int DetachedAgentCount,
    int WorkflowCount,
    IReadOnlyList<string> Blockers)
{
    /// <summary>True when the project owns something, so the delete needs an explicit force.</summary>
    public bool RequiresConfirmation => BoardCount > 0 || CardCount > 0 || DetachedAgentCount > 0;

    /// <summary>False when something makes the delete impossible regardless of force.</summary>
    public bool CanDelete => Blockers.Count == 0;
}

/// <summary>
/// Outcome of deleting a board. <paramref name="ProjectDeleted"/> reports the reverse cascade:
/// the last board leaving an otherwise-empty project takes the project with it.
/// </summary>
public record DeleteBoardResultDto(Guid BoardId, Guid ProjectId, bool ProjectDeleted);
