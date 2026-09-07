using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityRecoveryAttentionTests
{
    [Test]
    public async Task Card0412_V24_exhausted_attention_survives_without_incident()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var waitId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        db.CapacityRecoveryWaits.Add(new CapacityRecoveryWait
        {
            Id = waitId,
            ConsumerKey = $"task:{taskId:N}",
            ConsumerKind = CapacityWaitConsumerKind.QueuedTask,
            ExecutionKind = AgentKind.ClaudeCode,
            RequestedKind = AgentKind.ClaudeCode,
            RequestedAlias = "opus",
            ActionKey = CapacityRecoveryPolicyAction(waitId),
            State = CapacityRecoveryWaitState.Exhausted,
            AdmissionCount = 3,
            BlockedAt = DateTime.UtcNow.AddHours(-2),
            TaskId = taskId,
            Version = 4,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var exhausted = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == waitId);
        exhausted.State.ShouldBe(CapacityRecoveryWaitState.Exhausted);
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.CapacityRecovery)).ShouldBe(0);
        AttentionKind.CapacityRecoveryExhausted.ShouldBe((AttentionKind)32);

        var attention = new AttentionService(
            db,
            new BridgeQueueHarness.EmptyRunnerClient(),
            Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<AttentionService>.Instance);
        var items = await attention.GetAsync(CancellationToken.None);
        var item = items.Items.Single(i => i.Kind == AttentionKind.CapacityRecoveryExhausted && i.TaskId == taskId);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.Title.ShouldBe("Capacity retries paused");
        item.Headline.ShouldContain("manual continuation");
    }

    [Test]
    public async Task Card0412_V24_queued_is_not_auto_resumed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.QueuedTask,
            holdAlreadyCleared: true), CancellationToken.None);
        await service.RecordLifecycleAsync(
            wait.Id, "Auto-resume queued; awaiting transcript confirmation.", CancellationToken.None);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var incident = await verify.AgentIncidents.SingleAsync(i => i.Kind == AgentIncidentKind.CapacityRecovery);
        incident.Message.ShouldContain("queued");
        incident.Message.ShouldNotContain("Auto-resumed");
    }

    private static string CapacityRecoveryPolicyAction(Guid waitId) =>
        Antiphon.Server.Application.Services.CapacityRecoveryPolicy.ActionKey(waitId, 3);
}
