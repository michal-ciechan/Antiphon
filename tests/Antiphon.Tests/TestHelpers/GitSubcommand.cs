using Antiphon.Server.Application.Dtos;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1714: the git subcommand after leading global options. <c>LandingGit.InspectAsync</c> runs its status as
/// <c>git -c core.longpaths=true status ...</c>, so a double that matches <c>args[0] == "status"</c> never sees it.
/// </summary>
internal static class GitSubcommand
{
    public static string? Of(IReadOnlyList<string> arguments)
    {
        var index = LandingGitProfile.SubcommandIndex(arguments);
        return index < arguments.Count ? arguments[index] : null;
    }
}
