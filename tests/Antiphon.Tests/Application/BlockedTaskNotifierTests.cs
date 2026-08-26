using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
/// CARD-0036 S3: one loud ping per new block. Isolated schema because SweepAsync walks every
/// Blocked task and every digest-enabled channel, and a send-then-record miss must not write
/// HumanNotified onto another suite's row.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class BlockedTaskNotifierTests
{
    [Test]
    public void Human_notified_is_an_appended_event_type_after_the_existing_contract()
    {
        ((int)AgentTaskEventType.HumanNotified).ShouldBe(17);
    }

    [Test]
    public async Task A_newly_blocked_task_is_pinged_exactly_once()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var channel = await h.SeedChannelAsync();
            var task = await h.SeedBlockedAsync("Need a decision.");

            await h.Notifier.SweepAsync(CancellationToken.None);
            await h.Notifier.SweepAsync(CancellationToken.None);

            PingsFor(h, task).Count.ShouldBe(1);
            PingsFor(h, task)[0].ConversationId.ShouldBe(channel.ExternalId);
            (await h.NotifiedAsync(task)).Count.ShouldBe(1);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_task_that_blocks_again_after_being_answered_is_pinged_again()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedBlockedAsync("First question.");

            await h.Notifier.SweepAsync(CancellationToken.None);
            PingsFor(h, task).Count.ShouldBe(1);

            h.Clock.Advance(TimeSpan.FromMinutes(5));
            await h.AddEventAsync(task, AgentTaskEventType.Blocked, "Second question.");

            await h.Notifier.SweepAsync(CancellationToken.None);

            PingsFor(h, task).Count.ShouldBe(2);
            (await h.NotifiedAsync(task)).Count.ShouldBe(2);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_task_already_answered_before_the_sweep_is_not_pinged()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedBlockedAsync("Can I continue?");
            await h.AnswerAsync(task);

            await h.Notifier.SweepAsync(CancellationToken.None);

            PingsFor(h, task).ShouldBeEmpty();
            (await h.NotifiedAsync(task)).ShouldBeEmpty();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_throwing_send_records_no_human_notified_event_so_the_next_sweep_retries()
    {
        var producer = new ThrowingProducer();
        await using var h = await Harness.CreateAsync(producer);
        try
        {
            await h.SeedChannelAsync();
            var task = await h.SeedBlockedAsync("Still waiting.");

            await h.Notifier.SweepAsync(CancellationToken.None);

            producer.Attempts.ShouldBe(1);
            (await h.NotifiedAsync(task)).ShouldBeEmpty();
            PingsFor(h, task).ShouldBeEmpty();

            h.ReplaceProducer(new FakeAntiphonMessagingClient());
            await h.Notifier.SweepAsync(CancellationToken.None);

            PingsFor(h, task).Count.ShouldBe(1);
            (await h.NotifiedAsync(task)).Count.ShouldBe(1);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    private static IReadOnlyList<ChannelReply> PingsFor(Harness h, Guid taskId)
    {
        var shortId = taskId.ToString("N")[..8];
        return h.Messaging.SentReplies.Where(r => r.Text is not null && r.Text.Contains($"task {shortId}")).ToList();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly DigestSettings _settings;
        private ChatChannelService _channels;

        private Harness(
            IsolatedTestSchema schema,
            AppDbContext db,
            FakeTimeProvider clock,
            IAntiphonMessagingProducer producer,
            FakeAntiphonMessagingClient messaging,
            DigestSettings settings)
        {
            _schema = schema;
            _settings = settings;
            Db = db;
            Clock = clock;
            Messaging = messaging;
            _channels = new ChatChannelService(db, clock, producer);
            Notifier = BuildNotifier();
        }

        public static async Task<Harness> CreateAsync(IAntiphonMessagingProducer? producer = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
            var messaging = producer as FakeAntiphonMessagingClient ?? new FakeAntiphonMessagingClient();
            var settings = new DigestSettings { WakeOnBlocked = true, TimeZone = "Europe/London" };
            return new Harness(schema, db, clock, producer ?? messaging, messaging, settings);
        }

        public AppDbContext Db { get; }
        public FakeTimeProvider Clock { get; }
        public FakeAntiphonMessagingClient Messaging { get; private set; }
        public BlockedTaskNotifier Notifier { get; private set; }

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
                ExternalId = $"-blocked-{Guid.NewGuid():N}",
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

        public async Task<Guid> SeedBlockedAsync(string question)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var id = Guid.NewGuid();
            Db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"blocked-{id:N}"[..20],
                Goal = "needs an answer",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Blocked,
                ReplyTo = AgentTaskReplyTo.Session,
                Result = question,
                CreatedAt = now.AddMinutes(-20),
                DispatchedAt = now.AddMinutes(-20),
            });
            Db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = id,
                Type = AgentTaskEventType.Blocked,
                Detail = question,
                At = now.AddMinutes(-5),
            });
            await Db.SaveChangesAsync();
            return id;
        }

        public async Task AddEventAsync(Guid taskId, AgentTaskEventType type, string detail)
        {
            Db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = taskId,
                Type = type,
                Detail = detail,
                At = Clock.GetUtcNow().UtcDateTime,
            });
            await Db.SaveChangesAsync();
        }

        public async Task AnswerAsync(Guid taskId)
        {
            var task = await Db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Status = AgentTaskStatus.Working;
            await AddEventAsync(taskId, AgentTaskEventType.Replied, "the human answered");
        }

        public async Task<List<AgentTaskEvent>> NotifiedAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.HumanNotified)
                .OrderBy(e => e.At)
                .ToListAsync();
        }

        public Task CleanupAsync() => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
        }

        private BlockedTaskNotifier BuildNotifier()
        {
            var attention = new AttentionService(
                Db, new RefusingSessionRunnerClient(), Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), Clock, NullLogger<AttentionService>.Instance);
            return new BlockedTaskNotifier(
                Db, attention, _channels, Options.Create(_settings), Clock,
                NullLogger<BlockedTaskNotifier>.Instance);
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
