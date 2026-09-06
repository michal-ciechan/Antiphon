using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Card-file publication permission is independent of repository visibility.</summary>
public sealed class CardFilePolicyService
{
    public string? GetReason(bool enabled, bool boardArchived, bool projectArchived,
        bool syncCardFiles, RepositoryVisibility repositoryVisibility, CardFileVisibility cardVisibility)
    {
        if (!enabled) return "card_file_sync_disabled";
        if (boardArchived) return "board_archived";
        if (projectArchived) return "project_archived";
        if (!syncCardFiles) return "board_not_opted_in";
        if (repositoryVisibility is not (RepositoryVisibility.Private or RepositoryVisibility.Public))
            return "repository_visibility_unknown";
        if (cardVisibility is not (CardFileVisibility.Inherit or CardFileVisibility.Public)) return "card_private";
        return null;
    }
}
