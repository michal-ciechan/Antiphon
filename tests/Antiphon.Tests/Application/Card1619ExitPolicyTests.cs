using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class Card1619ExitPolicyTests
{
    [Test]
    [Arguments(0, 9, SessionTerminationSource.ProcessExit, "killed by signal 9 (SIGKILL); sender unknown")]
    [Arguments(0, 15, SessionTerminationSource.ProcessExit, "killed by signal 15 (SIGTERM); sender unknown")]
    [Arguments(0, 99, SessionTerminationSource.ProcessExit, "killed by signal 99 (UNKNOWN); sender unknown")]
    [Arguments(0, 0, SessionTerminationSource.ProcessExit, "process exited with exit code 0")]
    [Arguments(23, 0, SessionTerminationSource.ProcessExit, "process exited with exit code 23")]
    [Arguments(0, null, SessionTerminationSource.ProcessExit, "process ended (exit code 0; signal unknown)")]
    [Arguments(null, null, SessionTerminationSource.ProcessExit, "process ended (exit code unknown; signal unknown)")]
    [Arguments(null, null, SessionTerminationSource.OperatorRequest, "Antiphon stop requested; signal unknown")]
    [Arguments(137, 0, SessionTerminationSource.OperatorRequest, "process exited with exit code 137")]
    [Arguments(0, 9, SessionTerminationSource.SystemRequest, "Antiphon stop requested; killed by signal 9 (SIGKILL); sender unconfirmed")]
    [Arguments(0, 15, SessionTerminationSource.OperatorRequest, "Antiphon stop requested; killed by signal 15 (SIGTERM); sender unconfirmed")]
    [Arguments(null, null, SessionTerminationSource.Unknown, "Antiphon host stop issued; child exit unobserved (exit code unknown; signal unknown)")]
    public void Describe_exit_evidence(int? code, int? signal, SessionTerminationSource source, string expected)
    {
        var host = expected.StartsWith("Antiphon host stop issued") ? new ProcessStopIntentDto {
            Target = ProcessStopTarget.Host, Outcome = ProcessStopOutcome.Issued,
            Provenance = ProcessStopProvenance.LiveIssuer } : null;
        var snapshot = new AgentTaskLiveness.SessionSnapshot(SessionStatus.Failed, DateTime.UtcNow,
            "retained failure", source, code, ExitSignal: signal, ExitStopIntent: host);
        var failure = AgentTaskLiveness.ClassifyFailure(Guid.NewGuid(), snapshot, true);
        failure.Reason.ShouldContain("retained failure");
        failure.Reason.ShouldContain(expected);
        failure.Reason.ShouldNotContain("exited on its own");
        failure.Reason.ShouldNotContain("stopped by Antiphon");
        if (source == SessionTerminationSource.ProcessExit) failure.Reason.ShouldContain("ProcessExit");
    }
}
