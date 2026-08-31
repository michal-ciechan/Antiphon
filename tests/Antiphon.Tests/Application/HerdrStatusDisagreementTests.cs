using System.Reflection;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0162: disagreement matrix helpers + the never-act structural pin (no delivery/control
/// deps on <see cref="HerdrStatusCorroborationService"/>).
/// </summary>
public class HerdrStatusDisagreementTests
{
    [Test]
    [Arguments("working", false, true)]
    [Arguments("blocked", false, true)]
    [Arguments("idle", true, true)]
    [Arguments("done", true, true)]
    [Arguments("working", true, false)]
    [Arguments("blocked", true, false)]
    [Arguments("idle", false, false)]
    [Arguments("done", false, false)]
    [Arguments("unknown", true, false)]
    [Arguments("unknown", false, false)]
    public void Disagreement_matrix_matches_plan_section_5(string herdr, bool isWorking, bool raise)
    {
        HerdrStatusCorroborationService.IsDisagreement(herdr, isWorking).ShouldBe(raise);
    }

    [Test]
    public void Corroboration_service_ctor_has_no_delivery_or_control_dependencies()
    {
        // Mirror of S3's no-agent.prompt structural pin: the only capability this service is
        // wired for is raising an incident (via scoped AgentSupervisorService) — never kill,
        // retype, or queue delivery.
        var ctor = typeof(HerdrStatusCorroborationService).GetConstructors()
            .ShouldHaveSingleItem();
        var types = ctor.GetParameters().Select(p => p.ParameterType).ToHashSet();

        types.ShouldContain(typeof(IServiceScopeFactory));
        types.ShouldContain(typeof(AgentSessionRuntime));
        types.ShouldContain(typeof(ISessionRunnerClient));
        types.ShouldContain(typeof(IOptions<SupervisionSettings>));

        types.ShouldNotContain(typeof(SessionMessageQueueService));
        types.ShouldNotContain(typeof(AgentControlService));
        types.ShouldNotContain(typeof(AgentSessionService));
        types.ShouldNotContain(typeof(AgentTaskReplyService));
        types.ShouldNotContain(typeof(AgentTaskService));
        types.ShouldNotContain(typeof(IDelegateSessionStopper));
        types.Any(t => t.Name.Contains("Kill", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
        types.Any(t => t.Name.Contains("Queue", StringComparison.OrdinalIgnoreCase)
                       && t != typeof(IServiceScopeFactory))
            .ShouldBeFalse();
    }

    [Test]
    public void A_bare_done_status_does_not_settle_fail_release_or_replace_a_delegate()
    {
        // CARD-0286: Herdr's "done" is corroboration only. The Codex error TurnEnd, not the
        // pane status, is the causal input for the 401 path; a clean worktree check runs only
        // on an explicit task-report verdict.
        var names = typeof(HerdrStatusCorroborationService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();
        names.Any(n => n.Contains("Settle", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        names.Any(n => n.Contains("Release", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        names.Any(n => n.Contains("Replace", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        names.Any(n => n.Contains("FailTask", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        HerdrStatusCorroborationService.IsDisagreement("done", isWorking: false).ShouldBeFalse();
    }

    [Test]
    public void HerdrStatusDisagreement_kind_is_34()
    {
        ((int)AgentIncidentKind.HerdrStatusDisagreement).ShouldBe(34);
    }

    [Test]
    public void HerdrCorroboration_defaults_match_plan()
    {
        var settings = new SupervisionSettings().HerdrCorroboration;
        settings.Enabled.ShouldBeTrue();
        settings.SweepPeriodSeconds.ShouldBe(60);
        settings.MinSustainedMinutes.ShouldBe(10);
    }
}
