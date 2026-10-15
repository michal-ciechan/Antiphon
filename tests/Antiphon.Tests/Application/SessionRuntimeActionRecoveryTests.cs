using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class SessionRuntimeActionRecoveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1259_admitted_task_attempt_cannot_settle_its_replacement(bool catchup)
        => await VerifyTaskAttemptReplacementAsync(catchup);

    internal static async Task VerifyTaskAttemptReplacementAsync(bool catchup)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s =>
            {
                s.AddSingleton<AgentTaskReplyService>();
                s.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.GetTempPath() });
            }
        });
        var taskId = Guid.NewGuid();
        await using (var db = h.CreateObserverContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, AgentSessionId = h.SessionId,
                Status = AgentTaskStatus.Working, Attempt = 1, Title = "attempt fence", Role = AgentTaskRole.Docs,
                AgentId = h.AgentId, Workspace = WorkspaceMode.Shared, WorkingDirectory = h.TempRoot,
                CreatedAt = h.Now.AddMinutes(-2), DispatchedAt = h.Now.AddMinutes(-1),
                CommitOnSettle = CommitOnSettlePolicy.Never
            });
            await db.SaveChangesAsync();
        }
        var prompt = DelegationReportFormatter.TaskMarker(taskId);
        var report = $"Original attempt needs a decision.\n[antiphon-report:{DelegationReportFormatter.Short(taskId)} blocked]";
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", prompt), Entry(h, 2, "AssistantText", report), Entry(h, 3, "TurnEnd", null)]);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        if (catchup)
        {
            await using var db = h.CreateObserverContext();
            await db.SessionRuntimeActions.ExecuteDeleteAsync();
            await actions.RequestAsync(h.SessionId, "catchup", default);
        }
        await using (var db = h.CreateObserverContext())
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Attempt, 2));
        await actions.DrainPageAsync(h.SessionId, default);
        await using var verify = h.CreateObserverContext();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Attempt.ShouldBe(2);
        task.Status.ShouldBe(AgentTaskStatus.Working, "a persisted action belongs to its admitted task attempt");
        task.Result.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId)).ShouldBe(0);
        (await verify.SessionRuntimeActions.CountAsync()).ShouldBe(0, "stale settlement is superseded without retrying another attempt");
    }

    [Test]
    [Arguments("restart")]
    [Arguments("failed-tail")]
    public async Task C1259_direct_tail_recovers_its_original_turn_and_recipient(string variant)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var producer = new RecordingProducer();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            Outbound = new ChannelOutboundSettings { UnifiedRecoveryEnabled = false },
            ConfigureServices = s => s.AddSingleton<IAntiphonMessagingProducer>(producer)
        });
        const string destination = "original-direct-recipient";
        const string prompt = "[Telegram Family] Original question";
        await h.SeedChannelCorrelationAsync(prompt, "telegram:" + destination);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 1, "UserPrompt", prompt), Entry(h, 2, "AssistantText", "first answer"), Entry(h, 3, "TurnEnd", null)]);
        await h.Dispatcher.OnTranscriptActionAsync(h.SessionId, 3, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first answer"]);
        await h.Runtime.PersistTranscriptAsync(h.SessionId,
            [Entry(h, 4, "AssistantText", "late answer"), Entry(h, 5, "UserPrompt", "new unrelated turn"),
                Entry(h, 6, "AssistantText", "must not escape"), Entry(h, 7, "TurnEnd", null)]);
        var dispatcher = variant == "restart" ? new ChannelReplyDispatcher(
            h.Provider.GetRequiredService<IServiceScopeFactory>(), producer,
            h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), h.Clock,
            NullLogger<ChannelReplyDispatcher>.Instance) : h.Dispatcher;
        if (variant == "failed-tail")
        {
            producer.FailNext = true;
            try { await dispatcher.OnTranscriptActionAsync(h.SessionId, 4, default); }
            catch (IOException) { }
            producer.Failures.ShouldBe(1, "the real trailing producer must be reached");
        }
        await dispatcher.OnTranscriptActionAsync(h.SessionId, 4, default);
        await dispatcher.OnTranscriptActionAsync(h.SessionId, 4, default);
        producer.Sent.Select(r => r.Text).ShouldBe(["first answer", "late answer"]);
        producer.Sent.ShouldAllBe(r => r.ConversationId == destination);
    }

    [Test]
    public async Task C1259_queue_prune_holds_the_transcript_mutation_lease()
    {
        var gate = new QueueDeleteGate();
        await using var f = await SessionStateTestFixture.CreateAsync(gate);
        await using var db = f.Db();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = f.SessionId,
            Body = "old settled source", Sequence = 1, Status = QueuedMessageStatus.Sent,
            CreatedAt = DateTime.UtcNow.AddDays(-100) });
        await db.SaveChangesAsync();
        await f.Store.ReadAsync(f.SessionId, default);
        var audit = Options.Create(new AuditSettings());
        var retention = new DataRetentionService(db, Options.Create(new RetentionSettings()), audit,
            TimeProvider.System, NullLogger<DataRetentionService>.Instance, new AuditService(db, audit), f.Store);
        var prune = retention.PruneQueuedMessagesAsync(default);
        Task<SessionStateStore.Lease>? writer = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The cached store makes an uncontended lease acquisition synchronous. This
            // observes the actual gate, without inferring ownership from database latency.
            writer = f.Store.BeginWriteAsync(f.SessionId, default);
            writer.IsCompleted.ShouldBeFalse("queue deletion must hold the same lease as action admission");
        }
        finally
        {
            gate.Release.TrySetResult(); await prune;
            if (writer is not null) (await writer).Dispose();
        }
        await f.Runtime.ObserveTranscriptAsync(f.Event(1, "ToolResult", text: "new durable debt"), default);
        await using var verify = f.Db();
        (await verify.TranscriptEntries.SingleAsync()).Text.ShouldBe("new durable debt");
        (await verify.SessionRuntimeActions.SingleAsync()).Sequence.ShouldBe(1);
    }

    [Test]
    public async Task C1259_worker_diagnostics_identify_debt_without_transcript_or_exception_text()
    {
        var log = new RecordingLogger();
        await using var f = await SessionStateTestFixture.CreateAsync(configure: s =>
            s.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(log));
        await f.Runtime.ObserveTranscriptAsync(f.Event(1, "ToolResult", text: "private-transcript-body"), default);
        await f.Services.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(f.SessionId, default);
        log.Values.ShouldContain(v => v.ContainsKey("PendingCount") && Equals(v["PendingCount"], 1));
        log.Values.ShouldContain(v => v.ContainsKey("Frontier") && Equals(v["Frontier"], 1L));
        log.Values.ShouldContain(v => v.ContainsKey("AgeSeconds") && v.ContainsKey("Phase") && v.ContainsKey("ActionId"));
        log.Messages.ShouldContain(m => m.Contains("start"));
        log.Messages.ShouldContain(m => m.Contains("end"));
        log.Messages.ShouldAllBe(m => !m.Contains("private-transcript-body"));
        var failure = new PrivateFailureBus();
        await using var failing = await SessionStateTestFixture.CreateAsync(configure: s =>
        {
            s.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(log);
            s.AddSingleton<IEventBus>(failure);
        });
        await failing.Runtime.ObserveTranscriptAsync(failing.Event(1, "ToolResult", text: "second-private-body"), default);
        await failing.Services.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(failing.SessionId, default);
        failure.Hits.ShouldBe(1);
        await using var verify = failing.Db();
        var retained = await verify.SessionRuntimeActions.SingleAsync();
        retained.Phase.ShouldBe(0);
        retained.Attempts.ShouldBe(1);
        retained.FailureReason.ShouldBe(nameof(IOException));
        log.Values.ShouldContain(v => v.ContainsKey("FailureType") && Equals(v["FailureType"], nameof(IOException)));
        log.Messages.ShouldAllBe(m => !m.Contains("second-private-body") && !m.Contains("private-exception-detail"));
    }

    private static SessionRunnerTranscriptEvent Entry(BridgeQueueHarness h, long seq, string kind, string? text) =>
        new(h.SessionId, seq, kind, Guid.NewGuid().ToString(), null, h.Clock.GetUtcNow(),
            kind == "UserPrompt" ? "user" : "assistant", text, null, null, null, null,
            kind == "TurnEnd" ? "end_turn" : null);

    private sealed class QueueDeleteGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("DELETE FROM \"SessionQueuedMessages\"", StringComparison.Ordinal))
            { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
    }

    private sealed class RecordingProducer : IAntiphonMessagingProducer
    {
        public List<ChannelReply> Sent { get; } = [];
        public bool FailNext;
        public int Failures;
        public Task SendAsync(ChannelReply reply, CancellationToken ct = default)
        {
            if (FailNext) { FailNext = false; Failures++; throw new IOException("pre-accept failure"); }
            Sent.Add(reply); return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<SessionRuntimeActionCoordinator>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ConcurrentQueue<Dictionary<string, object?>> Values { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> format)
        {
            Messages.Enqueue(format(state, ex));
            if (state is IEnumerable<KeyValuePair<string, object?>> values) Values.Enqueue(values.ToDictionary());
        }
    }

    private sealed class PrivateFailureBus : IEventBus
    {
        public int Hits;
        public Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default)
        { Hits++; throw new IOException("private-exception-detail"); }
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }
}
