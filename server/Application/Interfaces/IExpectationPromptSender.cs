namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0650 S4. How one direct watchdog send ended. Only <see cref="Confirmed"/> is a receipt;
/// the others are typed facts for the watchdog ledger, never a reason to kill, restart or retype.
/// </summary>
public enum ExpectationSendOutcome
{
    /// <summary>A complete submitted prompt past the committed floor, in the destination session.</summary>
    Confirmed = 0,

    /// <summary>The attempt finished without a receipt. The body may still stand in the composer.</summary>
    Unconfirmed = 1,

    /// <summary>The transport failed mid-attempt, or the attempt was interrupted. Never resent.</summary>
    Uncertain = 2,

    /// <summary>Known pre-input refusal. No byte reached the terminal.</summary>
    Refused = 3,
}

/// <summary>The frozen destination, committed under the session lock before any byte.</summary>
public sealed record ExpectationSendAttempt(Guid SessionId, DateTime Generation, long? BaselineSequence);

public sealed record ExpectationSendResult(
    ExpectationSendOutcome Outcome,
    string Reason,
    bool AttemptCommitted,
    DateTime? ReceiptAt = null)
{
    public static ExpectationSendResult Refuse(string reason) => new(ExpectationSendOutcome.Refused, reason, false);
}

/// <summary>
/// Narrow direct Now send for the expectation watchdog. It bypasses the WhenIdle policy and the
/// repository lease, never composer safety, rules admission, modal gates or session ownership.
/// It has no delivery-failure recovery: no kill, restart, compact, Escape or retype.
/// </summary>
public interface IExpectationPromptSender
{
    /// <summary>
    /// Types <paramref name="body"/> only when <paramref name="sessionId"/> is still the owner's
    /// current session at <paramref name="expectedGeneration"/>. <paramref name="commitAttempt"/>
    /// runs under the session lock after every preflight and before any byte; false means another
    /// claimant won and nothing is typed.
    /// </summary>
    Task<ExpectationSendResult> SendAsync(
        Guid sessionId,
        DateTime expectedGeneration,
        Guid ownerAgentId,
        string body,
        Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
        CancellationToken ct);
}
