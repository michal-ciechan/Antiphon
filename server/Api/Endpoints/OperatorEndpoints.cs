using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

/// <summary>
/// CARD-0658: browser access to the Hangfire dashboard. A header-authenticated caller
/// (<c>scripts/hangfire-dashboard.ps1</c>) gets a one-time login link; redeeming it sets a
/// short-lived, server-side dashboard session cookie scoped to <c>/hangfire</c>. These routes sit
/// outside the dashboard branch, so the login is never subject to the filter it bootstraps.
/// </summary>
public static class OperatorEndpoints
{
    public const string SessionsPath = "/api/operator/dashboard-sessions";
    public const string LoginPath = "/api/operator/dashboard-login";

    public static void MapOperatorEndpoints(this WebApplication app)
    {
        app.MapPost(SessionsPath, (
            HttpContext http,
            OperatorDashboardSessions sessions,
            TimeProvider clock,
            IOptions<PhoneHomeRunnerSettings> settings) =>
        {
            OperatorCredential.Require(
                http, settings.Value,
                "The dashboard login requires the operator token (scripts/hangfire-dashboard.ps1 sends it).");
            var expiresAt = clock.GetUtcNow() + OperatorDashboardSessions.NonceLifetime;
            var nonce = sessions.IssueNonce();
            return Results.Ok(new OperatorDashboardLoginDto($"{LoginPath}?nonce={nonce}", expiresAt));
        }).WithTags("Operator");

        app.MapGet(LoginPath, (HttpContext http, OperatorDashboardSessions sessions) =>
        {
            var session = sessions.Redeem(http.Request.Query["nonce"].ToString());
            if (session is null)
                throw new ForbiddenException(
                    "The dashboard login link is invalid or has expired; run scripts/hangfire-dashboard.ps1 again.",
                    "operator_login_invalid");
            http.Response.Cookies.Append(OperatorDashboardSessions.CookieName, session, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Path = "/hangfire",
                MaxAge = OperatorDashboardSessions.SessionLifetime,
                Secure = http.Request.IsHttps,
            });
            return Results.Redirect("/hangfire");
        }).WithTags("Operator");
    }
}
