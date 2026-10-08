namespace Antiphon.Server.Application.Services;

/// <summary>
/// The facts of one unresolved boot episode (CARD-1151 D-1/A-3): a transcript-confirmed,
/// accepted prompt on this task's launch clock with no model row since. Carried on
/// <see cref="TaskDeadlinePolicy.Verdict.Boot"/> independent of which clock won, so a ceiling or
/// general model-wait breach can never strip the boot identity off the task it judges.
/// </summary>
/// <param name="SessionStartedAt">
/// The session row's accepted generation (<c>AgentSession.StartedAt</c>); null when the row was
/// not found. Part of the episode key, never a clock.
/// </param>
/// <param name="LaunchClock"><c>max(DispatchedAt, LaunchResumedAt)</c>, the boot predicate's lower bound.</param>
/// <param name="PromptSequence">The latest accepted prompt's sequence (R1: never a queued-only row).</param>
/// <param name="PromptAt">That prompt's own timestamp. Every due time is derived from it.</param>
/// <param name="BootDueAt">
/// <c>PromptAt + BootModelWaitDeadlineMinutes</c>; null when the boot notification is disabled.
/// Disabling it removes the eight-minute event, never the protection.
/// </param>
/// <param name="OperatorDueAt">
/// <c>PromptAt + max(positive boot wait, operator wait)</c>: when the episode asks the operator to
/// decide. The operator wait is <c>ModelWaitDeadlineMinutes</c>, or 20 minutes when that is <c>&lt;= 0</c>.
/// </param>
internal sealed record BootStallFacts(
    DateTime? SessionStartedAt,
    DateTime LaunchClock,
    long PromptSequence,
    DateTime PromptAt,
    int PromptCount,
    DateTime? BootDueAt,
    DateTime OperatorDueAt);

/// <summary>
/// CARD-1151 (decision Q-1 option B). A boot prompt the model never answered is DETECTION and
/// nothing else: there is no automatic failure, retry, stop, release, alias hold or incident on
/// this path. CARD-0079 remains the only automatic stop of a Working session.
///
/// <para><b>Disposition</b> has exactly two values. <see cref="Disposition.NotBoot"/> needs the
/// positive answer of the boot predicate (a model row since the launch clock, or no accepted
/// prompt at all; a queued-only prompt is not one, R1) and hands the task back to the ordinary,
/// non-destructive deadline policy.
/// <see cref="Disposition.DetectOnly"/> is every other case, and it returns from the overdue
/// sweep before any failure. There is deliberately no third value: a dead boot session is the
/// dead-session reconciler's (A-1), and a human Retry is the only retry.</para>
///
/// <para><b>Emission</b> is a whitelist of positive conditions, read in this order: session row
/// loaded and not terminal (A-1), the task's own brief row positively not Pending (A-2), no
/// unresolved API-error recovery, no pending commit-recovery obligation, the task identity still
/// that of the episode, and a stage due by the facts' own clock. Unknown is never admitted; a
/// refused emission is still DetectOnly. Emission is telemetry and can never change the
/// disposition.</para>
/// </summary>
internal static class BootStallPolicy
{
    /// <summary>
    /// The operator wait when the general model-wait clock is disarmed. It replaces only that
    /// operand: the threshold is still <c>max(positive boot wait, 20 minutes)</c> (<see cref="Facts"/>).
    /// </summary>
    internal const int DefaultOperatorMinutes = 20;

    internal const string DetectedToken = "BootStallDetected";
    internal const string NeedsOperatorToken = "BootStallNeedsOperator";

    internal enum Disposition
    {
        /// <summary>A model row answered the boot prompt (or no accepted prompt exists, R1): ordinary policy.</summary>
        NotBoot = 0,

        /// <summary>Unresolved boot episode: detection only, never a failure.</summary>
        DetectOnly = 1,
    }

    internal enum Stage
    {
        None = 0,
        Detected = 1,
        NeedsOperator = 2,
    }

    /// <summary>
    /// What the sweep knows when it decides. Every nullable field is "unknown when null", and
    /// unknown never admits an emission.
    /// </summary>
    /// <param name="SessionTerminal">
    /// <see cref="AgentTaskLiveness.IsDeadSession"/> over the loaded row; null when the row could
    /// not be loaded.
    /// </param>
    /// <param name="BriefPending">The task's own first delegation brief row is still Pending.</param>
    /// <param name="IdentityMatches">The task is still open on the attempt/session/dispatch of the episode.</param>
    internal sealed record Observation(
        BootStallFacts? Boot,
        bool? SessionTerminal,
        bool? BriefPending,
        bool? ApiRecoveryUnresolved,
        bool? CommitRecoveryPending,
        bool? IdentityMatches,
        DateTime Now);

    internal sealed record Decision(Disposition Disposition, Stage Stage, string Reason);

