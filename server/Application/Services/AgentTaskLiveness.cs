using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The ONE definition of "this open task's session is dead" (CARD-0021), and the one sentence that
/// says which way it died.
///
/// <para>It exists as a shared function rather than as two copies because this repo already carries
/// three lockstep implementations of the working/idle rule and every drift between them has cost a
/// real incident. Here the two consumers are
/// <see cref="AttentionService"/>'s <c>DeadSession</c> condition — which SURFACES the state — and
/// <c>AgentTaskDispatcher.FailDeadSessionTasksAsync</c> — which ACTS on it. A projection that showed
/// a row the sweep would not fail, or a sweep that failed a task the projection never showed, would
/// be a defect with no single place to fix it. <c>AgentTaskDeadSessionReconciliationTests</c> pins
/// them to the same table of verdicts.</para>
///
/// <para>Deliberately pure: no DB, no runner, no clock. The runner-answer gate and the grace window
/// that guard the ACTION (CARD-0056) live in the sweep, because they are about whether it is safe to
/// act on this verdict — not about what the verdict is.</para>
/// </summary>
public static class AgentTaskLiveness
{
    /// <summary>
    /// The five fields of a session row the verdict reads. A <c>null</c> snapshot means the row is
    /// GONE — that is the only way "row missing" is spelled, so no caller can disagree with another
    /// about what a present-but-empty snapshot would mean.
    /// </summary>
    public readonly record struct SessionSnapshot(
        SessionStatus Status,
        DateTime? EndedAt,
        string? FailureReason,
        SessionTerminationSource TerminationSource = SessionTerminationSource.Unknown,
        int? ExitCode = null,
        SessionLaunchBlock? LaunchBlock = null);

    /// <summary>
    /// The reason written onto a task the dead-session sweep fails, plus the durable failure
    /// code when the evidence supports one (CARD-0256).
    /// </summary>
    public readonly record struct DeadSessionFailure(
        string Reason, AgentTaskFailureCode? FailureCode);

    /// <summary>
    /// Is the session behind an open (Dispatched/Working) task dead — i.e. is it certain that no
    /// report is coming from it?
    ///
    /// <para>Four ways, all of them positive facts about a row rather than inferences from silence:
    /// the task names no session at all (a dispatch writes <c>AgentSessionId</c> in the same save as
    /// its status, so a null one is as broken as a Stopped row); the session row is gone; the status
    /// is <see cref="SessionStatus.Stopped"/> or <see cref="SessionStatus.Failed"/>; or
    /// <c>EndedAt</c> is set while the status still says otherwise.</para>
    ///
    /// <para><b>Stopped counts.</b> Settlement is not coming from a terminal session, and leaving
    /// the task open forever is the 2026-08-09 zombie shape this card is about. Stopped is <i>not</i>
    /// evidence that an operator ended it — a clean process exit lands the same status. Name an
    /// operator only when <see cref="SessionSnapshot.TerminationSource"/> is
    /// <see cref="SessionTerminationSource.OperatorRequest"/>.</para>
    /// </summary>
    public static bool IsDeadSession(Guid? agentSessionId, SessionSnapshot? session) =>
        agentSessionId is null
        || session is not { } s
        || s.Status is SessionStatus.Stopped or SessionStatus.Failed
        || s.EndedAt is not null;

    /// <summary>
    /// Which of the four ways it died, as a clause that reads inside a sentence ("Still Dispatched
    /// but <i>its session is Failed</i>."). Shared for the same reason the predicate is: the
    /// attention row and the task's own <c>FailureReason</c> describing the same session differently
    /// is how an operator ends up believing they are two problems.
    ///
    /// <para>Only meaningful when <see cref="IsDeadSession"/> is true; on a live session it falls
    /// through to naming the status, which is honest but says nothing.</para>
    /// </summary>
    public static string Describe(Guid? agentSessionId, SessionSnapshot? session) =>
        agentSessionId is null
            ? "the task has no session at all"
            : session is not { } s
                ? "its session row is gone"
                : s.EndedAt is not null && s.Status is not (SessionStatus.Stopped or SessionStatus.Failed)
                    ? $"its session ended at {s.EndedAt:u} while still marked {s.Status}"
                    : $"its session is {s.Status}";

    /// <summary>
    /// Evidence-based failure text (and optional code) for a dead session the sweep is about to
    /// fail. Bind-refusal recovery must already have been offered: this classifier does not guess
    /// a cause the row cannot prove.
    /// </summary>
    public static DeadSessionFailure ClassifyFailure(
        Guid? agentSessionId, SessionSnapshot? session, bool hasTranscriptEntries)
    {
        var what = Describe(agentSessionId, session);
        if (session?.LaunchBlock == SessionLaunchBlock.ProviderSignInRequired)
        {
            var named = session.Value.FailureReason is { Length: > 0 } text
                ? text
                : "ProviderSignInRequired";
            return new DeadSessionFailure(
                Format(what, named, agentSessionId),
                AgentTaskFailureCode.AuthenticationRequired);
        }

        if (session?.FailureReason is { Length: > 0 } existing)
            return new DeadSessionFailure(Format(what, existing, agentSessionId), null);

        if (session is { Status: SessionStatus.Stopped } stopped && !hasTranscriptEntries)
        {
            if (stopped.TerminationSource == SessionTerminationSource.OperatorRequest)
            {
                return new DeadSessionFailure(
                    Format(what, "stopped by an operator request before any prompt was recorded", agentSessionId),
                    null);
            }

            return new DeadSessionFailure(
                Format(
                    what,
                    "StoppedBeforeFirstPrompt: Antiphon observed no prompt before the session stopped"
                    + DescribeStop(stopped.TerminationSource, stopped.ExitCode),
                    agentSessionId),
                AgentTaskFailureCode.StoppedBeforeFirstPrompt);
        }

        if (session is { Status: SessionStatus.Stopped, TerminationSource: SessionTerminationSource.OperatorRequest })
        {
            return new DeadSessionFailure(
                Format(what, "stopped by an operator request before the task settled", agentSessionId),
                null);
        }

        var evidence = session?.Status == SessionStatus.Stopped
            ? "stopped before the task settled, with no failure reason recorded"
            : "no failure reason recorded";
        if (session is { Status: SessionStatus.Stopped } settled
            && settled.TerminationSource is SessionTerminationSource.SystemRequest
                or SessionTerminationSource.ProcessExit)
        {
            evidence += DescribeStop(settled.TerminationSource, settled.ExitCode);
        }

        return new DeadSessionFailure(Format(what, evidence, agentSessionId), null);
    }

    /// <summary>
    /// Source-specific clause after the empty-Stopped prefix, and the same clause appended to the
    /// non-empty Stopped fall-through for <see cref="SessionTerminationSource.SystemRequest"/> and
    /// <see cref="SessionTerminationSource.ProcessExit"/>. <see cref="SessionTerminationSource.Unknown"/>
    /// keeps "not recorded"; operator stops do not use this helper.
    /// </summary>
    private static string DescribeStop(SessionTerminationSource source, int? exitCode) => source switch
    {
        SessionTerminationSource.SystemRequest => "; Antiphon itself ended it (SystemRequest)",
        SessionTerminationSource.ProcessExit =>
            $"; the agent process exited on its own (ProcessExit, exit code {exitCode?.ToString() ?? "unknown"})",
        _ => ", and the stop origin was not recorded",
    };

    private static string Format(string what, string evidence, Guid? sessionId) =>
        $"Session died before the task settled: {what} ({evidence}). No report is coming"
        + (sessionId is Guid id ? $"; read session {id} before re-running this task." : ".");
}
