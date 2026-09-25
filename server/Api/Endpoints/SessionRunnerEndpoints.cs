using System.Net.WebSockets;
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
                intents => RunnerSlotService.ReleaseAsync(directory, db, runnerId, sessionId, body.Reason, ct, intents),
                directory, db, ct);
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
                intents => RunnerSlotService.ReleaseOrphansAsync(directory, db, runnerId, body.Reason, ct, intents),
                directory, db, ct);
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
            ILogger<PhoneHomeLiveConnection> logger,
            IHostApplicationLifetime lifetime,
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

            logger.LogInformation(
                "Phone-home connection {RunnerId} epoch {Epoch} accepted: capacity {Capacity}, platform {Platform}, build {BuildVersion}",
                connection.RunnerId, connection.Epoch, connection.Capacity, connection.Platform,
                connection.Capabilities?.Version);

            // CARD-0679 D-1: every end is classified once, recorded once and logged once. A runner
            // dropping its socket is a transport event, not an unhandled request error; only a
            // receive fault nobody classified still reaches the exception middleware.
            string reason;
            Exception? fault = null;
            try
            {
                await connection.ReceiveLoopAsync(ct, logger);
                reason = ct.IsCancellationRequested ? AbortReason(lifetime) : "close_received";
            }
            catch (PhoneHomeTransportException ex) when (ex.Code == PhoneHomeProblemTypes.EventOverflow
                || ex.Code == PhoneHomeProblemTypes.MessageTooLarge)
            {
                reason = ex.Code;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                reason = AbortReason(lifetime);
            }
            catch (WebSocketException)
            {
                reason = "transport_abort";
            }
            catch (Exception) when (connection.LastDisconnectReason is { } recorded)
            {
                // Superseded by a newer connection, whose accept disposed this socket under the read.
                reason = recorded;
            }
            catch (Exception ex)
            {
                reason = $"receive_fault:{ex.GetType().Name}";
                fault = ex;
            }

            try
            {
                directory.Disconnect(connection, reason);
                logger.LogWarning(
                    "Phone-home connection {RunnerId} epoch {Epoch} ended: {Reason} after {LifetimeSeconds:0.0}s; "
                    + "socket {SocketState}; pending {PendingEvents} events / {PendingEventBytes} bytes; "
                    + "live buffer {LiveBufferEvents} events; in flight {InFlight}; failing {Waiters} waiters",
                    connection.RunnerId, connection.Epoch, connection.LastDisconnectReason ?? reason,
                    (connection.Clock.GetUtcNow() - connection.StartedAtUtc).TotalSeconds,
                    connection.SocketState, connection.PendingEvents, connection.PendingEventBytes,
                    connection.LiveBufferEvents, connection.InFlight, connection.PendingWaiters);
            }
            finally
            {
                // CARD-0679 D-5: every waiter fails with phone_home_connection_closed_in_flight naming this reason.
                await connection.DisposeAsync(connection.LastDisconnectReason ?? reason);
            }

            if (fault is not null)
                ExceptionDispatchInfo.Capture(fault).Throw();
        }).WithTags("SessionRunners");
    }

    /// <summary>
    /// CARD-0679 D-1: Kestrel cancels RequestAborted both when the runner's connection drops under
    /// the read and when this host stops. Only the second is the server aborting the request.
    /// </summary>
    private static string AbortReason(IHostApplicationLifetime lifetime) =>
        lifetime.ApplicationStopping.IsCancellationRequested ? "request_aborted" : "transport_abort";

    /// <summary>
    /// CARD-0653: force-release needs the operator credential. The client address proves nothing:
    /// the public vhost arrives through Caddy and Vite as a loopback connection.
    /// </summary>
    private static void RequireOperator(HttpContext http, PhoneHomeRunnerSettings settings) =>
        OperatorCredential.Require(
            http, settings, "Force-release requires the operator token (scripts/runner-slots.ps1 sends it).");

    /// <summary>
    /// A save that fails after the runner has released leaves a pending intent. Finish that
    /// audit before answering. The answer is about this request's own intents only: finishing an
    /// earlier request's intent says nothing about this one, and a failure that recorded no
    /// intent, or any intent of this request that is not released, is still a failure.
    /// </summary>
    private static async Task<IResult> ReleaseOrReconcileAsync(
        Func<ICollection<Guid>, Task<RunnerSlotReleaseDto>> release,
        PhoneHomeRunnerDirectory directory,
        AppDbContext db,
        CancellationToken ct)
    {
        var intents = new List<Guid>();
        Exception? failed = null;
        try
        {
            return Results.Ok(await release(intents));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ValidationException)
        {
            failed = ex;
        }

        db.ChangeTracker.Clear();
        await RunnerSlotService.ReconcilePendingReleasesAsync(directory, db, ct);
        var outcomes = await RunnerSlotService.IntentOutcomesAsync(db, intents, ct);
        if (outcomes.Count == 0 || outcomes.Count != intents.Count
            || outcomes.Any(outcome => outcome.Outcome != "released"))
            ExceptionDispatchInfo.Capture(failed!).Throw();
        var released = outcomes.Select(outcome => outcome.SessionId).ToArray();
        return Results.Ok(new RunnerSlotReleaseDto(released.Length, released, outcomes));
    }
}
