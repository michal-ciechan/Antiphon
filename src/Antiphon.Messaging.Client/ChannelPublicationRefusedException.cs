namespace Antiphon.Messaging.Client;

/// <summary>Transport evidence that this broker did not accept the outbound record.</summary>
public sealed class ChannelPublicationRefusedException : Exception
{
    public string Code { get; }

    public ChannelPublicationRefusedException(string code, Exception innerException)
        : base($"Broker refused outbound record ({code}).", innerException)
    {
        Code = code;
    }
}
