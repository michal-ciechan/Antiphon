namespace Antiphon.Server.Application.Exceptions;

/// <summary>The additive host-load read contract uses typed HTTP 400 for invalid selectors.</summary>
public sealed class HostLoadQueryException(string field, string message, string code = "invalid_host_load_query")
    : HttpException(400, message, code, new Dictionary<string, object?> { ["field"] = field });
