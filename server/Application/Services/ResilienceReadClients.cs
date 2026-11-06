using Antiphon.Resilience;

namespace Antiphon.Server.Infrastructure.Resilience;

internal static class ResilienceReadClients
{
    public static HttpClient Select(IHttpClientFactory? factory, HttpClient primary, string name)
    {
        if (factory is null)
            return primary;
        var named = factory.CreateClient(name);
        if (named.Timeout != Timeout.InfiniteTimeSpan)
        {
            named.Dispose();
            return primary;
        }

        if (named.BaseAddress is null && primary.BaseAddress is not null)
            named.BaseAddress = primary.BaseAddress;
        return named;
    }
}
