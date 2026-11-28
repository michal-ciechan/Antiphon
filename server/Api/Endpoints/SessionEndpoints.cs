using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var sessions = app.MapGroup("/api/sessions")
            .WithTags("Sessions");

        sessions.MapPost("/", async (
            StartAgentSessionRequest request,
            AgentRegistry registry,
            AgentSessionService service,
            ApiKeyEnvResolver apiKeys,
            CancellationToken cancellationToken) =>
        {
            ValidateTerminalSize(request.Cols, request.Rows);
            var definitionName = string.IsNullOrWhiteSpace(request.DefinitionName)
                ? registry.Settings.DefaultDefinition
                : request.DefinitionName;
            var spec = registry.Resolve(definitionName, new AgentLaunchOptions(
                Cwd: null,
                Cols: request.Cols,
                Rows: request.Rows,
                ExtraArgs: request.ExtraArgs,
                ExtraEnv: request.ExtraEnv));
            // A third direct AgentRegistry.Resolve caller, and therefore a third place Env is
            // finalized (CARD-0106 S2). No agent and no card here, so GLOBAL keys only. Without
            // this the tripwire would refuse an otherwise-legitimate launch — the tripwire is the
            // backstop for a FORGOTTEN path, not a substitute for wiring a known one.
            spec = await apiKeys.ResolveSpecAsync(
                spec, projectId: null, $"session start on '{definitionName}'", cancellationToken);
            var result = await service.StartAsync(
                request with { DefinitionName = definitionName, AgentKind = spec.Kind },
                spec,
                cancellationToken);

            return Results.Created($"/api/sessions/{result.SessionId}", result);
        });

        sessions.MapGet("/{id:guid}/buffer", async (
            Guid id,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetBufferAsync(id, cancellationToken));
        });

        sessions.MapGet("/{id:guid}/transcript", async (
            Guid id,
            long? since,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetTranscriptAsync(id, since ?? 0, cancellationToken));
        });

        sessions.MapSessionInput();

        sessions.MapGet("/{id:guid}/commands", async (
            Guid id,
            SlashCommandCatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await catalog.GetCommandsAsync(id, cancellationToken));
        });

        sessions.MapGet("/{id:guid}/messages", async (
            Guid id,
            long? since,
            int? limit,
            SessionMessageQueueService queue,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await queue.GetQueueAsync(id, since ?? 0,
                limit ?? SessionMessageQueueService.PendingNotePageDefault, cancellationToken));
        });

        sessions.MapGet("/{id:guid}/pending-notes", async (
            Guid id,
            long? since,
            int? limit,
            SessionMessageQueueService queue,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await queue.ListPendingNotesAsync(
                id, since ?? 0, limit ?? SessionMessageQueueService.PendingNotePageDefault, cancellationToken));
        });

        sessions.MapPost("/{id:guid}/messages", async (
            Guid id,
            EnqueueMessageRequest request,
            SessionMessageQueueService queue,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await queue.EnqueueAsync(id, request.Body, request.Mode, cancellationToken));
        });

        sessions.MapDelete("/{id:guid}/messages/{messageId:guid}", async (
            Guid id,
            Guid messageId,
            SessionMessageQueueService queue,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await queue.CancelAsync(id, messageId, cancellationToken));
        });

        sessions.MapPost("/{id:guid}/messages/{messageId:guid}/send-now", async (
            Guid id,
            Guid messageId,
            SessionMessageQueueService queue,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await queue.SendNowAsync(id, messageId, cancellationToken));
        });

        sessions.MapExpectationHoldRelease();
        sessions.MapQueuedMessageAbandon();

        sessions.MapGet("/{id:guid}/stop-preconditions", async (
            Guid id, VerifiedSessionStopService stops, CancellationToken cancellationToken) =>
            Results.Ok(await stops.InspectAsync(id, cancellationToken)));

        sessions.MapPost("/{id:guid}/resize", async (
            Guid id,
            ResizeSessionRequest request,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            ValidateTerminalSize(request.Cols, request.Rows);
            await service.ResizeAsync(id, request.Cols, request.Rows, cancellationToken);
            return Results.NoContent();
        });

        sessions.MapPost("/{id:guid}/resume", async (
            Guid id,
            ResumeAgentSessionRequest request,
            AppDbContext db,
            AgentRegistry registry,
            AgentSessionService service,
            ApiKeyEnvResolver apiKeys,
            CancellationToken cancellationToken) =>
        {
            var session = await db.AgentSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException("AgentSession", id);
            var spec = registry.Resolve(session.DefinitionName, new AgentLaunchOptions(
                Cwd: session.Cwd,
                Cols: session.Cols,
                Rows: session.Rows));
            spec = await apiKeys.ResolveSpecAsync(
                spec, projectId: null, $"resume of session {id:D}", cancellationToken);

            return Results.Accepted($"/api/sessions/{id}", await service.ResumeAsync(id, spec, request.Mode, cancellationToken));
        });

        sessions.MapSessionKill();
    }

    public static RouteHandlerBuilder MapSessionInput(this IEndpointRouteBuilder sessions) =>
        sessions.MapPost("/{id:guid}/input", async (
            Guid id,
            SendSessionInputRequest request,
            SessionTerminalAttachments attachments,
            AgentSessionService service,
            CancellationToken cancellationToken) =>
        {
            using var input = attachments.TryBeginInput(id)
                ?? throw new ConflictException(SessionTerminalAttachments.StopInFlightMessage, "session_stop_in_flight");
            await service.SendInputAsync(id, request.Input, cancellationToken);
            return Results.NoContent();
        });

    public static RouteHandlerBuilder MapSessionKill(this IEndpointRouteBuilder sessions) =>
        sessions.MapPost("/{id:guid}/kill", async (
            HttpContext http,
            Guid id,
            DateTime? expectedGeneration,
            Guid? expectedOwnerId,
            Guid? expectedLastTaskId,
            Guid? expectedLastTaskRevision,
            long? expectedTurnEndSequence,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SessionStopReason? request,
            AgentTaskService tasks,
            IOptions<PhoneHomeRunnerSettings> settings,
            ICurrentUser user,
            AgentSessionService service,
            VerifiedSessionStopService stops,
            CancellationToken cancellationToken) =>
        {
            var query = http.Request.Query;
            if (query.ContainsKey(nameof(expectedGeneration)) || query.ContainsKey(nameof(expectedOwnerId))
                || query.ContainsKey(nameof(expectedLastTaskId)) || query.ContainsKey(nameof(expectedLastTaskRevision))
                || query.ContainsKey(nameof(expectedTurnEndSequence)))
            {
                var caller = await tasks.AuthenticateAsync(http.Request.Headers[AgentTaskEndpoints.TokenHeader].FirstOrDefault(),
                    cancellationToken);
                if (!caller.MayDelegate)
                    throw new ForbiddenException("A verified stop requires an orchestrator credential.", "session_stop_principal_denied");

                if (expectedGeneration is not { } generation || expectedTurnEndSequence is not long sequence || sequence <= 0
                    || (expectedLastTaskId is null) != (expectedLastTaskRevision is null))
                    throw new ValidationException("stopPreconditions", "Supply the generation and TurnEnd sequence, and pair the last task ID with its revision.");
                var authority = await stops.ResolveAuthorityAsync(caller,
                    caller.Task?.ProjectId ?? await tasks.DeriveCallerProjectAsync(caller, cancellationToken), cancellationToken);
                return Results.Ok(await stops.StopAsync(id, new(generation, expectedOwnerId,
                    expectedLastTaskId, expectedLastTaskRevision, sequence), authority,
                    SessionStopAudit.Reason(request?.Reason, "Verified idle seat release"), cancellationToken));
            }

            OperatorCredential.Require(http, settings.Value, "An unconditional session kill requires the operator token.");
            await service.KillByOperatorAsync(id, $"operator:{user.UserName}",
                SessionStopAudit.Reason(request?.Reason, "Operator requested unconditional session kill"), cancellationToken);
            return Results.NoContent();
        });

    public sealed record SessionStopReason(string? Reason);

    /// <summary>
    /// CARD-0650: audited operator release of an expectation-watchdog composer hold. Types nothing.
    /// An operator surface (review 8adb4cd6): it needs the CARD-0658 operator token, compared in
    /// constant time, whatever the client address; the audit records the operator it authenticated.
    /// </summary>
    public static RouteHandlerBuilder MapExpectationHoldRelease(this IEndpointRouteBuilder sessions) =>
        sessions.MapPost("/{id:guid}/expectation-hold/release", async (
            HttpContext http,
            Guid id,
            ReleaseExpectationHoldRequest request,
            IOptions<PhoneHomeRunnerSettings> settings,
            ICurrentUser user,
            SessionMessageQueueService queue,
            CancellationToken cancellationToken) =>
        {
            OperatorCredential.Require(http, settings.Value,
                "Releasing an expectation-watchdog hold requires the operator token (X-Antiphon-Operator-Token).");
            var releasedBy = $"{user.UserName} (operator token)";
            return Results.Ok(await queue.ReleaseExpectationHoldAsync(id, request.Reason, releasedBy, cancellationToken));
        });

    public static RouteHandlerBuilder MapQueuedMessageAbandon(this IEndpointRouteBuilder sessions) =>
        sessions.MapPost("/{id:guid}/messages/{messageId:guid}/abandon", async (
            HttpContext http, Guid id, Guid messageId, AbandonQueuedMessageRequest request,
            IOptions<PhoneHomeRunnerSettings> settings, ICurrentUser user,
            SessionMessageQueueService queue, CancellationToken cancellationToken) =>
        {
            OperatorCredential.Require(http, settings.Value,
                "Abandoning unconfirmed input requires the operator token (X-Antiphon-Operator-Token).");
            return Results.Ok(await queue.AbandonAsync(id, messageId, request.Reason,
                $"{user.UserName} (operator token)", cancellationToken));
        });

    private static void ValidateTerminalSize(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0)
            throw new ValidationException("size", "Terminal cols and rows must be positive.");
    }
}
