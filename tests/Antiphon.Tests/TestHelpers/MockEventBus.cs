using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// In-memory mock IEventBus that captures published events for test assertions.
/// </summary>
public class MockEventBus : IEventBus
{
    private readonly object _gate = new();
    private readonly List<PublishedEvent> _events = [];

    public IReadOnlyList<PublishedEvent> PublishedEvents
    {
        get
        {
            lock (_gate)
                return _events.ToList();
        }
    }

    public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
    {
        lock (_gate)
            _events.Add(new PublishedEvent(group, eventName, payload));
        return Task.CompletedTask;
    }

    public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
    {
        lock (_gate)
            _events.Add(new PublishedEvent(null, eventName, payload));
        return Task.CompletedTask;
    }

    public void Clear()
    {
        lock (_gate)
            _events.Clear();
    }

    public record PublishedEvent(string? Group, string EventName, object Payload);
}
