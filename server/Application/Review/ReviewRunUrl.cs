namespace Antiphon.Server.Application.Review;

internal static class ReviewRunUrl
{
    public static string? Create(string? publicBaseUrl, Guid runId)
    {
        if (!Uri.TryCreate(publicBaseUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || uri.IsLoopback
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
            return null;

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return null;

        var root = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        return $"{root.TrimEnd('/')}/reviews/{runId:D}";
    }
}
