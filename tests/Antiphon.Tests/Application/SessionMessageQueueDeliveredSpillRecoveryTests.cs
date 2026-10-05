using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class SessionMessageQueueDeliveredSpillRecoveryTests
{
    [Test]
    public async Task C1056_Stranded_sweep_finds_delivered_spill_without_pending()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var cases = new List<SweepCase>
        {
            new("lone-obligation-releases"),
            new("ui-receipt-releases", Origin: QueuedMessageOrigin.Ui),
            new("non-always-on-releases", AlwaysOn: false),
            new("non-always-on-ui-releases", Origin: QueuedMessageOrigin.Ui, AlwaysOn: false),
            new("capped-receipt-releases", Attempts: 3),
            new("over-cap-receipt-releases", Attempts: 4),
            new("fresh-receipt-releases", AgeMinutes: 0),
            new("old-receipt-releases", AgeMinutes: 61),
            new("absent-recipient-releases", Absent: true),
            new("unavailable-transport-releases", Unavailable: true),
            new("all-adverse-releases", QueuedMessageOrigin.Ui, false, 4, 61,
                SessionStatus.Stopped, true, true),
        };
        cases.AddRange(Enum.GetValues<SessionStatus>().Where(s => s != SessionStatus.Running)
            .Select(s => new SweepCase("non-running-releases/" + s, Status: s)));
        foreach (var c in cases)
        {
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString,
                configureServices: s => s.AddSingleton<ISessionRunnerDirectory>(sp =>
                    new ReceiptRunnerDirectory(sp.GetRequiredService<ISessionRunnerClient>())));
            var directory = (ReceiptRunnerDirectory)f.H.Provider.GetRequiredService<ISessionRunnerDirectory>();
            await using (var db = f.Db())
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == f.H.SessionId);
                directory.Owner = new(session.RunnerId!, session.RunnerStoreId!.Value, session.RunnerCwd!);
                directory.SessionId = session.Id;
                directory.StartedAt = session.StartedAt;
            }
            if (c.Attempts == 1) await f.ScreenAsync(c.Origin);
            else // Historical cap obligations; never alter the primary reproduction's attempt floor.
                await f.SeedAsync(change: m => { m.DeliveryAttempts = c.Attempts; m.Origin = c.Origin; });
            await f.PublishAsync(f.Captured.SingleOrDefault() ?? f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
            await f.PublishAsync(null, TranscriptKinds.TurnEnd, f.H.Now);
            f.Clock.Advance(TimeSpan.FromMinutes(c.AgeMinutes));
            await using (var db = f.Db())
            {
                await db.Agents.Where(a => a.Id == f.H.AgentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.AlwaysOn, c.AlwaysOn));
                await db.AgentSessions.Where(s => s.Id == f.H.SessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, c.Status));
                (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.H.SessionId))
                    .ShouldBe(1, "lone-obligation-no-companion");
                (await SessionMessageQueueService.IsWorkingAsync(db, f.H.SessionId, CancellationToken.None))
                    .ShouldBeFalse("sweep-idle-control");
            }
            if (c.Absent)
            {
                f.H.Runtime.TryRemove(f.H.SessionId, out var removed).ShouldBeTrue();
                removed.ShouldBeSameAs(f.H.Adapter);
                directory.Absent = true;
                (await f.H.Runner.ListAsync(CancellationToken.None)).ShouldBeEmpty();
                f.H.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(f.H.SessionId);
            }
            else f.H.Runtime.ListLiveOrUnknownSessions().ShouldContain(f.H.SessionId);
            directory.Unavailable = c.Unavailable;
            if (c.Unavailable)
            {
                var ex = await Should.ThrowAsync<ServiceUnavailableException>(() =>
                    f.H.Runtime.EnsureInputTransportAvailableAsync(f.H.SessionId, null, CancellationToken.None));
                ex.ShouldNotBeNull("typed-input-transport-refusal");
            }
            var resolves = directory.ResolveCalls;
            var from = f.H.Now;
            (await f.H.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
                .ShouldBe(0, "receipt-sweep-deliveries-zero / " + c.Label);
            await f.ReleasedAsync(c.Label, from);
            directory.ResolveCalls.ShouldBe(resolves, "receipt-never-resolves-input-transport");
        }
    }

    [Test]
    public async Task C1056_Reconcile_while_working_does_not_send_pending()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var entry in new[] { "flush-session", "flush-if-idle", "sweep" })
        {
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
            await f.ScreenAsync();
            var receipt = f.StageReceipt();
            var sem = f.H.Queue.GetLock(f.H.SessionId);
            await sem.WaitAsync();
            var completed = false;
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { completed = await f.H.Runtime.CatchUpTranscriptAsync(f.H.SessionId, budget.Token); }
                catch (OperationCanceledException) { }
            }
            finally { sem.Release(); }
            completed.ShouldBeTrue("catchup-under-lock-completes");
            await f.AssertReceiptAsync(receipt);
            await f.RetainedAsync("catchup-alone-retains");
            // A fresh UserPrompt through Sync (without TurnEnd) also must not flush the queue.
            await f.PublishAsync("independent current work", TranscriptKinds.UserPrompt, f.H.Now);
            var extra = new SessionRunnerTranscriptEvent(f.H.SessionId, 3, TranscriptKinds.UserPrompt,
                Guid.NewGuid().ToString("N"), null, new DateTimeOffset(f.H.Now), "user",
                "sync-only current work", null, null, null, false, null);
            f.H.Runner.SetTranscript(new(f.H.SessionId, [receipt, extra], 3));
            await f.H.Runtime.SyncTranscriptAsync(f.H.SessionId, CancellationToken.None);
            await using (var db = f.Db())
                (await db.TranscriptEntries.AsNoTracking().SingleAsync(e => e.AgentSessionId == f.H.SessionId
                    && e.Uuid == extra.Uuid)).Text.ShouldBe(extra.Text, "sync-user-prompt-committed");
            await f.RetainedAsync("sync-without-turn-end-retains");
            await using (var db = f.Db())
                (await SessionMessageQueueService.IsWorkingAsync(db, f.H.SessionId, CancellationToken.None))
                    .ShouldBeTrue("receipt-recipient-is-working");
            await f.H.Queue.EnqueueAsync(f.H.SessionId, "unrelated pending body", MessageSendMode.WhenIdle,
                CancellationToken.None);
            SessionQueuedMessage pending;
            await using (var db = f.Db())
            {
                pending = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m =>
                    m.AgentSessionId == f.H.SessionId && m.Id != f.Before.Id);
                pending.Status.ShouldBe(QueuedMessageStatus.Pending, "busy-pending-untouched");
                pending.DeliveryAttempts.ShouldBe(0, "busy-pending-untouched");
            }
            f.NoInput();
            await sem.WaitAsync();
            var canceled = false;
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await f.H.Queue.FlushIfIdleAsync(f.H.SessionId, budget.Token); }
                catch (OperationCanceledException) { canceled = budget.IsCancellationRequested; }
                canceled.ShouldBeTrue("receipt-flush-waits-for-session-lock");
                await f.RetainedAsync("locked-flush-no-mutation");
            }
            finally { sem.Release(); }
            var from = f.H.Now;
            if (entry == "sweep")
                (await f.H.Queue.FlushStrandedQueuesAsync(CancellationToken.None)).ShouldBe(0);
            else await f.FlushAsync(entry);
            await f.ReleasedAsync("busy-receipt-releases / " + entry, from);
            await using (var db = f.Db())
            {
                var after = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == pending.Id);
                DeliveredSpillFixture.Identity(after).ShouldBe(DeliveredSpillFixture.Identity(pending),
                    "busy-pending-untouched");
                after.DeliveryVerdict.ShouldBe(pending.DeliveryVerdict, "busy-pending-untouched");
                after.RemoteSpillBody.ShouldBe(pending.RemoteSpillBody, "busy-pending-untouched");
            }
            f.NoInput();
        }
    }

    [Test]
    public async Task C1056_Fresh_graph_reconciles_committed_receipt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var cut in new[] { "enqueue-refused", "queued-before-flush", "screen-before-ingestion", "receipt-before-release" })
        {
            var fault = new ReceiptSaveFault();
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString, interceptor: fault);
            if (cut == "enqueue-refused")
            {
                fault.RefuseQueueInsert = true;
                await Should.ThrowAsync<ReceiptSaveException>(() => f.EnqueueSpillAsync());
                fault.Hits.ShouldBe(1, "enqueue-fault-hit");
                f.H.Adapter.Inputs.ShouldBeEmpty("failed-enqueue-zero-input");
                f.Captured.ShouldBeEmpty("failed-enqueue-zero-submissions");
                await using (var db = f.Db())
                    (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.H.SessionId)).ShouldBe(0);
                (await f.TranscriptCountAsync()).ShouldBe(0);
                fault.RefuseQueueInsert = false;
                await f.RecreateAsync();
            }
            if (cut == "queued-before-flush")
            {
                await f.EnqueueSpillAsync(deliverIfIdle: false);
                SessionQueuedMessage queued;
                await using (var db = f.Db())
                    queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == f.H.SessionId);
                queued.Status.ShouldBe(QueuedMessageStatus.Pending);
                queued.DeliveryAttempts.ShouldBe(0);
                queued.RemoteSpillBody.ShouldNotBeNull();
                f.H.Adapter.Inputs.ShouldBeEmpty();
                await f.RecreateAsync();
                await using (var db = f.Db())
                {
                    var after = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == queued.Id);
                    DeliveredSpillFixture.Identity(after).ShouldBe(DeliveredSpillFixture.Identity(queued));
                    after.RemoteSpillBody.ShouldBe(queued.RemoteSpillBody);
                }
                await f.FlushAsync("flush-session");
                await f.CaptureScreenAsync();
                f.Before.Id.ShouldBe(queued.Id, "durable-queue-id-survives-first-flush");
            }
            else await f.ScreenAsync();
            if (cut == "screen-before-ingestion")
            {
                await f.RecreateAsync();
                await f.RetainedAsync("screen-cut-preserves-obligation");
            }
            var receipt = f.StageReceipt();
            await f.H.Runtime.CatchUpTranscriptAsync(f.H.SessionId, CancellationToken.None);
            await f.AssertReceiptAsync(receipt);
            await f.RetainedAsync("committed-receipt-awaits-reconciliation");
            await f.RecreateAsync();
            var from = f.H.Now;
            await f.FlushAsync();
            await f.ReleasedAsync("fresh-graph-releases / " + cut, from);
            var released = await f.RowAsync();
            await f.RecreateAsync();
            await f.H.Runtime.CatchUpTranscriptAsync(f.H.SessionId, CancellationToken.None);
            await f.H.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            (await f.RowAsync()).DeliveryVerdictAt.ShouldBe(released.DeliveryVerdictAt, "release-survives-recreation");
            await f.AssertReceiptAsync(receipt);
            (await f.TranscriptCountAsync()).ShouldBe(1, "replayed-receipt-deduplicated");
            f.Captured.ShouldHaveSingleItem("one-recipient-submission-across-graphs");
            f.NoInput();
        }
    }

    [Test]
    public async Task C1056_Failed_release_commit_retries_atomically()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var cut in new[] { "ingestion", "release", "publication" })
        {
            var fault = new ReceiptSaveFault();
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString, interceptor: fault,
                configureServices: s => s.AddSingleton<IEventBus>(sp =>
                    new ReceiptEventBus(sp.GetRequiredService<MockEventBus>())));
            await f.ScreenAsync();
            var receipt = f.StageReceipt();
            // A DTO in the runner is not committed evidence, even when its complete text matches.
            await f.FlushAsync();
            await f.RetainedAsync("uncommitted-receipt-retains");
            (await f.TranscriptCountAsync()).ShouldBe(0, "runner-snapshot-not-persisted-by-reconcile");
            if (cut == "ingestion")
            {
                fault.RefuseTranscriptUuid = receipt.Uuid;
                (await f.H.Runtime.CatchUpTranscriptAsync(f.H.SessionId, CancellationToken.None)).ShouldBeFalse();
                fault.Hits.ShouldBe(1, "ingestion-fault-hit");
                (await f.TranscriptCountAsync()).ShouldBe(0, "failed-ingestion-no-durable-receipt");
                await f.FlushAsync();
                await f.RetainedAsync("failed-ingestion-retains");
                fault.RefuseTranscriptUuid = null;
                await f.RecreateAsync();
            }
            await f.H.Runtime.CatchUpTranscriptAsync(f.H.SessionId, CancellationToken.None);
            await f.AssertReceiptAsync(receipt);
            f.H.EventBus.Clear();
            if (cut == "release")
            {
                fault.RefuseReleaseId = f.Before.Id;
                await Should.ThrowAsync<ReceiptSaveException>(() => f.FlushAsync());
                fault.Hits.ShouldBe(1, "release-fault-hit");
                await f.RetainedAsync("release-failure-atomic");
                f.H.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionQueueChanged",
                    "failed-release-no-publication");
                fault.RefuseReleaseId = null;
                await f.RecreateAsync();
            }
            var from = f.H.Now;
            if (cut == "publication")
            {
                f.H.EventBus.ThrowOnceOnEvent = "SessionQueueChanged";
                await f.FlushAsync(); // Publication is best-effort; observe the fault at the bus boundary.
                ((ReceiptEventBus)f.H.Provider.GetRequiredService<IEventBus>()).FaultHits
                    .ShouldBe(1, "publication-fault-hit");
                f.H.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionQueueChanged",
                    "failed-publication-no-refresh-event");
            }
            else await f.FlushAsync();
            await f.ReleasedAsync("recovered-atomic-release", from);
            var releasedAt = (await f.RowAsync()).DeliveryVerdictAt;
            await f.RecreateAsync();
            var view = await f.H.Queue.GetQueueAsync(f.H.SessionId, CancellationToken.None);
            view.SessionId.ShouldBe(f.H.SessionId, "post-commit-queue-readable");
            view.Messages.ShouldBeEmpty("post-commit-queue-has-no-redelivery");
            await f.FlushAsync();
            (await f.RowAsync()).RemoteSpillBody.ShouldBeNull("post-commit-release-survives");
            (await f.RowAsync()).DeliveryVerdictAt.ShouldBe(releasedAt);
            await f.AssertReceiptAsync(receipt);
            f.Captured.ShouldHaveSingleItem();
            f.NoInput();
        }
    }

    [Test]
    public async Task C1056_Confirmation_preserves_attempt_identity_and_is_idempotent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
        await f.ScreenAsync();
        var receipt = f.StageReceipt();
        await f.H.Runtime.CatchUpTranscriptAsync(f.H.SessionId, CancellationToken.None);
        await f.AssertReceiptAsync(receipt);
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        var secondId = await f.H.SeedPendingMessageAsync("distinct unrelated spill wire", deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent, deliveryVerdict: DeliveryVerdict.Delivered);
        SessionQueuedMessage second;
        await using (var db = f.Db())
        {
            await db.AgentSessions.Where(s => s.Id == f.H.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, f.H.Now));
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == secondId);
            row.RemoteSpillBody = "other spill owned bytes";
            row.RemoteSpillRelativePath = TypedBodySpill.InboxRelativePath(secondId.ToString("D"));
            await db.SaveChangesAsync();
        }
        await using (var db = f.Db())
            second = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == secondId);
        var transcriptCount = await f.TranscriptCountAsync();
        f.H.EventBus.Clear();
        var from = f.H.Now;
        await f.FlushAsync();
        var released = await f.RowAsync();
        DeliveredSpillFixture.Identity(released).ShouldBe(DeliveredSpillFixture.Identity(f.Before),
            "attempt-identity-preserved");
        await f.ReleasedAsync("old-generation-receipt-releases", from);
        foreach (var entry in new[] { "flush-session", "sweep", "recreate" })
        {
            if (entry == "recreate") await f.RecreateAsync();
            else if (entry == "sweep") (await f.H.Queue.FlushStrandedQueuesAsync(CancellationToken.None)).ShouldBe(0);
            else await f.FlushAsync(entry);
            var after = await f.RowAsync();
            DeliveredSpillFixture.Identity(after).ShouldBe(DeliveredSpillFixture.Identity(f.Before), "attempt-identity-preserved");
            after.DeliveryVerdictAt.ShouldBe(released.DeliveryVerdictAt, "idempotent-release-time");
            after.RemoteSpillBody.ShouldBeNull();
            await using var db = f.Db();
            var other = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == secondId);
            DeliveredSpillFixture.Identity(other).ShouldBe(DeliveredSpillFixture.Identity(second), "other-spill-unchanged");
            other.RemoteSpillBody.ShouldBe(second.RemoteSpillBody, "other-spill-owned");
            other.DeliveryVerdict.ShouldBe(second.DeliveryVerdict);
            other.DeliveryVerdictAt.ShouldBe(second.DeliveryVerdictAt);
            (await f.TranscriptCountAsync()).ShouldBe(transcriptCount, "idempotent-transcript-count");
            f.H.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionFinished");
            f.NoInput();
        }
    }

    private sealed record SweepCase(string Label, QueuedMessageOrigin Origin = QueuedMessageOrigin.Delegation,
        bool AlwaysOn = true, int Attempts = 1, int AgeMinutes = 1,
        SessionStatus Status = SessionStatus.Running, bool Absent = false, bool Unavailable = false);

    private sealed class ReceiptSaveException : Exception;

    private sealed class ReceiptEventBus(MockEventBus inner) : IEventBus
    {
        public int FaultHits { get; private set; }
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) =>
            ObserveAsync(() => inner.PublishToGroupAsync(group, eventName, payload, ct));
        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) =>
            ObserveAsync(() => inner.PublishToAllAsync(eventName, payload, ct));

        private async Task ObserveAsync(Func<Task> publish)
        {
            try { await publish(); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("MockEventBus throw-once", StringComparison.Ordinal))
            {
                FaultHits++;
                throw;
            }
        }
    }

    private sealed class ReceiptSaveFault : SaveChangesInterceptor
    {
        public bool RefuseQueueInsert { get; set; }
        public string? RefuseTranscriptUuid { get; set; }
        public Guid? RefuseReleaseId { get; set; }
        public int Hits { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            var refuse = RefuseQueueInsert && db.ChangeTracker.Entries<SessionQueuedMessage>()
                .Any(e => e.State == EntityState.Added);
            refuse |= RefuseTranscriptUuid is { } uuid && db.ChangeTracker.Entries<TranscriptEntry>()
                .Any(e => e.State == EntityState.Added && e.Entity.Uuid == uuid);
            // Deliberately allow verdict-only saves: a split verdict/body commit must fail atomicity.
            refuse |= RefuseReleaseId is { } id && db.ChangeTracker.Entries<SessionQueuedMessage>()
                .Any(e => e.Entity.Id == id && e.State == EntityState.Modified
                    && e.Property(m => m.RemoteSpillBody).OriginalValue != null && e.Entity.RemoteSpillBody == null);
            if (refuse) { Hits++; throw new ReceiptSaveException(); }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReceiptRunnerDirectory(ISessionRunnerClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => client;
        public SessionRunnerOwner Owner { get; set; } = null!;
        public Guid SessionId { get; set; }
        public DateTime StartedAt { get; set; }
        public bool Absent { get; set; }
        public bool Unavailable { get; set; }
        public int ResolveCalls { get; private set; }
        public IReadOnlyList<string> KnownRunnerIds => ["c1056-fixture"];
        public ISessionRunnerClient Resolve(string? runnerId)
        {
            ResolveCalls++;
            if (Unavailable) throw new ServiceUnavailableException("fixture input unavailable", PhoneHomeProblemTypes.Unavailable);
            return client;
        }
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(Owner);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(new SessionRunnerBinding.Remote(Owner));
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Available(Absent ? [] :
                [new(SessionId, null, StartedAt, "Running", null, AgentExitReason.Unknown, 0)]));
        public IReadOnlyCollection<Guid> LiveRemoteSessionIds() => Absent ? [] : [SessionId];
        public Guid? GetLiveStoreId(string? runnerId) => Owner?.RunnerStoreId;
    }
}
