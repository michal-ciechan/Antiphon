using System.Xml.Linq;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class AgentTaskLandVerificationEvidenceTests
{
    [Test]
    [Arguments("1", "1", "0", true)]
    [Arguments("7", "7", "0", true)]
    [Arguments("0", "0", "0", false)]
    [Arguments("2", "1", "0", false)]
    [Arguments("2", "2", "1", false)]
    [Arguments("invalid", "1", "0", false)]
    [Arguments(null, "1", "0", false)]
    [Arguments("1", null, "0", false)]
    [Arguments("1", "1", null, false)]
    public void C448_V34_CountersNeedExecutedPassingTests(string? executed, string? passed, string? failed, bool accepted)
    {
        var counters = new XElement("Counters");
        if (executed is not null) counters.SetAttributeValue("executed", executed);
        if (passed is not null) counters.SetAttributeValue("passed", passed);
        if (failed is not null) counters.SetAttributeValue("failed", failed);
        AgentTaskLandService.HasPassingTestCounters(counters, out var count).ShouldBe(accepted,
            "process exit zero cannot replace nonempty, fully passing selected-test evidence");
        if (accepted) count.ShouldBe(int.Parse(executed!));
        AgentTaskLandService.HasPassingTestCounters(null, out _).ShouldBeFalse();
    }
}
