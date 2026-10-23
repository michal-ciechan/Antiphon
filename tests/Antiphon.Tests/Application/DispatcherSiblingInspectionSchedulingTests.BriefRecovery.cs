using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DispatcherSiblingInspectionSchedulingTests
{
    [Test]
    [Arguments("claim-rollback")]
    [Arguments("before-brief-insert")]
    [Arguments("after-claim-before-launch")]
    [Arguments("after-claim-before-launch-capacity")]
    [Arguments("after-claim-before-launch-integration")]
    [Arguments("after-claim-before-launch-late")]
    [Arguments("after-claim-before-launch-unrelated")]
    [Arguments("after-claim-before-launch-event-type")]
    public async Task C1349_BriefHandoffRecovers(string scenario)
    {
        var absent = scenario.StartsWith("after-claim-before-launch", StringComparison.Ordinal);
        var rejectAbsence = scenario.EndsWith("-late", StringComparison.Ordinal)
            || scenario.EndsWith("-unrelated", StringComparison.Ordinal) || scenario.EndsWith("-event-type", StringComparison.Ordinal);
        using var repo = new ScratchGitRepo("c1349-brief-recovery");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var taskId = Guid.NewGuid();
        using var cancel = new CancellationTokenSource();
        var fault = new BriefCut(taskId, scenario, cancel);
        var sink = new AbsentLaunchSink();
        var runner = new InspectionRecoveryRunner();
        var slot = new InspectionAdapterSlot();
        var git = new InspectionGit();
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = true, ValidateScopes = true, ConnectionString = schema.ConnectionString,
            Delegation = new DelegationSettings { AllowedRoots = ["/", "C:\\"], MaxConcurrentTasks = 32 },
            ConfigureDbContext = options => options.AddInterceptors(fault, fault.Insert),
            ConfigureServices = services =>
            {
                ConfigureInspectionDelivery(services, repo, git, new FakeTimeProvider(DateTimeOffset.UtcNow), slot);
                services.AddSingleton<ISessionRunnerClient>(runner);
                if (absent) services.AddSingleton<IAgentTaskLaunchSink>(sink);
            },
        });
        slot.Adapter = h.Adapter; h.Adapter.RegisterOnStart = h.Runtime; h.Adapter.ReadyHold = ready;
        h.Adapter.OnSubmitted = async body =>
        {
            var recipient = absent ? h.SessionId : h.Adapter.StartedSessionId.ShouldNotBeNull();
            await BridgeQueueHarness.InsertEntryAsync(recipient, TranscriptKinds.UserPrompt, body,
                timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(recipient, TranscriptKinds.TurnEnd, stopReason: "end_turn",
                connectionString: schema.ConnectionString);
        };
        const string goal = "c1349 retained input café\nKeep the entire owned brief.";
        await using (var seed = h.CreateObserverContext())
        {
            var card = await DispatchSiblingInspectionTests.CardAsync(seed);
            var source = await DispatchSiblingInspectionTests.SourceAsync(seed, repo, card.Id, "source", "master");
            source.Status = AgentTaskStatus.Blocked;
            var task = QueueTask(repo, card.Id);
            task.Id = taskId; task.RootTaskId = taskId; task.Goal = goal;
            task.ParentSessionId = h.SessionId; task.ReplyTo = AgentTaskReplyTo.Session;
            seed.AgentTasks.Add(task); await seed.SaveChangesAsync();
            await seed.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentKind, AgentKind.Raw));
            // The caller session is a live local session for the whole test. Without it in the runner
            // inventory, the reconciliation scan below would close the caller too (unknown to the
            // runner) and the queue would then refuse to type the retained note into a Failed session.
            var caller = await seed.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId);
            runner.Caller = new SessionRunnerSessionDto(h.SessionId, 4141, caller.StartedAt, "Running", null,
                AgentExitReason.Unknown, 0, AcceptedStartedAt: caller.StartedAt);
        }
        var worker = h.Provider.GetRequiredService<CardSiblingInspectionQueue>(); worker.Start();
        var launches = h.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        try
        {
            await using (var scope = h.Provider.CreateAsyncScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                await dispatcher.TickAsync(cancel.Token); await worker.WhenIdleAsync();
                if (scenario == "claim-rollback")
                    await Should.ThrowAsync<OperationCanceledException>(() => dispatcher.TickAsync(cancel.Token));
                else await dispatcher.TickAsync(cancel.Token);
            }
            fault.Fired.ShouldBe(!absent);
            await using (var check = h.CreateObserverContext())
            {
                var task = await check.AgentTasks.SingleAsync(t => t.Id == taskId);
                task.Goal.ShouldBe(goal);
                (await check.TranscriptEntries.CountAsync(p => p.Kind == TranscriptKinds.UserPrompt && p.AgentSessionId == task.AgentSessionId)).ShouldBe(0);
                if (scenario == "claim-rollback")
                {
                    task.Status.ShouldBe(AgentTaskStatus.Queued); task.AgentSessionId.ShouldBeNull();
                    (await check.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == taskId)).ShouldBe(0);
                    (await check.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == taskId)).ShouldBe(0);
                }
                else
                {
                    task.Status.ShouldBe(AgentTaskStatus.Dispatched); task.AgentSessionId.ShouldNotBeNull();
                    (await check.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == taskId))
                        .ShouldBe(scenario == "before-brief-insert" ? 0 : 1);
                }
            }
            fault.Armed = false;
            if (scenario == "claim-rollback")
            {
                await using var scope = h.Provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(default);
            }
            ready.TrySetResult(true);
            if (!absent)
            {
                await launches.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await using var read = h.CreateObserverContext();
                var task = await read.AgentTasks.SingleAsync(t => t.Id == taskId);
                var sid = task.AgentSessionId.ShouldNotBeNull();
                var session = await read.AgentSessions.SingleAsync(s => s.Id == sid);
                h.Adapter.Started.ShouldBeTrue(); h.Adapter.StartedSessionId.ShouldBe(sid);
                h.Adapter.StartedAcceptedGeneration.ShouldBe(session.StartedAt);
                runner.Live = new SessionRunnerSessionDto(sid, 4242, session.StartedAt, "Running", null,
                    AgentExitReason.Unknown, 0, AcceptedStartedAt: session.StartedAt);
                h.Runtime.SetTestAcceptedStartedAt(sid, session.StartedAt);
                if (scenario == "before-brief-insert")
                {
                    (await read.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == taskId)).ShouldBe(0,
                        "a real launch with the failed insert still owes its brief before recovery");
                    await using var scope = h.Provider.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                        .ResumeInterruptedLaunchAsync(sid, task.AgentId!.Value, default);
                    var discovery = SessionReconciliationServiceTests.BuildService(read, runner, h.EventBus, ownership: launches);
                    await discovery.ScanAsync(default);
                    await launches.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                }
                var brief = await read.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == taskId);
                await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(schema.ConnectionString, h, brief, sid, false);
                var prompts = await read.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == sid && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
                var prompt = prompts.ShouldHaveSingleItem();
                prompt.Text.ShouldContain(DelegationReportFormatter.TaskMarker(taskId));
                var spill = DispatchBriefEvidence.AbsoluteSpillPath(brief.Body);
                var payload = brief.RemoteSpillBody ?? (spill is null ? brief.Body : await File.ReadAllTextAsync(spill));
                payload.Contains(goal, StringComparison.Ordinal).ShouldBeTrue(
                    "the delivered brief pointer must retain the entire owned input");
                PromptSubmissionMatch.IsCompleteIn(brief.Body, prompt.Text!).ShouldBeTrue();
                runner.Starts.ShouldBe(0); runner.Kills.ShouldBe(0);
            }
            else
            {
                sink.Calls.ShouldBe(1); h.Adapter.Started.ShouldBeFalse(); runner.Live.ShouldBeNull();
                AgentTask original; SessionQueuedMessage brief; AgentSession session;
                await using (var read = h.CreateObserverContext())
                {
                    original = await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                    brief = await read.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == taskId);
                    session = await read.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == original.AgentSessionId);
                    if (scenario != "after-claim-before-launch")
                    {
                        read.AgentTaskEvents.Add(new AgentTaskEvent
                        {
                            Id = Guid.NewGuid(), AgentTaskId = taskId,
                            Type = scenario.EndsWith("-event-type", StringComparison.Ordinal) ? AgentTaskEventType.Refined : AgentTaskEventType.Held,
                            Detail = scenario.EndsWith("-capacity", StringComparison.Ordinal)
                                ? "Held: same-card base inspection capacity is occupied."
                                : scenario.EndsWith("-integration", StringComparison.Ordinal)
                                    ? "Held: same-card pending integration could not be inspected."
                                    : scenario.EndsWith("-unrelated", StringComparison.Ordinal) ? "Held: unknown prior activity."
                                        : "Held: same-card base inspection is pending.",
                            At = original.DispatchedAt!.Value.AddSeconds(scenario.EndsWith("-late", StringComparison.Ordinal) ? 1 : -1),
                        });
                        await read.SaveChangesAsync();
                    }
                    var clock = new FakeTimeProvider(new DateTimeOffset(session.StartedAt).AddSeconds(91));
                    await SessionReconciliationServiceTests.BuildService(read, runner, h.EventBus, time: clock).ScanAsync(default);
                }
                var sweepClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
                await using (var sweep = AbsentSweep(schema.ConnectionString, runner, sweepClock, h.Queue))
                {
                    async Task SweepAsync()
                    {
                        await using var scope = sweep.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().FailDeadSessionTasksAsync(default);
                    }
                    await SweepAsync(); sweepClock.Advance(TimeSpan.FromSeconds(181)); await SweepAsync(); await SweepAsync();
                }
                await using var check = h.CreateObserverContext();
                var held = await check.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                if (rejectAbsence)
                {
                    held.Status.ShouldBe(AgentTaskStatus.Failed);
                    held.FailureReason.ShouldContain(SessionReconciliationService.RunnerUnknownSessionReason);
                    held.Attempt.ShouldBe(original.Attempt); held.AgentSessionId.ShouldBe(original.AgentSessionId); held.Goal.ShouldBe(goal);
                    (await check.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0);
                    sink.Calls.ShouldBe(1); runner.Starts.ShouldBe(0); runner.Kills.ShouldBe(0); h.Adapter.Started.ShouldBeFalse();
                    return;
                }
                held.Status.ShouldBe(AgentTaskStatus.Blocked); held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason);
                held.Attempt.ShouldBe(original.Attempt); held.AgentSessionId.ShouldBe(original.AgentSessionId); held.Goal.ShouldBe(goal);
                var retained = await check.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == taskId);
                retained.Id.ShouldBe(brief.Id); retained.Body.ShouldBe(brief.Body); retained.Sequence.ShouldBe(brief.Sequence);
                retained.Status.ShouldBe(QueuedMessageStatus.Pending); retained.DeliveryAttempts.ShouldBe(0);
                (await check.TranscriptEntries.CountAsync(p => p.AgentSessionId == session.Id && p.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
                var note = await check.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == taskId && m.ExecutionTaskId == null);
                note.AgentSessionId.ShouldBe(h.SessionId);
                (await check.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId))
                    .Status.ShouldBe(SessionStatus.Running, "recovery must leave the caller available for its note");
                (await h.Runtime.ListLiveSessionsAsync(default)).ShouldContain(h.SessionId);
                note.Status.ShouldBe(QueuedMessageStatus.Pending);
                note.DeliveryAttempts.ShouldBe(0);
                note.HoldUntil.ShouldBeNull();
                // The caller session stayed Running through the scan (the runner inventory lists it),
                // so the idle flush types the retained note directly; the note carries no hold and
                // the queue does not gate delivery on the sweep clock.
                await h.Queue.FlushIfIdleAsync(h.SessionId, default);
                h.Adapter.SubmittedBodies.ShouldNotBeEmpty("the live caller must actually receive the recovery note");
                var prompts = await check.TranscriptEntries.AsNoTracking().Where(p => p.AgentSessionId == h.SessionId && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
                PromptSubmissionMatch.IsCompleteIn(note.Body, prompts.ShouldHaveSingleItem().Text!).ShouldBeTrue();
                sink.Calls.ShouldBe(1); runner.Starts.ShouldBe(0); runner.Kills.ShouldBe(0); h.Adapter.Started.ShouldBeFalse();
            }
        }
        finally
        {
            ready.TrySetResult(true); await worker.StopAsync(default);
            await launches.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
        }
    }

    private static ServiceProvider AbsentSweep(string connection, InspectionRecoveryRunner runner, TimeProvider clock,
        SessionMessageQueueService queue)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));
        services.AddSingleton<IEventBus, MockEventBus>(); services.AddSingleton(clock);
        services.AddSingleton(Options.Create(new SupervisionSettings())); services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { DeadSessionFailGraceMinutes = 3 }));
        services.AddOptions<AgentRegistrySettings>(); services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>(); services.AddSingleton<AgentSessionRuntime>();
        // The sweep shares the harness queue (system clock, live caller adapter). A queue built on the
        // sweep's fake clock would attempt the caller note's delivery when the sweep fails the task
        // and then wait on a transcript-confirm deadline that never arrives.
        services.AddSingleton(queue); services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>(); services.AddDelegationWorktreeGraph();
        services.AddScoped<AgentTaskService>(); services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton(new DeadSessionFirstSeenState()); services.AddSingleton(new BootWedgeRelaunchState());
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
    private sealed class AbsentLaunchSink : IAgentTaskLaunchSink
    {
        public int Calls { get; private set; }
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        { Calls++; throw new IOException("owned launch refusal after the committed claim"); }
    }
    private sealed class BriefCut(Guid id, string scenario, CancellationTokenSource cancel) : DbTransactionInterceptor, IInterceptor
    {
        public DbCommandInterceptor Insert => new InsertCut(this, id, scenario);
        public bool Armed { get; set; } = true;
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            if (Armed && scenario == "claim-rollback" && eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Id == id && e.Entity.Status == AgentTaskStatus.Dispatched))
            { Fired = true; Armed = false; cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            return ValueTask.FromResult(result);
        }
        private sealed class InsertCut(BriefCut owner, Guid taskId, string cut) : DbCommandInterceptor
        {
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
                CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
            {
                if (owner.Armed && cut == "before-brief-insert" && command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                    && command.CommandText.Contains("SessionQueuedMessages", StringComparison.Ordinal)
                    && eventData.Context is AppDbContext db
                    && db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.ExecutionTaskId == taskId))
                { owner.Fired = true; owner.Armed = false; throw new IOException("owned brief insert rejected"); }
                return ValueTask.FromResult(result);
            }
        }
    }
    private sealed class InspectionRecoveryRunner : ISessionRunnerClient
    {
        public SessionRunnerSessionDto? Live { get; set; }
        /// <summary>The caller session as the runner serves it: alive for the whole test, so a
        /// reconciliation scan fails only the absent worker session and never the note recipient.</summary>
        public SessionRunnerSessionDto? Caller { get; set; }
        public int Starts { get; private set; }
        public int Kills { get; private set; }
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(
                new[] { Caller, Live }.Where(s => s is not null).Select(s => s!).ToList());
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) =>
            Caller is not null && Caller.SessionId == id ? Task.FromResult(Caller)
                : Live is not null && Live.SessionId == id ? Task.FromResult(Live)
                : throw new KeyNotFoundException("fixture session absent");
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct)
        { Starts++; throw new NotSupportedException(); }
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct)
        { Kills++; throw new NotSupportedException(); }
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerBufferDto(id, "", 0));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerSnapshotDto(id, "", "", 0, DateTime.UtcNow));
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0));
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { yield break; }
        public Task<SessionRunnerAbsenceEvidenceResult> CertifyAbsenceAsync(Guid id, DateTime generation, Guid? store, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow; var nonce = RunnerAbsenceEvidence.NewNonce(); var runnerStore = store ?? Guid.NewGuid();
            var shape = AbsenceCertificateShape.Pristine(id, generation, runnerStore, Guid.NewGuid(), nonce, now.UtcDateTime);
            return Task.FromResult(RunnerAbsenceEvidenceValidator.Validate(shape,
                new RunnerAbsenceExpectation(id, generation, runnerStore, nonce), RunnerAbsenceAuthentication.Verified, now, now, now));
        }
    }
}
