using Serilog.Context;

namespace Antiphon.Server.Api.Middleware;

/// <summary>
/// Reads correlation IDs (workflowId, stageId, executionId) from request headers or route values
/// and pushes them to Serilog LogContext via AsyncLocal so every log line carries them (NFR19).
/// Generates a correlation ID if not present in headers.
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string WorkflowIdHeader = "X-Workflow-Id";
    private const string StageIdHeader = "X-Stage-Id";
    private const string ExecutionIdHeader = "X-Execution-Id";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate or read correlation ID
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? context.TraceIdentifier;

        // Set the trace identifier so ExceptionMiddleware can use it
        context.TraceIdentifier = correlationId;

        // Read domain-specific correlation IDs from headers
        var workflowId = context.Request.Headers[WorkflowIdHeader].FirstOrDefault()
            ?? context.Request.RouteValues["workflowId"]?.ToString();

        var stageId = context.Request.Headers[StageIdHeader].FirstOrDefault()
            ?? context.Request.RouteValues["stageId"]?.ToString();

        var executionId = context.Request.Headers[ExecutionIdHeader].FirstOrDefault()
            ?? context.Request.RouteValues["executionId"]?.ToString();

        // Add correlation ID to response headers for client tracing
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        // Push all IDs to Serilog LogContext via AsyncLocal — no parameter pollution
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("WorkflowId", workflowId ?? ""))
        using (LogContext.PushProperty("StageId", stageId ?? ""))
        using (LogContext.PushProperty("ExecutionId", executionId ?? ""))
        {
            await _next(context);
        }
    }
}
