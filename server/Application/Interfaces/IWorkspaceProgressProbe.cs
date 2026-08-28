using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>External workspace activity used by the read-only task-progress projection.</summary>
public interface IWorkspaceProgressProbe
{
    Task<WorkspaceProgressArm> ProbeProgressAsync(
        string? workingDirectory, DateTime since, bool sharedCheckout, CancellationToken ct);
}
