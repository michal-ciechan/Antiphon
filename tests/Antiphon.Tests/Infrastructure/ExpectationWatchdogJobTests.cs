using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[NotInParallel]
[Category("Integration")]
public sealed class ExpectationWatchdogJobTests
{
    [Test]
    public async Task C650_Registers_minute_job_on_dedicated_queue()
    {
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        HangfireConfiguration.AddOrUpdateExpectationWatchdogJob(new RecurringJobManager(storage));
        using var connection = storage.GetConnection();
        var job = connection.GetRecurringJobs().ShouldHaveSingleItem();
        job.Id.ShouldBe(ExpectationWatchdogJob.RecurringJobId);
        job.Cron.ShouldBe("* * * * *");
        job.TimeZoneId.ShouldBe("UTC");
        typeof(ExpectationWatchdogJob).GetMethod(nameof(ExpectationWatchdogJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(QueueAttribute), true).OfType<QueueAttribute>()
            .ShouldHaveSingleItem().Queue.ShouldBe("expectations");
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Disabled_host_or_feature_starts_no_worker()
    {
        await using var f = await Fixture.CreateAsync(enabled: false);
        await using var provider = f.Services();
        await provider.GetRequiredService<ExpectationWatchdogJob>().ExecuteAsync(CancellationToken.None);
        f.Producer.Sent.ShouldBeEmpty();
        await using var db = f.World.Db();
        (await db.ExpectationWatchStates.CountAsync()).ShouldBe(0);
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldBeEmpty();
    }

    [Test]
    public async Task C650_Fresh_host_recovers_due_work_with_real_adapters()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedDueAsync();
        await using (var first = f.Services())
            first.GetRequiredService<ExpectationObservationAdapter>().ShouldNotBeNull();
        await using (var fresh = f.Services())
            await fresh.GetRequiredService<ExpectationWatchdogJob>().ExecuteAsync(CancellationToken.None);
        f.Producer.Sent.ShouldHaveSingleItem().ConversationId
            .ShouldBe("c650-operator-" + f.World.Directive.OperatorChannelId.ToString("N"));
        await using var db = f.World.Db();
        (await db.ExpectationNudges.SingleAsync()).OperatorOutboxState
            .ShouldBe(ExpectationOperatorOutboxState.Published);
    }

    [Test]
    public async Task C650_Default_worker_occupancy_does_not_block_watchdog()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedDueAsync();
        await using var provider = f.Services();
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        GlobalConfiguration.Configuration.UseStorage(storage).UseActivator(new ScopeActivator(provider));
        var client = new BackgroundJobClient(storage);
        var blocker = provider.GetRequiredService<BlockedDefaultJob>();
        client.Enqueue<BlockedDefaultJob>(job => job.Execute());
        using var defaultWorker = new BackgroundJobServer(new BackgroundJobServerOptions
        { WorkerCount = 1, Queues = ["default"], SchedulePollingInterval = TimeSpan.FromMilliseconds(50) }, storage);
        await blocker.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        client.Enqueue<ExpectationWatchdogJob>(job => job.ExecuteAsync(CancellationToken.None));
        using var expectationWorker = new BackgroundJobServer(new BackgroundJobServerOptions
        { WorkerCount = 1, Queues = ["expectations"], SchedulePollingInterval = TimeSpan.FromMilliseconds(50) }, storage);
        try
        {
            await f.Producer.Published.Task.WaitAsync(TimeSpan.FromSeconds(15));
            blocker.Finished.Task.IsCompleted.ShouldBeFalse();
            f.Producer.Sent.Count.ShouldBe(1);
        }
        finally { blocker.Release(); }
    }

