using Antiphon.Server.Application.Interfaces;
using Serilog.Context;

namespace Antiphon.Server.Api.Middleware;

/// <summary>
/// Resolves ICurrentUser to the hardcoded default admin user and extracts client IP
/// from HttpContext.Connection.RemoteIpAddress. Logs the IP on every request (NFR11).
/// </summary>
public class CurrentUserMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CurrentUserMiddleware> _logger;

    /// <summary>
    /// Well-known ID for the seeded default admin user.
    /// Must match the seed data in AppDbContext.
    /// </summary>
    public static readonly Guid DefaultAdminId = new("a0000000-0000-0000-0000-000000000001");
    public const string DefaultAdminUserName = "admin";

    public CurrentUserMiddleware(RequestDelegate next, ILogger<CurrentUserMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation("Request from {ClientIp}: {Method} {Path}",
            ipAddress, context.Request.Method, context.Request.Path);

        var currentUser = new DefaultCurrentUser(DefaultAdminId, DefaultAdminUserName, ipAddress);

        // Register the resolved user into the DI scope so services can inject ICurrentUser
        context.Items["CurrentUser"] = currentUser;

        using (LogContext.PushProperty("UserId", currentUser.UserId))
        using (LogContext.PushProperty("UserName", currentUser.UserName))
        using (LogContext.PushProperty("ClientIp", ipAddress))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Default implementation of ICurrentUser for MVP (hardcoded admin).
    /// </summary>
    private sealed class DefaultCurrentUser : ICurrentUser
    {
        public Guid UserId { get; }
        public string UserName { get; }
        public string IpAddress { get; }

        public DefaultCurrentUser(Guid userId, string userName, string ipAddress)
        {
            UserId = userId;
            UserName = userName;
            IpAddress = ipAddress;
        }
    }
}
