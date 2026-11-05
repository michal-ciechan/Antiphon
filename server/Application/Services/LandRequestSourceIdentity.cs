using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Server.Application.Services;

internal static class LandRequestSourceIdentity
{
    public static bool Matches(AgentTaskLandRequest request, LandSourceCoordinates coordinates) =>
        request.SourceDesktopWorkspaceKindSnapshot == coordinates.DesktopWorkspaceKind
        && request.SourceTaskRefWorkspaceBindingIdSnapshot == coordinates.TaskRefWorkspaceBindingId
        && (coordinates.DesktopWorkspaceKind != DesktopWorkspaceKind.RefOnly
            || request.SourceFullRefSnapshot is not null && request.RepositoryPathSnapshot is not null
                && request.WorktreePathSnapshot is not null && request.TargetFullRefSnapshot is not null)
        && (request.SourceFullRefSnapshot is null || request.SourceFullRefSnapshot == coordinates.SourceFullRef)
        && (request.TargetFullRefSnapshot is null || request.TargetFullRefSnapshot == coordinates.TargetFullRef)
        && (request.RepositoryPathSnapshot is null || LandingGit.PathsEqual(request.RepositoryPathSnapshot, coordinates.RepositoryPath))
        && (request.WorktreePathSnapshot is null || LandingGit.PathsEqual(request.WorktreePathSnapshot, coordinates.WorktreePath));
}