    [Test]
    public async Task C650_Slow_directive_does_not_starve_operator_debt()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedDueAsync();
        // The page is published before the observer is resolved. This intentionally leaves the
        // observer unavailable, proving a failed scan cannot postpone durable operator debt.
        await using var provider = f.Services(includeObservation: false);
        await provider.GetRequiredService<ExpectationWatchdogJob>().ExecuteAsync(CancellationToken.None);
        f.Producer.Sent.Count.ShouldBe(1);
        await using var db = f.World.Db();
        (await db.ExpectationNudges.SingleAsync()).OperatorOutboxState
            .ShouldBe(ExpectationOperatorOutboxState.Published);
        (await db.ExpectationWatchStates.SingleAsync()).LastObservationError.ShouldContain("InvalidOperationException");
    }

    [Test]
    public async Task C650_Overlapping_passes_and_budget_cursors_are_safe()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedDueAsync();
        await f.SeedYoungNotesAsync(105);
        await using var provider = f.Services();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var settings = provider.GetRequiredService<IOptions<ExpectationWatchdogSettings>>();
        var left = new ExpectationWatchdogJob(scopes, settings, f.Clock);
        var right = new ExpectationWatchdogJob(scopes, settings, f.Clock);
        await Task.WhenAll(left.ExecuteAsync(CancellationToken.None), right.ExecuteAsync(CancellationToken.None));
        f.Producer.Sent.Count.ShouldBe(1);
        Guid? cursor;
        await using (var db = f.World.Db())
        {
            (await db.ExpectationNudges.SingleAsync()).OperatorPublicationOrdinal.ShouldBe(1);
            var state = await db.ExpectationWatchStates.SingleAsync();
            state.NoteQueueCursorId.ShouldNotBeNull();
            state.LastSuccessfulScanAt.ShouldBeNull("a page of 105 notes cannot clear unseen subjects");
            cursor = state.NoteQueueCursorId;
        }
        await left.ExecuteAsync(CancellationToken.None);
        await using var after = f.World.Db();
        (await after.ExpectationWatchStates.SingleAsync()).NoteQueueCursorId.ShouldNotBe(cursor);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(IsolatedTestSchema schema, ExpectationTestWorld world, FakeTimeProvider clock,
            bool enabled)
        {
            Schema = schema; World = world; Clock = clock;
            Settings = new ExpectationWatchdogSettings { Enabled = enabled, Directives = [world.Directive] };
        }
        public IsolatedTestSchema Schema { get; }
        public ExpectationTestWorld World { get; }
        public FakeTimeProvider Clock { get; }
        public ExpectationWatchdogSettings Settings { get; }
        public RecordingProducer Producer { get; } = new();
        public static async Task<Fixture> CreateAsync(bool enabled = true)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
            return new Fixture(schema, world, new FakeTimeProvider(new DateTimeOffset(world.Now, TimeSpan.Zero)), enabled);
        }
        public async Task SeedDueAsync()
        {
            var episode = new ExpectationEpisode
            {
                Id = Guid.NewGuid(), DirectiveId = World.Directive.Id, ConfigDigest = World.Digest,
                Kind = ExpectationEpisodeKind.DispatchFence, SubjectKey = "fence:job",
                Evidence = "held", FirstObservedAt = World.Now.AddMinutes(-20), LastObservedAt = World.Now,
            };
            var audit = new CardComment
            {
                Id = Guid.NewGuid(), CardId = World.CardId, Author = ExpectationLedger.AuditAuthor,
                Body = "job audit", CreatedAt = World.Now,
            };
            var body = "[expectation-nudge:" + Guid.NewGuid().ToString("D") + "] inspect";
            await using var db = World.Db();
            db.ExpectationEpisodes.Add(episode);
            db.CardComments.Add(audit);
            db.ExpectationNudges.Add(new ExpectationNudge
            {
                Id = Guid.NewGuid(), DirectiveId = World.Directive.Id, ConfigDigest = World.Digest,
                Ordinal = 1, EpisodeIdsJson = JsonSerializer.Serialize(new[] { episode.Id }),
                EvidenceSnapshot = "fence held", Body = body,
                BodyDigest = ExpectationDirectiveDigest.HashUtf8(body), AuditCommentId = audit.Id,
                AttemptState = ExpectationAttemptState.Refused,
                OperatorOutboxState = ExpectationOperatorOutboxState.Due,
                OperatorFirstDueAt = World.Now, OperatorNextAttemptAt = World.Now,
                CreatedAt = World.Now.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }
        public async Task SeedYoungNotesAsync(int count)
        {
            var taskId = Guid.NewGuid();
            await using var db = World.Db();
            db.AgentTasks.Add(World.Task(taskId, AgentTaskStatus.Succeeded, World.Now.AddMinutes(-1)));
            for (var i = 0; i < count; i++)
            {
                var note = ExpectationTestWorld.Queued(Guid.NewGuid(), World.OwnedSessionId,
                    QueuedMessageStatus.Pending, World.Now, i + 1);
                note.SourceTaskId = taskId;
                note.NoteHeader = "Completion for task " + taskId.ToString("D");
                note.Body = "[task " + taskId.ToString("D") + "] completed " + i;
                db.SessionQueuedMessages.Add(note);
            }
            await db.SaveChangesAsync();
        }
        public ServiceProvider Services(bool includeObservation = true)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Settings.Timing);
            services.AddSingleton<IOptions<ExpectationWatchdogSettings>>(Options.Create(Settings));
            services.AddSingleton<IOptions<SubscriptionQuotaGateSettings>>(Options.Create(new SubscriptionQuotaGateSettings()));
            services.AddScoped(_ => new AppDbContext(World.Options));
            services.AddSingleton<IAntiphonMessagingProducer>(Producer);
            services.AddSingleton<IExpectationCatchUp>(NoExpectationCatchUp.Instance);
            services.AddSingleton<IExpectationPromptSender, RefusePrompt>();
            services.AddSingleton<Antiphon.Server.Application.Interfaces.ISessionRunnerClient>(new RefusingSessionRunnerClient());
            services.AddSingleton<PhoneHomeRunnerDirectory>(sp => new PhoneHomeRunnerDirectory(
                sp.GetRequiredService<Antiphon.Server.Application.Interfaces.ISessionRunnerClient>(),
                Options.Create(new PhoneHomeRunnerSettings { Enabled = true, AllowedRunnerId = "server2" }),
                sp.GetRequiredService<IServiceScopeFactory>(), Clock));
            services.AddScoped<SubscriptionUsageReader>();
            services.AddSingleton<IModelAvailability, OpenModel>();
            services.AddSingleton<AgentTuiRunnerCatalog>();
            if (includeObservation) services.AddScoped<ExpectationObservationAdapter>();
            services.AddScoped<ExpectationLedger>(sp => new ExpectationLedger(sp.GetRequiredService<AppDbContext>(),
                Clock, new ExpectationTestWorld.QuietBus()));
            services.AddScoped<ExpectationWatchdogService>(sp => new ExpectationWatchdogService(
                sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ExpectationLedger>(), Clock,
                catchUp: sp.GetRequiredService<IExpectationCatchUp>(), timing: Settings.Timing));
            services.AddScoped<ExpectationNudgeDeliveryService>();
            services.AddScoped<ExpectationResponseService>();
            services.AddScoped<ExpectationOperatorDeliveryService>();
            services.AddTransient<ExpectationWatchdogJob>();
            services.AddSingleton<BlockedDefaultJob>();
            return services.BuildServiceProvider();
        }
        public ValueTask DisposeAsync() => Schema.DisposeAsync();
    }
    private sealed class OpenModel : IModelAvailability
    { public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) => Task.FromResult(false); }
    private sealed class RefusePrompt : IExpectationPromptSender
    {
        public Task<ExpectationSendResult> SendAsync(Guid sessionId, DateTime expectedGeneration,
            Guid ownerAgentId, string body, Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
            CancellationToken ct, Func<ExpectationSendResult, CancellationToken, Task>? recordOutcome = null) =>
            Task.FromResult(ExpectationSendResult.Refuse("no test recipient"));
    }
    private sealed class RecordingProducer : IAntiphonMessagingProducer
    {
        private readonly object _gate = new();
        public List<ChannelReply> Sent { get; } = [];
        public TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            lock (_gate) Sent.Add(reply);
            Published.TrySetResult();
            return Task.CompletedTask;
        }
    }
    public sealed class BlockedDefaultJob
    {
        private readonly ManualResetEventSlim _release = new(false);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Execute()
        {
            Started.TrySetResult();
            _release.Wait(TimeSpan.FromSeconds(20));
            Finished.TrySetResult();
        }
        public void Release() => _release.Set();
    }
    private sealed class ScopeActivator(IServiceProvider provider) : JobActivator
    {
        public override object ActivateJob(Type jobType) => provider.GetRequiredService(jobType);
    }
}
