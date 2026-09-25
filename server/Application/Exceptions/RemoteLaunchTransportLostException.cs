using System.Globalization;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0679 D-8: a remote launch lost its phone-home connection inside the Start-to-ready segment
/// and its bounded retries ran out, or the runner did not come back within one retry's wait. The
/// message is what the launch persists as <c>FailureReason</c>: the runner, the phase of the last
/// loss (<c>pre-ack</c> or <c>post-ack</c>), the attempts and the time spent waiting for the runner.
/// </summary>
public sealed class RemoteLaunchTransportLostException : Exception
{
    public const string PreAck = "pre-ack";
    public const string PostAck = "post-ack";

    public RemoteLaunchTransportLostException(
        string runnerId, string phase, int attempts, int retries, TimeSpan waited, int? waitLimitSeconds,
        Exception lastLoss)
        : base(Describe(runnerId, phase, attempts, retries, waited, waitLimitSeconds, lastLoss), lastLoss)
    {
        RunnerId = runnerId;
        Phase = phase;
        Attempts = attempts;
        Waited = waited;
    }

    public string RunnerId { get; }
    public string Phase { get; }
    public int Attempts { get; }
    public TimeSpan Waited { get; }

    // AgentSession.FailureReason is 2000 characters; the fixed part above stays well inside that.
    private static string Truncate(string text) => text.Length <= 600 ? text : text[..600];

    private static string Describe(
        string runnerId, string phase, int attempts, int retries, TimeSpan waited, int? waitLimitSeconds,
        Exception lastLoss)
    {
        var allowed = retries == 1 ? "1 retry allowed" : $"{retries} retries allowed";
        var end = waitLimitSeconds is { } limit
            ? $"the runner did not reconnect within {limit} s"
            : "no retry was left";
        return string.Create(CultureInfo.InvariantCulture,
            $"Remote launch on runner '{runnerId}' lost its phone-home connection {phase} on attempt {attempts} "
            + $"({allowed}); {end}; waited {waited.TotalSeconds:0.#} s for the runner in total. "
            + $"Last transport error: {Truncate(lastLoss.Message)}");
    }
}
