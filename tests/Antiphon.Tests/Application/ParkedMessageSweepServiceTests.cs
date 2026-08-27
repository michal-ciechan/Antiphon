using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0091: global queue cleanup tests use an isolated migrated schema, so the sweep can never
/// see another test's rows. No group key is intentional: a global sweep must serialize against
/// every other shared-Postgres integration sweep.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ParkedMessageSweepServiceTests
{
    [Test]
    public async Task A_parked_Delegation_row_with_a_terminal_task_is_canceled()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Failed);
        await world.SeedTaskAsync(session, AgentTaskStatus.Succeeded);
        var message = await world.SeedMessageAsync(session, QueuedMessageOrigin.Delegation);

        (await world.ScanAsync()).ShouldBe(1);

        var row = await world.ReadMessageAsync(message);
        row.Status.ShouldBe(QueuedMessageStatus.Canceled);
        row.CanceledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task A_parked_compact_on_a_running_session_with_no_open_task_is_canceled()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Running);
        var message = await world.SeedMessageAsync(
            session, QueuedMessageOrigin.Delegation, body: "/compact This session is being handed new work");

        (await world.ScanAsync()).ShouldBe(1);
        (await world.ReadMessageAsync(message)).Status.ShouldBe(QueuedMessageStatus.Canceled);
    }

    [Test]
    public async Task A_working_sibling_keeps_a_parked_row_until_it_settles()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Failed);
        var sibling = await world.SeedTaskAsync(session, AgentTaskStatus.Working);
        var message = await world.SeedMessageAsync(session, QueuedMessageOrigin.Delegation);

        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadMessageAsync(message)).Status.ShouldBe(QueuedMessageStatus.Pending);

        await world.SetTaskStatusAsync(sibling, AgentTaskStatus.Succeeded);
        (await world.ScanAsync()).ShouldBe(1);
        (await world.ReadMessageAsync(message)).Status.ShouldBe(QueuedMessageStatus.Canceled);
    }

    [Test]
    public async Task A_blocked_task_counts_as_open()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Stopped);
        await world.SeedTaskAsync(session, AgentTaskStatus.Blocked);
        var message = await world.SeedMessageAsync(session, QueuedMessageOrigin.Delegation);

        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadMessageAsync(message)).Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Rows_below_the_delivery_cap_and_human_origin_rows_stay_parked()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Failed);
        var belowCap = await world.SeedMessageAsync(
            session, QueuedMessageOrigin.Delegation, attempts: world.MaxAttempts - 1);
        var ui = await world.SeedMessageAsync(session, QueuedMessageOrigin.Ui);
        var channel = await world.SeedMessageAsync(session, QueuedMessageOrigin.Channel);

        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadMessageAsync(belowCap)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await world.ReadMessageAsync(ui)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await world.ReadMessageAsync(channel)).Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Completion_and_conversation_keyed_rows_stay_parked()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Failed);
        var task = await world.SeedTaskAsync(session, AgentTaskStatus.Succeeded);
        var completion = await world.SeedMessageAsync(
            session, QueuedMessageOrigin.Delegation, sourceTaskId: task);
        var keyed = await world.SeedMessageAsync(
            session, QueuedMessageOrigin.Delegation, conversationKey: "check:correlation");

        (await world.ScanAsync()).ShouldBe(0);
        (await world.ReadMessageAsync(completion)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await world.ReadMessageAsync(keyed)).Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task The_minimum_parked_age_is_enforced()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Failed);
        var younger = await world.SeedMessageAsync(
            session, QueuedMessageOrigin.System, parkedSince: world.Now.AddMinutes(-9));
        var older = await world.SeedMessageAsync(
            session, QueuedMessageOrigin.Check, parkedSince: world.Now.AddMinutes(-11));

        (await world.ScanAsync()).ShouldBe(1);
        (await world.ReadMessageAsync(younger)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await world.ReadMessageAsync(older)).Status.ShouldBe(QueuedMessageStatus.Canceled);
    }

    [Test]
    public async Task Dry_run_and_disabled_sweeps_write_nothing()
    {
        await using var dryRun = await World.CreateAsync(dryRun: true);
        var drySession = await dryRun.SeedSessionAsync(SessionStatus.Failed);
        var dryMessage = await dryRun.SeedMessageAsync(drySession, QueuedMessageOrigin.Supervision);

        (await dryRun.ScanAsync()).ShouldBe(0);
        (await dryRun.ReadMessageAsync(dryMessage)).Status.ShouldBe(QueuedMessageStatus.Pending);

        await using var disabled = await World.CreateAsync(enabled: false);
        var disabledSession = await disabled.SeedSessionAsync(SessionStatus.Failed);
        var disabledMessage = await disabled.SeedMessageAsync(disabledSession, QueuedMessageOrigin.Delegation);

        (await disabled.ScanAsync()).ShouldBe(0);
        (await disabled.ReadMessageAsync(disabledMessage)).Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task The_under_lock_primitive_refuses_sent_and_human_canceled_rows()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(SessionStatus.Failed);
        var sent = await world.SeedMessageAsync(session, QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Sent);
        var canceled = await world.SeedMessageAsync(
            session, QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Canceled);

        (await world.Queue.CancelParkedIfStaleAsync(session, sent, CancellationToken.None)).ShouldBeFalse();
        (await world.Queue.CancelParkedIfStaleAsync(session, canceled, CancellationToken.None)).ShouldBeFalse();
        (await world.ReadMessageAsync(sent)).Status.ShouldBe(QueuedMessageStatus.Sent);
        (await world.ReadMessageAsync(canceled)).Status.ShouldBe(QueuedMessageStatus.Canceled);
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private long _sequence;

        private World(IsolatedTestSchema schema, ServiceProvider provider, SessionMessageQueueService queue)
        {
            _schema = schema;
            _provider = provider;
            Queue = queue;
        }

        public DateTime Now { get; } = DateTime.UtcNow;

        public int MaxAttempts { get; } = 3;

        public SessionMessageQueueService Queue { get; }

        public static async Task<World> CreateAsync(bool enabled = true, bool dryRun = false)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(schema.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton(Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { MaxDeliveryAttempts = 3 },
            }));
            services.AddSingleton(Options.Create(new ParkedMessageSweepSettings
            {
                Enabled = enabled,
                MinParkedMinutes = 10,
                DryRun = dryRun,
            }));
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddScoped<ParkedMessageSweepService>();
            services.AddLogging();

            var provider = services.BuildServiceProvider();
            return new World(schema, provider, provider.GetRequiredService<SessionMessageQueueService>());
        }

        public async Task<Guid> SeedSessionAsync(SessionStatus status)
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = id,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = status,
                Cwd = Path.Combine(Path.GetTempPath(), $"antiphon-parked-{id:N}"),
                Cols = 120,
                Rows = 30,
                CreatedAt = Now.AddHours(-1),
                StartedAt = Now.AddHours(-1),
                LastSeenAt = Now,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> SeedTaskAsync(Guid sessionId, AgentTaskStatus status)
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "parked sweep test task",
                Goal = "exercise the parked sweep",
                Role = AgentTaskRole.Code,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = sessionId,
                Status = status,
                CreatedAt = Now.AddHours(-1),
                DispatchedAt = Now.AddMinutes(-30),
                CompletedAt = status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled
                    ? Now.AddMinutes(-20)
                    : null,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task SetTaskStatusAsync(Guid id, AgentTaskStatus status)
        {
            await using var db = CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
            task.Status = status;
            task.CompletedAt = Now;
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedMessageAsync(
            Guid sessionId,
            QueuedMessageOrigin origin,
            int? attempts = null,
            string? body = null,
            Guid? sourceTaskId = null,
            string? conversationKey = null,
            DateTime? parkedSince = null,
            QueuedMessageStatus status = QueuedMessageStatus.Pending)
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = id,
                AgentSessionId = sessionId,
                Body = body ?? "parked message",
                Status = status,
                Sequence = ++_sequence,
                Origin = origin,
                SourceTaskId = sourceTaskId,
                ConversationKey = conversationKey,
                CreatedAt = Now.AddHours(-1),
                LastDeliveryStartedAt = parkedSince ?? Now.AddMinutes(-11),
                DeliveryAttempts = attempts ?? MaxAttempts,
                SentAt = status == QueuedMessageStatus.Sent ? Now : null,
                CanceledAt = status == QueuedMessageStatus.Canceled ? Now : null,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<int> ScanAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ParkedMessageSweepService>()
                .ScanAsync(CancellationToken.None);
        }

        public async Task<SessionQueuedMessage> ReadMessageAsync(Guid id)
        {
            await using var db = CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        }

        private AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
        }
    }
}
