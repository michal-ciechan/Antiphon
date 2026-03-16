namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Abstraction for pushing real-time events to connected clients.
/// Services depend on this interface, never on the SignalR hub directly.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publish an event to a specific group (e.g., "workflow-{id}", "dashboard").
    /// </summary>
    Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default);

    /// <summary>
    /// Publish an event to all connected clients.
    /// </summary>
    Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default);
}
