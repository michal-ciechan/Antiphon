using System.Security.Claims;
using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// Who is looking at the review UI and acting on it (api.md <c>GET /api/review-me</c>). With sign-in on
/// (<c>Review:Auth:Mode</c> GitHub), the signed-in GitHub login, reading runs by ID and deciding gates only on
/// <see cref="Repos"/>. With it off: <c>Review:DefaultViewerLogin</c>, else the current user, overridden
/// by the <c>X-Review-Viewer</c> header only while <c>Review:AllowViewerHeader</c> is on (development).
/// </summary>
/// <param name="Repos">The repos this viewer may read by ID and decide gates on (case-insensitive); null means every repo.</param>
public sealed record ReviewViewer(string Login, string? DisplayName, IReadOnlyList<string> Teams, IReadOnlySet<string>? Repos = null)
{
    public const string HeaderName = "X-Review-Viewer";

    /// <summary>The session cookie's authentication scheme.</summary>
    public const string AuthScheme = "ReviewSignIn";

    /// <summary>One claim per repo the viewer may decide on.</summary>
    public const string RepoClaim = "antiphon:review-repo";

    /// <summary>Present ("true") when the viewer may decide on every repo.</summary>
    public const string AllReposClaim = "antiphon:review-all-repos";

    public const string DisplayNameClaim = "antiphon:display-name";

    public static ReviewViewer Resolve(ReviewSettings settings, string? header, string fallbackLogin, ClaimsPrincipal? user = null)
    {
        if (settings.Auth.IsGitHub && user?.Identity is { IsAuthenticated: true, AuthenticationType: AuthScheme } identity
            && !string.IsNullOrWhiteSpace(identity.Name))
            return FromPrincipal(settings, user, identity.Name!);

        var login = settings.AllowViewerHeader && !settings.Auth.IsGitHub && !string.IsNullOrWhiteSpace(header)
            ? header.Trim()
            : !string.IsNullOrWhiteSpace(settings.DefaultViewerLogin) ? settings.DefaultViewerLogin.Trim() : fallbackLogin;
        settings.Viewers.TryGetValue(login, out var known);
        return new ReviewViewer(login, known?.DisplayName, known?.Teams ?? []);
    }

    private static ReviewViewer FromPrincipal(ReviewSettings settings, ClaimsPrincipal user, string login)
    {
        settings.Viewers.TryGetValue(login, out var known);
        var displayName = known?.DisplayName ?? user.FindFirst(DisplayNameClaim)?.Value;
        IReadOnlySet<string>? repos = user.HasClaim(AllReposClaim, "true")
            ? null
            : user.FindAll(RepoClaim).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ReviewViewer(login, string.IsNullOrWhiteSpace(displayName) ? null : displayName, known?.Teams ?? [], repos);
    }

    /// <summary>True when this viewer may decide gates on <paramref name="repo"/>.</summary>
    public bool MayDecideOn(string repo) => Repos is null || Repos.Contains(repo);

    public ReviewViewerDto ToDto() => new(Login, DisplayName, Teams);
}
