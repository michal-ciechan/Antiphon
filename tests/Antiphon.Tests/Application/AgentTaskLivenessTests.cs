using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public class AgentTaskLivenessTests
{
    [Test]
    public void ClassifyFailure_empty_stopped_without_operator_source_is_StoppedBeforeFirstPrompt()
    {
        var sessionId = Guid.NewGuid();
        var snapshot = new AgentTaskLiveness.SessionSnapshot(
            SessionStatus.Stopped, DateTime.UtcNow, null, SessionTerminationSource.Unknown);

        var result = AgentTaskLiveness.ClassifyFailure(sessionId, snapshot, hasTranscriptEntries: false);

        result.FailureCode.ShouldBe(AgentTaskFailureCode.StoppedBeforeFirstPrompt);
        result.Reason.ShouldContain("StoppedBeforeFirstPrompt");
        result.Reason.ShouldNotContain("operator");
        result.Reason.ShouldContain(sessionId.ToString());
    }

    [Test]
    public void ClassifyFailure_empty_stopped_with_OperatorRequest_names_the_source()
    {
        var snapshot = new AgentTaskLiveness.SessionSnapshot(
            SessionStatus.Stopped, DateTime.UtcNow, null, SessionTerminationSource.OperatorRequest);

        var result = AgentTaskLiveness.ClassifyFailure(Guid.NewGuid(), snapshot, hasTranscriptEntries: false);

        result.FailureCode.ShouldBeNull();
        result.Reason.ShouldContain("operator request");
        result.Reason.ShouldNotContain("StoppedBeforeFirstPrompt");
    }

    [Test]
    public void ClassifyFailure_process_exit_and_legacy_unknown_are_not_operator_stops()
    {
        foreach (var source in new[]
                 {
                     SessionTerminationSource.ProcessExit,
                     SessionTerminationSource.Unknown,
                     SessionTerminationSource.SystemRequest,
                 })
        {
            var result = AgentTaskLiveness.ClassifyFailure(
                Guid.NewGuid(),
                new AgentTaskLiveness.SessionSnapshot(SessionStatus.Stopped, DateTime.UtcNow, null, source),
                hasTranscriptEntries: false);

            result.FailureCode.ShouldBe(AgentTaskFailureCode.StoppedBeforeFirstPrompt, source.ToString());
            result.Reason.ShouldNotContain("operator");
        }
    }

    [Test]
    public void ClassifyFailure_preserves_an_existing_session_failure_reason()
    {
        var snapshot = new AgentTaskLiveness.SessionSnapshot(
            SessionStatus.Failed, DateTime.UtcNow, "the pty-host exited (code 1)");

        var result = AgentTaskLiveness.ClassifyFailure(Guid.NewGuid(), snapshot, hasTranscriptEntries: false);

        result.FailureCode.ShouldBeNull();
        result.Reason.ShouldContain("the pty-host exited (code 1)");
        result.Reason.ShouldNotContain("StoppedBeforeFirstPrompt");
    }
}
