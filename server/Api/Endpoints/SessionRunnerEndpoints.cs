using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Mvc;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public static class SessionRunnerEndpoints
{
    public static void MapSessionRunnerEndpoints(this WebApplication app)
    {
        app.MapPost(PhoneHomeProtocol.RegisterPath, (
            HttpContext http,
            PhoneHomeRegistrationRequest request,
            PhoneHomeRunnerDirectory directory,
            IOptions<PhoneHomeRunnerSettings> settings) =>
        {
            if (!settings.Value.Enabled)
                throw new ConflictException("Phone-home runner is disabled.", "phone_home_disabled");
            var secret = http.Request.Headers[PhoneHomeProtocol.SecretHeader].ToString();
            if (!directory.AuthenticateSecret(secret))
                throw new ForbiddenException("Runner authentication failed.");
            return Results.Ok(directory.Register(request));
        }).WithTags("SessionRunners");

        app.MapGet("/api/session-runners/{runnerId}/status", (
            string runnerId,
            PhoneHomeRunnerDirectory directory) =>
            Results.Ok(directory.Status(runnerId))).WithTags("SessionRunners");

        app.MapGet("/api/session-runners/{runnerId}/slots", async (
            string runnerId,
            PhoneHomeRunnerDirectory directory,
            [FromServices] AppDbContext db,
            CancellationToken ct) =>
            Results.Ok(await RunnerSlotService.ListAsync(directory, db, runnerId, ct)))
            .WithTags("SessionRunners");

        app.MapPost("/api/session-runners/{runnerId}/slots/{sessionId:guid}/release", async (
            HttpContext http,
            string runnerId,
            Guid sessionId,
            RunnerSlotReleaseRequest body,
            PhoneHomeRunnerDirectory directory,
            IOptions<PhoneHomeRunnerSettings> settings,
            [FromServices] AppDbContext db,
            CancellationToken ct) =>
        {
            RequireOperator(http, settings.Value);
            return await ReleaseOrReconcileAsync(
                () => RunnerSlotService.ReleaseAsync(directory, db, runnerId, sessionId, body.Reason, ct),
                directory, db, sessionId, ct);
        }).WithTags("SessionRunners");

        app.MapPost("/api/session-runners/{runnerId}/slots/release-orphans", async (
            HttpContext http,
            string runnerId,
            RunnerSlotReleaseRequest body,
            PhoneHomeRunnerDirectory directory,
            IOptions<PhoneHomeRunnerSettings> settings,
            [FromServices] AppDbContext db,
            CancellationToken ct) =>
        {
            RequireOperator(http, settings.Value);
            return await ReleaseOrReconcileAsync(
                () => RunnerSlotService.ReleaseOrphansAsync(directory, db, runnerId, body.Reason, ct),
                directory, db, null, ct);
        }).WithTags("SessionRunners");

        app.MapGet("/api/session-runners/{runnerId}/provider-auth/{provider}", async (
            string runnerId,
            string provider,
            PhoneHomeRunnerDirectory directory,
            CancellationToken ct) =>
            Results.Ok(await directory.RequestProviderAuthAsync(runnerId, provider, ct)))
            .WithTags("SessionRunners");

        app.MapGet("/api/session-runners/{runnerId}/connect", async (
            string runnerId,
            HttpContext http,
            PhoneHomeRunnerDirectory directory,
            IOptions<PhoneHomeRunnerSettings> settings,
            CancellationToken ct) =>
        {
            if (!settings.Value.Enabled)
                throw new ConflictException("Phone-home runner is disabled.", "phone_home_disabled");
            if (!http.WebSockets.IsWebSocketRequest)
                throw new ConflictException("WebSocket upgrade is required.", "phone_home_websocket_required");
            var ticket = http.Request.Headers[PhoneHomeProtocol.TicketHeader].ToString();
            if (string.IsNullOrWhiteSpace(ticket))
                throw new ForbiddenException("Connection ticket is required.");
            directory.PeekTicket(runnerId, ticket);
            var socket = await http.WebSockets.AcceptWebSocketAsync();
            PhoneHomeLiveConnection connection;
            try
            {
                connection = directory.AcceptConnect(runnerId, ticket, socket);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            try
            {
                await connection.ReceiveLoopAsync(ct);
            }
            catch (PhoneHomeTransportException ex) when (ex.Code == PhoneHomeProblemTypes.EventOverflow
                || ex.Code == PhoneHomeProblemTypes.MessageTooLarge)
            {
                directory.Disconnect(connection, ex.Code);
            }
            finally
            {
                directory.Disconnect(connection, "closed");
                await connection.DisposeAsync();
            }
        }).WithTags("SessionRunners");
    }

    /// <summary>
    /// CARD-0653: force-release needs the operator credential. The client address proves nothing:
    /// the public vhost arrives through Caddy and Vite as a loopback connection.
    /// </summary>
    private static void RequireOperator(HttpContext http, PhoneHomeRunnerSettings settings)
    {
        var path = OperatorTokenFile.ResolvePath(settings.OperatorTokenPath);
        var provided = http.Request.Headers[OperatorTokenFile.Header].ToString();
        if (!OperatorTokenFile.Matches(OperatorTokenFile.ReadOrCreate(path), provided))
            throw new ForbiddenException(
                "Force-release requires the operator token (scripts/runner-slots.ps1 sends it).",
                "operator_token_required");
    }

    /// <summary>
    /// A save that fails after the runner has released leaves a pending intent. Finish that
    /// audit before answering; a failure that saved nothing is still a failure.
    /// </summary>
    private static async Task<IResult> ReleaseOrReconcileAsync(
        Func<Task<RunnerSlotReleaseDto>> release,
        PhoneHomeRunnerDirectory directory,
        AppDbContext db,
        Guid? sessionId,
        CancellationToken ct)
    {
        Exception? failed = null;
        try
        {
            return Results.Ok(await release());
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ValidationException)
        {
            failed = ex;
        }

        db.ChangeTracker.Clear();
        var finished = await RunnerSlotService.ReconcilePendingReleasesAsync(directory, db, ct);
        if (finished.Count == 0 || (sessionId is Guid id && !finished.Contains(id)))
            ExceptionDispatchInfo.Capture(failed!).Throw();
        return Results.Ok(new RunnerSlotReleaseDto(finished.Count, finished));
    }
}
