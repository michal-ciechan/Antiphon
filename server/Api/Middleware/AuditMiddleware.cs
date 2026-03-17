using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Middleware;

/// <summary>
/// Intercepts API calls to record audit events. Captures client IP (FR50)
/// and stores request context for downstream audit recording.
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Store client IP in HttpContext.Items for downstream services to use (FR50)
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        context.Items["ClientIp"] = clientIp;

        // Store request timestamp for duration tracking
        context.Items["RequestStartedAt"] = DateTime.UtcNow;

        await _next(context);
    }
}