    internal static Decision Decide(Observation o)
    {
        if (o.Boot is not { } boot)
            return new(Disposition.NotBoot, Stage.None, "model-reply-or-no-prompt");

        // From here on the disposition is fixed. Everything below only decides whether the
        // detection is written down.
        if (o.SessionTerminal is not false)
            return Silent(o.SessionTerminal is null ? "session-row-missing" : "session-terminal-reconciler-owned");
        if (o.BriefPending is not false)
            return Silent(o.BriefPending is null ? "brief-state-unknown" : "brief-pending-watchdog-owned");
        if (o.ApiRecoveryUnresolved is not false)
            return Silent(o.ApiRecoveryUnresolved is null ? "api-recovery-unknown" : "api-recovery-unresolved");
        if (o.CommitRecoveryPending is not false)
            return Silent(o.CommitRecoveryPending is null ? "commit-recovery-unknown" : "commit-recovery-pending");
        if (o.IdentityMatches is not true)
            return Silent("identity-changed");

        var stage = DueStage(boot, o.Now);
        return stage == Stage.None
            ? Silent("stage-not-due")
            : new(Disposition.DetectOnly, stage, stage == Stage.NeedsOperator ? NeedsOperatorToken : DetectedToken);
    }

    /// <summary>
    /// The highest stage the facts make due at <paramref name="now"/>. Derived from the prompt's
    /// own timestamp every time and never stored, so a restart or a clock step cannot reset it.
    /// </summary>
    internal static Stage DueStage(BootStallFacts boot, DateTime now)
    {
        if (now >= boot.OperatorDueAt)
            return Stage.NeedsOperator;
        return boot.BootDueAt is DateTime due && now >= due ? Stage.Detected : Stage.None;
    }

    /// <summary>The operator wait: the general model-wait clock, or 20 minutes when it is disarmed.</summary>
    internal static TimeSpan OperatorWait(int modelWaitMinutes) =>
        TimeSpan.FromMinutes(modelWaitMinutes > 0 ? modelWaitMinutes : DefaultOperatorMinutes);

    /// <summary>
    /// The task's boot facts, anchored on the latest ACCEPTED prompt (CARD-1151 R1). Null unless
    /// the turn carries one: a queued-only prompt was never received, so it opens no episode and
    /// earns no protection, and the task keeps the ordinary deadline policy exactly as before.
    /// A later queued row never advances an episode; only a later accepted prompt does.
    /// </summary>
    internal static BootStallFacts? Facts(
        DateTime? sessionStartedAt,
        DateTime launchClock,
        BootReplyWatch.BootTurn turn,
        int bootWaitMinutes,
        int modelWaitMinutes)
    {
        if (turn.AcceptedSequence is not long sequence || turn.AcceptedAt is not DateTime promptAt)
            return null;

        var bootWait = bootWaitMinutes > 0 ? TimeSpan.FromMinutes(bootWaitMinutes) : TimeSpan.Zero;
        var operatorWait = OperatorWait(modelWaitMinutes);
        if (bootWait > operatorWait)
            operatorWait = bootWait;
        return new(
            sessionStartedAt, launchClock, sequence, promptAt, turn.AcceptedCount,
            bootWait > TimeSpan.Zero ? promptAt + bootWait : null,
            promptAt + operatorWait);
    }

    /// <summary>
    /// The episode key (A-3): task, attempt, session, accepted generation, launch clock and the
    /// latest real prompt's sequence. No wall-clock reading, so a clock step cannot mint a key.
    /// </summary>
    internal static string EpisodeKey(Guid taskId, int attempt, Guid sessionId, BootStallFacts boot) =>
        $"{taskId:N}/{attempt}/{sessionId:N}/{boot.SessionStartedAt?.Ticks ?? 0}/"
        + $"{boot.LaunchClock.Ticks}/{boot.PromptSequence}";

    /// <summary>
    /// The Warning event Detail (A-4): a stable token, the key and the due times. Never the prompt
    /// text, composer text or anything else a session typed.
    /// </summary>
    internal static string Detail(Stage stage, string episodeKey, BootStallFacts boot) =>
        $"{Token(stage)} episode={episodeKey}; promptAt={boot.PromptAt:o}; "
        + $"bootDueAt={(boot.BootDueAt is DateTime due ? due.ToString("o") : "disabled")}; "
        + $"operatorDueAt={boot.OperatorDueAt:o}. "
        + (stage == Stage.NeedsOperator
            ? "The boot prompt is still unanswered past the operator threshold: decide whether to "
              + "keep waiting, reply, or explicitly cancel or retry the task. "
            : "The boot prompt has had no model reply yet. ")
        + "Detection only: the session was not stopped, failed, retried or released.";

    internal static string Token(Stage stage) => stage switch
    {
        Stage.Detected => DetectedToken,
        Stage.NeedsOperator => NeedsOperatorToken,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "no event for this stage"),
    };

    /// <summary>
    /// Which stages are already recorded for <paramref name="episodeKey"/> among a task's
    /// boot Warning details. A recorded higher stage covers the lower one, so a clock stepping
    /// back after escalation cannot add a Detected event.
    /// </summary>
    internal static bool IsRecorded(IEnumerable<string> details, string episodeKey, Stage stage)
    {
        var needle = $" episode={episodeKey};";
        foreach (var detail in details)
        {
            if (!detail.Contains(needle, StringComparison.Ordinal))
                continue;
            if (detail.StartsWith(NeedsOperatorToken + " ", StringComparison.Ordinal))
                return true;
            if (stage == Stage.Detected && detail.StartsWith(DetectedToken + " ", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static Decision Silent(string reason) => new(Disposition.DetectOnly, Stage.None, reason);
}
