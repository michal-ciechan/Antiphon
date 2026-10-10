using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1183. The boot qualification fixture's deadline summary is a fact in the goal the
/// check interpreter reads. It must state the same detection-only policy as
/// <c>server/Bundles/check-interpreter.md</c>. Restoring the kill-and-retry sentence fails
/// the negative assertion; deleting the detection sentence fails the positive one.
/// </summary>
[Category("Unit")]
public class SpecialistQualificationContractTests
{
    /// <summary>
    /// The deadline clause in <c>server/Bundles/check-interpreter.md</c>, reused verbatim.
    /// </summary>
    private const string DetectionSentence =
        "the harness only detects the stall (CARD-1151 for a delegate task, CARD-1156 for a "
        + "taskless AlwaysOn session): it does not kill, fail, retry, stop or release the session.";

    private const string RetiredSentence = "the harness kills and retries this boot once";

    [Test]
    public void C1183_Boot_fixture_deadline_detects_without_kill_or_retry()
    {
        var boot = SpecialistQualificationContract.CreateBatch(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc))
            .Single(fixture => fixture.Shape == "boot");

        boot.Goal.ShouldContain("DEADLINE: PAST BootModelWait 2m — ", Case.Sensitive, "boot deadline line");
        boot.Goal.ShouldContain(DetectionSentence, Case.Sensitive, "detection-only deadline summary");
        boot.Goal.ShouldNotContain(RetiredSentence, Case.Sensitive, "retired kill-and-retry sentence");
        boot.Goal.ShouldNotContain("kills and retries", Case.Sensitive, "retired kill/retry claim");
    }
}
