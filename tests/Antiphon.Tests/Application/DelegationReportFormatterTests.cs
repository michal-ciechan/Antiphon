using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class DelegationReportFormatterTests
{
    [Test]
    public void the_header_carries_the_session_bit_only_when_supplied()
    {
        var task = new AgentTask { Id = Guid.NewGuid(), Title = "Liveness" };
        var settings = new DelegationSettings();
        DelegationReportFormatter.BuildCompletionNote(task, settings, "Failed", sessionLiveness: "live-working")
            .Header.ShouldContain("session=live-working");
        DelegationReportFormatter.BuildCompletionNote(task, settings, "Failed")
            .Header.ShouldNotContain("session=");
    }
}
