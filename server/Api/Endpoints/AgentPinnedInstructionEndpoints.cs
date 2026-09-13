using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Api.Endpoints;

public static class AgentPinnedInstructionEndpoints
{
    public static void MapAgentPinnedInstructionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agents/{id:guid}/pinned-instructions")
            .WithTags("Agents");

        group.MapGet("/", async (
            Guid id,
            bool? includeRevoked,
            HttpContext http,
            AgentPinnedInstructionService pins,
            AgentTaskService tasks,
            AppDbContext db,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var principal = await ResolvePrincipalAsync(id, http, tasks, db, user, ct);
            return Results.Ok(await pins.GetAsync(id, principal, includeRevoked ?? false, ct));
        });

        group.MapPost("/", async (
            Guid id,
            CapturePinnedInstructionRequest request,
            HttpContext http,
            AgentPinnedInstructionService pins,
            AgentTaskService tasks,
            AppDbContext db,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var principal = await ResolvePrincipalAsync(id, http, tasks, db, user, ct);
            var result = await pins.CaptureAsync(id, request, principal, ct);
            return result.CreatedNewRow
                ? Results.Created($"/api/agents/{id}/pinned-instructions", result.Set)
                : Results.Ok(result.Set);
        });

        group.MapPost("/{pinId:guid}/revoke", async (
            Guid id,
            Guid pinId,
            RevokePinnedInstructionRequest request,
            HttpContext http,
            AgentPinnedInstructionService pins,
            AgentTaskService tasks,
            AppDbContext db,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var principal = await ResolvePrincipalAsync(id, http, tasks, db, user, ct);
            return Results.Ok(await pins.RevokeAsync(id, pinId, request, principal, ct));
        });

        group.MapPost("/reconcile", async (
            Guid id,
            ReconcilePinnedInstructionsRequest request,
            HttpContext http,
            AgentPinnedInstructionService pins,
            AgentTaskService tasks,
            AppDbContext db,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var principal = await ResolvePrincipalAsync(id, http, tasks, db, user, ct);
            return Results.Ok(await pins.ReconcileAsync(id, request, principal, ct));
        });
    }

    internal static async Task<PinPrincipal> ResolvePrincipalAsync(
        Guid agentId,
        HttpContext http,
        AgentTaskService tasks,
        AppDbContext db,
        ICurrentUser user,
        CancellationToken ct)
    {
        if (!http.Request.Headers.ContainsKey(AgentTaskEndpoints.TokenHeader))
            return PinPrincipal.Operator(user.UserId);

        var token = http.Request.Headers[AgentTaskEndpoints.TokenHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ForbiddenException(
                "A live named-agent session token is required when the token header is present.",
                "pin_caller_mismatch");
        }

        var caller = await tasks.AuthenticateAsync(token, ct);
        if (caller.Task is not null || caller.CapabilityId is not null || caller.SessionId is null)
        {
            throw new ForbiddenException(
                "Pinned instructions require a live named-agent session token.",
                "pin_caller_mismatch");
        }

        var session = await db.AgentSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == caller.SessionId.Value, ct)
            ?? throw new ForbiddenException("Session token is not recognised.", "pin_caller_mismatch");

        if (session.Status is not SessionStatus.Starting and not SessionStatus.Running
            || session.EndedAt is not null)
        {
            throw new ForbiddenException("The session token is not for a live named agent.", "pin_caller_mismatch");
        }

        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == agentId, ct)
            ?? throw new NotFoundException("Agent", agentId);

        var ownsSession = session.StandingAgentId == agent.Id
            || (Guid.TryParse(agent.PersistentSessionId, out var persistent)
                && persistent == session.Id);
        if (!ownsSession || agent.IsPoolDelegate)
        {
            throw new ForbiddenException(
                "A session token may only access its own agent's pins.",
                "pin_caller_mismatch");
        }

        return PinPrincipal.AgentSession(session.Id, agent.Id);
    }
}
