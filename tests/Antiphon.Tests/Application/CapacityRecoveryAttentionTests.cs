using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
    }

    private static string CapacityRecoveryPolicyAction(Guid waitId) =>
        Antiphon.Server.Application.Services.CapacityRecoveryPolicy.ActionKey(waitId, 3);
}
