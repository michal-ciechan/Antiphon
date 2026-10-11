using Microsoft.AspNetCore.Routing;

namespace Antiphon.Server.Api;

/// <summary>
/// CARD-1123. Query and route bind failures throw in every hosting environment so
/// <see cref="Middleware.ExceptionMiddleware"/> can write the 400 problem body.
/// The framework default is true only in Development.
/// </summary>
public static class RequestBinding
{
    public static void PinThrowOnBadRequest(IServiceCollection services) =>
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
}
