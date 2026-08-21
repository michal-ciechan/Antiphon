namespace Antiphon.Agents.Pty;

/// <param name="PreSubmitPause">The discrete body-to-Enter gap, measured after composer evidence.</param>
/// <param name="EvidenceTimeout">The bound before a non-echoing child falls back to the old submit behaviour.</param>
/// <param name="PollInterval">The rendered-screen polling cadence while waiting for body-consumed evidence.</param>
public sealed record SendLineGateOptions(
    TimeSpan PreSubmitPause,
    TimeSpan EvidenceTimeout,
    TimeSpan PollInterval);

/// <summary>How a two-write line submit reached its one and only submitting CR.</summary>
public enum SendLineGateOutcome
{
    EvidenceSeen,
    TimedOutProceeded,
    EmptyBody,
}

/// <summary>
/// Sends a line as the required two writes, waiting for evidence that the body's tail was consumed
/// before starting the discrete pre-Enter pause. The bounded fallback deliberately retains
/// <see cref="PtyAgentRunner.SendLineAsync"/>'s best-effort behaviour for non-echoing children;
/// this primitive owns no submit retry because it has no submission oracle.
///
/// Delegate-based so the in-process PTY runner and the session-runner transport can share the
/// same evidence and timing semantics without duplicating the matching rules.
/// </summary>
public static class EchoGatedLineSender
{
    public static readonly SendLineGateOptions DefaultOptions = new(
        PreSubmitPause: TimeSpan.FromMilliseconds(20),
        EvidenceTimeout: TimeSpan.FromSeconds(2),
        PollInterval: TimeSpan.FromMilliseconds(25));

    /// <param name="body">The raw line body; it is encoded through <see cref="PtyInputEncoding"/> internally.</param>
    /// <param name="snapshotScreen">Returns the current rendered screen.</param>
    /// <param name="write">Writes raw terminal input without appending a CR.</param>
    public static async Task<SendLineGateOutcome> SendAsync(
        string body,
        Func<CancellationToken, Task<string>> snapshotScreen,
        Func<string, CancellationToken, Task> write,
        SendLineGateOptions options,
        CancellationToken ct)
    {
        var screenBefore = await snapshotScreen(ct);
        var normalizedBody = PtyInputEncoding.NormalizeBody(body);
        await write(PtyInputEncoding.WrapIfMultiline(normalizedBody), ct);

        SendLineGateOutcome outcome;
        if (normalizedBody.Length == 0)
        {
            outcome = SendLineGateOutcome.EmptyBody;
        }
        else
        {
            var deadline = DateTime.UtcNow + options.EvidenceTimeout;
            while (true)
            {
                var screenAfter = await snapshotScreen(ct);
                if (ComposerDeliveryEvidence.BodyConsumed(screenBefore, screenAfter, normalizedBody))
                {
                    outcome = SendLineGateOutcome.EvidenceSeen;
                    break;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    outcome = SendLineGateOutcome.TimedOutProceeded;
                    break;
                }

                await Task.Delay(options.PollInterval, ct);
            }
        }

        await Task.Delay(options.PreSubmitPause, ct);
        await write("\r", ct);
        return outcome;
    }
}
