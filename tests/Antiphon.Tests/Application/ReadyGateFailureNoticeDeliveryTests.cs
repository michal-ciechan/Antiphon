using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1190 D4. The producer is the dead-session reconciler
/// (<see cref="AgentTaskDispatcher.FailDeadSessionTasksAsync"/>), given a child session the
/// ready-gate writer already stored as Failed / <see cref="AgentSessionService.NotReadyBase"/> /
/// SystemRequest / exit 0. The recipient is the parent session's real
/// <see cref="SessionMessageQueueService"/> on a <see cref="BridgeQueueHarness"/>.
/// This is the CARD-1165 fake-recipient shape of that same queue: the harness adapter records the
/// typed body as a transcript UserPrompt. ConPTY and <c>SessionMessageQueuePtyIntegrationTests</c>
/// are not available on the Linux runner, and this class does not claim they ran.
/// A queue row alone is not the receipt. The receipt is one complete matching UserPrompt.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ReadyGateFailureNoticeDeliveryTests
{
    /// <summary>
    /// Producer to recipient for the ready-gate failure notice.
    /// eligible — case: an idle parent takes the note inline. mutation: drop the completion-note
    /// enqueue. red: no complete UserPrompt of the not-ready notice.
    /// busy — case: an open turn keeps the row Pending and untyped; a later TurnEnd makes the
    /// parent eligible and the real turn-end flush types it once. mutation: type during the open
    /// turn, or drop the pending row. red: a UserPrompt before the turn ends, a second prompt
    /// after, or the row gone.
    /// enqueue-failure — case: the insert save throws, the Failed task stays unstamped, and the
    /// next sweep's recovery types the note once. mutation: treat the failed save as delivered.
    /// red: a queue row or prompt before the retry, or the retry is not the only prompt.
    /// crash-after-enqueue — case: the insert commits, the pre-type Sent save throws, and a
    /// restarted queue's stranded flush types that same row once. mutation: replay inserts
    /// another row. red: two queue rows or two UserPrompts.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("enqueue-failure")]
    [Arguments("crash-after-enqueue")]
    public async Task C1190_D4_Ready_gate_failure_notice_reaches_the_parent(string path, CancellationToken ct)
    {
        var cut = path switch
        {
            "enqueue-failure" => NoticeCut.Insert,
            "crash-after-enqueue" => NoticeCut.SentStamp,
            "eligible" or "busy" => NoticeCut.None,
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, "unknown D4 path"),
        };
        var fault = new NoticeFault { Mode = cut };
        var preserve = path == "crash-after-enqueue";
        var parent = await ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            PreserveDatabaseOnDispose = preserve,
            ConfigureDbContext = cut == NoticeCut.None ? null : o => o.AddInterceptors(fault),
        });
        fault.SessionId = parent.SessionId;

        var childCwd = Path.Combine(Path.GetTempPath(), $"antiphon-c1190-child-{Guid.NewGuid():N}");
        Directory.CreateDirectory(childCwd);
        var child = await SeedChildAsync(parent.SessionId, childCwd);
        var stopper = new RecordingSessionStopper();
        var graph = Graph.Open(parent, stopper, child.SessionId);
        BridgeQueueHarness? restarted = null;
        var parentDisposed = false;
        try
        {
            await graph.SweepAsync(ct);
            (await ReadTaskAsync(child.TaskId)).Status.ShouldBe(
                AgentTaskStatus.Dispatched, $"{path}: inside the grace the task is still open");
            (await RowsAsync(parent.SessionId)).ShouldBeEmpty($"{path}: inside the grace nothing is queued");
            fault.Fired.ShouldBeFalse($"{path}: the observe sweep does not enqueue");

            if (path == "busy")
            {
                await parent.MarkWorkingAsync();
                (await NoticePromptsAsync(parent.SessionId)).ShouldBeEmpty("control: the open turn has no notice");
            }

            graph.Clock.Advance(TimeSpan.FromMinutes(5));
            await graph.SweepAsync(ct);

            var failed = await ReadTaskAsync(child.TaskId);
            failed.Status.ShouldBe(AgentTaskStatus.Failed, path);
            failed.FailureReason.ShouldNotBeNull();
            failed.FailureReason.ShouldStartWith(AgentTaskLiveness.DeadSessionFailurePrefix);
            failed.FailureReason.ShouldContain(AgentSessionService.NotReadyBase);
            failed.FailureReason.ShouldNotContain("exited on its own");
            failed.FailureReason.ShouldNotContain("ProcessExit");
            stopper.Killed.ShouldBeEmpty($"{path}: the sweep does not kill");

            if (path == "enqueue-failure")
            {
                fault.Fired.ShouldBeTrue("control: the enqueue save faulted");
                failed.CompletionNoteQueuedAt.ShouldBeNull("a failed insert leaves the task unstamped");
                (await RowsAsync(parent.SessionId)).ShouldBeEmpty("a failed enqueue commits no row");
                (await NoticePromptsAsync(parent.SessionId)).ShouldBeEmpty("a failed enqueue types nothing");
                parent.Adapter.SubmittedBodies.ShouldNotContain(
                    b => b.Contains(AgentSessionService.NotReadyBase, StringComparison.Ordinal));

                fault.Mode = NoticeCut.None;
                await graph.SweepAsync(ct);
                var recovered = (await RowsAsync(parent.SessionId)).ShouldHaveSingleItem("recovery inserts the one note");
                await AssertDeliveredOnceAsync(parent, child.TaskId, recovered.Id, path);
                await graph.SweepAsync(ct);
                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                await AssertDeliveredOnceAsync(parent, child.TaskId, recovered.Id, path + " replay");
            }
            else if (path == "crash-after-enqueue")
            {
                fault.Fired.ShouldBeTrue("control: the pre-type Sent save faulted");
                fault.SawInsert.ShouldBeTrue("control: the insert committed before the cut");
                var pending = (await RowsAsync(parent.SessionId)).ShouldHaveSingleItem("the committed row survives the crash");
                pending.Status.ShouldBe(QueuedMessageStatus.Pending, path);
                pending.DeliveryAttempts.ShouldBe(0, path);
                pending.SourceTaskId.ShouldBe(child.TaskId);
                pending.ContentDigest.ShouldNotBeNullOrWhiteSpace();
                failed.CompletionNoteQueuedAt.ShouldNotBeNull("the stamp committed with the insert");
                failed.CompletionNoteDigest.ShouldBe(pending.ContentDigest);
                (await NoticePromptsAsync(parent.SessionId)).ShouldBeEmpty("the crash is before typing");
                parent.Adapter.SubmittedBodies.ShouldNotContain(
                    b => b.Contains(AgentSessionService.NotReadyBase, StringComparison.Ordinal));

                var sessionId = parent.SessionId;
                var agentId = parent.AgentId;
                var rowId = pending.Id;
                await graph.DisposeAsync();
                await parent.DisposeAsync();
                parentDisposed = true;

                restarted = await ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
                {
                    AlwaysOn = true,
                    AttachSessionId = sessionId,
                    AttachAgentId = agentId,
                });
                await using var replay = Graph.Open(restarted, stopper, child.SessionId);
                await replay.SweepAsync(ct);
                var still = (await RowsAsync(sessionId)).ShouldHaveSingleItem("recovery stamps the existing row and does not insert another");
                still.Id.ShouldBe(rowId);
                still.Status.ShouldBe(QueuedMessageStatus.Pending, "recovery of a committed row does not type it");
                (await NoticePromptsAsync(sessionId)).ShouldBeEmpty();

                (await restarted.Queue.FlushStrandedQueuesAsync(ct))
                    .ShouldBeGreaterThan(0, "the restarted queue delivers the committed row");
                await AssertDeliveredOnceAsync(restarted, child.TaskId, rowId, path);
                await replay.SweepAsync(ct);
                (await restarted.Queue.FlushStrandedQueuesAsync(ct)).ShouldBe(0, "a second flush has nothing left to type");
                await restarted.Queue.OnTurnEndAsync(sessionId, ct);
                await AssertDeliveredOnceAsync(restarted, child.TaskId, rowId, path + " replay");
            }
            else if (path == "busy")
            {
                var pending = (await RowsAsync(parent.SessionId)).ShouldHaveSingleItem("the open turn keeps the note");
                pending.Status.ShouldBe(QueuedMessageStatus.Pending, path);
                pending.DeliveryAttempts.ShouldBe(0, path);
                (await NoticePromptsAsync(parent.SessionId)).ShouldBeEmpty("busy: nothing is typed into the open turn");
                parent.Adapter.SubmittedBodies.ShouldNotContain(
                    b => b.Contains(AgentSessionService.NotReadyBase, StringComparison.Ordinal));

                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                var held = (await RowsAsync(parent.SessionId)).ShouldHaveSingleItem("becoming eligible does not drop the row");
                held.Id.ShouldBe(pending.Id);
                held.Status.ShouldBe(QueuedMessageStatus.Pending, "becoming eligible does not type on its own");
                (await NoticePromptsAsync(parent.SessionId)).ShouldBeEmpty();

                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                await AssertDeliveredOnceAsync(parent, child.TaskId, pending.Id, path);
                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                await graph.SweepAsync(ct);
                await AssertDeliveredOnceAsync(parent, child.TaskId, pending.Id, path + " replay");
            }
            else
            {
                var row = (await RowsAsync(parent.SessionId)).ShouldHaveSingleItem("an idle parent takes the note inline");
                await AssertDeliveredOnceAsync(parent, child.TaskId, row.Id, path);
                await graph.SweepAsync(ct);
                await parent.Queue.OnTurnEndAsync(parent.SessionId, ct);
                await AssertDeliveredOnceAsync(parent, child.TaskId, row.Id, path + " replay");
            }

            await AssertChildUnchangedAsync(child.SessionId);
            stopper.Killed.ShouldBeEmpty(path);
        }
        finally
        {
            // The task references the parent session. Delete it before a harness dispose deletes that session.
            await DeleteOwnedRowsAsync(
                child.TaskId, child.SessionId, child.AgentId, parent.SessionId, parent.AgentId);
            if (restarted is not null)
                await restarted.DisposeAsync();
            if (!parentDisposed)
                await parent.DisposeAsync();
            await graph.DisposeAsync();
            try
            {
                if (Directory.Exists(childCwd))
                    Directory.Delete(childCwd, recursive: true);
            }
            catch
            {
                // Best-effort. The harness deletes its own temp root.
            }
        }
    }

    private static async Task AssertDeliveredOnceAsync(
        BridgeQueueHarness recipient, Guid taskId, Guid rowId, string path)
    {
        var row = (await RowsAsync(recipient.SessionId)).ShouldHaveSingleItem(path);
        row.Id.ShouldBe(rowId, path);
        row.Status.ShouldBe(QueuedMessageStatus.Sent, path);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, path);
        row.Origin.ShouldBe(QueuedMessageOrigin.Delegation, path);
        row.SourceTaskId.ShouldBe(taskId, path);
        row.ConversationKey.ShouldBe($"task:{taskId:N}", path);
        row.Body.ShouldContain(AgentSessionService.NotReadyBase);
        row.Body.ShouldNotContain("exited on its own");
        row.Body.ShouldNotContain("ProcessExit");

        var task = await ReadTaskAsync(taskId);
        task.CompletionNoteQueuedAt.ShouldNotBeNull(path);
        task.CompletionNoteDigest.ShouldNotBeNull(path);
        task.CompletionNoteDigest.ShouldBe(DelegationNoteDigest.Compute(task.FailureReason), path);
        row.ContentDigest.ShouldBe(task.CompletionNoteDigest, path);

        var prompt = (await NoticePromptsAsync(recipient.SessionId)).ShouldHaveSingleItem(path);
        prompt.Kind.ShouldBe(TranscriptKinds.UserPrompt, path);
        PromptSubmissionMatch.IsCompleteIn(row.Body, prompt.Text).ShouldBeTrue($"{path}: complete matching UserPrompt");
        recipient.Adapter.SubmittedBodies.Count(b => PromptSubmissionMatch.IsCompleteIn(row.Body, b))
            .ShouldBe(1, $"{path}: typed once");
    }

    private static async Task AssertChildUnchangedAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldBe(AgentSessionService.NotReadyBase);
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        session.ExitCode.ShouldBe(0);
    }

    private static async Task<Seeded> SeedChildAsync(Guid parentSessionId, string cwd)
    {
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var agentName = $"rg-{agentId:N}"[..12];
        var dispatched = DateTime.UtcNow.AddMinutes(-1);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "ready-gate-child",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Failed,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
            EndedAt = dispatched,
            ExitCode = 0,
            FailureReason = AgentSessionService.NotReadyBase,
            TerminationSource = SessionTerminationSource.SystemRequest,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = cwd,
            Details = "Ready-gate failure notice delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.High,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = dispatched,
            UpdatedAt = dispatched,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Ready-gate failure notice",
            Goal = "Become ready.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            AgentId = agentId,
            AgentName = agentName,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = parentSessionId,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        });
        await db.SaveChangesAsync();
        return new Seeded(taskId, sessionId, agentId);
    }

    private static async Task<List<SessionQueuedMessage>> RowsAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
    }

    private static async Task<List<TranscriptEntry>> NoticePromptsAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .OrderBy(t => t.Sequence)
            .ToListAsync();
        return rows.Where(t => t.Text != null
            && t.Text.Contains(AgentSessionService.NotReadyBase, StringComparison.Ordinal)).ToList();
    }

    private static async Task<AgentTask> ReadTaskAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
    }

    private static async Task DeleteOwnedRowsAsync(
        Guid taskId, Guid childSessionId, Guid childAgentId, Guid parentSessionId, Guid parentAgentId)
    {
        await using var db = CreateContext();
        await db.AgentTaskLandNotifications.Where(n => n.TaskId == taskId).ExecuteDeleteAsync();
        await db.AgentTaskEvents.Where(e => e.AgentTaskId == taskId).ExecuteDeleteAsync();
        await db.SessionQueuedMessages.Where(m =>
            m.AgentSessionId == parentSessionId || m.AgentSessionId == childSessionId || m.SourceTaskId == taskId)
            .ExecuteDeleteAsync();
        await db.TranscriptEntries.Where(t => t.AgentSessionId == childSessionId || t.AgentSessionId == parentSessionId)
            .ExecuteDeleteAsync();
        await db.AgentIncidents.Where(i => i.AgentId == childAgentId || i.AgentId == parentAgentId).ExecuteDeleteAsync();
        await db.Alerts.Where(a => a.AgentId == parentAgentId || a.AgentId == childAgentId).ExecuteDeleteAsync();
        await db.AgentSupervisionStates.Where(s => s.AgentId == parentAgentId || s.AgentId == childAgentId).ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.Id == childAgentId || a.Id == parentAgentId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
        await db.AgentSessions.Where(s => s.Id == childSessionId || s.Id == parentSessionId).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.Id == childAgentId || a.Id == parentAgentId).ExecuteDeleteAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed record Seeded(Guid TaskId, Guid SessionId, Guid AgentId);

    /// <summary>
    /// The reconciler graph from <c>AgentTaskDeadSessionReconciliationTests</c>, pointed at the
    /// harness queue and runtime so the note is typed into the registered fake adapter. The queue
    /// keeps the harness system clock. This clock is only the grace window.
    /// </summary>
    private sealed class Graph : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _projectsRoot;
        private readonly string _worktreeRoot;
        private int _disposed;

        public FakeTimeProvider Clock { get; }

        private Graph(ServiceProvider provider, FakeTimeProvider clock, string projectsRoot, string worktreeRoot)
        {
            _provider = provider;
            Clock = clock;
            _projectsRoot = projectsRoot;
            _worktreeRoot = worktreeRoot;
        }

        public static Graph Open(BridgeQueueHarness parent, RecordingSessionStopper stopper, Guid goneSessionId)
        {
            var projectsRoot = Path.Combine(Path.GetTempPath(), $"antiphon-c1190-projects-{Guid.NewGuid():N}");
            var worktreeRoot = Path.Combine(Path.GetTempPath(), $"antiphon-c1190-wt-{Guid.NewGuid():N}");
            Directory.CreateDirectory(projectsRoot);
            Directory.CreateDirectory(worktreeRoot);

            var runner = new FleetRunner();
            runner.Gone.Add(goneSessionId);
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddOptions<AgentRegistrySettings>();
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton(parent.Runtime);
            services.AddSingleton(parent.Queue);
            services.AddSingleton<IDelegateSessionStopper>(stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = worktreeRoot });
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<ISessionRunnerClient>(runner);
            services.AddSingleton(new DeadSessionFirstSeenState());
            services.AddSingleton(new BootWedgeRelaunchState());
            services.AddSingleton<AgentTaskReplyService>();
            services.AddSingleton(Options.Create(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = projectsRoot,
            }));
            services.AddSingleton<DelegateBindRefusalRecovery>();
            services.AddScoped<AgentTaskDispatcher>();
            return new Graph(services.BuildServiceProvider(), clock, projectsRoot, worktreeRoot);
        }

        public async Task SweepAsync(CancellationToken ct)
        {
            await using var scope = _provider.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await dispatcher.FailDeadSessionTasksAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            await _provider.DisposeAsync();
            TryDelete(_projectsRoot);
            TryDelete(_worktreeRoot);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    /// <summary>
    /// Reports every database session as Running except the ids in <see cref="Gone"/>. An empty
    /// list would fail every other suite's open task. The parent session is never in <see cref="Gone"/>.
    /// </summary>
    private sealed class FleetRunner : ISessionRunnerClient
    {
        public HashSet<Guid> Gone { get; } = [];

        public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
        {
            await using var db = CreateContext();
            var rows = await db.AgentSessions.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
            var named = await db.AgentTasks.AsNoTracking()
                .Where(t => t.AgentSessionId != null)
                .Select(t => t.AgentSessionId!.Value)
                .Distinct()
                .ToListAsync(ct);
            return rows.Concat(named).Distinct()
                .Where(id => !Gone.Contains(id))
                .Select(id => new SessionRunnerSessionDto(
                    id, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                    Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown,
                    LastSequence: 10, HostPid: 4243))
                .ToList();
        }

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException("the dead-session sweep must not kill");

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "> ", 10));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private enum NoticeCut
    {
        None = 0,
        Insert = 1,
        SentStamp = 2,
    }

    /// <summary>
    /// Insert cuts the enqueue before the row commits. SentStamp lets that commit through and
    /// throws on the later save that marks the same session's row Sent, which is before typing.
    /// </summary>
    private sealed class NoticeFault : SaveChangesInterceptor
    {
        public const string InsertMessage = "c1190: injected enqueue save fault";
        public const string SentMessage = "c1190: injected pre-type sent stamp fault";

        public NoticeCut Mode { get; set; }
        public Guid SessionId { get; set; }
        public bool Fired { get; private set; }
        public bool SawInsert { get; private set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            Cut(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Cut(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Cut(DbContext? context)
        {
            if (Fired || Mode == NoticeCut.None || context is null)
                return;
            var mine = context.ChangeTracker.Entries<SessionQueuedMessage>()
                .Where(e => e.Entity.AgentSessionId == SessionId)
                .ToList();
            if (Mode == NoticeCut.Insert && mine.Any(e => e.State == EntityState.Added))
            {
                Fired = true;
                throw new InvalidOperationException(InsertMessage);
            }

            if (Mode != NoticeCut.SentStamp)
                return;
            if (mine.Any(e => e.State == EntityState.Added))
                SawInsert = true;
            if (SawInsert && mine.Any(e => e.State == EntityState.Modified
                && e.Property(m => m.Status).CurrentValue == QueuedMessageStatus.Sent))
            {
                Fired = true;
                throw new InvalidOperationException(SentMessage);
            }
        }
    }
}
