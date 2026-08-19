using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using ValidationException = Antiphon.Server.Application.Exceptions.ValidationException;

namespace Antiphon.Server.Api.Middleware;

/// <summary>
/// Global exception handler that catches all unhandled exceptions and returns
/// sanitized RFC 9457 Problem Details JSON responses with correlation IDs and stable codes.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, detail) = Classify(exception);
        var traceId = context.TraceIdentifier;

        // Log at appropriate level
        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);
        }
        else
        {
            _logger.LogWarning(exception, "HTTP {StatusCode} exception. TraceId: {TraceId}", statusCode, traceId);
        }

        var problemDetails = new Dictionary<string, object?>
        {
            ["type"] = GetProblemType(statusCode),
            ["title"] = GetProblemTitle(statusCode),
            ["status"] = statusCode,
            ["detail"] = detail,
            ["traceId"] = traceId,
        };

        if (exception is HttpException { Code: not null } codedException)
            problemDetails["code"] = codedException.Code;

        // Add structured validation errors for ValidationException
        if (exception is ValidationException validationEx)
        {
            problemDetails["errors"] = validationEx.Errors;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
    }

    /// <summary>
    /// Body-bind failures (unknown enum name, numeric enum token with integers disallowed) arrive
    /// as <see cref="BadHttpRequestException"/> wrapping a <see cref="JsonException"/>. Without
    /// this mapping they used to become a generic 500. A raw <see cref="JsonException"/> is
    /// treated the same only when it is the body-bind escape — a service parsing a stored file
    /// should still 500 if it lets one out.
    /// </summary>
    private static (int StatusCode, string Detail) Classify(Exception exception) => exception switch
    {
        HttpException http => (http.StatusCode, http.Message),
        BadHttpRequestException bad => (bad.StatusCode, BindFailureDetail(bad)),
        JsonException json when IsRequestBodyBindFailure(json) => (StatusCodes.Status400BadRequest, json.Message),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };

    private static string BindFailureDetail(BadHttpRequestException exception) =>
        FindJsonException(exception)?.Message ?? exception.Message;

    private static JsonException? FindJsonException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is JsonException json)
                return json;
        }

        return null;
    }

    /// <summary>
    /// STJ names the failing token with a JSON pointer (<c>$.modelLevel</c>). Used only as the
    /// body-bind escape when Minimal APIs do not wrap the throw in
    /// <see cref="BadHttpRequestException"/>.
    /// </summary>
    private static bool IsRequestBodyBindFailure(JsonException exception) =>
        exception.Path is { Length: > 0 } path && path.StartsWith('$');

    private static string GetProblemType(int statusCode) => statusCode switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        422 => "https://tools.ietf.org/html/rfc4918#section-11.2",
        503 => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
    };

    private static string GetProblemTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };
}
