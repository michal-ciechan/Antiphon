using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("SessionGenerationDeliveryOverlap")]
public class SessionGenerationDeliveryOverlapTests
{
    [Test]
    public async Task C502_V11_same_generation_delivery_failure_kills_G_and_the_paused_launch_tail_settles_G()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = true });
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            db.TranscriptEntries.Add(new Antiphon.Server.Domain.Entities.TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = 1,
                Kind = TranscriptKinds.TurnEnd,
                Text = "done",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        h.Adapter.EchoTypedInputToScreen = false;
        await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "into the void", MessageSendMode.Now, CancellationToken.None));

        h.Adapter.KillGenerationCalls.Count.ShouldBeGreaterThan(0);
        h.Adapter.KillCount.ShouldBe(0);
        h.Adapter.Killed.ShouldBeTrue();
    }

    [Test]
    public async Task A_late_UserPrompt_confirmation_prevents_the_generation_conditional_recovery_kill()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = true });
        h.Adapter.OnSubmitted = async body =>
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
        };

        await h.Queue.EnqueueAsync(h.SessionId, "late confirm body", MessageSendMode.Now, CancellationToken.None);

        h.Adapter.KillGenerationCalls.ShouldBeEmpty();
        h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(1);
    }
}
