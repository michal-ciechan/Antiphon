using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-1032. Mirror path plus the binding the runner returned, if it returned one.</summary>
public sealed record RemoteWorkspaceMirrorResult(
    string Path,
    CompletedCardWorkspaceBinding? Binding,
    MirrorBindingExpectation? Expectation,
    string? SlotId = null,
    int? SlotGeneration = null);

/// <summary>Facts committed before the mirror request. A returned binding must match them.</summary>
public sealed record MirrorBindingExpectation(
    Guid TaskId,
    int Attempt,
    Guid PreparationId,
    string RunnerId,
    string RunnerStoreId,
    string RepositoryIdentity,
    string SourceFullRef,
    string CreationBaselineSha);

/// <summary>CARD-0824 S1. <c>POST /api/session-runners/{runnerId}/workspace-slots/cold-reset</c>.</summary>
public sealed record WorkspaceSlotColdResetRouteRequest(IReadOnlyList<string>? Slots, bool All, string Reason, string? Requester = null);

/// <summary>
/// Review f4c293bc D-1. <c>POST /api/session-runners/{runnerId}/workspace-slots/{slotId}/record-release</c>: an operator
/// records the release of a <c>workspace_slot_release_stuck</c> slot for this task and lease generation. CARD-1649 A-2:
/// the optional requester (at most 64 UTF-16 code units, never a credential) goes into the audit event.
/// </summary>
public sealed record WorkspaceSlotRecordReleaseRouteRequest(Guid TaskId, int Generation, string Reason, string? Requester = null);
