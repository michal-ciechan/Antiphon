namespace Antiphon.Observability;

/// <summary>Measures the existing SendAsync boundary; never reads URI, headers or content.</summary>
public sealed class LiveTimingHttpHandler(LiveTiming timing) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var phase = timing.BeginExternal(ExternalOperation.Http);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) phase.Complete();
        return response;
    }
}
