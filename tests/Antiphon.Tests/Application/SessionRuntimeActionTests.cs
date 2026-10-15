using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class SessionRuntimeActionTests
{
    [Test]
    [Arguments("batch-fail")]
    [Arguments("fallback-fail")]
    [Arguments("safe-stub")]
    [Arguments("replay-after-ack")]
    [Arguments("commit-wake-gap")]
    [Arguments("cancel-before-commit")]
    [Arguments("cancel-after-commit")]
    public async Task C1259_commit_and_effect_admission_are_atomic(string variant)
    {
        using var cancel = new CancellationTokenSource();
        var fault = new CommitFault(variant, cancel);
        var receiver = new Receiver();
        await using var f = await SessionStateTestFixture.CreateAsync(fault, configure: s => s.AddSingleton<IEventBus>(receiver));
        var entry = f.Event(1, "ToolResult", text: "original payload", uuid: "target");
        if (variant == "safe-stub") entry = entry with { ToolName = new string('x', 201), ToolInput = "distinct lost input" };
        if (variant == "cancel-before-commit")
        {
            using var lease = await f.Store.BeginWriteAsync(f.SessionId, default);
            var ingest = f.Runtime.PersistTranscriptAsync(f.SessionId, [entry], cancel.Token);
            await cancel.CancelAsync();
            try { await ingest; } catch (OperationCanceledException) { }
        }
        else
        {
            var entries = variant == "fallback-fail"
                ? new[] { f.Event(1, "ToolResult", text: "first", uuid: "first"), entry with { Sequence = 2 }, f.Event(3, "ToolResult", text: "third", uuid: "third") }
                : new[] { entry };
            try { await f.Runtime.PersistTranscriptAsync(f.SessionId, entries, cancel.Token); }
            catch (OperationCanceledException) when (variant == "cancel-after-commit") { }
        }
        await using (var db = f.Db())
        {
            var rows = await db.TranscriptEntries.OrderBy(t => t.Sequence).ToListAsync();
            var actions = await db.SessionRuntimeActions.OrderBy(a => a.Sequence).ToListAsync();
            actions.Select(a => a.TranscriptEntryId).ShouldBe(rows.Select(t => (Guid?)t.Id));
            actions.Select(a => a.Sequence).ShouldBe(rows.Select(t => t.Sequence));
            if (variant is "batch-fail" or "cancel-before-commit")
            { rows.ShouldBeEmpty(); actions.ShouldBeEmpty(); }
            else if (variant == "fallback-fail")
                rows.Select(t => t.Uuid).ShouldBe(["first", "third"]);
            else
            {
                rows.ShouldHaveSingleItem(); actions.ShouldHaveSingleItem();
                if (variant == "safe-stub")
                {
                    rows[0].Text.ShouldBe("[transcript text not persistable: 22001]");
                    rows[0].ToolInput.ShouldBeNull(); actions[0].UiProjectionDelta.ShouldNotBeEmpty();
                }
            }
        }
        receiver.Payloads.ShouldBeEmpty("admission cannot run an external effect");
        if (variant is "batch-fail" or "fallback-fail" or "cancel-after-commit") fault.Hits.ShouldBeGreaterThan(0);
        if (variant == "cancel-after-commit")
            (await f.Store.ReadAsync(f.SessionId, default)).Count.ShouldBe(1);
        if (variant == "replay-after-ack")
        {
            await using (var db = f.Db())
                await db.SessionRuntimeActions.ExecuteUpdateAsync(u => u.SetProperty(a => a.Phase, 3));
            await f.Runtime.PersistTranscriptAsync(f.SessionId, [entry]);
            await using (var db = f.Db())
            { (await db.SessionRuntimeActions.SingleAsync()).Phase.ShouldBe(3); }
            await Actions(f).DrainPageAsync(f.SessionId, default);
            await f.Runtime.PersistTranscriptAsync(f.SessionId, [entry]);
            await using var verify = f.Db();
            (await verify.SessionRuntimeActions.CountAsync()).ShouldBe(0);
            (await verify.TranscriptEntries.CountAsync()).ShouldBe(1);
        }
        if (variant == "commit-wake-gap")
        {
            await using var restarted = f.NewContainer();
            await RunUntilAsync(restarted, async () =>
            { await using var db = f.Db(); return !await db.SessionRuntimeActions.AnyAsync(); });
            receiver.Payloads.ShouldHaveSingleItem();
            Text(receiver.Payloads.Single()).ShouldBe("original payload");
        }
    }

    [Test]
    [Arguments("mixed-stream-pulls")]
    [Arguments("same-api-siblings")]
    [Arguments("synthetic-race")]
    [Arguments("missing-store")]
    [Arguments("queue-lock-catchup")]
    public async Task C1259_stream_pull_and_replay_preserve_order(string variant)
    {
        var runner = new ScriptedSessionRunnerClient();
        await using var f = await SessionStateTestFixture.CreateAsync(configure: s => s.AddSingleton<ISessionRunnerClient>(runner));
        var entries = new[] { f.Event(1, "UserPrompt", text: "prompt", uuid: "shared"),
            f.Event(1, "AssistantText", text: "answer", uuid: "shared"),
            f.Event(1, "TurnEnd", uuid: "shared") with { StopReason = "end_turn" } };
        if (variant == "missing-store")
        {
            var runtime = new AgentSessionRuntime(runner, new MockEventBus(),
                Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.AgentSessionSettings()),
                f.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<AgentSessionRuntime>.Instance);
            await runtime.ObserveTranscriptAsync(entries[0], default);
            (await f.RowsAsync()).ShouldBeEmpty();
            await using var db = f.Db(); (await db.SessionRuntimeActions.CountAsync()).ShouldBe(0);
            runtime.TryGetTranscriptPersistFailure(f.SessionId, out _).ShouldBeTrue();
            return;
        }
        if (variant == "same-api-siblings")
        {
            var api = Guid.NewGuid().ToString();
            entries = [entries[0], entries[1] with { ApiCallId = api }, entries[2] with { ApiCallId = api },
                entries[2] with { Uuid = "second-end", ApiCallId = api }];
            await f.Runtime.PersistTranscriptAsync(f.SessionId, entries);
            await using var db = f.Db();
            (await db.SessionRuntimeActions.CountAsync(a => a.TurnBoundary)).ShouldBe(1);
        }
        else if (variant == "queue-lock-catchup")
        {
            runner.SetTranscript(new(f.SessionId, entries, 1));
            var gate = f.Services.GetRequiredService<SessionMessageQueueService>().GetLock(f.SessionId);
            await gate.WaitAsync();
            try { await f.Runtime.SyncTranscriptAsync(f.SessionId, default).WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { gate.Release(); }
            await using var db = f.Db();
            (await db.SessionRuntimeActions.CountAsync(a => a.TriggerKind == "catchup")).ShouldBe(1);
        }
        else if (variant == "synthetic-race")
        {
            await f.Runtime.PersistTranscriptAsync(f.SessionId, [entries[0]]);
            await Task.WhenAll(f.Runtime.WriteRestartBoundaryIfInterruptedAsync(f.SessionId, default),
                f.Runtime.PersistTranscriptAsync(f.SessionId, [entries[1] with { Uuid = "live" }]));
            var rows = await f.RowsAsync();
            rows.Select(t => t.Sequence).ShouldBe([1L, 2L, 3L]);
            rows.Count(t => t.Kind == "SessionRestartBoundary").ShouldBe(1);
            rows.Count(t => t.Uuid == "live").ShouldBe(1);
            await using var db = f.Db(); (await db.SessionRuntimeActions.CountAsync()).ShouldBe(3);
            return;
        }
        else
        {
            runner.SetTranscript(new(f.SessionId, entries, 1));
            await Task.WhenAll(f.Runtime.PersistTranscriptAsync(f.SessionId, entries), f.Runtime.SyncTranscriptAsync(f.SessionId, default),
                f.Runtime.SyncTranscriptAsync(f.SessionId, default));
        }
        await f.Runtime.PersistTranscriptAsync(f.SessionId, entries);
        var saved = await f.RowsAsync();
        saved.Select(t => (t.Uuid, t.Kind)).ShouldBe(entries.Select(t => (t.Uuid, t.Kind)));
        saved.Select(t => t.Sequence).ShouldBe(Enumerable.Range(1, entries.Length).Select(i => (long)i));
        await using var verify = f.Db();
        (await verify.SessionRuntimeActions.CountAsync(a => a.TriggerKind == "transcript")).ShouldBe(entries.Length);
    }

    [Test]
    [Arguments("production-capacity")]
    [Arguments("duplicate-owner")]
    [Arguments("overflow")]
    [Arguments("backstop")]
    [Arguments("poison-page")]
    public async Task C1259_saturation_keeps_ingestion_live_and_recovers_overflow(string variant)
    {
        var receiver = new Receiver();
        var clock = DatabaseClock();
        await using var f = await SessionStateTestFixture.CreateAsync(clock: clock, configure: s => s.AddSingleton<IEventBus>(receiver));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new ConcurrentDictionary<Guid, int>();
        var holding = new HashSet<Guid>();
        var blockCount = variant == "duplicate-owner" ? 1 : 4;
        var extras = variant is "production-capacity" or "overflow" or "poison-page" ? 130 : 1;
        var ids = Enumerable.Range(0, blockCount + extras).Select(_ => Guid.NewGuid()).ToArray();
        await using (var db = f.Db())
        {
            foreach (var id in ids) db.AgentSessions.Add(new AgentSession { Id = id, DefinitionName = "action-capacity",
                Cwd = Path.GetTempPath(), Status = SessionStatus.Running, CreatedAt = clock.GetUtcNow().UtcDateTime,
                StartedAt = clock.GetUtcNow().UtcDateTime, LastSeenAt = clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync();
        }
        foreach (var id in ids.Take(blockCount)) holding.Add(id);
        receiver.BeforeReceive = async (payload, ct) =>
        {
            var id = (Guid)payload.GetType().GetProperty("sessionId")!.GetValue(payload)!;
            if (variant == "poison-page" && id == ids[0]) throw new IOException("poison session");
            if (holding.Contains(id) && variant is not "poison-page" and not "backstop")
            { entered.AddOrUpdate(id, 1, (_, count) => count + 1); await release.Task.WaitAsync(ct); }
        };
        await using var restarted = f.NewContainer();
        var coordinator = restarted.GetRequiredService<SessionRuntimeActionCoordinator>();
        using var worker = new SessionRuntimeActionHostedService(coordinator);
        await worker.StartAsync(default);
        try
        {
            foreach (var id in ids.Take(blockCount))
                await restarted.GetRequiredService<AgentSessionRuntime>().ObserveTranscriptAsync(f.Event(1, "ToolResult", text: id.ToString()) with { SessionId = id }, default);
            if (variant is not "poison-page" and not "backstop") await UntilAsync(() => Task.FromResult(entered.Count == blockCount));
            if (variant == "duplicate-owner")
            {
                for (var i = 0; i < 100; i++) coordinator.Hint(ids[0]);
            }
            foreach (var id in ids.Skip(blockCount))
            {
                var runtime = variant == "backstop" ? f.Runtime : restarted.GetRequiredService<AgentSessionRuntime>();
                await runtime.ObserveTranscriptAsync(f.Event(1, "ToolResult", text: id.ToString()) with { SessionId = id }, default);
            }
            await using (var db = f.Db())
                (await db.TranscriptEntries.CountAsync(t => ids.Contains(t.AgentSessionId))).ShouldBe(ids.Length);
            if (variant == "duplicate-owner")
            {
                await UntilAsync(() => Task.FromResult(receiver.Payloads.Any(p => Text(p) == ids[^1].ToString())));
                entered[ids[0]].ShouldBe(1);
            }
            release.TrySetResult();
            clock.Advance(TimeSpan.FromSeconds(1));
            await UntilAsync(async () =>
            {
                await using var db = f.Db();
                return !await db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId != ids[0] || variant != "poison-page");
            });
            receiver.Payloads.Select(Text).Distinct().Count().ShouldBe(ids.Length - (variant == "poison-page" ? 1 : 0));
            if (variant == "poison-page")
            {
                await using var db = f.Db();
                var poison = await db.SessionRuntimeActions.SingleAsync();
                poison.AgentSessionId.ShouldBe(ids[0]); poison.Attempts.ShouldBeGreaterThan(0); poison.Phase.ShouldBe(0);
            }
        }
        finally { release.TrySetResult(); await worker.StopAsync(default); }
    }

    [Test]
    [Arguments("before-wake")]
    [Arguments("during-confirm")]
    [Arguments("after-input")]
    [Arguments("after-effect-before-ack")]
    [Arguments("uncooperative-effect")]
    [Arguments("reconnect-cohort")]
    public async Task C1259_shutdown_joins_and_restart_reconciles_submission(string variant)
    {
        if (variant == "after-input")
        {
            await using var clone = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = clone.ConnectionString });
            await h.InsertTurnAsync("previous", "previous answer");
            var message = await h.SeedPendingMessageAsync("one complete queued body");
            var prior = h.Adapter.OnSubmitted!;
            var submitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Adapter.OnSubmitted = async body => { await prior(body); submitted.TrySetResult(); await releaseInput.Task; };
            var coordinator = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
            await coordinator.RequestAsync(h.SessionId, "status", default);
            using var worker = new SessionRuntimeActionHostedService(coordinator);
            await worker.StartAsync(default);
            Task? stop = null;
            try
            {
                await submitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stop = worker.StopAsync(default);
                stop.IsCompleted.ShouldBeFalse("the submitted input callback is still owned");
            }
            finally { releaseInput.TrySetResult(); if (stop is not null) await stop; else await worker.StopAsync(default); }
            h.Adapter.OnSubmitted = prior;
            // Retry the durable action; the existing complete prompt must prevent another body.
            await coordinator.DrainPageAsync(h.SessionId, default);
            h.Adapter.SubmittedBodies.ShouldBe(["one complete queued body"]);
            await using var db = h.CreateObserverContext();
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == message)).DeliveryVerdict
                .ShouldBeOneOf(DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed);
            return;
        }
        var receiver = new Receiver();
        var fault = new AckFault { Armed = variant == "after-effect-before-ack" };
        await using var f = await SessionStateTestFixture.CreateAsync(fault, configure: s => s.AddSingleton<IEventBus>(receiver));
        var entry = f.Event(1, variant == "during-confirm" ? "UserPrompt" : "ToolResult", text: "complete original body");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (variant == "uncooperative-effect") receiver.BeforeReceive = async (_, _) => { entered.TrySetResult(); await release.Task; };
        SemaphoreSlim? queueGate = null;
        if (variant == "during-confirm")
        {
            await using var db = f.Db();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
                Sequence = 1, Status = QueuedMessageStatus.Sent, Origin = QueuedMessageOrigin.Delegation,
                ConversationKey = "refine:shutdown", Body = entry.Text!, CreatedAt = DateTime.UtcNow, SentAt = DateTime.UtcNow,
                LastDeliveryBaselineSequence = 0, DeliveryAttempts = 1 });
            await db.SaveChangesAsync();
            queueGate = f.Services.GetRequiredService<SessionMessageQueueService>().GetLock(f.SessionId);
            await queueGate.WaitAsync();
        }
        await f.Runtime.ObserveTranscriptAsync(entry, default);
        if (variant == "before-wake")
        {
            receiver.Payloads.ShouldBeEmpty();
            await using var cold = f.NewContainer();
            await RunUntilAsync(cold, async () => { await using var db = f.Db(); return !await db.SessionRuntimeActions.AnyAsync(); });
            receiver.Payloads.Select(Text).ShouldBe([entry.Text]); return;
        }
        using var first = new SessionRuntimeActionHostedService(Actions(f));
        await first.StartAsync(default);
        Task? stopping = null;
        try
        {
            if (variant == "uncooperative-effect")
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                stopping = first.StopAsync(default);
                stopping.IsCompleted.ShouldBeFalse();
                receiver.Payloads.ShouldBeEmpty();
            }
            else if (variant == "during-confirm")
            {
                await UntilAsync(async () => { await using var db = f.Db(); return await db.SessionRuntimeActions.AnyAsync(a => a.Phase == 2); });
                await first.StopAsync(default);
                await using var db = f.Db();
                (await db.SessionQueuedMessages.SingleAsync()).DeliveryVerdict.ShouldBeNull();
                (await db.SessionRuntimeActions.SingleAsync()).Phase.ShouldBe(2);
            }
            else if (variant == "after-effect-before-ack")
                await UntilAsync(async () => { await using var db = f.Db(); return await db.SessionRuntimeActions.AnyAsync(a => a.Attempts == 1); });
            else
                await UntilAsync(async () => { await using var db = f.Db(); return !await db.SessionRuntimeActions.AnyAsync(); });
        }
        finally
        {
            release.TrySetResult(); queueGate?.Release();
            if (stopping is not null) await stopping; else await first.StopAsync(default);
        }
        receiver.BeforeReceive = null;
        await using (var db = f.Db()) await db.SessionRuntimeActions.ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, DateTime.UtcNow));
        await using var restarted = f.NewContainer();
        await RunUntilAsync(restarted, async () => { await using var db = f.Db(); return !await db.SessionRuntimeActions.AnyAsync(); });
        if (variant == "during-confirm")
        { await using var db = f.Db(); (await db.SessionQueuedMessages.SingleAsync()).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed); }
        else receiver.Payloads.ShouldNotBeEmpty();
        if (variant == "after-effect-before-ack") fault.Hits.ShouldBe(1);
        var beforeReplay = receiver.Payloads.Count;
        await restarted.GetRequiredService<AgentSessionRuntime>().ObserveTranscriptAsync(entry, default);
        await restarted.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(f.SessionId, default);
        receiver.Payloads.Count.ShouldBe(beforeReplay);
    }

    [Test]
    [Arguments("retry-clock")]
    [Arguments("phase-failure")]
    [Arguments("prune-candidate")]
    [Arguments("prune-race")]
    [Arguments("correlation-retention")]
    public async Task C1259_retry_and_retention_preserve_pending_turn(string variant)
    {
        var receiver = new Receiver();
        var clock = DatabaseClock();
        var attempts = 0;
        receiver.BeforeReceive = (_, _) =>
        { Interlocked.Increment(ref attempts); throw new IOException("receiver unavailable"); };
        var phaseFault = new AckFault { Armed = variant == "phase-failure", Phase = 3 };
        await using var f = await SessionStateTestFixture.CreateAsync(phaseFault, clock: clock, configure: s => s.AddSingleton<IEventBus>(receiver));
        await f.Runtime.ObserveTranscriptAsync(f.Event(1, "ToolResult", text: "retained turn"), default);
        if (variant == "phase-failure")
        {
            receiver.BeforeReceive = null;
            await Actions(f).DrainPageAsync(f.SessionId, default);
            await using (var db = f.Db())
            { var failed = await db.SessionRuntimeActions.SingleAsync(); failed.Phase.ShouldBe(2); failed.Attempts.ShouldBe(1); }
            phaseFault.Hits.ShouldBe(1); receiver.Payloads.Count.ShouldBe(1);
            clock.Advance(TimeSpan.FromSeconds(1));
            await Actions(f).DrainPageAsync(f.SessionId, default);
            receiver.Payloads.Count.ShouldBe(1, "the acknowledged UI phase cannot run again after a later phase fails");
            await using var verify = f.Db(); (await verify.SessionRuntimeActions.CountAsync()).ShouldBe(0);
            return;
        }
        if (variant == "retry-clock")
        {
            foreach (var seconds in new[] { 1, 2, 4, 8, 16, 30 })
            {
                var at = clock.GetUtcNow().UtcDateTime;
                await Actions(f).DrainPageAsync(f.SessionId, default);
                await using var db = f.Db(); var debt = await db.SessionRuntimeActions.SingleAsync();
                debt.DueAt.ShouldBe(at.AddSeconds(seconds)); debt.Phase.ShouldBe(0);
                var before = attempts;
                await Actions(f).DrainPageAsync(f.SessionId, default);
                attempts.ShouldBe(before, "not due yet");
                clock.Advance(TimeSpan.FromSeconds(seconds));
            }
            receiver.BeforeReceive = null;
            await Actions(f).DrainPageAsync(f.SessionId, default);
            receiver.Payloads.Select(Text).ShouldBe(["retained turn"]);
            await using var verify = f.Db(); (await verify.SessionRuntimeActions.CountAsync()).ShouldBe(0);
            return;
        }
        await using var retentionDb = f.Db();
        var old = clock.GetUtcNow().UtcDateTime.AddDays(-100);
        await retentionDb.AgentSessions.Where(s => s.Id == f.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped).SetProperty(s => s.LastSeenAt, old));
        await retentionDb.TranscriptEntries.ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedAt, old));
        var queueId = Guid.NewGuid();
        retentionDb.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = queueId, AgentSessionId = f.SessionId,
            Sequence = 1, Body = "source correlation", CreatedAt = old, Status = QueuedMessageStatus.Sent,
            Origin = variant == "correlation-retention" ? QueuedMessageOrigin.Channel : QueuedMessageOrigin.Ui,
            ReviewThreadId = variant == "correlation-retention" ? Guid.NewGuid() : null,
            ChannelReplySettledAt = variant == "correlation-retention" ? old : null });
        await retentionDb.SaveChangesAsync();
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(retentionDb, Options.Create(new RetentionSettings()), audit,
            clock, NullLogger<DataRetentionService>.Instance, new AuditService(retentionDb, audit), f.Store);
        (await retention.PruneTranscriptsAsync(default)).ShouldBe(0);
        (await retention.PruneQueuedMessagesAsync(default)).ShouldBe(0);
        (await retention.PruneSessionsAsync(default)).ShouldBe(0);
        (await retentionDb.TranscriptEntries.CountAsync()).ShouldBe(1);
        (await retentionDb.SessionQueuedMessages.CountAsync()).ShouldBe(1);
        receiver.BeforeReceive = null;
        await Actions(f).DrainPageAsync(f.SessionId, default);
        if (variant == "prune-race")
        {
            var gate = new DeleteGate();
            await using var racingDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(retentionDb.Database.GetConnectionString()).AddInterceptors(gate).Options);
            var racing = new DataRetentionService(racingDb, Options.Create(new RetentionSettings()), audit,
                clock, NullLogger<DataRetentionService>.Instance, new AuditService(racingDb, audit), f.Store);
            var prune = racing.PruneTranscriptsAsync(default);
            Task? ingest = null;
            try
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                ingest = f.Runtime.ObserveTranscriptAsync(f.Event(2, "ToolResult", text: "new action after pruning"), default);
                ingest.IsCompleted.ShouldBeFalse("retention owns the actual mutation lease");
            }
            finally { gate.Release.TrySetResult(); await prune; if (ingest is not null) await ingest; }
            (await f.RowsAsync()).Select(t => t.Text).ShouldBe(["new action after pruning"]);
            await using var raceVerify = f.Db();
            (await raceVerify.SessionRuntimeActions.SingleAsync()).Sequence.ShouldBe(2);
            (await retention.PruneQueuedMessagesAsync(default)).ShouldBe(0);
            return;
        }
        (await retention.PruneTranscriptsAsync(default)).ShouldBe(1);
        if (variant == "correlation-retention")
        {
            (await retention.PruneQueuedMessagesAsync(default)).ShouldBe(0, "review consumption owns correlation retention independently of action completion");
            await retentionDb.SessionQueuedMessages.Where(m => m.Id == queueId)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.ReviewReplyConsumedAt, old));
        }
        (await retention.PruneQueuedMessagesAsync(default)).ShouldBe(1);
    }

    private static FakeTimeProvider DatabaseClock()
    {
        var now = DateTimeOffset.UtcNow;
        return new(new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero));
    }

    private sealed class DeleteGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"TranscriptEntries\"", StringComparison.Ordinal))
            { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
    }

    private sealed class AckFault : SaveChangesInterceptor
    {
        public bool Armed;
        public int Phase = 1;
        public int Hits;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<SessionRuntimeAction>().Any(e => e.State == EntityState.Modified && e.Entity.Phase == Phase))
            { Armed = false; Hits++; throw new IOException("accepted UI publication, acknowledgement did not commit"); }
            return ValueTask.FromResult(result);
        }
    }

    private static SessionRuntimeActionCoordinator Actions(SessionStateTestFixture f) => f.Services.GetRequiredService<SessionRuntimeActionCoordinator>();
    private static string? Text(object payload) => (string?)payload.GetType().GetProperty("text")!.GetValue(payload);

    private static async Task RunUntilAsync(ServiceProvider services, Func<Task<bool>> done)
    {
        using var worker = new SessionRuntimeActionHostedService(services.GetRequiredService<SessionRuntimeActionCoordinator>());
        await worker.StartAsync(default);
        try { await UntilAsync(done); }
        finally { await worker.StopAsync(default); }
    }

    private static async Task UntilAsync(Func<Task<bool>> done)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await done()) await Task.Delay(20, deadline.Token);
    }

    private sealed class Receiver : IEventBus
    {
        public ConcurrentQueue<object> Payloads { get; } = new();
        public Func<object, CancellationToken, Task>? BeforeReceive { get; set; }
        public async Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default)
        {
            if (name != "SessionTranscript") return;
            if (BeforeReceive is { } before) await before(payload, ct);
            Payloads.Enqueue(payload);
        }
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CommitFault(string variant, CancellationTokenSource cancel) : SaveChangesInterceptor
    {
        public int Hits;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var target = data.Context!.ChangeTracker.Entries<TranscriptEntry>().Any(e => e.State == EntityState.Added && e.Entity.Uuid == "target");
            if (!target) return ValueTask.FromResult(result);
            if (variant == "batch-fail") { Hits++; throw new InvalidOperationException("before transcript/action commit"); }
            if (variant == "fallback-fail") { Hits++; throw new DbUpdateException("row rejected", new PostgresException("planned", "ERROR", "ERROR", "XX000")); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (variant == "cancel-after-commit" && data.Context!.ChangeTracker.Entries<TranscriptEntry>().Any())
            { Hits++; cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            return ValueTask.FromResult(result);
        }
    }
}
