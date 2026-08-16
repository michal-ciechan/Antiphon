using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Api.Endpoints;

public static class AgentTaskEndpoints
{
    /// <summary>Header the delegate script sends; matches ANTIPHON_TASK_TOKEN in the agent's env.</summary>
    public const string TokenHeader = "X-Antiphon-Task-Token";

    public static void MapAgentTaskEndpoints(this WebApplication app)
    {
        var tasks = app.MapGroup("/api/agent-tasks").WithTags("AgentTasks");

        // Agent-invoked AND manual creation land here. The caller is resolved from the token, never
        // from the body — a delegate cannot claim to be someone else's parent.
        tasks.MapPost("/", async (
            CreateAgentTaskRequest request,
            HttpContext http,
            AgentTaskService service,
            CancellationToken ct) =>
        {
            var caller = await ResolveCallerAsync(http, service, ct);
            var created = await service.CreateAsync(request, caller, ct);
            return Results.Created($"/api/agent-tasks/{created.Id}", created);
        });

        // includeChecks defaults false: the interpretation tasks behind check-ins (CARD-0047) are
        // machinery, one per interpreted check, and the board is for delegated work.
        tasks.MapGet("/", async (
            Guid? rootId,
            AgentTaskStatus? status,
            bool? includeChecks,
            AgentTaskService service,
            CancellationToken ct) =>
            Results.Ok(await service.ListAsync(rootId, status, includeChecks ?? false, ct)));

        // {id} is a string, not :guid — a delegate only ever SEES 8-char short ids (the completion
        // note, the board chip), so -Status and -Reply must accept them or they are unusable.
        tasks.MapGet("/{id}", async (
            string id,
            AgentTaskService service,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            return Results.Ok(await service.GetAsync(taskId, ct));
        });

        tasks.MapPost("/{id:guid}/cancel", async (
            Guid id,
            AgentTaskService service,
            CancellationToken ct) => Results.Ok(await service.CancelAsync(id, ct)));

        tasks.MapPost("/{id:guid}/retry", async (
            Guid id,
            AgentTaskService service,
            CancellationToken ct) => Results.Ok(await service.RetryAsync(id, ct)));

        tasks.MapPost("/{id:guid}/escalate", async (
            Guid id,
            EscalateAgentTaskRequest? request,
            AgentTaskService service,
            CancellationToken ct) => Results.Ok(await service.EscalateAsync(id, request?.ModelLevel, ct)));

        tasks.MapPost("/{id}/reply", async (
            string id,
            ReplyToAgentTaskRequest request,
            AgentTaskService service,
            AgentTaskReplyService replies,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            return Results.Ok(await replies.AnswerAsync(taskId, request.Message, ct));
        });
    }

    /// <summary>
    /// A request with no token is the MANUAL path (the UI, already authenticated by the app's own
    /// auth): no parent, no reply routing, result lands on the board. A request WITH one is an
    /// agent delegating, and the token decides whether it is allowed to.
    /// </summary>
    private static async Task<AgentTaskService.Caller> ResolveCallerAsync(
        HttpContext http, AgentTaskService service, CancellationToken ct)
    {
        var token = http.Request.Headers[TokenHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return new AgentTaskService.Caller(null, null, Directory.GetCurrentDirectory());

        return await service.AuthenticateAsync(token, ct);
    }
}
