using System.Net;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

/// <summary><c>GET /api/review-auth/status</c>: whether sign-in is on, and who is acting.</summary>
public sealed record ReviewAuthStatusDto(string Mode, bool SignedIn, string? Login, string? DisplayName);

/// <summary>
/// Sign-in with GitHub (<c>Review:Auth</c>, getting-started section 7.1). <c>login</c> sends the browser to
/// GitHub with a one-time state held in a short-lived protected cookie; <c>callback</c> checks the state,
/// trades the code for the user's login, asks <see cref="ReviewSignInService"/> whether they may sign in,
/// and sets the session cookie. <c>logout</c> clears it. These routes stay open while every other
/// <c>/api</c> route needs a session (<see cref="Middleware.ReviewSignInGateMiddleware"/>).
/// </summary>
public static class ReviewAuthEndpoints
{
    public const string Prefix = "/api/review-auth";
    public const string CallbackPath = Prefix + "/callback";
    public const string StateCookie = "antiphon.review.signin";
    private const string StatePurpose = "Antiphon.Review.SignIn.State";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static void MapReviewAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup(Prefix).WithTags("Review sign-in");

        auth.MapGet("/status", (HttpContext http, IOptions<ReviewSettings> settings, ReviewViewer viewer) =>
        {
            var gitHub = settings.Value.Auth.IsGitHub;
            var signedIn = gitHub && http.User.Identity is { IsAuthenticated: true, AuthenticationType: ReviewViewer.AuthScheme };
            return Results.Ok(new ReviewAuthStatusDto(gitHub ? ReviewAuthModes.GitHub : ReviewAuthModes.None, signedIn,
                !gitHub || signedIn ? viewer.Login : null, !gitHub || signedIn ? viewer.DisplayName : null));
        });

        auth.MapGet("/login", (string? returnUrl, HttpContext http, IOptions<ReviewSettings> settings, IGitHubSignIn gitHub,
            IDataProtectionProvider protection) =>
        {
            if (!settings.Value.Auth.IsGitHub)
                return Page(HttpStatusCode.NotFound, "Sign-in is off", "This Antiphon has no sign-in (Review:Auth:Mode is None).", null);
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var target = LocalReturnUrl(returnUrl);
            var sealedState = protection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector()
                .Protect($"{state}\n{target}", StateLifetime);
            http.Response.Cookies.Append(StateCookie, sealedState, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps || IsHttps(settings.Value.PublicBaseUrl),
                Path = Prefix,
                MaxAge = StateLifetime,
            });
            return Results.Redirect(gitHub.AuthorizeUrl(state, CallbackUrl(http, settings.Value)).ToString());
        });

        auth.MapGet("/callback", async (string? code, string? state, string? error, HttpContext http, IOptions<ReviewSettings> settings,
            IGitHubSignIn gitHub, ReviewSignInService signIn, IDataProtectionProvider protection, ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger(typeof(ReviewAuthEndpoints).FullName!);
            if (!settings.Value.Auth.IsGitHub)
                return Page(HttpStatusCode.NotFound, "Sign-in is off", "This Antiphon has no sign-in (Review:Auth:Mode is None).", null);
            var sealedState = http.Request.Cookies[StateCookie];
            http.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = Prefix });
            if (!string.IsNullOrWhiteSpace(error))
                return Page(HttpStatusCode.BadRequest, "Sign-in cancelled", $"GitHub did not sign you in ({error}).", "Try again");

            string? returnUrl = null;
            try
            {
                var opened = string.IsNullOrEmpty(sealedState)
                    ? null
                    : protection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector().Unprotect(sealedState);
                var parts = opened?.Split('\n', 2);
                if (parts is [var expected, var target] && state is not null
                    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(state)))
                    returnUrl = LocalReturnUrl(target);
            }
            catch (CryptographicException)
            {
            }

            if (returnUrl is null || string.IsNullOrWhiteSpace(code))
                return Page(HttpStatusCode.BadRequest, "Sign-in expired",
                    "This sign-in link has expired or was started in another browser.", "Start again");

            GitHubSignInUser user;
            try
            {
                var token = await gitHub.ExchangeCodeAsync(code, CallbackUrl(http, settings.Value), ct);
                user = await gitHub.GetUserAsync(token, ct);
            }
            catch (GitHubSignInException ex)
            {
                logger.LogWarning("Sign-in failed at GitHub: {Reason}", ex.Message);
                return Page(HttpStatusCode.BadGateway, "Sign-in failed", ex.Message, "Try again");
            }

            var decision = await signIn.DecideAsync(user, ct);
            if (!decision.Allowed)
            {
                logger.LogWarning("Sign-in refused for @{Login}: {Reason}", user.Login, decision.Refusal);
                return Page(HttpStatusCode.Forbidden, "No access", decision.Refusal ?? $"@{user.Login} may not use this Antiphon.", null);
            }

            await http.SignInAsync(ReviewViewer.AuthScheme, ReviewSignInService.Principal(user, decision),
                new AuthenticationProperties { IsPersistent = true });
            logger.LogInformation("Signed in @{Login} ({Scope})", user.Login,
                decision.AllRepos ? "every repo" : $"{decision.Repos.Count} repo(s): {string.Join(", ", decision.Repos)}");
            return Results.Redirect(returnUrl);
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(ReviewViewer.AuthScheme);
            return Page(HttpStatusCode.OK, "Signed out",
                "You are signed out of Antiphon. While you are signed in to GitHub, signing in again takes one click.", "Sign in again");
        });
    }

    /// <summary>The callback GitHub returns to: under <c>Review:PublicBaseUrl</c> when set (behind a proxy), else this request's origin.</summary>
    public static string CallbackUrl(HttpContext http, ReviewSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.PublicBaseUrl)
            ? settings.PublicBaseUrl.TrimEnd('/') + CallbackPath
            : $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}{CallbackPath}";

    /// <summary>A path on this site only (never <c>//host</c> or <c>/\host</c>); anything else is the home page.</summary>
    public static string LocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/";

    private static bool IsHttps(string? url) => url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A small HTML page for the browser-facing routes, with a link to start sign-in when <paramref name="action"/> is set.</summary>
    private static IResult Page(HttpStatusCode status, string title, string message, string? action)
    {
        var link = action is null ? "" : $"<p><a href=\"{Prefix}/login\">{WebUtility.HtmlEncode(action)}</a></p>";
        var html = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Antiphon: {{WebUtility.HtmlEncode(title)}}</title>
            <style>body{font-family:system-ui,sans-serif;max-width:36rem;margin:4rem auto;padding:0 1rem;line-height:1.5;color:#222;background:#fff}
            @media (prefers-color-scheme: dark){body{color:#ddd;background:#1a1b1e}a{color:#74c0fc} }</style></head>
            <body><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p>{{link}}</body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, (int)status);
    }
}
