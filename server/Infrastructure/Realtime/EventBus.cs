using Microsoft.AspNetCore.SignalR;
using Serilog;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Realtime;

/// <summary>
/// Routes events to SignalR groups via IHubContext.
/// This is the only class that touches SignalR directly — services use IEventBus.
/// </summary>
public class EventBus : IEventBus
{
    private readonly IHubContext<AntiphonHub> _hubContext;
    private readonly CardFileBoardLookup? _boardLookup;
    private static readonly Serilog.ILogger _logger = Log.ForContext<EventBus>();

    public EventBus(IHubContext<AntiphonHub> hubContext, CardFileBoardLookup? boardLookup = null)
    {
        _hubContext = hubContext;
        _boardLookup = boardLookup;
    }

    public async Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
    {
        if (eventName == "BoardChanged") _boardLookup?.Invalidate();
        _logger.Debug("Publishing {Event} to group {Group}", eventName, group);
        await _hubContext.Clients.Group(group).SendAsync(eventName, payload, ct);
    }

    public async Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
    {
        if (eventName == "BoardChanged") _boardLookup?.Invalidate();
        _logger.Debug("Publishing {Event} to all clients", eventName);
        await _hubContext.Clients.All.SendAsync(eventName, payload, ct);
    }
}
