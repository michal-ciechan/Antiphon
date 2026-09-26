using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Tests.TestHelpers;

internal sealed class RecordingHostStatsEventBus : IEventBus
{
    public List<(string Group, string EventName, object Payload)> Events { get; } = [];
    public bool ThrowOnce { get; set; }
    public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
    {
        if (ThrowOnce)
        {
            ThrowOnce = false;
            throw new IOException("test bus fault");
        }
        Events.Add((group, eventName, payload));
        return Task.CompletedTask;
    }
    public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) =>
        throw new InvalidOperationException("Host stats must publish to the hosts group.");
}
