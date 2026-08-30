using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

/// <summary>
/// GET AppHost /health for the inbound-unconsumed event's diagnostic field. Never the lag verdict.
/// </summary>
public sealed class HttpAppHostHealthProbe(
    IOptions<AntiphonGatewayOptions> options,
    ILogger<HttpAppHostHealthProbe> logger) : IAppHostHealthProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly AntiphonGatewayOptions _options = options.Value;

    public async Task<string> ProbeAsync(CancellationToken cancellationToken)
    {
        var url = _options.AppHostHealthUrl;
        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            return $"http {(int)response.StatusCode}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "[inbound-unconsumed] AppHost health probe failed at {Url}", url);
            return $"fail: {ex.GetType().Name}";
        }
    }
}
