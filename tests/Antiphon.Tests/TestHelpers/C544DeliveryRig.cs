using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0544 V-4 delivery rig over <see cref="C544World"/>: the real reply settlement produces the
/// Completion obligation; the real <see cref="AgentTaskLandNotificationHostedService"/>,
/// <see cref="AgentTaskLandNotificationService"/> and <see cref="SessionMessageQueueService"/> carry it;
/// the caller's terminal is a <see cref="FakeAgentProtocolAdapter"/> whose <c>OnSubmitted</c> records
/// the complete UserPrompt. The rig never inserts an expected prompt and never edits task,
/// notification or queue rows to simulate progress.
/// </summary>
internal sealed class C544DeliveryRig : IAsyncDisposable
{
    public C544World World { get; private set; } = null!;
    public FakeAgentProtocolAdapter Caller { get; } = new();
    public C544Boundary Boundary { get; } = new();
    public C544DeliveryFault Fault { get; } = new();
    public OutputDistillationQueue DistillQueue { get; private set; } = new();
    public bool Busy { get; private set; }
    public Func<string, string>? SubmitTransform { get; set; }
    public const string Summary = "C544 distilled summary: the review found nothing; land after Final.";

    public static async Task<C544DeliveryRig> CreateAsync(bool busy, bool spill = false, bool distill = false,
        int replyInlineMaxChars = 20_000, bool reportStore = false)
    {
        var rig = new C544DeliveryRig { Busy = busy, _reportStore = reportStore };
        if (reportStore)
        {
            // The store refuses TEMP roots; the CARD-0419 fixture is a persistent, owned Git checkout.
            rig.Reports = new ReportWorkspace();
            await rig.Reports.InitializeAsync();
        }
        rig.World = await C544World.CreateAsync(rig.Fault, configure: rig.Configure, delegation: d =>
        {
            if (rig.Reports is not null) d.ReportStorageRoot = Path.Combine(rig.Reports.Main, ".antiphon", "reports");
            d.PtySingleChunkBytes = spill ? 400 : 43_200;
            d.ModernPtySingleWriteMaxBytes = spill ? 400 : 86_400;
            d.ReplyInlineMaxChars = replyInlineMaxChars;
            d.OutputDistillerEnabled = distill;
            d.OutputDistillerMode = OutputDistillerMode.Apply;
            d.OutputDistillerWaitSeconds = 120;
            d.ShrinkPolledCompletionNotes = true;
        });
        rig.AttachCaller();
        await rig.SeedCallerHistoryAsync();
        return rig;
    }

    private bool _reportStore;
    public ReportWorkspace? Reports { get; private set; }

    private void Configure(IServiceCollection services)
    {
        // The production canonical report store (repo-local .antiphon/reports), for report regeneration rows.
        if (_reportStore)
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IAgentReportStore, Antiphon.Server.Infrastructure.Files.AgentReportStore>();
        services.AddSingleton<LandDeliveryBoundary>(Boundary);
        services.AddSingleton(DistillQueue);
    }

    private void AttachCaller()
    {
        var connection = World.Schema.ConnectionString;
        Caller.OnSubmitted = async submitted =>
        {
            var recorded = SubmitTransform?.Invoke(submitted) ?? submitted;
            if (recorded.Length > 0)
                await BridgeQueueHarness.InsertEntryAsync(World.CallerSessionId, TranscriptKinds.UserPrompt, recorded,
                    timestamp: DateTime.UtcNow, connectionString: connection);
            await BridgeQueueHarness.InsertEntryAsync(World.CallerSessionId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: connection);
        };
        World.Services.GetRequiredService<AgentSessionRuntime>().Register(World.CallerSessionId, Caller);
    }

    private async Task SeedCallerHistoryAsync()
    {
        var connection = World.Schema.ConnectionString;
        await BridgeQueueHarness.InsertEntryAsync(World.CallerSessionId, TranscriptKinds.UserPrompt, "dispatch the review", connectionString: connection);
        await BridgeQueueHarness.InsertEntryAsync(World.CallerSessionId, TranscriptKinds.AssistantText, "Dispatched.", connectionString: connection);
        await BridgeQueueHarness.InsertEntryAsync(World.CallerSessionId, TranscriptKinds.TurnEnd,
            stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: connection);
        if (Busy)
            await BridgeQueueHarness.InsertEntryAsync(World.CallerSessionId, TranscriptKinds.AssistantText, "caller is mid-turn",
                connectionString: connection);
    }

    public SessionMessageQueueService Queue => World.Services.GetRequiredService<SessionMessageQueueService>();

