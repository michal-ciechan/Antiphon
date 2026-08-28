namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Read-only workspace activity supplied to the task-progress policy. It can only withhold a
/// progress-stalled verdict; unavailable workspace information leaves the transcript arm intact.
/// </summary>
public sealed record WorkspaceProgressArm(
    bool Available,
    DateTime? LastFileChangeAt,
    DateTime? LastCommitAt,
    bool SharedCheckout);
