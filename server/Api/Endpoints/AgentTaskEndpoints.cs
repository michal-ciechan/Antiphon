using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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

        // includeChecks defaults false: specialist rows (Check, Distill, Diagnose) are
        // machinery, not delegated work, and the board is for the latter.
        tasks.MapGet("/", async (
            Guid? rootId,
            DateTime? since,
            string? status,
            bool? includeChecks,
            AgentTaskService service,
            CancellationToken ct) =>
            Results.Ok(await service.ListAsync(
                rootId, ParseStatuses(status), includeChecks ?? false, since, ct)));

        // These are deliberately calculated over the whole fleet, not the board's current history
        // window. The headline must not imply an old blocked task stopped existing.
        tasks.MapGet("/summary", async (
            AgentTaskService service,
            CancellationToken ct) => Results.Ok(await service.GetListSummaryAsync(ct)));

        // CARD-0304. Declared BEFORE /{id} so "pipeline" is never read as a task id. Read-only
        // fleet projection; advisory recommendations never refuse dispatch from here.
        tasks.MapGet("/pipeline", async (
            AgentTaskPipelineStatusService pipeline,
            CancellationToken ct) => Results.Ok(await pipeline.GetAsync(ct)));

        // The repo's named areas (CARD-0063). Declared BEFORE /{id} so "areas" is never read as a
        // task id. A read-only listing: the caller needs it to write a -Scope, so a missing or
        // stale token must not stop it — a token-less caller passes ?directory= instead.
        tasks.MapGet("/areas", async (
            string? directory,
            HttpContext http,
            AgentTaskService service,
            CancellationToken ct) =>
        {
            var caller = await ResolvePollingCallerAsync(http, service, ct)
                ?? new AgentTaskService.Caller(null, null, string.Empty);
            return Results.Ok(await service.ListAreasAsync(directory, caller, ct));
        });

        // CARD-0147 S3. Declared BEFORE /{id} so "worktree-health" is never read as a task id.
        // Detection only: upserts WorktreeHealthFinding rows and returns them. Never prune.
        tasks.MapPost("/worktree-health", async (
            WorktreeHealthService health,
            CancellationToken ct) => Results.Ok(await health.SweepAsync(ct)));

        // {id} is a string, not :guid — a delegate only ever SEES 8-char short ids (the completion
        // note, the board chip), so -Status and -Reply must accept them or they are unusable.
        tasks.MapGet("/{id}", async (
            string id,
            HttpContext http,
            AgentTaskService service,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            var caller = await ResolvePollingCallerAsync(http, service, ct);
            return Results.Ok(await service.GetAsync(taskId, ct, caller?.SessionId));
        });

        tasks.MapPost("/{id}/read", async (
            string id,
            AgentTaskService service,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            return Results.Ok(await service.MarkReadAsync(taskId, ct));
        });

        tasks.MapPost("/{id:guid}/cancel", async (
            Guid id,
            AgentTaskService service,
            CancellationToken ct) => Results.Ok(await service.CancelAsync(id, ct)));

        tasks.MapPost("/{id:guid}/retry", async (
            Guid id,
            AgentTaskService service,
            CancellationToken ct) => Results.Ok(await service.RetryAsync(id, ct)));

        tasks.MapPost("/{id:guid}/reroute", async (
            Guid id,
            RerouteAgentTaskRequest request,
            AgentTaskService service,
            CancellationToken ct) =>
            Results.Ok(await service.RerouteAsync(id, request.AgentKind, request.ModelLevel, ct)));

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
            return Results.Ok(await replies.AnswerAsync(
                taskId,
                request.Message,
                request.Origin ?? AnswerOrigin.Web,
                request.Round,
                ct));
        });

        // CARD-0294 S1: replay the standing authority given at dispatch as the answer.
        // Body is optional — omitted origin is Web, matching /reply.
        tasks.MapPost("/{id}/continue", async (
            string id,
            ContinueAgentTaskRequest? request,
            AgentTaskService service,
            AgentTaskReplyService replies,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            return Results.Ok(await replies.ContinueWithAuthorityAsync(
                taskId, request?.Origin ?? AnswerOrigin.Web, ct));
        });

        // Steer a RUNNING delegate without cancelling it (CARD-0062). Same body shape as reply;
        // unlike reply it never changes status — a Queued task's brief is amended in place, a
        // running one gets the message WhenIdle, and a settled one is refused.
        tasks.MapPost("/{id}/refine", async (
            string id,
            ReplyToAgentTaskRequest request,
            AgentTaskService service,
            AgentTaskReplyService replies,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            return Results.Ok(await replies.RefineAsync(taskId, request.Message, ct));
        });

        // CARD-0330 S4. Explicit flag on a distillation. 409 if the task has no ledger row.
        tasks.MapPost("/{id}/distillation/feedback", async (
            string id,
            DistillationFeedbackRequest request,
            AgentTaskService service,
            OutputDistillationService distiller,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            var verdict = ParseDistillationFeedback(request.Verdict);
            await distiller.RecordFeedbackAsync(taskId, verdict, request.Note, by: "api", ct);
            return Results.NoContent();
        });

        // CARD-0272 S3. Orchestrator override of a stage finding. Declared before /{id}/land
        // only for grouping; {id}/finding cannot collide with {id}.
        tasks.MapPost("/{id}/finding", async (
            string id,
            RecordStageFindingRequest request,
            AgentTaskService service,
            StageOutcomeService outcomes,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            return Results.Ok(await outcomes.RecordFindingAsync(taskId, request, ct));
        });

        // Explicit and ordered: a succeeded Worktree task is left for review until the caller
        // chooses to land it. The request only queues deterministic git work; it never waits for it.
        tasks.MapPost("/{id}/land", async (
            string id,
            LandAgentTaskRequest? request,
            AgentTaskService service,
            AgentTaskLandService lands,
            CancellationToken ct) =>
        {
            var taskId = await service.ResolveTaskIdAsync(id, ct);
            return Results.Accepted($"/api/agent-tasks/{taskId}",
                await lands.RequestAsync(taskId, request?.Verify, ct));
        });
    }

    private static DistillationFeedback ParseDistillationFeedback(string? verdict)
    {
        if (string.IsNullOrWhiteSpace(verdict))
            throw new ValidationException(nameof(verdict), "Verdict is required (Good, Lost, or Noisy).");
        return verdict.Trim().ToLowerInvariant() switch
        {
            "good" => DistillationFeedback.Good,
            "lost" or "lostinformation" or "lost_information" => DistillationFeedback.LostInformation,
            "noisy" => DistillationFeedback.Noisy,
            _ => throw new ValidationException(
                nameof(verdict), "Verdict must be Good, Lost, or Noisy."),
        };
    }

    /// <summary>
    /// A request with no token is the MANUAL path (the UI, already authenticated by the app's own
    /// auth): no parent, no reply routing, result lands on the board. A request WITH one is an
    /// agent delegating, and the token decides whether it is allowed to.
    ///
    /// <para><b>A token-less caller inherits NOTHING</b> (CARD-0020 S1). This used to hand back
    /// <c>Directory.GetCurrentDirectory()</c> — the SERVER PROCESS's own cwd, which on this
    /// deployment is <c>&lt;repo&gt;\server</c>. That is not the caller's directory in any sense,
    /// and it silently became the one implicit permission
    /// <see cref="DelegationWorkspaceResolver"/> grants ("the parent's OWN tree"), so a shell
    /// <c>curl</c> with no token either authorised the server's own folder without anyone asking
    /// for it, or — for the repo root, which is that folder's PARENT — was refused as "outside the
    /// allowed roots" and told to edit config it did not need to edit (reproduced live twice,
    /// 2026-08-20). An empty directory makes <c>workingDirectory</c> effectively mandatory on this
    /// path and turns both silences into one explicit refusal.</para>
    ///
    /// <para>Internal rather than private so <c>AgentTaskCallerResolutionTests</c> can pin the
    /// no-token shape directly; there is nothing else to assert it through short of an HTTP round
    /// trip against a configured server.</para>
    /// </summary>
    internal static async Task<AgentTaskService.Caller> ResolveCallerAsync(
        HttpContext http, AgentTaskService service, CancellationToken ct)
    {
        var token = http.Request.Headers[TokenHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return new AgentTaskService.Caller(null, null, string.Empty);

        return await service.AuthenticateAsync(token, ct);
    }

    /// <summary>
    /// GET remains a public status read. An absent or stale delegation token simply means this
    /// poll cannot be attributed to a receiving session.
    /// </summary>
    internal static async Task<AgentTaskService.Caller?> ResolvePollingCallerAsync(
        HttpContext http, AgentTaskService service, CancellationToken ct)
    {
        try
        {
            return await ResolveCallerAsync(http, service, ct);
        }
        catch (ForbiddenException)
        {
            return null;
        }
    }

    private static IReadOnlyCollection<AgentTaskStatus>? ParseStatuses(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var statuses = new List<AgentTaskStatus>();
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<AgentTaskStatus>(name, ignoreCase: true, out var status)
                || !Enum.IsDefined(status))
            {
                throw new ValidationException("status", $"'{name}' is not a valid agent task status.");
            }

            statuses.Add(status);
        }

        return statuses.Distinct().ToArray();
    }
}