    /// <summary>Recreate every provider-owned service and context; keep the database, repository and caller composer.</summary>
    public async Task RestartAsync()
    {
        DistillQueue = new OutputDistillationQueue(); // in-memory distiller admissions do not survive a restart
        await World.RestartAsync(keepInterceptor: true);
        AttachCaller();
    }

    /// <summary>A settled profile-v1 Review whose completion obligation targets the caller.</summary>
    public async Task<(Guid TaskId, string Report)> SettleReviewAsync(Antiphon.Server.Application.Dtos.CreateAgentTaskRequest? request = null,
        string scope = "Full", string next = "land", int padding = 0, AgentTask? root = null)
    {
        var caller = root is null ? null : World.Caller() with { Task = root };
        var created = await World.CreateTaskAsync(request ?? World.FinalReview(), caller);
        var sessionId = await World.DispatchAsync(created.Id);
        // Padding goes into the body; the evidence, finding and next-stage markers stay in the tail.
        var report = C544World.ReviewReport(created.Id, World.Owner.Id, World.OwnerSha, scope, found: false, next);
        if (padding > 0)
            report = report.Replace("Reviewed the owner.", "Reviewed the owner.\n\n" + string.Join("\n",
                Enumerable.Range(0, padding).Select(i => $"evidence line {i:D4}: preserved review detail")));
        await World.SeedTurnAsync(sessionId, created.Id, report);
        await World.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(sessionId, CancellationToken.None);
        return (created.Id, report);
    }

