using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public static class HerdrSupervisionFailureEvidence
{
    public static HerdrSupervisionFailureKind? FromExitReason(AgentExitReason reason) => reason switch
    {
        AgentExitReason.HerdrPaneClosed => HerdrSupervisionFailureKind.PaneClosed,
        AgentExitReason.HerdrChildGone => HerdrSupervisionFailureKind.ChildGone,
        AgentExitReason.HerdrLaunchDetectTimeout => HerdrSupervisionFailureKind.DetectTimeout,
        AgentExitReason.Unknown => null,
        _ => HerdrSupervisionFailureKind.NonQualifying,
    };

    public static HerdrSupervisionFailureKind? FromLaunchFailure(Exception exception) => exception switch
    {
        AgentSessionService.ResumeTargetMissingException => null,
        ConflictException { Code: "detect_timeout" } => HerdrSupervisionFailureKind.DetectTimeout,
        _ => HerdrSupervisionFailureKind.NonQualifying,
    };

    public static void Record(AgentSession session, HerdrSupervisionFailureKind? evidence)
    {
        if (session.TerminationSource is SessionTerminationSource.OperatorRequest or SessionTerminationSource.PolicyRefresh
            || evidence is null)
            return;

        // A cleanup close must never erase the detect-timeout that caused it. Other proven
        // qualifying causes retain first-writer precedence over subsequent observations.
        if (session.HerdrSupervisionFailureKind is null or HerdrSupervisionFailureKind.NonQualifying
            || (evidence == HerdrSupervisionFailureKind.DetectTimeout
                && session.HerdrSupervisionFailureKind == HerdrSupervisionFailureKind.PaneClosed))
            session.HerdrSupervisionFailureKind = evidence;
    }
}
