using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
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
    public const string ShutdownPath = "/api/operator/shutdown";

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

        app.MapPost(ShutdownPath, (
            HttpContext http,
            OperatorShutdownRequest? body,
            OperatorShutdownCoordinator coordinator,
            IOptions<PhoneHomeRunnerSettings> settings,
            IOptions<OperatorSettings> op) =>
        {
            OperatorCredential.Require(
                http, settings.Value,
                "Shutdown requires the operator token (scripts/restart-apphost.ps1 sends it).");
            if (body?.Reason is { Length: > 200 })
                throw new ValidationException("reason", "Shutdown reason must be at most 200 characters.");
            var reason = body?.Reason;
            var drainSeconds = op.Value.ShutdownDrainSeconds;
            http.Response.OnCompleted(() => coordinator.StopAsync(reason));
            return Results.Json(
                new OperatorShutdownDto(true, Environment.ProcessId, drainSeconds, 0),
                statusCode: StatusCodes.Status202Accepted);
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
                Secure = ArrivedOverHttps(http),
            });
            return Results.Redirect("/hangfire");
        }).WithTags("Operator");
    }

    /// <summary>
    /// CARD-0676: Caddy terminates TLS and reaches Kestrel over plain http from loopback, so an
    /// https <c>X-Forwarded-Proto</c> counts only from a loopback peer (the local proxy).
    /// </summary>
    private static bool ArrivedOverHttps(HttpContext http)
    {
        if (http.Request.IsHttps)
            return true;
        if (http.Connection.RemoteIpAddress is not { } peer || !System.Net.IPAddress.IsLoopback(peer))
            return false;
        var forwarded = http.Request.Headers["X-Forwarded-Proto"].ToString().Split(',')[0].Trim();
        return string.Equals(forwarded, "https", StringComparison.OrdinalIgnoreCase);
    }
}
