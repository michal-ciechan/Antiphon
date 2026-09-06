using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.HerdrSupervisionBackoffTests;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class HerdrSupervisionAttentionTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task A_held_agent_is_one_error_row_derived_from_state_without_any_incident(bool withSession)
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        Guid? session = withSession ? await scenario.AddSessionAsync(SessionStatus.Failed) : null;
        var agent = await scenario.AddAgentAsync(session);
        await scenario.HoldHerdrAsync(agent, DateTime.UtcNow.AddHours(-48), 3, HerdrSupervisionFailureKind.DetectTimeout, session);
        var rows = (await AttentionServiceTests.BuildService(new AttentionServiceTests.FakeRunnerClient()).GetAsync(CancellationToken.None))
            .Items.Where(i => i.AgentId == agent && i.Kind == AttentionKind.HerdrSupervisionHeld).ToList();
        var row = rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(AlertSeverity.Error);
        row.SessionId.ShouldBe(session);
        row.Headline.ShouldContain("3 of 3 attempts failed (DetectTimeout)");
        row.Actions.ShouldContain(AttentionAction.OpenAgent);
        row.Actions.Contains(AttentionAction.OpenDrawer).ShouldBe(withSession);
        row.Evidence.ShouldBe(row.Headline);
    }

    [Test]
    public async Task The_row_survives_the_lookback_incident_pruning_and_always_on_off()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var agent = await scenario.AddAgentAsync(alwaysOn: true);
        await scenario.HoldHerdrAsync(agent, DateTime.UtcNow.AddHours(-48), 3, HerdrSupervisionFailureKind.PaneClosed, null);
        await using var db = Db();
        await db.AgentIncidents.Where(i => i.AgentId == agent).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.Id == agent).ExecuteUpdateAsync(u => u.SetProperty(a => a.AlwaysOn, false));
        var result = await AttentionServiceTests.BuildService(new AttentionServiceTests.FakeRunnerClient()).GetAsync(CancellationToken.None);
        result.Items.Count(i => i.AgentId == agent && i.Kind == AttentionKind.HerdrSupervisionHeld).ShouldBe(1);
    }

    [Test]
    public async Task The_hold_incident_is_not_also_a_recent_critical_incident_row_and_clearing_removes_the_row()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var agent = await scenario.AddAgentAsync();
        var heldAt = DateTime.UtcNow.AddSeconds(-1);
        await scenario.HoldHerdrAsync(agent, heldAt, 3, HerdrSupervisionFailureKind.ChildGone, null);
        await using var db = Db();
        db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = agent, Kind = AgentIncidentKind.HerdrSupervisionHeld,
            Severity = AlertSeverity.Error, Message = "typed hold evidence", CreatedAt = heldAt });
        await db.SaveChangesAsync();
        var service = AttentionServiceTests.BuildService(new AttentionServiceTests.FakeRunnerClient());
        var rows = (await service.GetAsync(CancellationToken.None)).Items.Where(i => i.AgentId == agent).ToList();
        rows.ShouldHaveSingleItem().Evidence.ShouldBe("typed hold evidence");
        await db.AgentSupervisionStates.Where(s => s.AgentId == agent).ExecuteUpdateAsync(u => u.SetProperty(s => s.HerdrFailureHeldAt, (DateTime?)null));
        (await service.GetAsync(CancellationToken.None)).Items.Where(i => i.AgentId == agent).ShouldBeEmpty();
    }

    [Test]
    public async Task Two_held_agents_are_two_rows()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var a = await scenario.AddAgentAsync();
        var b = await scenario.AddAgentAsync();
        foreach (var id in new[] { a, b }) await scenario.HoldHerdrAsync(id, DateTime.UtcNow, 3, HerdrSupervisionFailureKind.ChildGone, null);
        var result = await AttentionServiceTests.BuildService(new AttentionServiceTests.FakeRunnerClient()).GetAsync(CancellationToken.None);
        result.Items.Count(i => (i.AgentId == a || i.AgentId == b) && i.Kind == AttentionKind.HerdrSupervisionHeld).ShouldBe(2);
    }
}
