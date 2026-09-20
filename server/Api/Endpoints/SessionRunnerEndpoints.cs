using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
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
}