    /// <summary>A running orchestrator whose session is the caller: its children share one root and one destination.</summary>
    public async Task<AgentTask> RootOrchestratorAsync()
    {
        await using var db = World.CreateContext();
        var id = Guid.NewGuid();
        var now = World.Clock.GetUtcNow().UtcDateTime;
        var root = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "CARD-0544 root", Goal = "Orchestrate the reviews.", Kind = AgentTaskKind.Orchestrator,
            Role = AgentTaskRole.Custom, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.ReadOnly, WorkingDirectory = World.RepositoryPath, Status = AgentTaskStatus.Dispatched,
            AgentSessionId = World.CallerSessionId, ReplyTo = AgentTaskReplyTo.None, CardId = World.Card.Id, ProjectId = World.Project.Id,
            CreatedAt = now, DispatchedAt = now,
        };
        db.AgentTasks.Add(root);
        await db.SaveChangesAsync();
        return root;
    }

    public async Task SetCallerStatusAsync(SessionStatus status)
    {
        await using var db = World.CreateContext();
        await db.AgentSessions.Where(s => s.Id == World.CallerSessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status));
    }

    public async Task<SessionQueuedMessage> RowAsync(Guid taskId) => (await RowsAsync(taskId)).ShouldHaveSingleItem();

    public async Task<AgentTaskLandNotification?> NotificationAsync(Guid taskId)
    {
        await using var db = World.CreateContext();
        return await db.AgentTaskLandNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.TaskId == taskId && n.Kind == LandNotificationKind.Completion);
    }

    public async Task<List<SessionQueuedMessage>> RowsAsync(Guid taskId)
    {
        await using var db = World.CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().Where(m => m.SourceTaskId == taskId).OrderBy(m => m.Sequence).ToListAsync();
    }

    public async Task<List<TranscriptEntry>> CallerPromptsAsync()
    {
        await using var db = World.CreateContext();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == World.CallerSessionId && t.Kind == TranscriptKinds.UserPrompt)
            .OrderBy(t => t.Sequence).ToListAsync();
    }

    /// <summary>One pass of the production notification scanner (boot scan) on the current provider.</summary>
    public async Task ScanAsync()
    {
        Boundary.ResetScan();
        var worker = new AgentTaskLandNotificationHostedService(World.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandNotificationHostedService>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await worker.StartAsync(stop.Token);
        try { await Boundary.Scanned.Task.WaitAsync(TimeSpan.FromSeconds(90)); }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    /// <summary>What the flush worker does on a wakeup, and what the stranded sweep does after restart.</summary>
    public async Task FlushAsync()
    {
        await Queue.FlushIfIdleAsync(World.CallerSessionId, CancellationToken.None);
        await Queue.FlushStrandedQueuesAsync(CancellationToken.None);
    }

    /// <summary>A busy caller finishes its turn; the queue's turn-end flush runs.</summary>
    public async Task EndCallerTurnAsync()
    {
        await BridgeQueueHarness.InsertEntryAsync(World.CallerSessionId, TranscriptKinds.TurnEnd,
            stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: World.Schema.ConnectionString);
        await Queue.OnTurnEndAsync(World.CallerSessionId, CancellationToken.None);
        Busy = false;
    }

    /// <summary>Deliver as the caller allows: flush now if eligible, otherwise zero writes until TurnEnd.</summary>
    public async Task DeliverAsync(string row)
    {
        var before = Caller.SubmittedBodies.Count;
        await FlushAsync();
        if (Busy)
        {
            Caller.SubmittedBodies.Count.ShouldBe(before, row + ": a busy caller gets zero writes before TurnEnd");
            await EndCallerTurnAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await World.DisposeAsync();
        await Caller.DisposeAsync();
        if (Reports is not null) await Reports.DisposeAsync();
    }
}

/// <summary>Boundary hooks the production services already call; cuts throw or drop, observations record.</summary>
internal sealed class C544Boundary : LandDeliveryBoundary
{
    public TaskCompletionSource Scanned { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public HashSet<string> Throw { get; } = [];
    public bool DropCompletionWakeup { get; set; }
    public List<string> Reached { get; } = [];

    public void ResetScan() => Scanned = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
    {
        lock (Reached) Reached.Add(boundary);
        if (boundary == "notification-scan") Scanned.TrySetResult();
        if (Throw.Remove(boundary)) throw new IOException("c544 boundary cut: " + boundary);
        return Task.CompletedTask;
    }

    public override bool DropWakeup(string boundary, Guid identity)
    {
        if (boundary == "completion" && DropCompletionWakeup)
        {
            DropCompletionWakeup = false;
            return true;
        }
        return false;
    }
}

/// <summary>
/// Persistence cuts on the Completion path, cloned from ReceiptFailureDeliveryTests.CallerFailureFault
/// with <c>Kind == Completion</c> in the predicate. Each armed cut fires once.
/// </summary>
internal sealed class C544DeliveryFault : SaveChangesInterceptor, IDbTransactionInterceptor
{
    public string? Cut { get; set; }
    public Guid? TaskId { get; set; }
    public int Throws { get; private set; }
    private bool _afterSave;
    private bool _afterCommit;
    private bool _beforeCommit;

    private static bool Matches(Guid? taskId, Guid candidate) => taskId is null || taskId == candidate;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
        InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (Cut is null || Throws > 0) return ValueTask.FromResult(result);
        var tracker = data.Context!.ChangeTracker;
        var obligation = tracker.Entries<AgentTaskLandNotification>().Any(e => e.State == EntityState.Added
            && e.Entity.Kind == LandNotificationKind.Completion && Matches(TaskId, e.Entity.TaskId));
        if (obligation && Cut == "obligation-insert") Fire();
        if (obligation && Cut == "settled-committed") _afterSave = true;
        var keyedInsert = tracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added
            && e.Entity.SourceLandNotificationId != null && e.Entity.SourceTaskId is Guid t && Matches(TaskId, t));
        if (keyedInsert && Cut == "note-insert") Fire();
        if (keyedInsert && Cut == "note-committed") _afterSave = true;
        var claim = tracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Modified
            && e.Entity.SourceLandNotificationId != null && e.Entity.Status == QueuedMessageStatus.Sent
            && e.Entity.DeliveryVerdict is null && e.Property(m => m.DeliveryAttempts).IsModified
            && e.Entity.SourceTaskId is Guid c && Matches(TaskId, c));
        if (claim && Cut == "render-committed") _afterCommit = true;
        if (claim && Cut == "spill-written") _beforeCommit = true;
        var verdict = tracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Modified
            && e.Entity.SourceLandNotificationId != null && e.Entity.DeliveryVerdict == DeliveryVerdict.Delivered
            && e.Entity.SourceTaskId is Guid v && Matches(TaskId, v));
        if (verdict && Cut == "attempt-committed") Fire();
        var receipt = tracker.Entries<AgentTaskLandNotification>().Any(e => e.State == EntityState.Modified
            && e.Entity.Kind == LandNotificationKind.Completion && e.Entity.State == LandNotificationState.Confirmed
            && Matches(TaskId, e.Entity.TaskId));
        if (receipt && Cut == "prompt-accepted") Fire();
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
    {
        if (_afterSave && data.Context!.Database.CurrentTransaction is null)
        {
            _afterSave = false;
            Fire();
        }
        return ValueTask.FromResult(result);
    }

    public ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (_beforeCommit)
        {
            _beforeCommit = false;
            Fire();
        }
        return ValueTask.FromResult(result);
    }

    public Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_afterCommit || _afterSave)
        {
            _afterCommit = false;
            _afterSave = false;
            Fire();
        }
        return Task.CompletedTask;
    }

    private void Fire()
    {
        Throws++;
        var cut = Cut;
        Cut = null;
        throw new IOException("c544 completion persistence cut: " + cut);
    }
}
