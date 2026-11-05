using System.Text.Json;
using System.Data.Common;
using System.Threading.Channels;
using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1716: one held session queue lock must not stall the inbound wake worker for every agent.
/// A pass waits a bounded time for a session's lock, skips that session's remaining rows in order,
/// and retries them on the next pass; busy is never a failure, incident or alert.
/// </summary>
public sealed partial class ChannelInboundRecoveryTests
{
    // Generous for one bounded pass (1 s per busy session plus the healthy delivery); the base
    // code never finishes the pass while the lock is held.
    private static readonly TimeSpan C1716PassBound = TimeSpan.FromSeconds(8);

    [Test]
    public async Task C1716_BusyPass_PublishesHeartbeatAndPreventsPrematureStall()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        var clock = new FakeTimeProvider(h.Now);
        var bridge = Bridge(h, clock);
        var inbound = await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-heartbeat-{Guid.NewGuid():N}",
            "busy pending body", h.Now.AddMinutes(-10));
        var signal = h.Provider.GetRequiredService<ChannelInboundWakeSignal>();
        signal.LastPass.ShouldBeNull();
        var queueLock = h.Queue.GetLock(h.SessionId);
        await queueLock.WaitAsync();
        try
        {
            clock.Advance(TimeSpan.FromMinutes(4));
            await bridge.DrainPendingAsync(Ct).WaitAsync(C1716PassBound);
            var published = signal.LastPass.ShouldNotBeNull("busy-heartbeat: a busy pass is still a completed pass");
            published.CompletedAt.ShouldBe(clock.GetUtcNow(), "busy-heartbeat-time");
            published.RowsSeen.ShouldBe(1);
            published.RowsQueued.ShouldBe(0);
            published.SessionsSkippedBusy.ShouldBe(1);
            await using var db = Db(schema.ConnectionString);
            (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == inbound)).QueueMessageId.ShouldBeNull();
            clock.Advance(TimeSpan.FromMinutes(1));
            (await bridge.CheckWakeWorkerStallAsync(Ct)).ShouldBeFalse("busy-heartbeat-age: five minutes since startup, only one since a healthy busy pass");
            (await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId)).ShouldBe(0);
        }
        finally { queueLock.Release(); bridge.Dispose(); }
    }

    [Test]
    public async Task C1716_HostedWorker_RetriesBusyAtFiveSecondsThenUsesIdleTick()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
        });
        await InstallTranscriptReceipt(h);
        var chat = await h.BindChannelAsync();
        var inbound = await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-loop-{Guid.NewGuid():N}", "retry body");
        var healthy = await SeedHealthyRecipientAsync(h, "healthy during busy pass");
        var clock = new C1716LoopClock(h.Now);
        using var bridge = Bridge(h, clock);
        clock.CompletedPasses = () => bridge.CompletedDrainIterations;
        var queueLock = h.Queue.GetLock(h.SessionId);
        await queueLock.WaitAsync();
        var held = true;
        try
        {
            await bridge.StartAsync(Ct);
            var retry = await clock.NextTimerAsync(due => due == TimeSpan.FromSeconds(5) || due == TimeSpan.FromSeconds(10), completedPasses: 1);
            retry.ShouldBe(TimeSpan.FromSeconds(5), "busy-loop-delay: the actual worker must use the busy retry");
            bridge.CompletedDrainIterations.ShouldBe(1);
            await AssertCompleteHealthyReceiptAsync(h, healthy);
            queueLock.Release();
            held = false;
            clock.Advance(TimeSpan.FromMilliseconds(4999));
            bridge.CompletedDrainIterations.ShouldBe(1);
            await using (var before = Db(schema.ConnectionString))
                (await before.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == inbound)).QueueMessageId.ShouldBeNull();
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var idle = await clock.NextTimerAsync(due => due == TimeSpan.FromSeconds(5) || due == TimeSpan.FromSeconds(10), completedPasses: 2);
            idle.ShouldBe(TimeSpan.FromSeconds(10), "idle-loop-delay: a completed retry returns to the idle tick");
            bridge.CompletedDrainIterations.ShouldBe(2);
            await using (var after = Db(schema.ConnectionString))
                (await after.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == inbound)).QueueMessageId.ShouldNotBeNull("busy-loop-owner: retried at five seconds, before the idle tick");
            clock.Advance(TimeSpan.FromMilliseconds(9999));
            bridge.CompletedDrainIterations.ShouldBe(2);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            (await clock.NextTimerAsync(due => due == TimeSpan.FromSeconds(10), completedPasses: 3)).ShouldBe(TimeSpan.FromSeconds(10));
            bridge.CompletedDrainIterations.ShouldBe(3);
        }
        finally
        {
            if (held) queueLock.Release();
            await bridge.StopAsync(Ct).WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Test]
    public async Task C1716_HostedService_StartsIndependentStallCheckerAndRearmsAfterRecovery()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var gate = new C1716WakeScanGate();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(gate),
        });
        await InstallTranscriptReceipt(h);
        var chat = await h.BindChannelAsync();
        var clock = new C1716LoopClock(h.Now);
        using var bridge = Bridge(h, clock);
        clock.CompletedPasses = () => bridge.CompletedDrainIterations;
        await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-startup-{Guid.NewGuid():N}", "first stall", h.Now.AddMinutes(-10));
        var first = gate.Arm();
        try
        {
            await bridge.StartAsync(Ct);
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await clock.NextTimerAsync(due => due == TimeSpan.FromMinutes(1));
            bridge.CompletedDrainIterations.ShouldBe(0, "the wake worker is held before completing its first pass");
            clock.Advance(TimeSpan.FromMinutes(5));
            await WaitForAsync(async () => await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId) == 1);
            await clock.NextTimerAsync(due => due == TimeSpan.FromMinutes(1));
            clock.Advance(TimeSpan.FromMinutes(1));
            await clock.NextTimerAsync(due => due == TimeSpan.FromMinutes(1));
            (await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId)).ShouldBe(1, "scheduled-stall-once");
            first.Release.TrySetResult();
            await clock.NextTimerAsync(due => due == TimeSpan.FromSeconds(10), completedPasses: 1);
            bridge.CompletedDrainIterations.ShouldBe(1);
            h.Provider.GetRequiredService<ChannelInboundWakeSignal>().LastPass!.CompletedAt.ShouldBe(clock.GetUtcNow());
            await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-startup-again-{Guid.NewGuid():N}",
                "second stall", clock.GetUtcNow().UtcDateTime.AddMinutes(-10));
            var second = gate.Arm();
            clock.Advance(TimeSpan.FromSeconds(10));
            await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromMinutes(5));
            await WaitForAsync(async () => await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId) == 2);
            bridge.CompletedDrainIterations.ShouldBe(1, "the independent checker runs while the next wake pass is still held");
        }
        finally
        {
            gate.ReleaseAll();
            await bridge.StopAsync(Ct).WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    // Observe timers only after their real registration, so fake time never races a worker's wait setup.
    private sealed class C1716LoopClock(DateTimeOffset start) : TimeProvider
    {
        private readonly FakeTimeProvider _clock = new(start);
        private readonly Channel<(TimeSpan Due, int CompletedPasses)> _timers = Channel.CreateUnbounded<(TimeSpan, int)>();
        public Func<int> CompletedPasses { get; set; } = () => 0;
        public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();
        public override long GetTimestamp() => _clock.GetTimestamp();
        public override long TimestampFrequency => _clock.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _clock.CreateTimer(callback, state, dueTime, period);
            _timers.Writer.TryWrite((dueTime, CompletedPasses()));
            return timer;
        }
        public void Advance(TimeSpan amount) => _clock.Advance(amount);
        public async Task<TimeSpan> NextTimerAsync(Func<TimeSpan, bool> matches, int? completedPasses = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var timer = await _timers.Reader.ReadAsync(timeout.Token);
                if (matches(timer.Due) && (completedPasses is null || timer.CompletedPasses == completedPasses))
                    return timer.Due;
            }
        }
    }

    private sealed class C1716WakeScanGate : DbCommandInterceptor
    {
        internal sealed record Hold(TaskCompletionSource Entered, TaskCompletionSource Release);
        private readonly List<Hold> _holds = [];
        private Hold? _next;
        public Hold Arm()
        {
            var hold = new Hold(new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously));
            _holds.Add(hold);
            Volatile.Write(ref _next, hold);
            return hold;
        }
        public void ReleaseAll() { foreach (var hold in _holds) hold.Release.TrySetResult(); }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ChannelInbounds", StringComparison.Ordinal)
                && command.CommandText.Contains("min(", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _next, null) is { } hold)
            {
                hold.Entered.TrySetResult();
                await hold.Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [Test]
    [Arguments("enqueue")]
    [Arguments("sweep")]
    public async Task C1716_HeldQueueLock_OtherSessionQueuedWithinOnePass(string phase)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
        });
        await InstallTranscriptReceipt(h);
        var chat = await h.BindChannelAsync();
        var heldNative = $"c1716-held-{Guid.NewGuid():N}";
        var heldInbound = await SeedC1716InboundAsync(h, h.AgentId, chat, heldNative, "held body");
        if (phase == "sweep")
        {
            // A queue row the pass's owned-row sweep flushes; the held lock meets that flush.
            await h.Queue.EnqueueAsync(h.SessionId, "held channel body", MessageSendMode.WhenIdle, Ct,
                origin: QueuedMessageOrigin.Channel, conversationKey: $"telegram:{chat}", deliverIfIdle: false,
                sourceChannelInboundId: heldInbound, channelMemberInboundIds: [heldInbound]);
        }
        var healthy = await SeedHealthyRecipientAsync(h, "healthy body");

        var queueLock = h.Queue.GetLock(h.SessionId);
        await queueLock.WaitAsync();
        Task? drain = null;
        try
        {
            drain = Bridge(h).DrainPendingAsync(Ct);
            await drain.WaitAsync(C1716PassBound);
            await AssertCompleteHealthyReceiptAsync(h, healthy);
            await using var db = Db(schema.ConnectionString);
            if (phase == "enqueue")
                (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == heldInbound))
                    .QueueMessageId.ShouldBeNull("the held session's row waits unchanged for a later pass");
            else
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.SourceChannelInboundId == heldInbound))
                    .Status.ShouldBe(QueuedMessageStatus.Pending);
            await AssertC1716NoFailureAsync(schema.ConnectionString, h.AgentId);
        }
        finally
        {
            queueLock.Release();
            if (drain is not null)
                try { await drain.WaitAsync(TimeSpan.FromSeconds(30)); } catch { /* the red run's diagnostic is above */ }
        }
    }

    [Test]
    public async Task C1716_BusySession_KeepsOrderWhenLockFreesMidPass_AndRetriesNextPass()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
        });
        await InstallTranscriptReceipt(h);
        var chat = await h.BindChannelAsync();
        // Acceptance order A1 < B < A2.
        var a1 = await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-a1-{Guid.NewGuid():N}", "a1 body");
        var healthy = await SeedHealthyRecipientAsync(h, "b body");
        var a2 = await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-a2-{Guid.NewGuid():N}", "a2 body");

        var queueLock = h.Queue.GetLock(h.SessionId);
        await queueLock.WaitAsync();
        var released = 0;
        // A's lock frees the moment A1 is reported busy, before the pass reaches A2.
        var logger = new C1716BusyHookLogger(() =>
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        });
        var bridge = Bridge(h, logger);
        Task? drain = null;
        try
        {
            drain = bridge.DrainPendingAsync(Ct);
            await drain.WaitAsync(C1716PassBound);
            Volatile.Read(ref released).ShouldBe(1, "A1 met the held lock and A's lock freed mid-pass");
            var firstPass = await (Task<ChannelWakeWorkerPass>)drain;
            firstPass.SessionsSkippedBusy.ShouldBe(1);
            firstPass.RowsSeen.ShouldBe(3);
            firstPass.RowsQueued.ShouldBe(1, "only B is queued while A is busy");
            ChannelBridgeService.NextWakeDelay(firstPass).ShouldBe(ChannelBridgeService.BusyRetryDelay,
                "skipped busy work retries soon, not at the idle tick");
            await AssertCompleteHealthyReceiptAsync(h, healthy);
            await using (var first = Db(schema.ConnectionString))
            {
                (await first.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a1))
                    .QueueMessageId.ShouldBeNull("A1 met the held lock");
                (await first.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a2))
                    .QueueMessageId.ShouldBeNull("A2 is not queued in the pass where A1 was busy, even after A's lock frees");
            }
            await AssertC1716NoFailureAsync(schema.ConnectionString, h.AgentId);

            // The next pass retries the skipped session, oldest row first.
            var secondPass = await bridge.DrainPendingAsync(Ct).WaitAsync(C1716PassBound);
            secondPass.SessionsSkippedBusy.ShouldBe(0);
            secondPass.RowsQueued.ShouldBe(2);
            ChannelBridgeService.NextWakeDelay(secondPass).ShouldBe(TimeSpan.FromSeconds(10));
            await using var second = Db(schema.ConnectionString);
            var owner1 = (await second.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a1)).QueueMessageId;
            var owner2 = (await second.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a2)).QueueMessageId;
            owner1.ShouldNotBeNull("the skipped session's oldest row is retried on the next pass");
            owner2.ShouldNotBeNull("the skipped session's later row is retried on the next pass");
            var sequence1 = await second.SessionQueuedMessages.Where(q => q.Id == owner1).Select(q => q.Sequence).SingleAsync();
            var sequence2 = await second.SessionQueuedMessages.Where(q => q.Id == owner2).Select(q => q.Sequence).SingleAsync();
            sequence1.ShouldBeLessThan(sequence2, "A1 keeps its place ahead of A2");
            await AssertC1716NoFailureAsync(schema.ConnectionString, h.AgentId);
        }
        finally
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
            if (drain is not null)
                try { await drain.WaitAsync(TimeSpan.FromSeconds(30)); } catch { /* the red run's diagnostic is above */ }
        }
    }

    [Test]
    public async Task C1716_StalledWakeWorker_RaisesOneChannelIncidentPerStall()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        var bridge = Bridge(h, clock);
        await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-stalled-{Guid.NewGuid():N}", "stalled body",
            clock.GetUtcNow().UtcDateTime);

        // The worker has completed no pass since it started, but not yet for the stall age.
        clock.Advance(ChannelBridgeService.WakeWorkerStallAge - TimeSpan.FromMilliseconds(1));
        (await bridge.CheckWakeWorkerStallAsync(Ct)).ShouldBeFalse();
        (await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId)).ShouldBe(0);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        (await bridge.CheckWakeWorkerStallAsync(Ct)).ShouldBeTrue("an accepted row waited 5 min while no pass completed");
        (await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId)).ShouldBe(1);
        clock.Advance(TimeSpan.FromMinutes(3));
        (await bridge.CheckWakeWorkerStallAsync(Ct)).ShouldBeFalse("one incident per stall");
        (await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId)).ShouldBe(1);

        // A completed pass is the heartbeat: it is published and re-arms the check.
        await bridge.DrainPendingAsync(Ct);
        h.Provider.GetRequiredService<ChannelInboundWakeSignal>().LastPass!.CompletedAt.ShouldBe(clock.GetUtcNow());
        await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-stalled-again-{Guid.NewGuid():N}", "stalled again",
            clock.GetUtcNow().UtcDateTime.AddMinutes(-10));
        (await bridge.CheckWakeWorkerStallAsync(Ct)).ShouldBeFalse("the worker completed a pass just now");
        clock.Advance(ChannelBridgeService.WakeWorkerStallAge);
        (await bridge.CheckWakeWorkerStallAsync(Ct)).ShouldBeTrue("a new stall after a completed pass raises again");
        (await C1716StallIncidentsAsync(schema.ConnectionString, h.AgentId)).ShouldBe(2);
    }

    private static async Task<int> C1716StallIncidentsAsync(string connectionString, Guid agentId)
    {
        await using var db = Db(connectionString);
        return await db.AgentIncidents.CountAsync(i => i.AgentId == agentId
            && i.Kind == AgentIncidentKind.ChannelReplyLost && i.FailureReason == "ChannelWakeWorkerStalled"
            && i.Severity == AlertSeverity.Critical);
    }

    private static async Task<Guid> SeedC1716InboundAsync(BridgeQueueHarness h, Guid agentId, string chat,
        string native, string body, DateTime? acceptedAt = null)
    {
        await using var seed = Db(h.ConnectionString);
        var channelId = await seed.ChatChannels.Where(c => c.ExternalId == chat).Select(c => c.Id).SingleAsync();
        var inbound = new ChannelInbound
        {
            Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
            NativeMessageId = native, AgentId = agentId, ChatChannelId = channelId,
            EnvelopeJson = JsonSerializer.Serialize(Message(chat, native, body), MessagingJson.Options),
            AcceptedAt = acceptedAt ?? h.Now,
        };
        seed.ChannelInbounds.Add(inbound);
        await seed.SaveChangesAsync();
        return inbound.Id;
    }

    /// <summary>Busy is not a failed attempt: no incident, no pending-message alert.</summary>
    private static async Task AssertC1716NoFailureAsync(string connectionString, Guid agentId)
    {
        await using var db = Db(connectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == agentId)).ShouldBe(0, "busy is not a channel incident");
        (await db.Alerts.CountAsync(a => a.DedupKey.StartsWith("bridge:pending:"))).ShouldBe(0, "busy raises no bridge alert");
    }

    private static ChannelBridgeService Bridge(BridgeQueueHarness h, ILogger<ChannelBridgeService> logger) => new(
        h.Messaging, h.Queue, h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), h.Clock, logger,
        h.Provider.GetRequiredService<ChannelInboundWakeSignal>());

    /// <summary>Runs <paramref name="onBusy"/> when the bridge reports a session's queue lock busy.</summary>
    private sealed class C1716BusyHookLogger(Action onBusy) : ILogger<ChannelBridgeService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains("queue lock is held", StringComparison.Ordinal))
                onBusy();
        }
    }
}
