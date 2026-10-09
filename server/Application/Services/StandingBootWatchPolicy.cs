using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The facts of one unresolved taskless AlwaysOn boot episode (CARD-1156 D-4): the session's
/// accepted generation, its launch clock and the latest real prompt on it, with the two due times
/// derived from that prompt's own timestamp.
/// </summary>
/// <param name="Generation"><c>SessionGeneration.Normalize(AgentSession.StartedAt)</c>.</param>
/// <param name="LaunchClock"><c>BootReplyWatch.LaunchClock</c>: <c>max(StartedAt, LaunchResumedAt)</c>.</param>
/// <param name="PromptSequence">The latest real prompt's transcript sequence.</param>
/// <param name="PromptAt">That prompt's timestamp. Every due time is derived from it.</param>
/// <param name="BootDueAt"><c>PromptAt + BootModelWaitDeadlineMinutes</c> (8 by default).</param>
/// <param name="OperatorDueAt">
/// <c>PromptAt + max(boot wait, ModelWaitDeadlineMinutes, or 20 minutes when that is &lt;= 0)</c>.
/// </param>
internal sealed record StandingBootFacts(
    DateTime Generation,
    DateTime LaunchClock,
    long PromptSequence,
    DateTime PromptAt,
    DateTime BootDueAt,
    DateTime OperatorDueAt);

/// <summary>
/// CARD-1156 (operator decision: option A, detection only; D-2 "unknown keeps the session"). A
/// taskless AlwaysOn session whose boot prompt the model never answered is DETECTION and nothing
/// else: a Warning receipt at the boot due and an Error receipt at the operator due. There is no
/// stop, restart, failure count, latch, probe, input or requeue on this path, for any evidence:
/// CARD-0079 remains the only automatic stop of a Working session.
///
/// <para><b>Emission</b> is a whitelist of positive conditions (D-3). Every nullable
/// <see cref="StandingBootWatchObservation"/> field is unknown when null, and unknown is never
/// admitted. A refused emission is not a recovery of any kind: <see cref="Disposition"/> has the
/// single value <see cref="Disposition.Observe"/>, and nothing here can select anything else.
/// Working is deliberately not an input: true, false and unavailable all have the same outcome.</para>
/// </summary>
internal static class StandingBootWatchPolicy
{
    /// <summary>The operator wait when the general model-wait clock is disarmed (the CARD-1151 rule).</summary>
    internal const int DefaultOperatorMinutes = BootStallPolicy.DefaultOperatorMinutes;

    /// <summary>The versioned receipt prefix. Old <c>bootSeq=</c> receipts keep their own parser.</summary>
    internal const string KeyPrefix = "standingBoot:v1;";

    internal const string DetectedStage = "detected";
    internal const string OperatorStage = "operator";

    /// <summary>The only disposition this policy has. Deliberately a single value.</summary>
    internal enum Disposition
    {
        Observe = 0,
    }

    internal enum Stage
    {
        None = 0,
        Detected = 1,
        NeedsOperator = 2,
    }

    internal sealed record Decision(Disposition Disposition, Stage Stage, string Reason, StandingBootFacts? Facts);

    internal static Decision Decide(StandingBootWatchObservation o)
    {
        if (o.BootWaitMinutes <= 0)
            return Silent("boot-wait-disabled");
        if (o.ProviderVerified is not true)
            return Silent(o.ProviderVerified is null ? "provider-unknown" : "provider-unverified");
        if (o.GrokRules is not (GrokRulesState.None or GrokRulesState.Ready))
            return Silent(o.GrokRules is null ? "grok-rules-unknown" : "grok-rules-not-ready");
        if (o.SessionLive is not true)
            return Silent(o.SessionLive is null ? "session-missing" : "session-terminal");
        if (o.EndedAtNull is not true)
            return Silent(o.EndedAtNull is null ? "ended-at-unknown" : "session-ended");
        if (o.Generation is not DateTime generation || o.LaunchClock is not DateTime launchClock)
            return Silent("generation-unknown");
        if (o.OwnerCount is not int owners)
            return Silent("owner-unknown");
        if (owners == 0)
            return Silent("owner-missing");
        if (owners > 1)
            return Silent("owner-ambiguous");
        if (o.OwnerAlwaysOn is not true)
            return Silent(o.OwnerAlwaysOn is null ? "owner-unknown" : "owner-not-alwayson");
        if (o.StandingAgentConflict is not false)
            return Silent(o.StandingAgentConflict is null ? "owner-unknown" : "owner-conflict");
        if (o.TaskOwner is not StandingBootTaskOwner taskOwner)
            return Silent("task-unknown");
        if (taskOwner != StandingBootTaskOwner.None)
            return Silent("task-owned");
        if (o.PromptSequence is not long sequence || o.PromptAt is not DateTime promptAt)
            return Silent("prompt-missing");
        if (o.ReplyObserved is not false)
            return Silent(o.ReplyObserved is null ? "reply-unknown" : "reply-observed");
        if (o.IdentityMatches is not true)
            return Silent("identity-changed");

        var facts = Facts(generation, launchClock, sequence, promptAt, o.BootWaitMinutes, o.ModelWaitMinutes);
        var stage = DueStage(facts, o.Now);
        return stage == Stage.None
            ? new(Disposition.Observe, Stage.None, "stage-not-due", facts)
            : new(Disposition.Observe, stage, StageName(stage), facts);
    }

