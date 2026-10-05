using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundCaptureTests
{
    [Test]
    public async Task C519_Capture_precedes_all_preparation()
    {
        await using var world = await World.CreateAsync();
        foreach (var kind in new[] { "main", "machine", "trailing" })
        foreach (var fault in new[] { "none", "capture", "attachment", "prompt", "stage" })
        {
            var members = await world.SeedMembersAsync(3, kind == "machine");
            var prompt = world.NextPrompt++;
            Guid? rootId = null;
            if (kind == "trailing")
            {
                await using var rootDb = world.Db();
                var root = await world.Service(rootDb).CaptureAsync(world.Route,
                    world.Source(prompt, members), ChannelReplyPreparation.Describe("root"), world.Bridge, default);
                rootId = root.Id;
            }
            var source = world.Source(prompt, kind == "trailing" ? [] : members, kind,
                first: kind == "trailing" ? prompt + 4 : prompt + 1,
                last: kind == "trailing" ? prompt + 5 : prompt + 2);
            var body = ChannelReplyPreparation.Describe("original [[attach:/source.md]]", ["/source.md"]);
            var readerCalls = 0;
            var captureFaultFired = false;
            var preparationFaultFired = false;
            Guid deliveryId = Guid.Empty;
            async Task AssertCapturedAsync(string operation)
            {
                readerCalls++;
                await using var observer = world.Db();
                var stored = await observer.ChannelOutboundDeliveries.SingleAsync(d => d.Id == deliveryId);
                stored.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
                stored.InputPath.ShouldBeEmpty();
                stored.PublishedAt.ShouldBeNull();
                stored.PreparationDeadlineAt.ShouldNotBeNull();
                var capture = ChannelReplyPreparation.Deserialize(stored.CaptureJson!);
                capture.Body.OriginalResponse.ShouldBe(body.OriginalResponse);
                capture.Route.ReplyHandle.ShouldBe("native-original-handle");
                capture.Route.ReplyToMessageId.ShouldBe("original-message");
                capture.Tasks.Select(t => t.TaskId).ShouldBe(world.TaskIds);
                var rows = await observer.SessionQueuedMessages.Where(m => members.Contains(m.Id)).ToListAsync();
                rows.Count.ShouldBe(3);
                rows.ShouldAllBe(m => m.ChannelOutboundDeliveryId == (rootId ?? deliveryId)
                    && m.ChannelReplySettledAt == null);
                if (kind == "trailing")
                    (await observer.ChannelOutboundDeliveries.SingleAsync(d => d.Id == rootId))
                        .ReservedThroughSequence.ShouldBe(source.LastTextSequence);
                if (fault == operation)
                {
                    preparationFaultFired = true;
                    throw new IOException("injected " + operation);
                }
            }
            var reader = new Reader(AssertCapturedAsync);
            var store = new Store(async () => await AssertCapturedAsync("stage"));
            await using var db = world.Db();
            var service = world.Service(db, store);
            service.ProbeBarrierAsync = async (barrier, id, _) =>
            {
                if (barrier != "capture-before-commit") return;
                // An independent connection cannot see the uncommitted intent/claims.
                await using var observer = world.Db();
                (await observer.ChannelOutboundDeliveries.AnyAsync(d => d.Id == id)).ShouldBeFalse();
                readerCalls.ShouldBe(0);
                if (fault == "capture")
                {
                    captureFaultFired = true;
                    throw new IOException("injected capture commit refusal");
                }
            };
            async Task ExerciseAsync()
            {
                var delivery = await service.CaptureAsync(world.Route, source, body, world.Bridge, default, rootId);
                deliveryId = delivery.Id;
                // The production helper opens descriptors only after capture returns committed.
                var prepared = await new ChannelReplyPreparation(reader).PrepareAsync(delivery, default);
                prepared.Reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe("original bytes"u8.ToArray());
                prepared.PromptText.ShouldBe("frozen converter goal");
                await store.StageAsync(delivery.Id, prepared.Reply, default, prepared.SourceManifestJson);
            }
            if (fault == "none") await ExerciseAsync();
            else await Should.ThrowAsync<IOException>(ExerciseAsync);
            await using var check = world.Db();
            var owned = await check.ChannelOutboundDeliveries.Where(d => d.PromptSequence == prompt).ToListAsync();
            if (fault == "capture")
            {
                captureFaultFired.ShouldBeTrue();
                readerCalls.ShouldBe(0);
                store.Calls.ShouldBe(0);
                owned.Count.ShouldBe(kind == "trailing" ? 1 : 0);
                if (rootId != null) owned.Single().ReservedThroughSequence.ShouldBe(prompt + 2);
            }
            else
            {
                if (fault != "none") preparationFaultFired.ShouldBeTrue();
                owned.Count.ShouldBe(kind == "trailing" ? 2 : 1);
                owned.ShouldAllBe(d => d.State == ChannelOutboundDeliveryState.Captured && d.PublishedAt == null);
            }
            (await check.SessionQueuedMessages.Where(m => members.Contains(m.Id)).ToListAsync())
                .ShouldAllBe(m => m.ChannelReplySettledAt == null);
            world.Producer.Calls.ShouldBe(0);
        }
    }

    [Test]
    public async Task C519_Batch_members_share_one_owner()
    {
        await using var world = await World.CreateAsync();
        foreach (var machine in new[] { false, true })
        {
            var members = await world.SeedMembersAsync(3, machine);
            await using var db = world.Db();
            var delivery = await world.Service(db).CaptureAsync(world.Route,
                world.Source(world.NextPrompt++, members, machine ? "machine" : "main"),
                ChannelReplyPreparation.Describe("batch answer"), world.Bridge, default);
            await using var check = world.Db();
            var rows = await check.SessionQueuedMessages.Where(m => members.Contains(m.Id)).ToListAsync();
            rows.Count.ShouldBe(3);
            rows.ShouldAllBe(m => m.ChannelOutboundDeliveryId == delivery.Id && m.ChannelReplySettledAt == null);
            var capture = ChannelReplyPreparation.Deserialize((await check.ChannelOutboundDeliveries.SingleAsync(d => d.Id == delivery.Id)).CaptureJson!);
            capture.MemberIds.Order().ShouldBe(members.Order());
            capture.Tasks.Select(t => t.TaskId).ShouldBe(world.TaskIds);
            delivery.PreparationDeadlineAt.ShouldBe(world.ObligationAt.AddMinutes(world.Bridge.PendingReplyTtlMinutes));
        }
        world.Producer.Calls.ShouldBe(0);
    }

    [Test]
    public async Task C519_Existing_source_owner_is_not_replaced()
    {
        await using var world = await World.CreateAsync();
        var members = await world.SeedMembersAsync(3);
        await using var firstDb = world.Db();
        await using var secondDb = world.Db();
        // Force a stale local view in the loser before the winner commits.
        await secondDb.SessionQueuedMessages.Where(m => members.Contains(m.Id)).LoadAsync();
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var competing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstService = world.Service(firstDb);
        firstService.ProbeBarrierAsync = async (barrier, _, ct) =>
        {
            if (barrier != "capture-before-commit") return;
            locked.SetResult();
            await competing.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        };
        var prompt = world.NextPrompt++;
        var first = firstService.CaptureAsync(world.Route, world.Source(prompt, members[..2]),
            ChannelReplyPreparation.Describe("winner"), world.Bridge, default);
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var second = world.Service(secondDb).CaptureAsync(world.Route,
            world.Source(prompt + 10, members[1..]), ChannelReplyPreparation.Describe("loser different payload"), world.Bridge, default);
        competing.SetResult();
        var deliveries = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        deliveries[1].Id.ShouldBe(deliveries[0].Id);
        await using var check = world.Db();
        (await check.ChannelOutboundDeliveries.CountAsync()).ShouldBe(1);
        var rows = await check.SessionQueuedMessages.Where(m => members.Contains(m.Id)).OrderBy(m => m.Sequence).ToListAsync();
        rows[0].ChannelOutboundDeliveryId.ShouldBe(deliveries[0].Id);
        rows[1].ChannelOutboundDeliveryId.ShouldBe(deliveries[0].Id);
        rows[2].ChannelOutboundDeliveryId.ShouldBeNull();
        ChannelReplyPreparation.Deserialize(deliveries[1].CaptureJson!).Body.OriginalResponse.ShouldBe("winner");
        world.Store.Calls.ShouldBe(0);
        world.Producer.Calls.ShouldBe(0);
    }

    [Test]
    public async Task C519_Growing_main_window_reuses_its_root()
    {
        await using var world = await World.CreateAsync();
        var members = await world.SeedMembersAsync(3);
        var prompt = world.NextPrompt++;
        await using var firstDb = world.Db();
        var first = await world.Service(firstDb).CaptureAsync(world.Route, world.Source(prompt, members),
            ChannelReplyPreparation.Describe("original short answer"), world.Bridge, default);
        await using var secondDb = world.Db();
        // Empty members isolates root-identity dedupe from source membership dedupe.
        var second = await world.Service(secondDb).CaptureAsync(world.Route,
            world.Source(prompt, [], "machine", last: prompt + 20),
            ChannelReplyPreparation.Describe("answer with more text"), world.Bridge, default);
        second.Id.ShouldBe(first.Id);
        await using var check = world.Db();
        (await check.ChannelOutboundDeliveries.CountAsync()).ShouldBe(1);
        var stored = await check.ChannelOutboundDeliveries.SingleAsync();
        stored.LastTextSequence.ShouldBe(prompt + 2);
        stored.ReservedThroughSequence.ShouldBe(prompt + 2);
        ChannelReplyPreparation.Deserialize(stored.CaptureJson!).Body.OriginalResponse.ShouldBe("original short answer");
        world.Store.Calls.ShouldBe(0);
        world.Producer.Calls.ShouldBe(0);
    }

    [Test]
    public async Task C519_Reservation_and_cursor_commit_together()
    {
        await using var world = await World.CreateAsync();
        var members = await world.SeedMembersAsync(3);
        var prompt = world.NextPrompt++;
        await using var rootDb = world.Db();
        var root = await world.Service(rootDb).CaptureAsync(world.Route, world.Source(prompt, members),
            ChannelReplyPreparation.Describe("root answer"), world.Bridge, default);
        var fragment = ChannelReplyPreparation.Describe("recoverable trailing fragment");
        var source = world.Source(prompt, members, "trailing", prompt + 3, prompt + 6);
        // Fault the child insert itself, then separately refuse commit after cursor update.
        foreach (var atInsert in new[] { true, false })
        {
            var interceptor = new TailInsertFault();
            await using var failingDb = world.Db(atInsert ? interceptor : null);
            var service = world.Service(failingDb);
            var commitFaultFired = false;
            service.ProbeBarrierAsync = (barrier, _, _) =>
            {
                if (barrier == "capture-before-commit")
                {
                    commitFaultFired = true;
                    throw new IOException("injected reservation commit refusal");
                }
                return Task.CompletedTask;
            };
            await Should.ThrowAsync<IOException>(() => service.CaptureAsync(world.Route, source, fragment,
                world.Bridge, default, root.Id));
            (atInsert ? interceptor.Fired : commitFaultFired).ShouldBeTrue();
            await using var check = world.Db();
            var unchanged = await check.ChannelOutboundDeliveries.SingleAsync(d => d.Id == root.Id);
            unchanged.ReservedThroughSequence.ShouldBe(prompt + 2);
            unchanged.Version.ShouldBe(0);
            (await check.ChannelOutboundDeliveries.CountAsync()).ShouldBe(1);
        }
        // Recreate the service to recover the unreserved fragment, then race duplicate callers.
        await using var recoveryDb = world.Db();
        await using var competingDb = world.Db();
        var results = await Task.WhenAll(
            world.Service(recoveryDb).CaptureAsync(world.Route, source, fragment, world.Bridge, default, root.Id),
            world.Service(competingDb).CaptureAsync(world.Route, source, fragment, world.Bridge, default, root.Id));
        results[0].Id.ShouldBe(results[1].Id);
        await using var finalDb = world.Db();
        var advanced = await finalDb.ChannelOutboundDeliveries.SingleAsync(d => d.Id == root.Id);
        advanced.ReservedThroughSequence.ShouldBe(prompt + 6);
        advanced.Version.ShouldBe(1);
        var tail = await finalDb.ChannelOutboundDeliveries.SingleAsync(d => d.RootDeliveryId == root.Id);
        tail.FirstTextSequence.ShouldBe(prompt + 3);
        tail.LastTextSequence.ShouldBe(prompt + 6);
        tail.PublishedAt.ShouldBeNull();
        ChannelReplyPreparation.Deserialize(tail.CaptureJson!).Body.OriginalResponse.ShouldBe(fragment.OriginalResponse);
        (await finalDb.SessionQueuedMessages.Where(m => members.Contains(m.Id)).ToListAsync())
            .ShouldAllBe(m => m.ChannelOutboundDeliveryId == root.Id && m.ChannelReplySettledAt == null);
        world.Producer.Calls.ShouldBe(0);
    }

    private sealed class TailInsertFault : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ChannelOutboundDelivery>().Any(e =>
                    e.State == EntityState.Added && e.Entity.RootDeliveryId != null))
            {
                Fired = true;
                throw new IOException("injected child insert refusal");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Reader(Func<string, Task> observe) : IChannelReplyAttachmentReader
    {
        public async Task<byte[]> ReadAttachmentAsync(string path, CancellationToken ct)
        { await observe("attachment"); return "original bytes"u8.ToArray(); }
        public async Task<string> ReadTextAsync(string path, CancellationToken ct)
        { await observe("prompt"); return "frozen converter goal"; }
    }

    private sealed class Store(Func<Task>? observe = null) : IChannelOutboundFileStore
    {
        public int Calls { get; private set; }
        public async Task<ChannelOutboundSnapshot> StageAsync(Guid id, ChannelReply reply, CancellationToken ct, string? manifest = null)
        {
            Calls++;
            if (observe != null) await observe();
            return new("snapshot.json", "hash", "request.json", "output");
        }
        public Task<ChannelReply> ReadReplyAsync(string path, string hash, CancellationToken ct) => throw new NotSupportedException();
        public Task<ChannelOutboundSealed> ValidateAndSealAsync(Guid id, string path, string hash, int max, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Producer : IAntiphonMessagingProducer
    {
        public int Calls { get; private set; }
        public Task SendAsync(ChannelReply reply, CancellationToken ct = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class World(IsolatedTestSchema schema) : IAsyncDisposable
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid[] TaskIds { get; } = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        public ChannelBridgeSettings Bridge { get; } = new();
        public Store Store { get; } = new();
        public Producer Producer { get; } = new();
        public DateTime ObligationAt { get; } = DateTime.UtcNow.AddMinutes(-5);
        public long NextPrompt { get; set; } = 100;
        private long _memberSequence;
        private readonly Guid _converter = Guid.NewGuid();
        private readonly Guid _inbound = Guid.NewGuid();
        private readonly Guid _board = Guid.NewGuid();
        private readonly Guid _project = Guid.NewGuid();
        public ChannelReply Route { get; } = new() { Channel = "slack", ConversationId = "capture-chat",
            ReplyHandle = "native-original-handle", ReplyToMessageId = "original-message", Kind = ChannelReplyKind.Question };
        public AppDbContext Db(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            if (interceptor != null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        public ChannelOutboundService Service(AppDbContext db, Store? store = null) => new(db, store ?? Store, Producer,
            Options.Create(new ChannelOutboundSettings { Profiles = new() { ["captured-profile"] = new()
            { ProjectId = _project, AgentId = _converter, PromptFile = "converter.md", Trigger = ChannelOutboundTrigger.EveryAgentReply } } }),
            TimeProvider.System);
        public ChannelOutboundSource Source(long prompt, Guid[] members, string kind = "main", long? first = null, long? last = null) =>
            new(SessionId, prompt, first ?? prompt + 1, last ?? prompt + 2, kind, members);
        public async Task<Guid[]> SeedMembersAsync(int count, bool machine = false)
        {
            await using var db = Db();
            var rows = Enumerable.Range(0, count).Select(i => new SessionQueuedMessage { Id = Guid.NewGuid(),
                AgentSessionId = SessionId, Body = "original prompt " + i, Sequence = ++_memberSequence,
                Origin = machine ? QueuedMessageOrigin.Delegation : QueuedMessageOrigin.Channel,
                SourceTaskId = TaskIds[i], Status = QueuedMessageStatus.Sent,
                CreatedAt = ObligationAt.AddSeconds(i), SentAt = ObligationAt }).ToArray();
            db.SessionQueuedMessages.AddRange(rows);
            await db.SaveChangesAsync();
            return rows.Select(r => r.Id).ToArray();
        }
        public static async Task<World> CreateAsync()
        {
            var world = new World(await TestDbFixture.CreateIsolatedSchemaAsync());
            await using var db = world.Db();
            db.AgentSessions.Add(new AgentSession { Id = world.SessionId, Cwd = Path.GetTempPath(),
                CreatedAt = world.ObligationAt, StartedAt = world.ObligationAt, LastSeenAt = world.ObligationAt });
            db.Projects.Add(new Project { Id = world._project, Name = "capture-project" });
            db.Boards.Add(new Board { Id = world._board, ProjectId = world._project, Name = "capture-board" });
            db.Agents.AddRange(new Agent { Id = world._converter, Name = "converter", Slug = "converter",
                    BoardId = world._board, WorkingDirectory = Path.GetTempPath() },
                new Agent { Id = world._inbound, Name = "inbound", Slug = "inbound",
                    BoardId = world._board, WorkingDirectory = Path.GetTempPath() });
            db.ChatChannels.Add(new ChatChannel { Id = Guid.NewGuid(), Provider = "slack", ExternalId = "capture-chat",
                AgentId = world._inbound, Enabled = true, OutboundAgentProfile = "captured-profile",
                CreatedAt = world.ObligationAt, UpdatedAt = world.ObligationAt });
            foreach (var id in world.TaskIds)
                db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Title = "source", Goal = "source goal",
                    WorkingDirectory = Path.GetTempPath(), CreatedAt = world.ObligationAt });
            await db.SaveChangesAsync();
            // Deterministic comparison independent of member GUID generation/order.
            Array.Sort(world.TaskIds);
            return world;
        }
        public ValueTask DisposeAsync() => schema.DisposeAsync();
    }
}
