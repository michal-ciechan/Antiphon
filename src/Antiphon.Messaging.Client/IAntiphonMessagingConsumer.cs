namespace Antiphon.Messaging.Client;

/// <summary>Streams inbound <see cref="ChannelMessage"/>s from the bridge's inbound topic. Enumerate it from a
/// background service; the stream ends when the token is cancelled.</summary>
public interface IAntiphonMessagingConsumer
{
    IAsyncEnumerable<ChannelMessage> ConsumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Records requiring an explicit durable disposition before broker progress.</summary>
    async IAsyncEnumerable<InboundDelivery> ConsumeDeliveriesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in ConsumeAsync(cancellationToken))
            yield return new InboundDelivery(message, null, (_, _) => Task.CompletedTask);
    }
}

/// <summary>A broker record. A malformed record has no Message and requires an explicit disposition.</summary>
public sealed class InboundDelivery(
    ChannelMessage? message,
    string? diagnostic,
    Func<string, CancellationToken, Task> acknowledge)
{
    public ChannelMessage? Message { get; } = message;
    public string? Diagnostic { get; } = diagnostic;

    /// <summary>Call only after the envelope or a deliberate discard has committed.</summary>
    public Task AcknowledgeAsync(string disposition, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(disposition))
            throw new ArgumentException("An explicit disposition is required.", nameof(disposition));
        return acknowledge(disposition, cancellationToken);
    }
}
