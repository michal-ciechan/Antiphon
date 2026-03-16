using System.Text.Json;
using Antiphon.Server.Application.Exceptions;
using ValidationException = Antiphon.Server.Application.Exceptions.ValidationException;

namespace Antiphon.Server.Api.Middleware;

/// <summary>
/// Global exception handler that catches all unhandled exceptions and returns
/// RFC 9457 Problem Details JSON responses with correlation IDs and full stack traces (MVP).
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
        var statusCode = exception is HttpException httpEx ? httpEx.StatusCode : 500;
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
            ["detail"] = exception.Message,
            ["traceId"] = traceId,
            // Full stack trace in MVP — no security filtering
            ["stackTrace"] = BuildStackTrace(exception)
        };

        // Add structured validation errors for ValidationException
        if (exception is ValidationException validationEx)
        {
            problemDetails["errors"] = validationEx.Errors;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
    }

    private static string GetProblemType(int statusCode) => statusCode switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        422 => "https://tools.ietf.org/html/rfc4918#section-11.2",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
    };

    private static string GetProblemTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        _ => "Internal Server Error"
    };

    /// <summary>
    /// Builds the full stack trace including inner exceptions.
    /// MVP only — will be stripped in production in a future story.
    /// </summary>
    private static string BuildStackTrace(Exception exception)
    {
        var traces = new List<string>();
        var current = exception;

        while (current != null)
        {
            if (current.StackTrace != null)
            {
                var prefix = current == exception ? "" : $"--- Inner: {current.GetType().Name}: {current.Message} ---\n";
                traces.Add(prefix + current.StackTrace);
            }

            current = current.InnerException;
        }

        return string.Join("\n", traces);
    }
}
