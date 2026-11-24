using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Middleware;

/// <summary>
/// With sign-in on (<c>Review:Auth:Mode</c> GitHub), every <c>/api</c> and <c>/hubs</c> request needs a
/// session, apart from the sign-in routes and <c>/api/version</c>; anything else answers 401 with
/// <c>error: sign_in_required</c>, which the UI turns into a sign-in redirect. The UI's own files stay
/// public: they carry no data. <c>/health</c> and <c>/hangfire</c> (operator credential) are outside
/// <c>/api</c>. With sign-in off, nothing changes.
/// </summary>
public sealed class ReviewSignInGateMiddleware(RequestDelegate next, IOptionsMonitor<ReviewSettings> settings)
{
    public const string ErrorCode = "sign_in_required";

    private static readonly PathString[] OpenPaths = [ReviewAuthEndpoints.Prefix, "/api/version"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (settings.CurrentValue.Auth.IsGitHub && NeedsSignIn(context.Request.Path) && context.User.Identity is not { IsAuthenticated: true })
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = ErrorCode,
                title = "Sign in to Antiphon",
                signInUrl = ReviewAuthEndpoints.Prefix + "/login",
            });
            return;
        }

        await next(context);
    }

    public static bool NeedsSignIn(PathString path) =>
        (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs")) && !OpenPaths.Any(p => path.StartsWithSegments(p));
}
