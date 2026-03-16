using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace Antiphon.Server.Infrastructure.Realtime;

/// <summary>
/// Single SignalR hub for all real-time communication.
/// Clients join/leave groups to receive targeted events.
/// Services NEVER call this hub directly — they use IEventBus.
/// </summary>
public class AntiphonHub : Hub
{
    private static readonly Serilog.ILogger _logger = Log.ForContext<AntiphonHub>();

    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.Information("Connection {ConnectionId} joined group {Group}", Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.Information("Connection {ConnectionId} left group {Group}", Context.ConnectionId, groupName);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.Information("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.Information("Client disconnected: {ConnectionId}, Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
