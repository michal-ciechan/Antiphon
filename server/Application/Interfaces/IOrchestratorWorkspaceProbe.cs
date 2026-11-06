using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Application.Interfaces;

public interface IOrchestratorWorkspaceProbe
{
    Task<OrchestratorWorkspaceDirectoryFacts> GatherAsync(
        string directory, OrchestratorWorkspaceCli cli, CancellationToken cancellationToken = default);

    OrchestratorWorkspaceHomeState ReadHomeFromProfile(string directory);
}
