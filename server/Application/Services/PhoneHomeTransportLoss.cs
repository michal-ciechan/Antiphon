using Antiphon.Server.Application.Exceptions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0679 D-5/D-7: the failures that mean "no phone-home connection could carry this request",
/// as opposed to a runner that answered. A timeout is deliberately not one of them: a request that
/// timed out may have reached the runner and been acted on.
/// </summary>
public static class PhoneHomeTransportLoss
{
    /// <summary>The dispatch gate's refusal on a connection that is not (or no longer) eligible.</summary>
    public const string NotDispatchEligibleMessage = "Phone-home connection is not dispatch-eligible.";

    public static bool Is(Exception ex) =>
        ex is PhoneHomeTransportException { Code: PhoneHomeProblemTypes.ConnectionClosed }
        || ex is ServiceUnavailableException { Code: PhoneHomeProblemTypes.Unavailable }
        || ex is InvalidOperationException { Message: NotDispatchEligibleMessage };
}
