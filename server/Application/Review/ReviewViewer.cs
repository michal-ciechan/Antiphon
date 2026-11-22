using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// Who is looking at the review UI and acting on it, until identity lands (api.md
/// <c>GET /api/review-me</c>): <c>Review:DefaultViewerLogin</c>, else the current user, overridden by
/// the <c>X-Review-Viewer</c> header only while <c>Review:AllowViewerHeader</c> is on (development).
/// </summary>
public sealed record ReviewViewer(string Login, string? DisplayName, IReadOnlyList<string> Teams)
{
    public const string HeaderName = "X-Review-Viewer";

    public static ReviewViewer Resolve(ReviewSettings settings, string? header, string fallbackLogin)
    {
        var login = settings.AllowViewerHeader && !string.IsNullOrWhiteSpace(header)
            ? header.Trim()
            : !string.IsNullOrWhiteSpace(settings.DefaultViewerLogin) ? settings.DefaultViewerLogin.Trim() : fallbackLogin;
        settings.Viewers.TryGetValue(login, out var known);
        return new ReviewViewer(login, known?.DisplayName, known?.Teams ?? []);
    }

    public ReviewViewerDto ToDto() => new(Login, DisplayName, Teams);
}
