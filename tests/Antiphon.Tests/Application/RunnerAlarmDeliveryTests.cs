using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0726 V-30..V-34. Outage notes reach the caller through the real queue.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerAlarmDeliveryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 1, 2, 3, TimeSpan.Zero);

    [Test]
    [Timeout(60_000)]
    public async Task an_idle_caller_receives_the_outage_and_recovery_notes_as_complete_user_prompts()
    {
        await using var rig = await StartAsync();
        var task = await SeedPinnedAsync(rig, AgentTaskStatus.Working);
        await rig.Coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        var outage = await UntilPromptAsync(rig, "[runner server2 unavailable]");
        outage.ShouldContain(Short(task));
        outage.ShouldContain("transport_abort");
        (await RowAsync(rig, outage)).Status.ShouldBe(QueuedMessageStatus.Sent);

        rig.Source.Rows[0] = rig.Source.Rows[0] with { Eligible = true, DisconnectReason = null };
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(444), CancellationToken.None);
        var recovery = await UntilPromptAsync(rig, "[runner server2 recovered]");
        recovery.ShouldContain("after 7.4 min");
        await rig.Worker.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task a_busy_caller_gets_the_note_only_after_its_turn_ends()
    {
        await using var rig = await StartAsync();
        await SeedPinnedAsync(rig, AgentTaskStatus.Working);
        await SetWorkingAsync(rig, true);
        await rig.Coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        await rig.Harness.Queue.FlushIfIdleAsync(rig.Harness.SessionId, CancellationToken.None);
        rig.Harness.Adapter.Inputs.Any(input => input.Contains("[runner server2 unavailable]", StringComparison.Ordinal))
            .ShouldBeFalse();
        (await PendingAsync(rig)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await PromptsAsync(rig)).ShouldNotContain(text => text.Contains("[runner server2 unavailable]", StringComparison.Ordinal));

        await SetWorkingAsync(rig, false);
        await rig.Harness.Queue.OnTurnEndAsync(rig.Harness.SessionId, CancellationToken.None);
        var prompt = await UntilPromptAsync(rig, "[runner server2 unavailable]");
        prompt.ShouldContain("transport_abort");
        await rig.Worker.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task a_dropped_flush_hint_is_re_hinted_from_the_durable_row()
    {
        var flush = new DroppingFlushQueue(1);
        await using var rig = await StartAsync(flush);
        await SeedPinnedAsync(rig, AgentTaskStatus.Queued);
        await rig.Coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        await Task.Delay(100);
        (await PendingAsync(rig)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await PromptsAsync(rig)).ShouldBeEmpty();

        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(181), CancellationToken.None);
        await UntilPromptAsync(rig, "[runner server2 unavailable]");
        flush.Calls.Count(id => id == rig.Harness.SessionId).ShouldBe(2);
        await rig.Worker.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task a_failed_queue_insert_is_retried_on_the_next_wake_and_delivered_once()
    {
        // One interceptor instance. DbContext options are built per scope, so `new` inside the
        // configuration action would fail every insert and the retry wake could never deliver.
        var fault = new FailFirstInsert();
        await using var rig = await StartAsync(configure: options => options.AddInterceptors(fault));
        await SeedPinnedAsync(rig, AgentTaskStatus.Working);
        await rig.Coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        (await rig.Db.SessionQueuedMessages.CountAsync(row => row.AgentSessionId == rig.Harness.SessionId)).ShouldBe(0);
        rig.State.Current.Episodes.ShouldHaveSingleItem().NotifiedSessionIds.ShouldBeEmpty();

        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(181), CancellationToken.None);
        await UntilPromptAsync(rig, "[runner server2 unavailable]");
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(182), CancellationToken.None);
        (await PromptsAsync(rig)).Count(text => text.Contains("[runner server2 unavailable]", StringComparison.Ordinal)).ShouldBe(1);
        await rig.Worker.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task a_failed_recovery_insert_is_retried_and_delivered_once()
    {
        var fault = new FailFirstBodyInsert("[runner server2 recovered]");
        await using var rig = await StartAsync(configure: options => options.AddInterceptors(fault));
        await SeedPinnedAsync(rig, AgentTaskStatus.Working);
        await rig.Coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        await UntilProductionPromptAsync(rig, "[runner server2 unavailable]");

        rig.Source.Rows[0] = rig.Source.Rows[0] with { Eligible = true, DisconnectReason = null };
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(444), CancellationToken.None);
        (await PromptsAsync(rig)).ShouldNotContain(text => text.Contains("[runner server2 recovered]", StringComparison.Ordinal));

        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(445), CancellationToken.None);
        var recovery = await UntilProductionPromptAsync(rig, "[runner server2 recovered]");
        recovery.ShouldContain("after 7.4 min");
        (await PromptsAsync(rig)).Count(text => text.Contains("[runner server2 recovered]", StringComparison.Ordinal)).ShouldBe(1);
        (await RowAsync(rig, recovery)).Status.ShouldBe(QueuedMessageStatus.Sent);
        await rig.Worker.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(60_000)]
    public async Task a_dropped_recovery_hint_is_re_hinted_and_delivered_once()
    {
        // Call 1 is the outage hint. Call 2 is the recovery hint, which is dropped.
        var flush = new DropNthFlushQueue(2);
        await using var rig = await StartAsync(flush);
        await SeedPinnedAsync(rig, AgentTaskStatus.Working);
        await rig.Coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        await UntilProductionPromptAsync(rig, "[runner server2 unavailable]");

        rig.Source.Rows[0] = rig.Source.Rows[0] with { Eligible = true, DisconnectReason = null };
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(444), CancellationToken.None);
        await Task.Delay(100);
        (await PromptsAsync(rig)).ShouldNotContain(text => text.Contains("[runner server2 recovered]", StringComparison.Ordinal));
        (await rig.Db.SessionQueuedMessages.AsNoTracking().SingleAsync(row => row.Body.Contains("[runner server2 recovered]", StringComparison.Ordinal)))
            .Status.ShouldBe(QueuedMessageStatus.Pending);

        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(445), CancellationToken.None);
        var recovery = await UntilProductionPromptAsync(rig, "[runner server2 recovered]");
        recovery.ShouldContain("after 7.4 min");
        (await RowAsync(rig, recovery)).Status.ShouldBe(QueuedMessageStatus.Sent);
        flush.Calls.Count(id => id == rig.Harness.SessionId).ShouldBeGreaterThanOrEqualTo(3);
        await rig.Worker.StopAsync(CancellationToken.None);
    }

    [Test]
    [Timeout(90_000)]
    public async Task a_note_orphaned_by_a_restart_is_typed_with_the_next_flush()
    {
        var flush = new DroppingFlushQueue(1);
        await using var rig = await StartAsync(flush);
        await SeedPinnedAsync(rig, AgentTaskStatus.Working);
        await rig.Coordinator.EvaluateRunnersAsync(T0, CancellationToken.None);
        await rig.Coordinator.EvaluateRunnersAsync(T0.AddSeconds(180), CancellationToken.None);
        var first = await PendingAsync(rig);
        var firstBody = first.Body;

        var restarted = new RunnerAlarmState();
        var coordinator = NewCoordinator(rig, restarted);
        var t1 = T0.AddHours(1);
        await coordinator.EvaluateRunnersAsync(t1, CancellationToken.None);
        await coordinator.EvaluateRunnersAsync(t1.AddSeconds(180), CancellationToken.None);
        await UntilAsync(async () =>
        {
            await rig.Harness.Queue.FlushIfIdleAsync(rig.Harness.SessionId, CancellationToken.None);
            var text = string.Join("\n", await PromptsAsync(rig));
            return text.Contains(firstBody, StringComparison.Ordinal)
                && text.Contains("[runner server2 unavailable]", StringComparison.Ordinal);
        });
        rig.Db.ChangeTracker.Clear();
        var joined = string.Join("\n", await PromptsAsync(rig));
        joined.ShouldContain(firstBody);
        (await rig.Db.SessionQueuedMessages.AsNoTracking().Where(row => row.AgentSessionId == rig.Harness.SessionId).ToListAsync())
            .ShouldAllBe(row => row.Status == QueuedMessageStatus.Sent);
        await rig.Worker.StopAsync(CancellationToken.None);
    }

    private static async Task<Rig> StartAsync(CompletionNoteFlushQueue? flush = null, Action<DbContextOptionsBuilder>? configure = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var queue = flush ?? new CompletionNoteFlushQueue();
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = schema.ConnectionString,
            PreserveDatabaseOnDispose = true,
            ConfigureDbContext = configure,
            ConfigureServices = services => services.AddSingleton(queue),
        });
        var worker = new CompletionNoteWorkHostedService(
            harness.Provider.GetRequiredService<IServiceScopeFactory>(), queue, new SpecialistFailureQueue(),
            TimeProvider.System, NullLogger<CompletionNoteWorkHostedService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var scope = harness.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = new FakeEligibilitySource();
        source.Rows.Add(new RunnerEligibilitySnapshot("server2", "server2", true, false, "transport_abort", null, 0));
        var state = new RunnerAlarmState();
        var rig = new Rig(schema, harness, scope, db, source, state, queue, worker, NewCoordinator(db, source, state, queue, harness));
        return rig;
    }

    private static RunnerAlarmCoordinator NewCoordinator(Rig rig, RunnerAlarmState state) =>
        NewCoordinator(rig.Db, rig.Source, state, rig.Flush, rig.Harness);

    private static RunnerAlarmCoordinator NewCoordinator(AppDbContext db, FakeEligibilitySource source, RunnerAlarmState state,
        CompletionNoteFlushQueue flush, BridgeQueueHarness harness) =>
        new(source, new NeverExcluded(), state, new RepositoryChildJournalInspector(new LandingGit()), db,
            new QueueRunnerAlarmNotifier(harness.Queue, flush), flush, Options.Create(new AlarmSettings()),
            NullLogger<RunnerAlarmCoordinator>.Instance);

    private static async Task<Guid> SeedPinnedAsync(Rig rig, AgentTaskStatus status)
    {
        var id = Guid.NewGuid();
        rig.Db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "pinned", Goal = "goal", Role = AgentTaskRole.Code, Status = status,
            RunnerId = "server2", ParentSessionId = rig.Harness.SessionId, ReplyTo = AgentTaskReplyTo.Session,
            AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(), CreatedAt = DateTime.UtcNow,
        });
        await rig.Db.SaveChangesAsync();
        return id;
    }

    private static async Task SetWorkingAsync(Rig rig, bool working)
    {
        var seq = ((await rig.Db.TranscriptEntries.Where(entry => entry.AgentSessionId == rig.Harness.SessionId)
            .MaxAsync(entry => (long?)entry.Sequence)) ?? 0) + 1;
        rig.Db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = rig.Harness.SessionId, Sequence = seq,
            Kind = working ? TranscriptKinds.AssistantText : TranscriptKinds.TurnEnd,
            Text = working ? "busy" : null,
            StopReason = working ? null : TranscriptKinds.StopReasons.EndTurn,
            CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
        });
        await rig.Db.SaveChangesAsync();
    }

    private static async Task<List<string>> PromptsAsync(Rig rig) =>
        await rig.Db.TranscriptEntries.AsNoTracking()
            .Where(entry => entry.AgentSessionId == rig.Harness.SessionId && entry.Kind == TranscriptKinds.UserPrompt)
            .Select(entry => entry.Text ?? "").ToListAsync();

    private static async Task<string> UntilPromptAsync(Rig rig, string header)
    {
        string? found = null;
        await UntilAsync(async () =>
        {
            await rig.Harness.Queue.FlushIfIdleAsync(rig.Harness.SessionId, CancellationToken.None);
            found = (await PromptsAsync(rig)).FirstOrDefault(text => text.Contains(header, StringComparison.Ordinal));
            return found is not null;
        });
        return found!;
    }

    /// <summary>Polls the transcript only. A test-side flush would hide a missing production hint.</summary>
    private static async Task<string> UntilProductionPromptAsync(Rig rig, string header)
    {
        string? found = null;
        await UntilAsync(async () =>
        {
            found = (await PromptsAsync(rig)).FirstOrDefault(text => text.Contains(header, StringComparison.Ordinal));
            return found is not null;
        });
        return found!;
    }

    private static async Task<SessionQueuedMessage> PendingAsync(Rig rig) =>
        await rig.Db.SessionQueuedMessages.AsNoTracking().SingleAsync(row => row.AgentSessionId == rig.Harness.SessionId);

    private static async Task<SessionQueuedMessage> RowAsync(Rig rig, string prompt)
    {
        var rows = await rig.Db.SessionQueuedMessages.AsNoTracking()
            .Where(row => row.AgentSessionId == rig.Harness.SessionId)
            .ToListAsync();
        return rows.Single(row => prompt.Contains(row.Body, StringComparison.Ordinal));
    }

    private static async Task UntilAsync(Func<Task<bool>> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await ready()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("alarm note was not delivered");
    }

    private static string Short(Guid id) => id.ToString("N")[..8];

    private sealed class FailFirstInsert : SaveChangesInterceptor
    {
        private int _failures;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(entry => entry.State == EntityState.Added)
                && Interlocked.CompareExchange(ref _failures, 1, 0) == 0)
                throw new InvalidOperationException("owned first alarm insert failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailFirstBodyInsert(string marker) : SaveChangesInterceptor
    {
        private int _failures;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var added = data.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                .Any(entry => entry.State == EntityState.Added
                    && (entry.Entity.Body ?? "").Contains(marker, StringComparison.Ordinal));
            if (added && Interlocked.CompareExchange(ref _failures, 1, 0) == 0)
                throw new InvalidOperationException("owned recovery insert failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Rig(
        IsolatedTestSchema schema, BridgeQueueHarness harness, AsyncServiceScope scope, AppDbContext db,
        FakeEligibilitySource source, RunnerAlarmState state, CompletionNoteFlushQueue flush,
        CompletionNoteWorkHostedService worker, RunnerAlarmCoordinator coordinator) : IAsyncDisposable
    {
        public IsolatedTestSchema Schema { get; } = schema;
        public BridgeQueueHarness Harness { get; } = harness;
        public AppDbContext Db { get; } = db;
        public FakeEligibilitySource Source { get; } = source;
        public RunnerAlarmState State { get; } = state;
        public CompletionNoteFlushQueue Flush { get; } = flush;
        public CompletionNoteWorkHostedService Worker { get; } = worker;
        public RunnerAlarmCoordinator Coordinator { get; } = coordinator;
        public async ValueTask DisposeAsync()
        {
            await Worker.StopAsync(CancellationToken.None);
            await scope.DisposeAsync();
            await Harness.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }
}