    /// <summary>
    /// The episode facts, every due time from <paramref name="promptAt"/>. The operator due is
    /// <c>max(boot wait, operator wait)</c>, so it can never precede the boot notice.
    /// </summary>
    internal static StandingBootFacts Facts(
        DateTime generation, DateTime launchClock, long promptSequence, DateTime promptAt,
        int bootWaitMinutes, int modelWaitMinutes)
    {
        var bootWait = TimeSpan.FromMinutes(Math.Max(bootWaitMinutes, 0));
        var operatorWait = BootStallPolicy.OperatorWait(modelWaitMinutes);
        if (bootWait > operatorWait)
            operatorWait = bootWait;
        return new(generation, launchClock, promptSequence, promptAt, promptAt + bootWait, promptAt + operatorWait);
    }

    /// <summary>
    /// The highest stage due at <paramref name="now"/>. Equality is due. Only one stage is ever
    /// decided, so a first observation past the operator due is NeedsOperator alone.
    /// </summary>
    internal static Stage DueStage(StandingBootFacts facts, DateTime now)
    {
        if (now >= facts.OperatorDueAt)
            return Stage.NeedsOperator;
        return now >= facts.BootDueAt ? Stage.Detected : Stage.None;
    }

    /// <summary>
    /// The episode identity, without the stage: <c>standingBoot:v1;g=&lt;generation ticks&gt;;l=&lt;launch
    /// clock ticks&gt;;p=&lt;prompt sequence&gt;;</c>. No wall-clock reading, so a clock step cannot mint one.
    /// </summary>
    internal static string EpisodePrefix(DateTime generation, DateTime launchClock, long promptSequence) =>
        $"{KeyPrefix}g={generation.Ticks};l={launchClock.Ticks};p={promptSequence};";

    internal static string EpisodePrefix(StandingBootFacts facts) =>
        EpisodePrefix(facts.Generation, facts.LaunchClock, facts.PromptSequence);

    /// <summary>The receipt key: the episode prefix plus <c>stage=detected</c> or <c>stage=operator</c>.</summary>
    internal static string Key(string episodePrefix, Stage stage) => $"{episodePrefix}stage={StageName(stage)}";

    internal static string StageName(Stage stage) => stage switch
    {
        Stage.Detected => DetectedStage,
        Stage.NeedsOperator => OperatorStage,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "no receipt for this stage"),
    };

    /// <summary>
    /// Is <paramref name="stage"/> already on record for the episode? A recorded operator stage
    /// covers a later Detected decision, so a clock stepping back cannot add a lower receipt.
    /// </summary>
    internal static bool IsRecorded(IEnumerable<string?> failureReasons, string episodePrefix, Stage stage)
    {
        var operatorKey = Key(episodePrefix, Stage.NeedsOperator);
        var detectedKey = Key(episodePrefix, Stage.Detected);
        foreach (var reason in failureReasons)
        {
            if (string.Equals(reason, operatorKey, StringComparison.Ordinal))
                return true;
            if (stage == Stage.Detected && string.Equals(reason, detectedKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The receipt message (D-4). Identifiers, prompt kind, clocks and the policy only: never the
    /// prompt, composer or any runner text. A queued prompt record is labelled as queued; it is
    /// never described as accepted or delivered.
    /// </summary>
    internal static string Message(StandingBootFacts facts, string promptKind, DateTime now)
    {
        var age = now - facts.PromptAt;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;
        var opening = promptKind == TranscriptKinds.QueuedUserPrompt
            ? $"Boot queued prompt record at sequence {facts.PromptSequence}; no reply observed"
            : $"Boot prompt at sequence {facts.PromptSequence} ({promptKind})";
        return opening
            + $"; no qualifying model reply in {Describe(age)}"
            + $"; boot notice due {facts.BootDueAt:u}; operator decision due {facts.OperatorDueAt:u}. "
            + "Detection only: the session keeps its seat; nothing was stopped, restarted, typed or latched.";
    }

    internal static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes}m{span.Seconds:00}s"
            : $"{(int)span.TotalSeconds}s";

    private static Decision Silent(string reason) => new(Disposition.Observe, Stage.None, reason, null);
}
