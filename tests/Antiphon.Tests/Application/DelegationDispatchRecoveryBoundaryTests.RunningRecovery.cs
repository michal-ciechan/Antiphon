using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    public async Task C1150_Running_cut_is_discovered_and_delivered_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Deliver this brief once.");
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            await db.AgentSessions.Where(s => s.Id == seeded.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Starting));
        }

        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId);
        var insert = new OnceQueuedInsertFault();
        await using var world = await OpenRecoveryAsync(schema.ConnectionString, adapter, runner, insert);
        WirePrompt(adapter, world, seeded.SessionId);

        await world.ResumeAsync(seeded);

        adapter.Disposed.ShouldBeFalse();
        adapter.KillCount.ShouldBe(0);
        runner.Starts.ShouldBe(0);
        runner.Kills.ShouldBe(0);
        await using (var db = world.Read())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId);
            session.Status.ShouldBe(SessionStatus.Starting);
            session.LaunchResumedAt.ShouldNotBeNull();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seeded.SessionId)).ShouldBe(0);
        }

        insert.Armed = false;
        world.Harness.EventBus.ThrowOnce = new OperationCanceledException();
        world.Harness.EventBus.ThrowOnceOnEvent = "SessionStarted";
        await Should.ThrowAsync<OperationCanceledException>(() => world.ResumeAsync(seeded));

        Guid briefId;
        DateTime? resumedAt;
        await using (var db = world.Read())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId);
            session.Status.ShouldBe(SessionStatus.Running);
            resumedAt = session.LaunchResumedAt;
            resumedAt.ShouldNotBeNull();
            var briefs = await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
            briefs.Count.ShouldBe(1);
            briefs[0].Status.ShouldBe(QueuedMessageStatus.Pending);
            briefs[0].DeliveryAttempts.ShouldBe(0);
            AssertRecoveredBrief(briefs[0], seeded.Goal, "cut after Running");
            briefId = briefs[0].Id;
            (await db.TranscriptEntries.CountAsync(t =>
                t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
        }

        adapter.Disposed.ShouldBeFalse();
        runner.Kills.ShouldBe(0);
        adapter.StartedAcceptedGeneration.ShouldBeNull();
        runner.AcceptedStartedAt = seeded.StartedAt;
        world.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);

        await world.DiscoverAsync(seeded);
        await using (var db = world.Read())
        {
            var briefs = await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
            briefs.Count.ShouldBe(1);
            briefs[0].Id.ShouldBe(briefId);
            AssertRecoveredBrief(briefs[0], seeded.Goal, "discovered once");
            var prompts = await db.TranscriptEntries
                .Where(t => t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)
                .Select(t => t.Text ?? "")
                .ToListAsync();
            prompts.Count.ShouldBe(1);
            prompts[0].Contains(DelegationReportFormatter.TaskMarker(seeded.TaskId), StringComparison.Ordinal)
                .ShouldBeTrue("discovered once");
            PromptSubmissionMatch.IsCompleteIn(briefs[0].Body, prompts[0]).ShouldBeTrue("discovered once");
            (await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).LaunchResumedAt.ShouldBe(resumedAt);
        }

        runner.Starts.ShouldBe(0);
        adapter.StartedAcceptedGeneration.ShouldBeNull();
        await world.DiscoverAsync(seeded);
        await using (var db = world.Read())
        {
            (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == seeded.TaskId)).ShouldBe(1);
            AssertRecoveredBrief(
                await db.SessionQueuedMessages.SingleAsync(m => m.Id == briefId), seeded.Goal, "second scan");
            (await db.TranscriptEntries.CountAsync(t =>
                t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)).ShouldBe(1);
            (await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).LaunchResumedAt.ShouldBe(resumedAt);
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        }
    }

    [Test]
    public async Task C1150_Insert_before_flush_cut_reuses_pending_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Reuse this pending row.");
        var existing = HealthyRow(seeded, seeded.Goal);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.SessionQueuedMessages.Add(existing);
            await db.SaveChangesAsync();
        }

        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var world = await OpenRecoveryAsync(schema.ConnectionString, adapter, runner, interceptor: null);
        WirePrompt(adapter, world, seeded.SessionId);
        world.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);

        await world.ResumeAsync(seeded);

        await using var verify = world.Read();
        var briefs = await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
        briefs.Count.ShouldBe(1);
        briefs[0].Id.ShouldBe(existing.Id);
        briefs[0].Body.ShouldBe(existing.Body);
        var prompts = await verify.TranscriptEntries
            .Where(t => t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)
            .Select(t => t.Text ?? "")
            .ToListAsync();
        prompts.Count.ShouldBe(1);
        prompts[0].ShouldContain(seeded.Goal);
        runner.Starts.ShouldBe(0);
        adapter.StartedAcceptedGeneration.ShouldBeNull();
        runner.Kills.ShouldBe(0);
    }

    [Test]
    [Arguments("brief-before-ui")]
    [Arguments("ui-before-missing-backfill")]
    public async Task C1150_Followup_survives_both_queue_orders(string order)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Keep both bodies.");
        const string uiBody = "follow-up ui body café";
        var uiId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            if (order == "brief-before-ui")
            {
                var brief = HealthyRow(seeded, seeded.Goal);
                brief.Sequence = 1;
                db.SessionQueuedMessages.Add(brief);
            }

            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = uiId,
                AgentSessionId = seeded.SessionId,
                Origin = QueuedMessageOrigin.Ui,
                Status = QueuedMessageStatus.Pending,
                Sequence = order == "brief-before-ui" ? 2 : 1,
                CreatedAt = seeded.DispatchedAt,
                Body = uiBody,
            });
            await db.SaveChangesAsync();
        }

        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var world = await OpenRecoveryAsync(schema.ConnectionString, adapter, runner, interceptor: null);
        WirePrompt(adapter, world, seeded.SessionId);
        world.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);
        await world.ResumeAsync(seeded);

        await using (var mid = world.Read())
        {
            var ui = await mid.SessionQueuedMessages.SingleAsync(m => m.Id == uiId);
            ui.Body.ShouldBe(uiBody, order);
            ui.Sequence.ShouldBe(order == "brief-before-ui" ? 2 : 1, order);
            var briefs = await mid.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
            briefs.Count.ShouldBe(1, order);
            if (order == "brief-before-ui")
            {
                ui.Status.ShouldBe(QueuedMessageStatus.Pending, order);
                adapter.SubmittedBodies.Count.ShouldBe(1, order);
                adapter.SubmittedBodies[0].Contains(seeded.Goal, StringComparison.Ordinal).ShouldBeTrue(order);
                adapter.SubmittedBodies[0].Contains(uiBody, StringComparison.Ordinal).ShouldBeFalse(order);
            }
            else
            {
                ui.Status.ShouldBe(QueuedMessageStatus.Sent, order);
                briefs[0].Status.ShouldBe(QueuedMessageStatus.Pending, order);
                adapter.SubmittedBodies.Count.ShouldBe(1, order);
                adapter.SubmittedBodies[0].Contains(uiBody, StringComparison.Ordinal).ShouldBeTrue(order);
                adapter.SubmittedBodies[0].Contains(seeded.Goal, StringComparison.Ordinal).ShouldBeFalse(order);
            }
        }

        await world.Harness.Queue.FlushIfIdleAsync(seeded.SessionId, CancellationToken.None);
        await using var verify = world.Read();
        var keptUi = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == uiId);
        keptUi.Body.ShouldBe(uiBody, order);
        keptUi.Sequence.ShouldBe(order == "brief-before-ui" ? 2 : 1, order);
        var keptBrief = await verify.SessionQueuedMessages.SingleAsync(m => m.ExecutionTaskId == seeded.TaskId);
        AssertRecoveredBrief(keptBrief, seeded.Goal, order);
        var prompts = await verify.TranscriptEntries
            .Where(t => t.AgentSessionId == seeded.SessionId && t.Kind == TranscriptKinds.UserPrompt)
            .Select(t => t.Text ?? "")
            .ToListAsync();
        prompts.Count.ShouldBe(2, order);
        prompts.ShouldContain(p => PromptSubmissionMatch.IsCompleteIn(uiBody, p), order);
        prompts.ShouldContain(p => PromptSubmissionMatch.IsCompleteIn(keptBrief.Body, p), order);
    }

    [Test]
    [Arguments("working")]
    [Arguments("unavailable")]
    [Arguments("wrong-generation")]
    [Arguments("owned")]
    [Arguments("pending-ui")]
    [Arguments("compaction")]
    public async Task C1150_Discovery_gates_withhold_recovery(string flip)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedCurrentAsync(schema.ConnectionString, "Eligible baseline " + flip);
        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecoveryRunner(seeded.SessionId) { AcceptedStartedAt = seeded.StartedAt };
        await using var world = await OpenRecoveryAsync(schema.ConnectionString, adapter, runner, interceptor: null);
        world.Harness.Runtime.SetTestAcceptedStartedAt(seeded.SessionId, seeded.StartedAt);
        var queue = world.Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        DateTime startedAt;
        DateTime dispatchedAt;
        await using (var db = world.Read())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            startedAt = session.StartedAt;
            dispatchedAt = task.DispatchedAt!.Value;
            if (flip == "working")
            {
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = seeded.SessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.AssistantText,
                    Text = "still working",
                    Timestamp = dispatchedAt.AddSeconds(1),
                    CreatedAt = dispatchedAt.AddSeconds(1),
                });
                await db.SaveChangesAsync();
            }
            else if (flip == "compaction")
            {
                var episodeId = Guid.NewGuid();
                db.AgentSupervisionStates.Add(new AgentSupervisionState
                {
                    AgentId = seeded.AgentId,
                    ActiveCompactionRecoveryId = episodeId,
                    UpdatedAt = dispatchedAt,
                });
                db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
                {
                    Id = episodeId,
                    PhysicalAgentId = seeded.AgentId,
                    SessionId = seeded.SessionId,
                    AcceptedStartedAt = startedAt,
                    BoundaryIdentity = "boundary-" + flip,
                    BoundaryCreatedAt = dispatchedAt,
                    ContinuationCreatedAt = dispatchedAt,
                    ConfiguredThresholdMinutes = 10,
                    DetectedAt = dispatchedAt,
                    State = CheckCompactionRecoveryState.Confirmed,
                });
                await db.SaveChangesAsync();
            }
        }

        if (flip == "unavailable")
            runner.ThrowOnGet = true;
        else if (flip == "wrong-generation")
            runner.AcceptedStartedAt = startedAt.AddMinutes(5);
        else if (flip == "owned")
            queue.TryRegister(seeded.SessionId).ShouldBeTrue(flip);
        else if (flip == "pending-ui")
            runner.Pending = "ui";

        await world.ResumeAsync(seeded);

        adapter.Attached.ShouldBeFalse(flip);
        adapter.SubmittedBodies.ShouldBeEmpty(flip);
        adapter.KillCount.ShouldBe(0, flip);
        adapter.Disposed.ShouldBeFalse(flip);
        runner.Starts.ShouldBe(0, flip);
        runner.Kills.ShouldBe(0, flip);
        await using (var mid = world.Read())
        {
            (await mid.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seeded.SessionId)).ShouldBe(0, flip);
            var session = await mid.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId);
            session.Status.ShouldBe(SessionStatus.Running, flip);
            session.StartedAt.ShouldBe(startedAt, flip);
            session.LaunchResumedAt.ShouldBeNull(flip);
            var task = await mid.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, flip);
            task.DispatchedAt.ShouldBe(dispatchedAt, flip);
            task.Attempt.ShouldBe(1, flip);
        }

        if (flip == "working")
        {
            await using var db = world.Read();
            await db.TranscriptEntries.Where(t => t.AgentSessionId == seeded.SessionId).ExecuteDeleteAsync();
        }
        else if (flip == "unavailable")
            runner.ThrowOnGet = false;
        else if (flip == "wrong-generation")
            runner.AcceptedStartedAt = startedAt;
        else if (flip == "owned")
            queue.Unregister(seeded.SessionId);
        else if (flip == "pending-ui")
            runner.Pending = null;
        else if (flip == "compaction")
        {
            await using var db = world.Read();
            await db.AgentSupervisionStates.Where(s => s.AgentId == seeded.AgentId).ExecuteDeleteAsync();
            await db.CheckCompactionRecoveries.Where(r => r.SessionId == seeded.SessionId).ExecuteDeleteAsync();
        }

        WirePrompt(adapter, world, seeded.SessionId);
        await world.ResumeAsync(seeded);

        adapter.Attached.ShouldBeTrue(flip);
        runner.Kills.ShouldBe(0, flip);
        await using var verify = world.Read();
        var recovered = await verify.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
        recovered.Count.ShouldBe(1, flip);
        AssertRecoveredBrief(recovered[0], seeded.Goal, flip);
        (await verify.AgentSessions.SingleAsync(s => s.Id == seeded.SessionId)).LaunchResumedAt.ShouldBeNull(flip);
        (await verify.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).Status.ShouldBe(AgentTaskStatus.Dispatched, flip);
    }

    private static void AssertRecoveredBrief(SessionQueuedMessage brief, string goal, string label)
    {
        var marker = DelegationReportFormatter.TaskMarker(brief.ExecutionTaskId!.Value);
        brief.Body.Contains(marker, StringComparison.Ordinal).ShouldBeTrue(label);
        var payload = AuthoritativeBrief(brief);
        payload.Contains(marker, StringComparison.Ordinal).ShouldBeTrue(label);
        payload.Contains(goal, StringComparison.Ordinal).ShouldBeTrue(label);
    }

    private static void WirePrompt(FakeAgentProtocolAdapter adapter, RecoveryWorld world, Guid sessionId)
    {
        var sequence = 0;
        adapter.OnSubmitted = async body =>
        {
            var promptSequence = Interlocked.Add(ref sequence, 2) - 1;
            var now = DateTime.UtcNow;
            await using var db = world.Read();
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = promptSequence,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"c1150-{Guid.NewGuid():N}",
                Role = "user",
                Text = body,
                Timestamp = now,
                CreatedAt = now,
            });
            // The prompt is activity. A following turn end is the idle edge the next
            // single-message flush is allowed to see; recovery itself does not loop.
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = promptSequence + 1,
                Kind = TranscriptKinds.TurnEnd,
                Uuid = $"c1150-end-{Guid.NewGuid():N}",
                Timestamp = now,
                CreatedAt = now,
                StopReason = "end_turn",
            });
            await db.SaveChangesAsync();
        };
    }

    private static async Task<RecoveryWorld> OpenRecoveryAsync(
        string connection,
        FakeAgentProtocolAdapter adapter,
        RecoveryRunner runner,
        IInterceptor? interceptor)
    {
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connection,
            PreserveDatabaseOnDispose = true,
            ConfigureDbContext = interceptor is null ? null : options => options.AddInterceptors(interceptor),
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(new RecoveryAdapterFactory(adapter));
                services.AddSingleton<ISessionRunnerClient>(runner);
            },
        });
        adapter.RegisterOnStart = harness.Runtime;
        return new RecoveryWorld(harness);
    }

    private sealed class RecoveryWorld(BridgeQueueHarness harness) : IAsyncDisposable
    {
        public BridgeQueueHarness Harness { get; } = harness;

        public AppDbContext Read() => new(TestDbFixture.CreateDbContextOptions(Harness.ConnectionString));

        public async Task ResumeAsync(CurrentBrief seeded, CancellationToken ct = default)
        {
            await using var scope = Harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(seeded.SessionId, seeded.AgentId, ct);
        }

        public async Task DiscoverAsync(CurrentBrief seeded)
        {
            var queue = Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>();
            await using var db = Read();
            var service = SessionReconciliationServiceTests.BuildService(
                db, Harness.Provider.GetRequiredService<ISessionRunnerClient>(), Harness.EventBus, ownership: queue);
            await service.ScanAsync(CancellationToken.None);
            await queue.WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }

    private sealed class RecoveryAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RecoveryRunner(Guid sessionId) : ISessionRunnerClient
    {
        public DateTime? AcceptedStartedAt { get; set; }
        public string? Pending { get; set; }
        public bool ThrowOnGet { get; set; }
        public int Starts { get; private set; }
        public int Kills { get; private set; }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([Row(sessionId)]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct)
        {
            if (ThrowOnGet)
                throw new IOException("runner unavailable");
            return Task.FromResult(Row(id));
        }

        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct)
        {
            Kills++;
            return Task.FromResult(Row(id) with { Status = "Exited", ExitCode = 0 });
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct)
        {
            Starts++;
            throw new NotSupportedException();
        }

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(id, "", 0));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(id, "", "", 0, DateTime.UtcNow));
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0));
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }

        private SessionRunnerSessionDto Row(Guid id) =>
            new(id, Pid: 4242, StartedAt: AcceptedStartedAt ?? DateTime.UtcNow,
                Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown, LastSequence: 1,
                Pending: Pending, AcceptedStartedAt: AcceptedStartedAt);
    }

    private sealed class OnceQueuedInsertFault : DbCommandInterceptor
    {
        public bool Armed { get; set; } = true;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CutIfBriefInsert(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            // Npgsql returns the inserted row, so the brief insert is a reader, not a non-query.
            CutIfBriefInsert(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void CutIfBriefInsert(DbCommand command)
        {
            if (Armed
                && command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("SessionQueuedMessages", StringComparison.OrdinalIgnoreCase))
            {
                Armed = false;
                throw new IOException("brief insert cut");
            }
        }
    }
}
