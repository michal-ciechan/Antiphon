using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1803 V-3. One digest page for a Blocked seat past the hour bound whose caller note is
/// still undelivered. Isolated schema because the sweep walks every Blocked task. The page does
/// not cancel or release the seat.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class BlockedSeatEscalationNotifierTests
{
    [Test]
    public async Task C1803_PastBoundUndeliveredNotePagesOnceAndLeavesTheSeat()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedAsync();

            await h.Notifier.SweepAsync(CancellationToken.None);
            await h.Notifier.SweepAsync(CancellationToken.None);

            PagesFor(h, task).Count.ShouldBe(1);
            var events = await h.EventsAsync(task);
            events.Count(IsSeatStamp).ShouldBe(1);
            events.Any(IsTerminalStop).ShouldBeFalse();
            (await h.TaskAsync(task)).Status.ShouldBe(AgentTaskStatus.Blocked);
            (await h.SessionAsync()).Status.ShouldBe(SessionStatus.Running);
            h.Seats.Current.Hosts.SelectMany(host => host.Seats).Single(seat => seat.TaskId == task)
                .ParkState.ShouldBe("none");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task C1803_UnderTheBoundDoesNotPage()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedAsync(idleAge: TimeSpan.FromHours(6) - TimeSpan.FromTicks(1));

            await h.Notifier.SweepAsync(CancellationToken.None);

            PagesFor(h, task).ShouldBeEmpty();
            (await h.EventsAsync(task)).Any(IsSeatStamp).ShouldBeFalse();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task C1803_WithoutAnUndeliveredCallerNoteDoesNotPage()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedAsync(withNote: false);

            await h.Notifier.SweepAsync(CancellationToken.None);

            PagesFor(h, task).ShouldBeEmpty();
            (await h.EventsAsync(task)).Any(IsSeatStamp).ShouldBeFalse();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task C1803_BlockedTaskWithNoOccupyingSeatDoesNotPage()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedAsync(occupies: false);

            await h.Notifier.SweepAsync(CancellationToken.None);

            PagesFor(h, task).ShouldBeEmpty();
            (await h.EventsAsync(task)).Any(IsSeatStamp).ShouldBeFalse();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task C1803_FailedSendWritesNoStamp()
    {
        var producer = new ThrowingProducer();
        await using var h = await Harness.CreateAsync(producer);
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedAsync();

            await h.Notifier.SweepAsync(CancellationToken.None);

            producer.Attempts.ShouldBe(1);
            (await h.EventsAsync(task)).Any(IsSeatStamp).ShouldBeFalse();
            PagesFor(h, task).ShouldBeEmpty();

            h.ReplaceProducer(new FakeAntiphonMessagingClient());
            await h.Notifier.SweepAsync(CancellationToken.None);

            PagesFor(h, task).Count.ShouldBe(1);
            (await h.EventsAsync(task)).Count(IsSeatStamp).ShouldBe(1);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task C1803_ALaterBlockPagesAgainAfterItsOwnBound()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedAsync();

            await h.Notifier.SweepAsync(CancellationToken.None);
            PagesFor(h, task).Count.ShouldBe(1);

            h.Clock.Advance(TimeSpan.FromMinutes(1));
            await h.AddBlockedAsync(task);
            h.PublishSeat(task, h.Clock.GetUtcNow().UtcDateTime, occupies: true, SeatClass.IdleBlocked, AgentTaskRole.Code);

            await h.Notifier.SweepAsync(CancellationToken.None);
            PagesFor(h, task).Count.ShouldBe(1);

            h.Clock.Advance(TimeSpan.FromHours(6));
            await h.Notifier.SweepAsync(CancellationToken.None);

            PagesFor(h, task).Count.ShouldBe(2);
            (await h.EventsAsync(task)).Count(IsSeatStamp).ShouldBe(2);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task C1803_CheckRoleIsNotPaged()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedAsync(role: AgentTaskRole.Check);

            await h.Notifier.SweepAsync(CancellationToken.None);

            PagesFor(h, task).ShouldBeEmpty();
            (await h.EventsAsync(task)).Any(IsSeatStamp).ShouldBeFalse();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    private const int BlockedSeatEscalated = 43;

    private static bool IsSeatStamp(AgentTaskEvent e) => (int)e.Type == BlockedSeatEscalated;

    private static bool IsTerminalStop(AgentTaskEvent e) =>
        e.Type == AgentTaskEventType.Canceled
        || e.Type == AgentTaskEventType.Escalated
        || e.Type == AgentTaskEventType.Failed;

    private static IReadOnlyList<ChannelReply> PagesFor(Harness h, Guid taskId)
    {
        var shortId = taskId.ToString("N")[..8];
        return h.Messaging.SentReplies.Where(reply => reply.Text is not null && reply.Text.Contains($"⛔ task {shortId}")).ToList();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly DigestSettings _digest;
        private readonly AttentionSettings _attentionSettings;
        private ChatChannelService _channels;

        private Harness(
            IsolatedTestSchema schema,
            AppDbContext db,
            FakeTimeProvider clock,
            FakeAntiphonMessagingClient messaging,
            DigestSettings digest,
            AttentionSettings attentionSettings)
        {
            _schema = schema;
            _digest = digest;
            _attentionSettings = attentionSettings;
            Db = db;
            Clock = clock;
            Messaging = messaging;
            _channels = new ChatChannelService(db, clock, messaging);
            Notifier = BuildNotifier();
        }

        public static async Task<Harness> CreateAsync(IAntiphonMessagingProducer? producer = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.Zero));
            var messaging = producer as FakeAntiphonMessagingClient ?? new FakeAntiphonMessagingClient();
            var digest = new DigestSettings { WakeOnBlocked = true, TimeZone = "Europe/London" };
            var attention = new AttentionSettings();
            var harness = new Harness(schema, db, clock, messaging, digest, attention);
            if (producer is not null)
                harness._channels = new ChatChannelService(db, clock, producer);
            harness.Notifier = harness.BuildNotifier();
            return harness;
        }

        public AppDbContext Db { get; }
        public FakeTimeProvider Clock { get; }
        public FakeAntiphonMessagingClient Messaging { get; private set; }
        public BlockedSeatEscalationNotifier Notifier { get; private set; }
        public SeatOccupancyState Seats { get; } = new();
        public Guid SessionId { get; private set; }

        public void ReplaceProducer(FakeAntiphonMessagingClient producer)
        {
            Messaging = producer;
            _channels = new ChatChannelService(Db, Clock, producer);
            Notifier = BuildNotifier();
        }

        public async Task<ChatChannel> SeedChannelAsync()
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var channel = new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = $"-seat-{Guid.NewGuid():N}",
                Kind = ChatChannelKind.Direct,
                Title = "Family",
                Enabled = true,
                DigestEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            Db.ChatChannels.Add(channel);
            await Db.SaveChangesAsync();
            return channel;
        }

        public async Task<Guid> SeedAsync(
            AgentTaskRole role = AgentTaskRole.Code,
            TimeSpan? idleAge = null,
            bool occupies = true,
            bool withNote = true,
            SeatClass seatClass = SeatClass.IdleBlocked)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var idleSince = now - (idleAge ?? TimeSpan.FromHours(6));
            SessionId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            Db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "blocked-seat",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = "/work",
                CreatedAt = idleSince,
                StartedAt = idleSince,
                LastSeenAt = now,
            });
            Db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = $"blocked-{taskId:N}"[..20],
                Goal = "needs an owner",
                Kind = AgentTaskKind.Worker,
                Role = role,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = "/work",
                Status = AgentTaskStatus.Blocked,
                ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = SessionId,
                AgentSessionId = SessionId,
                Result = "still blocked",
                CreatedAt = idleSince,
                DispatchedAt = idleSince,
                Attempt = 1,
            });
            Db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = taskId,
                Type = AgentTaskEventType.Blocked,
                Detail = "still blocked",
                At = idleSince,
            });
            if (withNote)
            {
                Db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = SessionId,
                    Body = "caller note still pending",
                    Status = QueuedMessageStatus.Pending,
                    Origin = QueuedMessageOrigin.Delegation,
                    SourceTaskId = taskId,
                    CreatedAt = now.AddHours(-1),
                    Sequence = 1,
                });
            }

            await Db.SaveChangesAsync();
            PublishSeat(taskId, idleSince, occupies, seatClass, role);
            return taskId;
        }

        public void PublishSeat(Guid taskId, DateTime idleSince, bool occupies, SeatClass seatClass, AgentTaskRole role)
        {
            var seat = new SeatObservation(
                "server2", SessionId, "running", 4242, idleSince, occupies, false, false,
                "Running", taskId, AgentTaskStatus.Blocked, 1, role, null, null, null,
                seatClass, idleSince, "unknown");
            var host = new HostOccupancyObservation(
                "server2", "runner", "listed", null, 1, 0, 1, 0, 0,
                occupies ? 1 : 0, 0, 0, null, null, idleSince, [seat]);
            Seats.Publish(new SeatOccupancySnapshot(Clock.GetUtcNow().UtcDateTime, [host]));
        }

        public async Task AddBlockedAsync(Guid taskId)
        {
            Db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = taskId,
                Type = AgentTaskEventType.Blocked,
                Detail = "still holding",
                At = Clock.GetUtcNow().UtcDateTime,
            });
            await Db.SaveChangesAsync();
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId)
                .OrderBy(e => e.At)
                .ToListAsync();
        }

        public async Task<AgentTask> TaskAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<AgentSession> SessionAsync()
        {
            await using var db = NewDb();
            return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
        }

        public Task CleanupAsync() => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
        }

        private BlockedSeatEscalationNotifier BuildNotifier()
        {
            var attention = new AttentionService(
                Db, new RefusingSessionRunnerClient(), Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), Clock, NullLogger<AttentionService>.Instance,
                seats: Seats, attention: Options.Create(_attentionSettings));
            return new BlockedSeatEscalationNotifier(
                Db, attention, _channels, Options.Create(_digest), Seats, Options.Create(_attentionSettings),
                Clock, NullLogger<BlockedSeatEscalationNotifier>.Instance);
        }

        private AppDbContext NewDb() =>
            new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
    }

    private sealed class ThrowingProducer : IAntiphonMessagingProducer
    {
        public int Attempts { get; private set; }

        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("broker unavailable");
        }
    }
}
