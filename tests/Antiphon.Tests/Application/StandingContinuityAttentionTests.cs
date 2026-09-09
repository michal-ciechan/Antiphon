using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingContinuityAttentionTests
{
    [Test]
    [Arguments(StandingContinuityReason.NativeSessionMissing)] [Arguments(StandingContinuityReason.TargetMissing)]
    [Arguments(StandingContinuityReason.TargetIncompatible)] [Arguments(StandingContinuityReason.OwnershipUnproven)]
    public async Task Hold_survives_pruning_recreation_and_always_on_off_without_duplicate_alerts(StandingContinuityReason reason)
    {
        await using var f = new StandingRecoveryFixture(new FakeAgentProtocolAdapter());
        await f.SeedAsync(held: true);
        var alertId = Guid.NewGuid();
        await using (var db = f.Db())
        {
            db.Alerts.Add(new Antiphon.Server.Domain.Entities.Alert { Id = alertId, AgentId = f.Agent.Id,
                Source = "runner", Title = "Synthetic infrastructure outage", Detail = "Synthetic metadata",
                DedupKey = $"synthetic:{alertId:N}", Severity = AlertSeverity.Error, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await new StandingContinuityState(db, TimeProvider.System).HoldAsync(f.Agent.Id, f.B.Id, reason, default);
            await db.AgentIncidents.Where(i => i.AgentId == f.Agent.Id).ExecuteDeleteAsync();
        }
        await using (var scope = f.Harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(f.Agent.Id, default);
        for (var i = 0; i < 2; i++)
        {
            await using var db = f.Db();
            var service = new AttentionService(db, f.Harness.Runner, Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), TimeProvider.System, NullLogger<AttentionService>.Instance);
            var row = (await service.GetAsync(default)).Items.Where(x => x.AgentId == f.Agent.Id
                && x.Kind == AttentionKind.StandingContinuityDecision).ShouldHaveSingleItem();
            row.SessionId.ShouldBe(f.B.Id); row.Actions.ShouldContain(AttentionAction.OpenAgent);
            (await db.Alerts.CountAsync(a => a.AgentId == f.Agent.Id)).ShouldBe(1);
            (await db.Alerts.FindAsync(alertId))!.Detail.ShouldBe("Synthetic metadata");
            var state = (await db.AgentSupervisionStates.FindAsync(f.Agent.Id))!;
            state.ContinuityReason.ShouldBe(reason); state.ContinuityEvidence!.Length.ShouldBeLessThanOrEqualTo(1000);
        }
        await f.StartAsync(new(RetryContinuity: true)); await f.IdleAsync();
        await using var verify = f.Db();
        (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!.ContinuityHeldAt.ShouldBeNull();
        (await verify.Alerts.CountAsync(a => a.AgentId == f.Agent.Id)).ShouldBe(1);
        (await verify.Alerts.FindAsync(alertId))!.Title.ShouldBe("Synthetic infrastructure outage");
    }
}
