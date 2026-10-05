using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundRetentionTests
{
    [Test]
    public async Task C519_Session_retention_preserves_recovery_evidence()
    {
        await using var w = await World.CreateAsync();
        foreach (var origin in new[] { QueuedMessageOrigin.Channel, QueuedMessageOrigin.Delegation,
                     QueuedMessageOrigin.Check, QueuedMessageOrigin.System, QueuedMessageOrigin.Scheduled })
        {
            var session = await w.SessionAsync();
            await w.SourceAsync(session, origin);
            await w.TranscriptAsync(session);
            await using var db = w.Db();
            await w.Retention(db).PruneSessionsAsync(default);
            (await db.AgentSessions.AnyAsync(s => s.Id == session)).ShouldBeTrue(origin.ToString());
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == session)).ShouldBe(3);
        }
        var unrelated = await w.SessionAsync();
        await w.TranscriptAsync(unrelated);
        await using var verify = w.Db();
        await w.Retention(verify).PruneSessionsAsync(default);
        (await verify.AgentSessions.AnyAsync(s => s.Id == unrelated)).ShouldBeFalse();
        (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == unrelated)).ShouldBeFalse();
    }

    [Test]
    public async Task C519_Transcript_retention_rechecks_under_lock()
    {
        await using var w = await World.CreateAsync();
        var session = await w.SessionAsync();
        await w.TranscriptAsync(session);
        var unrelated = await w.SessionAsync();
        await w.TranscriptAsync(unrelated);
        using var states = new SessionStateStore(new SessionStateLoader(w.H.Provider.GetRequiredService<IServiceScopeFactory>()),
            w.Clock, Options.Create(new SessionStateSettings()));
        await using var db = w.Db();
        Task<int> pruning;
        using (var held = await states.BeginWriteAsync(session, default))
        {
            states.NextGateWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pruning = w.Retention(db, states).PruneTranscriptsAsync(default);
            await states.NextGateWait.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The initial candidate read already ran. Capture appears while deletion waits on
            // the same gate ingestion uses. It must be seen by the locked recheck.
            await w.DeliveryAsync(session, ChannelOutboundDeliveryState.Captured, closed: true);
        }
        await pruning;
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == session)).ShouldBe(3);
        (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == unrelated)).ShouldBeFalse();
    }

    [Test]
    public async Task C519_Queue_retention_keeps_open_machine_and_channel_sources()
    {
        await using var w = await World.CreateAsync();
        var session = await w.SessionAsync();
        var context = await w.SourceAsync(session, QueuedMessageOrigin.Channel, settled: true);
        var kept = new List<Guid>();
        foreach (var origin in new[] { QueuedMessageOrigin.Channel, QueuedMessageOrigin.Delegation,
                     QueuedMessageOrigin.Check, QueuedMessageOrigin.System, QueuedMessageOrigin.Scheduled })
            kept.Add(await w.SourceAsync(session, origin));
        var closed = await w.SourceAsync(session, QueuedMessageOrigin.Check, closed: true);
        var unrelated = await w.SourceAsync(session, QueuedMessageOrigin.Ui);
        await using var db = w.Db();
        await w.Retention(db).PruneQueuedMessagesAsync(default);
        (await db.SessionQueuedMessages.Where(m => kept.Contains(m.Id)).CountAsync()).ShouldBe(kept.Count);
        (await db.SessionQueuedMessages.AnyAsync(m => m.Id == context)).ShouldBeTrue("original machine route context");
        (await db.SessionQueuedMessages.AnyAsync(m => m.Id == closed || m.Id == unrelated)).ShouldBeFalse();
    }

    [Test]
    public async Task C519_Task_and_bundle_retention_keeps_referenced_inputs()
    {
        await using var w = await World.CreateAsync();
        var session = await w.SessionAsync();
        var discoveryTask = await w.TaskTreeAsync();
        await w.SourceAsync(session, QueuedMessageOrigin.Delegation, task: discoveryTask.Child);
        var capturedTask = await w.TaskTreeAsync();
        var impliedTask = await w.TaskTreeAsync();
        var conversionTask = await w.TaskTreeAsync();
        var id = await w.DeliveryAsync(session, ChannelOutboundDeliveryState.Published, closed: true,
            metadata: false, tasks: [capturedTask.Child, impliedTask.Child]);
        await using (var seed = w.Db())
            await seed.ChannelOutboundDeliveries.Where(d => d.Id == id).ExecuteUpdateAsync(u =>
                u.SetProperty(d => d.SourceTaskId, capturedTask.Child).SetProperty(d => d.ConversionTaskId, conversionTask.Child));
        var unrelated = await w.TaskTreeAsync();
        await using var db = w.Db();
        await w.Retention(db).PruneTasksAsync(default);
        foreach (var tree in new[] { discoveryTask, capturedTask, impliedTask, conversionTask })
        {
            (await db.AgentTasks.CountAsync(t => t.RootTaskId == tree.Root)).ShouldBe(2);
            File.ReadAllText(tree.Path).ShouldBe("retained bundle bytes");
        }
        (await db.AgentTasks.AnyAsync(t => t.RootTaskId == unrelated.Root)).ShouldBeFalse();
    }

    [Test]
    public async Task C519_Delivery_and_file_retention_keeps_unresolved_ownership()
    {
        await using var w = await World.CreateAsync();
        var files = new ChannelOutboundFileStore(Path.Combine(w.H.TempRoot, "retained-store"));
        var kept = new List<(Guid Session, Guid Delivery, string Path, string Hash)>();
        foreach (var state in Enum.GetValues<ChannelOutboundDeliveryState>())
        {
            if (state == ChannelOutboundDeliveryState.Suppressed) continue;
            var session = await w.SessionAsync();
            await w.TranscriptAsync(session);
            var id = await w.DeliveryAsync(session, state, closed: true, metadata: false);
            var snapshot = await files.StageAsync(id, new ChannelReply { Channel = "telegram", ConversationId = w.Key[9..],
                Text = "original frozen reply", Attachments = [new OutboundAttachment { Name = "source.md", Content = "frozen"u8.ToArray() }] }, default);
            await using var seed = w.Db();
            await seed.ChannelOutboundDeliveries.Where(d => d.Id == id).ExecuteUpdateAsync(u =>
                u.SetProperty(d => d.InputPath, snapshot.ReplyPath).SetProperty(d => d.InputSha256, snapshot.ReplySha256));
            var member = await w.SourceAsync(session, QueuedMessageOrigin.Channel, settled: true);
            await seed.SessionQueuedMessages.Where(m => m.Id == member).ExecuteUpdateAsync(u => u.SetProperty(m => m.ChannelOutboundDeliveryId, id));
            kept.Add((session, id, snapshot.ReplyPath, snapshot.ReplySha256));
        }
        var unrelated = await w.SessionAsync();
        await using var db = w.Db();
        await w.Retention(db).RunOnceAsync(default);
        foreach (var item in kept)
        {
            (await db.AgentSessions.AnyAsync(s => s.Id == item.Session)).ShouldBeTrue();
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == item.Session)).ShouldBe(3);
            (await db.SessionQueuedMessages.AnyAsync(m => m.ChannelOutboundDeliveryId == item.Delivery)).ShouldBeTrue();
            (await db.ChannelOutboundDeliveries.AnyAsync(d => d.Id == item.Delivery)).ShouldBeTrue();
            (await files.ReadReplyAsync(item.Path, item.Hash, default)).Attachments.ShouldHaveSingleItem().Content.ShouldBe("frozen"u8.ToArray());
        }
        (await db.AgentSessions.AnyAsync(s => s.Id == unrelated)).ShouldBeFalse();
    }

    [Test]
    public async Task C519_Closed_roots_eventually_become_prunable()
    {
        await using (var machine = await World.CreateAsync())
        {
            var session = await machine.SessionAsync();
            var source = await machine.SourceAsync(session, QueuedMessageOrigin.System);
            await machine.TranscriptAsync(session);
            await using var db = machine.Db();
            await db.TranscriptEntries.Where(t => t.AgentSessionId == session && t.Kind == TranscriptKinds.AssistantText)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.Text, "NO_REPLY"));
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == source);
            await machine.H.Dispatcher.DiscoverSourceAsync(row, 0, default);
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == source)).ChannelReplyDiscoveryClosedAt.ShouldBeNull();
            var generation = await db.AgentSessions.Where(s => s.Id == session).Select(s => s.StartedAt).SingleAsync();
            var entries = (await db.TranscriptEntries.Where(t => t.AgentSessionId == session).OrderBy(t => t.Sequence).ToListAsync())
                .Select(t => new SessionRunnerTranscriptEvent(session, t.Sequence, t.Kind, t.Uuid, null, null,
                    t.Role, t.Text, null, null, null, null, t.StopReason)).ToArray();
            machine.H.Runner.SetTranscript(new(session, entries, entries[^1].Sequence, true, generation));
            await machine.H.Dispatcher.DiscoverSourceAsync(row, 0, default);
            row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == source);
            row.ChannelReplyDiscoveryClosedAt.ShouldNotBeNull();
            row.ChannelReplySettledAt.ShouldBeNull();
            machine.H.Messaging.SentReplies.ShouldBeEmpty();
            await machine.Retention(db).PruneSessionsAsync(default);
            (await db.AgentSessions.AnyAsync(s => s.Id == session)).ShouldBeFalse();
        }
        foreach (var suppressed in new[] { false, true })
        {
            await using var w = await World.CreateAsync();
            var member = await w.H.SeedChannelCorrelationAsync("owning complete prompt", w.Key);
            await w.H.InsertTurnAsync("owning complete prompt", suppressed ? "NO_REPLY" : "root answer");
            await w.H.Dispatcher.OnTurnEndAsync(w.H.SessionId, default);
            await w.H.TickOutboundAsync(); await w.H.TickOutboundAsync();
            await using var db = w.Db();
            var root = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync();
            await db.AgentSessions.Where(s => s.Id == w.H.SessionId).ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.Status, SessionStatus.Stopped).SetProperty(s => s.LastSeenAt, w.Old));
            await db.TranscriptEntries.ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedAt, w.Old));
            await w.H.Dispatcher.DiscoverRootAsync(root.Id, default);
            (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync()).TailClosedAt.ShouldBeNull();
            await w.Retention(db).PruneSessionsAsync(default);
            (await db.AgentSessions.AnyAsync(s => s.Id == w.H.SessionId)).ShouldBeTrue();

            var generation = await db.AgentSessions.Where(s => s.Id == w.H.SessionId).Select(s => s.StartedAt).SingleAsync();
            var entries = (await db.TranscriptEntries.OrderBy(t => t.Sequence).ToListAsync()).Select(t =>
                new SessionRunnerTranscriptEvent(t.AgentSessionId, t.Sequence, t.Kind, t.Uuid, t.ParentUuid,
                    t.Timestamp, t.Role, t.Text, t.ToolName, t.ToolInput, t.ToolUseId, t.ToolIsError, t.StopReason)).ToList();
            entries.Add(new(w.H.SessionId, entries[^1].Sequence + 1, TranscriptKinds.AssistantText,
                Guid.NewGuid().ToString(), null, null, "assistant", "last native tail", null, null, null, null, null));
            w.H.Runner.SetTranscript(new(w.H.SessionId, entries, entries[^1].Sequence, true, generation.AddSeconds(-1)));
            await w.H.Dispatcher.DiscoverRootAsync(root.Id, default);
            (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync()).TailClosedAt.ShouldBeNull("stale generation");
            var mismatched = entries.ToArray();
            mismatched[0] = mismatched[0] with { Text = "payload absent from persisted evidence" };
            w.H.Runner.SetTranscript(new(w.H.SessionId, mismatched, entries[^1].Sequence, true, generation));
            await w.H.Dispatcher.DiscoverRootAsync(root.Id, default);
            (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == root.Id))
                .TailClosedAt.ShouldBeNull("deduplication cannot certify a different persisted payload");
            w.H.Runner.SetTranscript(new(w.H.SessionId, entries, entries[^1].Sequence, true, generation));
            await w.H.Dispatcher.DiscoverRootAsync(root.Id, default);
            root = await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == root.Id);
            root.TailClosedAt.ShouldNotBeNull();
            root.ReservedThroughSequence.ShouldBe(entries[^1].Sequence);
            if (suppressed) root.PublishedAt.ShouldBeNull();
            await w.H.TickOutboundAsync(); await w.H.TickOutboundAsync();
            w.H.Messaging.SentReplies.Last().Text.ShouldBe("last native tail");
            w.H.Messaging.SentReplies.Count.ShouldBe(suppressed ? 1 : 2);
            await db.TranscriptEntries.ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedAt, w.Old));
            await db.SessionQueuedMessages.ExecuteUpdateAsync(u => u.SetProperty(m => m.CreatedAt, w.Old));
            await w.Retention(db).PruneTranscriptsAsync(default);
            (await db.TranscriptEntries.AnyAsync()).ShouldBeFalse();
            await w.Retention(db).PruneQueuedMessagesAsync(default);
            (await db.SessionQueuedMessages.AnyAsync(m => m.Id == member)).ShouldBeFalse();
            await w.Retention(db).PruneSessionsAsync(default);
            (await db.AgentSessions.AnyAsync(s => s.Id == w.H.SessionId)).ShouldBeFalse();
        }
    }

    private sealed class World(IsolatedTestSchema schema, BridgeQueueHarness h, FakeTimeProvider clock, string key) : IAsyncDisposable
    {
        public BridgeQueueHarness H => h;
        public FakeTimeProvider Clock => clock;
        public string Key => key;
        public DateTime Old => h.Now.AddDays(-400);
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        public static async Task<World> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
            var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString,
                AlwaysOn = false, TimeProvider = clock, ConfigureServices = s =>
                    s.AddSingleton<IOptions<ChannelOutboundSettings>>(Options.Create(new ChannelOutboundSettings { UnifiedRecoveryEnabled = true })) });
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var project = Guid.NewGuid(); var board = Guid.NewGuid();
            db.Projects.Add(new() { Id = project, Name = "retention" });
            db.Boards.Add(new() { Id = board, ProjectId = project, Name = "retention" });
            await db.SaveChangesAsync();
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(u =>
                u.SetProperty(a => a.BoardId, board).SetProperty(a => a.PersistentSessionId, (string?)null));
            return new(schema, h, clock, "telegram:" + await h.BindChannelAsync());
        }
        public DataRetentionService Retention(AppDbContext db, SessionStateStore? states = null) => new(db,
            Options.Create(new RetentionSettings { SessionRetentionDays = 1, TranscriptRetentionDays = 1,
                QueuedMessageRetentionDays = 1, TaskRetentionDays = 1 }), Options.Create(new AuditSettings()), clock,
            NullLogger<DataRetentionService>.Instance, new AuditService(db, Options.Create(new AuditSettings())), states);
        public async Task<Guid> SessionAsync()
        {
            await using var db = Db(); var id = Guid.NewGuid();
            db.AgentSessions.Add(new() { Id = id, Status = SessionStatus.Stopped, CreatedAt = Old,
                StartedAt = Old, LastSeenAt = Old, EndedAt = Old });
            await db.SaveChangesAsync(); return id;
        }
        public async Task<Guid> SourceAsync(Guid session, QueuedMessageOrigin origin, bool settled = false,
            bool closed = false, Guid? task = null)
        {
            await using var db = Db(); var id = Guid.NewGuid();
            var sequence = (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == session)
                .MaxAsync(m => (long?)m.Sequence) ?? 0) + 1;
            db.SessionQueuedMessages.Add(new() { Id = id, AgentSessionId = session, Sequence = sequence, Body = "owning prompt",
                Origin = origin, Status = QueuedMessageStatus.Sent, ConversationKey = origin == QueuedMessageOrigin.Channel ? key : null,
                CreatedAt = Old, SentAt = Old, SourceTaskId = task, DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0,
                ChannelReplySettledAt = settled ? Old : null, ChannelReplyDiscoveryClosedAt = closed ? Old : null });
            await db.SaveChangesAsync(); return id;
        }
        public async Task TranscriptAsync(Guid session)
        {
            await using var db = Db();
            foreach (var (kind, sequence, text) in new[] { (TranscriptKinds.UserPrompt, 1L, "owning prompt"),
                         (TranscriptKinds.AssistantText, 2L, "original answer"), (TranscriptKinds.TurnEnd, 3L, "") })
                db.TranscriptEntries.Add(new() { Id = Guid.NewGuid(), AgentSessionId = session, Kind = kind,
                    Sequence = sequence, Text = text, CreatedAt = Old });
            await db.SaveChangesAsync();
        }
        public async Task<Guid> DeliveryAsync(Guid session, ChannelOutboundDeliveryState state,
            bool closed, bool metadata = true, Guid[]? tasks = null)
        {
            await using var db = Db(); var channel = await db.ChatChannels.SingleAsync();
            var id = Guid.NewGuid();
            var route = new ChannelReply { Channel = "telegram", ConversationId = key[9..], Text = "original answer" };
            db.ChannelOutboundDeliveries.Add(new() { Id = id, ChannelId = channel.Id, SourceSessionId = session,
                SourceKey = id.ToString("N"), PromptSequence = 1, FirstTextSequence = 2, LastTextSequence = 2,
                SendKind = "main", State = state, CreatedAt = Old, DeadlineAt = Old, ReservedThroughSequence = 2,
                TailClosedAt = closed ? Old : null, MetadataAppliedAt = metadata ? Old : null,
                CaptureJson = ChannelReplyPreparation.Serialize(new(1, route, ChannelReplyPreparation.Describe("original answer"), [],
                    (tasks ?? []).Select(t => new ChannelReplyTaskDescriptor(t, null)).ToArray(), null, 1024)) });
            await db.SaveChangesAsync(); return id;
        }
        public async Task<(Guid Root, Guid Child, string Path)> TaskTreeAsync()
        {
            await using var db = Db(); var root = Guid.NewGuid(); var child = Guid.NewGuid();
            var path = Path.Combine(h.TempRoot, child + ".md"); await File.WriteAllTextAsync(path, "retained bundle bytes");
            db.AgentTasks.Add(new() { Id = root, RootTaskId = root, Title = "unattached root", CreatedAt = Old,
                CompletedAt = Old, Status = AgentTaskStatus.Succeeded, WorkingDirectory = h.TempRoot });
            db.AgentTasks.Add(new() { Id = child, RootTaskId = root, ParentTaskId = root, Depth = 1, Title = "bundle child",
                CreatedAt = Old, CompletedAt = Old, Status = AgentTaskStatus.Succeeded, WorkingDirectory = h.TempRoot,
                DeliverableBundleDir = h.TempRoot });
            await db.SaveChangesAsync(); return (root, child, path);
        }
        public async ValueTask DisposeAsync() { await h.DisposeAsync(); await schema.DisposeAsync(); }
    }
}
