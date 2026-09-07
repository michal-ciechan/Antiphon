using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityRecoveryQueueTests
{
    [Test]
    public async Task Card0412_V12_held_channel_row_delivers_after_grant()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = schema.ConnectionString,
            Supervision = FastCapacitySupervision(),
        });
        await SeedTerminalAsync(schema, h.SessionId);
        await h.Queue.EnqueueAsync(
            h.SessionId, "channel body", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:-1001");

        await using (var db = Ctx(schema))
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.NoteHeader.ShouldBe("Held");
            row.Body.ShouldBe("channel body");
            row.Origin.ShouldBe(QueuedMessageOrigin.Channel);
        }

        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        var recovery = h.Provider.GetRequiredService<CapacityRecoveryService>();
        var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{h.SessionId:N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true,
            sessionId: h.SessionId), CancellationToken.None);
        await recovery.GrantReadyAsync(CancellationToken.None);
        Guid messageId;
        await using (var db = Ctx(schema))
        {
            messageId = (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId)).Id;
        }

        await recovery.StampQueueActionAsync(messageId, wait.Id, wait.ActionKey, CancellationToken.None);
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SentInput.ShouldContain("channel body");
        await using var verify = Ctx(schema);
        var delivered = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
        delivered.CapacityRecoveryActionKey.ShouldBe(wait.ActionKey);
        var recoveryRow = await verify.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        recoveryRow.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.WallModelPaused);
    }

    [Test]
    public async Task Card0412_V12_unselected_held_row_stays_pending()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = schema.ConnectionString,
            Supervision = FastCapacitySupervision(),
        });
        await SeedTerminalAsync(schema, h.SessionId);
        await h.Queue.EnqueueAsync(
            h.SessionId, "older channel", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:-1001");
        await h.Queue.EnqueueAsync(
            h.SessionId, "newer channel", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:-1002");

        var recovery = h.Provider.GetRequiredService<CapacityRecoveryService>();
        var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{h.SessionId:N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true,
            sessionId: h.SessionId), CancellationToken.None);
        await recovery.GrantReadyAsync(CancellationToken.None);
        Guid olderId;
        await using (var db = Ctx(schema))
        {
            olderId = (await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == h.SessionId)
                .OrderBy(m => m.Sequence)
                .FirstAsync()).Id;
        }

        await recovery.StampQueueActionAsync(olderId, wait.Id, wait.ActionKey, CancellationToken.None);
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SentInput.ShouldContain("older channel");
        h.Adapter.SentInput.ShouldNotContain("newer channel");
        await using var verify = Ctx(schema);
        var newer = await verify.SessionQueuedMessages.SingleAsync(
            m => m.AgentSessionId == h.SessionId && m.Body == "newer channel");
        newer.Status.ShouldBe(QueuedMessageStatus.Pending);
        newer.NoteHeader.ShouldBe("Held");
    }

    [Test]
    public async Task Card0412_V12_repeat_enqueue_rediscovers_keyed_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = schema.ConnectionString,
            Supervision = FastCapacitySupervision(),
        });
        await SeedTerminalAsync(schema, h.SessionId);
        var key = $"{Guid.NewGuid():N}:0";
        await h.Queue.EnqueueAsync(
            h.SessionId, "wake once", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Supervision, deliverIfIdle: false,
            capacityRecoveryActionKey: key);
        await h.Queue.EnqueueAsync(
            h.SessionId, "wake once again", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Supervision, deliverIfIdle: false,
            capacityRecoveryActionKey: key);
        await using var verify = Ctx(schema);
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(1);
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Body.ShouldBe("wake once");
    }

    [Test]
    [Arguments("NeedsHuman")]
    [Arguments("UnknownExhausted")]
    [Arguments("WallUnparsed")]
    public async Task Card0412_V15_non_capacity_terminal_reason_blocks(string reason)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = schema.ConnectionString,
            Supervision = FastCapacitySupervision(),
        });
        await SeedTerminalAsync(schema, h.SessionId, reason);
        await h.Queue.EnqueueAsync(
            h.SessionId, "must not send", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:-1001");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await using var verify = Ctx(schema);
        var row = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        var recovery = await verify.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        recovery.ResolvedReason.ShouldBe(reason);
    }

    [Test]
    public async Task Card0412_V13_enqueue_is_not_prompt_confirmation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, _, _) = CapacityRecoveryTestSupport.CreateService(schema);
        var wait = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{Guid.NewGuid():N}",
            CapacityWaitConsumerKind.LiveSession,
            holdAlreadyCleared: true), CancellationToken.None);
        await service.GrantReadyAsync(CancellationToken.None);
        wait = await CapacityRecoveryTestSupport.CreateContext(schema).CapacityRecoveryWaits
            .AsNoTracking().SingleAsync(w => w.Id == wait.Id);
        wait.State.ShouldNotBe(CapacityRecoveryWaitState.PromptConfirmed);
        wait.ConfirmedPromptSequence.ShouldBeNull();
    }

    [Test]
    public async Task Card0412_V14_later_user_prompt_is_unrelated_takeover()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = schema.ConnectionString,
            Supervision = FastCapacitySupervision(),
        });
        await SeedTerminalAsync(schema, h.SessionId);
        await using (var db = Ctx(schema))
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = 50,
                Kind = TranscriptKinds.UserPrompt,
                Text = "human continued",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await h.Queue.EnqueueAsync(
            h.SessionId, "stale recovery", MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:-1001");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    private static SupervisionSettings FastCapacitySupervision() => new()
    {
        CapacityRecovery = new CapacityRecoverySettings
        {
            Enabled = true,
            AdmissionIntervalSeconds = 1,
            JitterSeconds = 0,
            MaxEpisodeAttempts = 3,
        },
    };

    private static AppDbContext Ctx(IsolatedTestSchema schema) =>
        CapacityRecoveryTestSupport.CreateContext(schema);

    private static async Task SeedTerminalAsync(
        IsolatedTestSchema schema, Guid sessionId, string reason = ApiErrorRecoveryReasons.WallModelPaused)
    {
        await using var db = Ctx(schema);
        var now = DateTime.UtcNow;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.UserPrompt,
            Text = "hello",
            CreatedAt = now,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 2,
            Kind = TranscriptKinds.TurnEnd,
            StopReason = TranscriptKinds.StopReasons.Error,
            Text = "usage balance exhausted",
            IsApiError = true,
            ApiErrorClass = "payment_required",
            ApiErrorStatus = 402,
            CreatedAt = now,
        });
        db.ApiErrorRecoveries.Add(new ApiErrorRecovery
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            StubSequence = 2,
            Classification = ApiErrorClassification.Wall,
            ApiErrorClass = "payment_required",
            ApiErrorStatus = 402,
            DetectedAt = now,
            ResolvedAt = now,
            ResolvedReason = reason,
        });
        await db.SaveChangesAsync();
    }
}
