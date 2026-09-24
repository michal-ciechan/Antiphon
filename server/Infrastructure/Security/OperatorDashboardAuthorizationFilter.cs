using Antiphon.Server.Application.Settings;
using Hangfire.Dashboard;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Security;

/// <summary>
/// CARD-0658: the Hangfire dashboard's only authorization filter. A request is admitted when it
/// carries a live dashboard session cookie (bootstrapped by <c>scripts/hangfire-dashboard.ps1</c>)
/// or the operator token header. The client address is not consulted in either direction: every
/// request Kestrel receives arrives as loopback (Aspire's DCP proxy, Vite, Caddy), and
/// <c>X-Forwarded-*</c> is neither trusted nor required. Nothing here logs a credential.
/// </summary>
public sealed class OperatorDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly OperatorDashboardSessions _sessions;
    private readonly IOptions<PhoneHomeRunnerSettings> _settings;

    public OperatorDashboardAuthorizationFilter(
        OperatorDashboardSessions sessions,
        IOptions<PhoneHomeRunnerSettings> settings)
    {
        _sessions = sessions;
        _settings = settings;
    }

    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (http.Request.Cookies.TryGetValue(OperatorDashboardSessions.CookieName, out var session)
            && _sessions.IsLive(session))
            return true;
        return OperatorCredential.HeaderMatches(http, _settings.Value);
    }
}
