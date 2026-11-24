using System.Security.Claims;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Review;

/// <summary>A GitHub user who completed the sign-in redirect.</summary>
public sealed record GitHubSignInUser(long Id, string Login, string? Name);

/// <summary>GitHub's user authorization (the OAuth web flow), for sign-in (<c>Review:Auth</c>).</summary>
public interface IGitHubSignIn
{
    /// <summary>Where the browser goes to sign in: GitHub's authorize page for this client, with the state and the callback.</summary>
    Uri AuthorizeUrl(string state, string redirectUri);

    /// <summary>Trades the callback's code for a user access token. Throws <see cref="GitHubSignInException"/>.</summary>
    Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct);

    /// <summary>The signed-in user (<c>GET /user</c>). Throws <see cref="GitHubSignInException"/>.</summary>
    Task<GitHubSignInUser> GetUserAsync(string accessToken, CancellationToken ct);
}

/// <summary>A GitHub sign-in step that failed; the message is safe to show (it never carries a token or the secret).</summary>
public sealed class GitHubSignInException(string message) : Exception(message);

/// <summary>A person's access to one reviewed repo, as the code host reports it.</summary>
public interface IReviewRepoAccess
{
    /// <summary><c>admin</c>, <c>write</c>, <c>read</c> or <c>none</c>. Throws <see cref="CodeHostException"/> when the host cannot say.</summary>
    Task<string> GetPermissionAsync(string repo, string login, CancellationToken ct);
}

/// <summary>Whether a person may sign in, and the repos they may decide gates on.</summary>
/// <param name="AllRepos">True for an allowed login, or when repo access is not checked.</param>
/// <param name="Refusal">Why the sign-in is refused, for the person; null when allowed.</param>
public sealed record ReviewSignInDecision(bool Allowed, bool AllRepos, IReadOnlyList<string> Repos, string? Refusal);

/// <summary>
/// Who may sign in (<c>Review:Auth</c>): a login in <c>AllowedLogins</c> decides on every repo; anyone else
/// needs <c>RequiredRepoPermission</c> on at least one reviewed repo, and decides on those repos only. The
/// reviewed repos are the configured ones (polling, reviewers, code host rows) and every GitHub repo with
/// a review run. Repo access is read here, once, and carried in the session.
/// </summary>
public sealed class ReviewSignInService(
    IOptions<ReviewSettings> settings,
    IOptions<ReviewPollingSettings> polling,
    IOptions<ReviewAgentSettings> agents,
    IOptions<ReviewCodeHostSettings> hosts,
    AppDbContext db,
    IReviewRepoAccess access,
    ILogger<ReviewSignInService> logger)
{
    /// <summary>At most this many repos are checked for one sign-in.</summary>
    public const int MaxReposChecked = 100;

    public async Task<IReadOnlyList<string>> ReviewedReposAsync(CancellationToken ct)
    {
        var configured = polling.Value.Repos.Select(r => r.Repo)
            .Concat(agents.Value.Repos.Select(r => r.Repo))
            .Concat(hosts.Value.Repos.Where(r => string.IsNullOrWhiteSpace(r.LocalPath)).Select(r => r.Repo));
        var reviewed = await db.ReviewSubjects.AsNoTracking().Where(s => s.Host == ReviewHost.GitHub).Select(s => s.Repo).Distinct().ToListAsync(ct);
        return configured.Concat(reviewed)
            .Select(r => r?.Trim() ?? "")
            .Where(r => r.Split('/') is [{ Length: > 0 }, { Length: > 0 }])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ReviewSignInDecision> DecideAsync(GitHubSignInUser user, CancellationToken ct)
    {
        var auth = settings.Value.Auth;
        if (auth.IsAllowedLogin(user.Login) || !auth.ChecksRepoAccess)
            return new ReviewSignInDecision(true, true, [], null);

        var required = ReviewAuthSettings.Rank(auth.RequiredRepoPermission);
        var repos = await ReviewedReposAsync(ct);
        if (repos.Count == 0)
            return new ReviewSignInDecision(false, false, [],
                $"@{user.Login} is not in Review:Auth:AllowedLogins, and this Antiphon reviews no repos yet to check your access against.");

        var granted = new List<string>();
        var failures = 0;
        foreach (var repo in repos.Take(MaxReposChecked))
        {
            try
            {
                if (ReviewAuthSettings.Rank(await access.GetPermissionAsync(repo, user.Login, ct)) >= required)
                    granted.Add(repo);
            }
            catch (CodeHostException ex)
            {
                failures++;
                logger.LogWarning("Sign-in: {Repo} did not report @{Login}'s access: {Reason}", repo, user.Login, ex.Message);
            }
        }

        logger.LogInformation("Sign-in: @{Login} has {Permission} access to {Granted} of {Checked} reviewed repo(s)", user.Login,
            auth.RequiredRepoPermission, granted.Count, Math.Min(repos.Count, MaxReposChecked));
        if (granted.Count > 0)
            return new ReviewSignInDecision(true, false, granted, null);
        var permission = auth.RequiredRepoPermission.Trim().ToLowerInvariant();
        var named = string.Join(", ", repos.Take(5)) + (repos.Count > 5 ? $" and {repos.Count - 5} more" : "");
        return new ReviewSignInDecision(false, false, [],
            failures > 0 && failures == Math.Min(repos.Count, MaxReposChecked)
                ? $"GitHub did not say what access @{user.Login} has to the reviewed repos ({named}), so the sign-in is refused. Try again later."
                : $"@{user.Login} needs {permission} access to one of the repos this Antiphon reviews ({named}).");
    }

    /// <summary>The session principal: the login as the name, the display name, and the repos the person may decide on.</summary>
    public static ClaimsPrincipal Principal(GitHubSignInUser user, ReviewSignInDecision decision)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Login),
        };
        if (!string.IsNullOrWhiteSpace(user.Name))
            claims.Add(new Claim(ReviewViewer.DisplayNameClaim, user.Name.Trim()));
        if (decision.AllRepos)
            claims.Add(new Claim(ReviewViewer.AllReposClaim, "true"));
        claims.AddRange(decision.Repos.Select(r => new Claim(ReviewViewer.RepoClaim, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, ReviewViewer.AuthScheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}
