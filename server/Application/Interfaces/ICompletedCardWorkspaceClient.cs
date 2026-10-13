using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

public interface ICompletedCardWorkspaceClient
{
    Task<CompletedCardWorkspaceInspectionResponse> InspectCompletedCardWorkspaceAsync(
        CompletedCardWorkspaceInspectRequest request, CancellationToken ct);
    Task<CompletedCardWorkspaceRemovalResponse> RemoveCompletedCardWorkspaceAsync(
        CompletedCardWorkspaceCommand command, CancellationToken ct);
}
