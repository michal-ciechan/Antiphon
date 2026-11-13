using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>A missing Input reply is not evidence that the process failed to launch.</summary>
public sealed class InputDeliveryUncertainException(Guid sessionId) : Exception(
    "Input delivery is unknown; awaiting transcript evidence.")
{
    public Guid SessionId { get; } = sessionId;

    // Use at a delivery boundary, after the process has passed readiness. Definite
    // pre-send refusals and confirmed runner/provider errors keep their existing paths.
    public static bool Is(Exception exception) =>
        exception is InputDeliveryUncertainException or OperationCanceledException
            or TimeoutException or HttpRequestException or IOException
        || exception is PhoneHomeTransportException
        {
            Code: PhoneHomeProblemTypes.RequestTimeout or PhoneHomeProblemTypes.ConnectionClosedInFlight,
        };
}
