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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Api.Endpoints;

public sealed record RunnerDrainRequest(string? Reason, string? RedirectTo = null, bool RetireWhenIdle = false);

public sealed record RunnerDrainClearRequest(string? Reason);

public static class SessionRunnerEndpoints
{
    public static void MapSessionRunnerEndpoints(this WebApplication app)
    {
        app.MapGet("/api/session-runners", async (
            HttpContext http,
            PhoneHomeRunnerDirectory directory,
            CancellationToken ct) =>
        {
            var db = http.RequestServices.GetService<AppDbContext>();
            var delegation = http.RequestServices.GetService<IOptions<DelegationSettings>>()?.Value
                ?? new DelegationSettings();
            var prep = http.RequestServices.GetService<RemoteWorkspacePreparer>();
            return Results.Ok(await SessionRunnerCatalogue.ListAsync(directory, db, delegation, prep, ct));
        }).WithTags("SessionRunners");

        app.MapPost(PhoneHomeProtocol.RegisterPath, (
            HttpContext http,
            PhoneHomeRegistrationRequest request,
            PhoneHomeRunnerDirectory directory,
            IOptions<PhoneHomeRunnerSettings> settings) =>
        {
            if (!settings.Value.Enabled)
                throw new ConflictException("Phone-home runner is disabled.", "phone_home_disabled");
            var secret = http.Request.Headers[PhoneHomeProtocol.SecretHeader].ToString();
            if (!directory.AuthenticateSecret(request.RunnerId, secret))
                throw new ForbiddenException("Runner authentication failed.");
            return Results.Ok(directory.Register(request));
        }).WithTags("SessionRunners");

        app.MapGet("/api/session-runners/{runnerId}/status", (
            string runnerId,
            PhoneHomeRunnerDirectory directory) =>
            Results.Ok(directory.Status(runnerId))).WithTags("SessionRunners");

        app.MapPost("/api/session-runners/{runnerId}/drain", async (
            HttpContext http,
            string runnerId,
            RunnerDrainRequest body,
            RunnerStateService drains,
            IOptions<PhoneHomeRunnerSettings> settings,
            CancellationToken ct) =>
        {
            OperatorCredential.Require(http, settings.Value, "Draining a runner requires the operator token.");
            await drains.DrainAsync(runnerId, body.Reason, body.RedirectTo, body.RetireWhenIdle, ct);
            return Results.Ok();
        }).WithTags("SessionRunners");

        app.MapPost("/api/session-runners/{runnerId}/drain/clear", async (
            HttpContext http,
            string runnerId,
            RunnerDrainClearRequest body,
            RunnerStateService drains,
            AgentTaskService tasks,
            IOptions<PhoneHomeRunnerSettings> settings,
            CancellationToken ct) =>
        {
            OperatorCredential.Require(http, settings.Value, "Clearing a drain requires the operator token.");
            var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
            await drains.ClearAsync(runnerId, body.Reason, caller?.Task?.Id, ct);
            return Results.Ok();
        }).WithTags("SessionRunners");

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

            var connectionId = http.Connection.Id;
            var remoteIp = http.Connection.RemoteIpAddress?.ToString();
            var remotePort = http.Connection.RemotePort;
            logger.LogInformation(
                "Phone-home connection {RunnerId} epoch {Epoch} accepted: capacity {Capacity}, platform {Platform}, build {BuildVersion} connection {ConnectionId} peer {RemoteIp}:{RemotePort}",
                connection.RunnerId, connection.Epoch, connection.Capacity, connection.Platform,
                connection.Capabilities?.Version, connectionId, remoteIp, remotePort);

            // CARD-0679 D-1: every end is classified once, recorded once and logged once. A runner
            // dropping its socket is a transport event, not an unhandled request error; only a
            // receive fault nobody classified still reaches the exception middleware.
            // CARD-0716 D-1: the receive loop also ends when this host is stopping, so the close
            // frame goes out before Kestrel's shutdown timeout aborts the upgraded connection.
            // ReceiveAsync treats cancellation as Abort(), which would tear the socket down before
            // DisposeAsync can send 1001. Watch the host stop beside the receive instead, and leave
            // the socket open so the close frame is a send concurrent with that receive.
            string reason;
            Exception? fault = null;
            WebSocketException? transportFault = null;
            var receive = connection.ReceiveLoopAsync(ct, logger);
            var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (lifetime.ApplicationStopping.UnsafeRegister(
                static state => ((TaskCompletionSource)state!).TrySetResult(), stopping))
            {
                var winner = await Task.WhenAny(receive, stopping.Task);
                if (winner == stopping.Task && !receive.IsCompleted)
                {
                    reason = "request_aborted";
                    _ = receive.ContinueWith(
                        static completed => { _ = completed.Exception; },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                else
                {
                    try
                    {
                        await receive;
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
                    catch (WebSocketException ex)
                    {
                        reason = "transport_abort";
                        transportFault = ex;
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
                }
            }

            try
            {
                directory.Disconnect(connection, reason);
                var lifetimeSeconds = (connection.Clock.GetUtcNow() - connection.StartedAtUtc).TotalSeconds;
                var recordedReason = connection.LastDisconnectReason ?? reason;
                string? wsError = null;
                string? socketError = null;
                if (transportFault is not null)
                    (wsError, socketError) = PhoneHomeTransportFault.Describe(transportFault);
                LogEnded(
                    logger, connection, recordedReason, lifetimeSeconds, connectionId, wsError, socketError);
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
    /// CARD-0679 D-1 / CARD-0716 D-6: one ended warning. The state is an explicit list because the
    /// framework formatter drops a named hole once the line carries the connection id and both
    /// transport codes, and the tests read those names off the structured state.
    /// </summary>
    private static void LogEnded(
        ILogger logger,
        PhoneHomeLiveConnection connection,
        string reason,
        double lifetimeSeconds,
        string? connectionId,
        string? wsError,
        string? socketError)
    {
        var message =
            $"Phone-home connection {connection.RunnerId} epoch {connection.Epoch} ended: {reason} after {lifetimeSeconds:0.0}s; "
            + $"socket {connection.SocketState}; pending {connection.PendingEvents} events / {connection.PendingEventBytes} bytes; "
            + $"live buffer {connection.LiveBufferEvents} events; in flight {connection.InFlight}; failing {connection.PendingWaiters} waiters; "
            + $"connection {connectionId}";
        if (wsError is not null)
            message += $" wsError {wsError} socketError {socketError}";

        var state = new List<KeyValuePair<string, object?>>
        {
            new("RunnerId", connection.RunnerId),
            new("Epoch", connection.Epoch),
            new("Reason", reason),
            new("LifetimeSeconds", lifetimeSeconds),
            new("SocketState", connection.SocketState),
            new("PendingEvents", connection.PendingEvents),
            new("PendingEventBytes", connection.PendingEventBytes),
            new("LiveBufferEvents", connection.LiveBufferEvents),
            new("InFlight", connection.InFlight),
            new("Waiters", connection.PendingWaiters),
            new("ConnectionId", connectionId),
        };
        if (wsError is not null)
        {
            state.Add(new("WsError", wsError));
            state.Add(new("SocketError", socketError));
        }

        state.Add(new("{OriginalFormat}", message));
        logger.Log(LogLevel.Warning, new EventId(0), state, exception: null, static (values, _) =>
            values.LastOrDefault(pair => pair.Key == "{OriginalFormat}").Value as string ?? "");
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
