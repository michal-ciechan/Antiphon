using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingContinuityAttentionTests
{
    [Test]
    public async Task Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts()
    {
        await using var f = new StandingRecoveryFixture(new FakeAgentProtocolAdapter());
        await f.SeedAsync(held: true);
        await using (var db = f.Db()) await db.AgentIncidents.Where(i => i.AgentId == f.Agent.Id).ExecuteDeleteAsync();
        for (var i = 0; i < 2; i++)
        {
            await using var db = f.Db();
            var service = new AttentionService(db, f.Harness.Runner, Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), TimeProvider.System, NullLogger<AttentionService>.Instance);
            var row = (await service.GetAsync(default)).Items.Where(x => x.AgentId == f.Agent.Id
                && x.Kind == AttentionKind.StandingContinuityDecision).ShouldHaveSingleItem();
            row.SessionId.ShouldBe(f.B.Id); row.Actions.ShouldContain(AttentionAction.OpenAgent);
            (await db.Alerts.CountAsync(a => a.AgentId == f.Agent.Id)).ShouldBe(0);
        }
    }
}
