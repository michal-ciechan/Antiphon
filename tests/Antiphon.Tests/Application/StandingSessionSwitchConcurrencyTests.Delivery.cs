using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingSessionSwitchConcurrencyTests
{
    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Reservation_and_real_queue_flush_serialize_in_both_orders(bool deliveryWins)
    {
        var gate = new QueueReservationGate(deliveryWins);
        var target = new FakeAgentProtocolAdapter(); var source = new FakeAgentProtocolAdapter
            { ThrowOnSend = deliveryWins ? new IOException("synthetic in-flight transport refusal after the durable claim") : null };
        await using var f = new StandingRecoveryFixture(s =>
        {
            s.AddDbContext<AppDbContext>(b => b.AddInterceptors(gate));
            s.AddSingleton(Options.Create(new SupervisionSettings { DeliveryVerification = new DeliveryVerificationSettings
                { TranscriptConfirmTimeoutSeconds = 1, EvidenceTimeoutSeconds = 1, PostSubmitAdvanceTimeoutSeconds = 1 } }));
        }, target);
        await f.SeedAsync();
        var messageId = Guid.NewGuid(); gate.MessageId = messageId; gate.TargetId = f.A.Id;
        await using (var db = f.Db())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = messageId, AgentSessionId = f.B.Id,
                Sequence = 1, Body = "A stale delivery must never cross conversations", CreatedAt = DateTime.UtcNow,
                HoldUntil = deliveryWins ? null : DateTime.UtcNow.AddDays(1) });
            db.TranscriptEntries.AddRange(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 1,
                Kind = TranscriptKinds.UserPrompt, Text = "Previously confirmed source input", Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
                new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = f.B.Id, Sequence = 2, Kind = TranscriptKinds.TurnEnd,
                    StopReason = "end_turn", Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        f.Harness.Provider.GetRequiredService<AgentSessionRuntime>().Register(f.B.Id, source);
        var queue = f.Harness.Provider.GetRequiredService<SessionMessageQueueService>();
        Task? flush = null; Task? selection = null;
        try
        {
            if (deliveryWins) flush = queue.FlushSessionAsync(f.B.Id, default);
            else selection = f.StartAsync(new(ResumeSessionId: f.A.Id));
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (deliveryWins) selection = f.StartAsync(new(ResumeSessionId: f.A.Id));
            else flush = queue.FlushSessionAsync(f.B.Id, default);
            // The contender uses the actual singleton queue semaphore, not a mock lock.
            await Should.ThrowAsync<TimeoutException>((deliveryWins ? selection! : flush!).WaitAsync(TimeSpan.FromMilliseconds(150)));
            gate.Release.TrySetResult();
            await flush!;
            if (deliveryWins)
            {
                (await Should.ThrowAsync<ConflictException>(selection!)).Code.ShouldBe("standing_resume_delivery_pending");
                target.Started.ShouldBeFalse();
            }
            else
            {
                await selection!; await f.IdleAsync(); source.Inputs.ShouldBeEmpty();
            }
            await using var verify = f.Db();
            var row = (await verify.SessionQueuedMessages.FindAsync(messageId))!;
            row.AgentSessionId.ShouldBe(deliveryWins ? f.B.Id : f.A.Id);
            if (deliveryWins) row.LastDeliveryStartedAt.ShouldNotBeNull();
            else { row.DeliveryAttempts.ShouldBe(0); row.LastDeliveryBaselineSequence.ShouldBeNull(); }
        }
        finally
        {
            gate.Release.TrySetResult();
            if (flush is not null) await flush;
            if (selection is not null) { try { await selection; } catch (ConflictException) { } }
            await f.IdleAsync();
        }
    }

    private sealed class QueueReservationGate(bool deliveryWins) : SaveChangesInterceptor
    {
        public Guid MessageId { get; set; }
        public Guid TargetId { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hits;
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.Id == MessageId
                && (deliveryWins ? e.Entity.DeliveryAttempts > 0 : e.Entity.AgentSessionId == TargetId))
                && Interlocked.CompareExchange(ref _hits, 1, 0) == 0)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
    }
}
