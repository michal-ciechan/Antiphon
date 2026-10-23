using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class RefocusBriefIdentityTests
{
    [Test]
    [Arguments("pending")]
    [Arguments("completed")]
    [Arguments("post-persist-fault")]
    public async Task C1529_Producer_keeps_compact_and_admits_exactly_one_brief(string shape)
    {
        await using var f = await Fixture.CreateAsync(shape == "post-persist-fault");
        if (shape != "completed") await f.Harness.MarkWorkingAsync();
        await using (var scope = f.Harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .DeliverReuseMessagesAsync(f.Task, CancellationToken.None);

        await using var db = f.Harness.CreateObserverContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task.Id);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        task.FailureReason.ShouldBeNull();
        var rows = await f.RowsAsync();
        rows.Count.ShouldBe(2);
        var compact = rows[0];
        compact.Body.ShouldBe(f.CompactBody);
        compact.ConversationKey.ShouldBe(f.Key);
        compact.ExecutionTaskId.ShouldBe(f.Task.Id);
        compact.ExecutionDeadlineAt.ShouldBe(f.Task.ExecutionDeadlineAt);
        compact.Status.ShouldBe(shape == "completed" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending);
        f.Cut.Throws.ShouldBe(shape == "post-persist-fault" ? 1 : 0);
        rows[1].Body.ShouldContain(f.Marker);
        rows[1].Sequence.ShouldBeGreaterThan(compact.Sequence);
        if (shape != "completed")
        {
            f.Harness.Adapter.SubmittedBodies.ShouldBeEmpty();
            await f.Harness.MarkIdleAsync();
        }
        await f.Harness.Queue.FlushIfIdleAsync(f.Harness.SessionId, CancellationToken.None);
        await f.Harness.Queue.FlushIfIdleAsync(f.Harness.SessionId, CancellationToken.None);
        await f.AssertReceiptAsync();
        var again = await f.Harness.Queue.EnsureDispatchBriefAsync(f.Request, CancellationToken.None);
        again.Kind.ShouldBe(DispatchBriefKind.Received);
        again.Inserted.ShouldBeFalse();
        again.MessageId.ShouldBe(rows[1].Id, "the receipt belongs to the brief, not the compact");
        (await f.RowsAsync()).Count.ShouldBe(2);
        await f.AssertReceiptAsync();
    }

    [Test]
    [Arguments("pending")]
    [Arguments("sent")]
    [Arguments("starting")]
    public async Task C1529_Recovery_backfills_after_a_persisted_refocus(string shape)
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = f.Harness.CreateObserverContext())
        {
            var compact = f.Compact();
            if (shape != "pending")
            {
                compact.Status = QueuedMessageStatus.Sent;
                compact.SentAt = f.Task.DispatchedAt;
                compact.DeliveryAttempts = 1;
            }
            db.SessionQueuedMessages.Add(compact);
            if (shape == "starting")
                await db.AgentSessions.Where(s => s.Id == f.Harness.SessionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Starting));
            await db.SaveChangesAsync();
        }
        await using (var scope = f.Harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(f.Harness.SessionId, f.Harness.AgentId, CancellationToken.None);
        await f.Harness.Queue.FlushIfIdleAsync(f.Harness.SessionId, CancellationToken.None);
        (await f.RowsAsync()).Count.ShouldBe(2, "recovery must not count the compact as an existing brief");
        await f.AssertReceiptAsync();
    }

    [Test]
    [Arguments("missing-key")]
    [Arguments("empty-key")]
    [Arguments("unknown-key")]
    [Arguments("other-task")]
    [Arguments("other-attempt")]
    [Arguments("other-generation")]
    [Arguments("wrong-provider")]
    [Arguments("wrong-body")]
    [Arguments("marker-in-goal")]
    [Arguments("retained-payload")]
    [Arguments("foreign-owner")]
    public async Task C1529_Only_positive_current_refocus_identity_excludes_evidence(string shape)
    {
        await using var f = await Fixture.CreateAsync();
        await f.Harness.MarkWorkingAsync();
        await using (var db = f.Harness.CreateObserverContext())
        {
            var row = f.Compact();
            switch (shape)
            {
                case "missing-key": row.ConversationKey = null; break;
                case "empty-key": row.ConversationKey = ""; break;
                case "unknown-key": row.ConversationKey = "unknown-purpose"; break;
                case "other-task": row.ConversationKey = f.Key.Replace(f.Task.Id.ToString("D"), Guid.NewGuid().ToString("D")); break;
                case "other-attempt": row.ConversationKey = f.Key.Replace(":1:", ":2:"); break;
                case "other-generation": row.ConversationKey += ":stale"; break;
                case "wrong-provider":
                    await db.AgentSessions.Where(s => s.Id == f.Harness.SessionId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.AgentKind, AgentKind.Codex));
                    break;
                case "wrong-body": row.Body = "uncertain input"; break;
                case "marker-in-goal": row.Body += f.Marker; break;
                case "retained-payload": row.RemoteSpillBody = f.Marker + " possible brief"; break;
                case "foreign-owner":
                    row.ExecutionTaskId = await db.AgentTasks.Where(t => t.Id != f.Task.Id).Select(t => t.Id).FirstAsync();
                    row.SourceTaskId = f.Task.Id; // Still correlated evidence; ownership is foreign.
                    break;
            }
            db.SessionQueuedMessages.Add(row);
            await db.SaveChangesAsync();
        }
        var result = await f.Harness.Queue.EnsureDispatchBriefAsync(f.Request, CancellationToken.None);
        result.Kind.ShouldBe(DispatchBriefKind.Uncertain, shape);
        result.Inserted.ShouldBeFalse(shape);
        (await f.RowsAsync()).Count.ShouldBe(1, shape);
        await using var verify = f.Harness.CreateObserverContext();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
        task.Status.ShouldBe(AgentTaskStatus.Blocked, shape);
        task.FailureReason.ShouldBe(DispatchBriefEvidence.EvidenceUncertainReason, shape);
        f.Harness.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1529_Real_brief_blocks_a_second_brief_without_a_receipt(bool compactReceived)
    {
        await using var f = await Fixture.CreateAsync();
        await f.Harness.MarkWorkingAsync();
        await using (var db = f.Harness.CreateObserverContext())
        {
            var compact = f.Compact();
            if (compactReceived)
            {
                compact.Status = QueuedMessageStatus.Sent;
                compact.DeliveryAttempts = 1;
                compact.SentAt = DateTime.UtcNow;
            }
            db.SessionQueuedMessages.Add(compact);
            await db.SaveChangesAsync();
        }
        if (compactReceived)
            await StoreAwareTranscript.WriteAsync(f.Harness.Provider, f.Harness.SessionId, () =>
                BridgeQueueHarness.InsertEntryAsync(f.Harness.SessionId, TranscriptKinds.UserPrompt, f.CompactBody,
                    timestamp: DateTime.UtcNow, connectionString: f.Harness.ConnectionString));
        var first = await f.Harness.Queue.EnsureDispatchBriefAsync(f.Request, CancellationToken.None);
        first.Inserted.ShouldBeTrue();
        var second = await f.Harness.Queue.EnsureDispatchBriefAsync(f.Request, CancellationToken.None);
        second.Kind.ShouldBe(DispatchBriefKind.Reuse);
        second.Inserted.ShouldBeFalse();
        second.MessageId.ShouldBe(first.MessageId);
        (await f.RowsAsync()).Count.ShouldBe(2);
        f.Harness.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    [Arguments("goal-null")]
    [Arguments("goal-empty")]
    [Arguments("kind-null")]
    [Arguments("payload-empty")]
    [Arguments("value")]
    public async Task C1529_Optional_identity_inputs_fail_closed_without_throwing(string shape)
    {
        await using var f = await Fixture.CreateAsync();
        var row = f.Compact();
        if (shape == "payload-empty")
        {
            row.RemoteSpillBody = "";
            row.RemoteSpillRelativePath = "";
        }
        var snapshot = new DispatchBriefTaskSnapshot(AgentTaskStatus.Dispatched, f.Task.Attempt,
            f.Harness.SessionId, f.Task.DispatchedAt, f.Started,
            shape == "goal-null" ? null : shape == "goal-empty" ? "" : f.Task.Goal,
            shape == "kind-null" ? null : AgentKind.ClaudeCode);
        var result = DispatchBriefEvidence.Classify(f.Request, snapshot,
            [SessionMessageQueueService.ToBriefEvidence(row)], []);
        result.Kind.ShouldBe(shape is "value" or "payload-empty" ? DispatchBriefKind.Absent : DispatchBriefKind.Uncertain);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required BridgeQueueHarness Harness { get; init; }
        public required AgentTask Task { get; init; }
        public required DateTime Started { get; init; }
        public required PersistCut Cut { get; init; }
        public string Marker => DelegationReportFormatter.TaskMarker(Task.Id);
        public string CompactBody => "/compact This session is being handed NEW, unrelated work. Keep only context useful for: " + Task.Goal;
        // Independent wire fixture: do not generate the identity using the implementation under test.
        public string Key => FormattableString.Invariant($"refocus-compact:v1:{Task.Id:D}:{Task.Attempt}:{Harness.SessionId:D}:{SessionGeneration.Normalize(Task.DispatchedAt!.Value).Ticks}:{SessionGeneration.Normalize(Started).Ticks}:ClaudeCode");
        public DispatchBriefEnsureRequest Request => new(Task.Id, Task.Attempt, Harness.SessionId, Task.DispatchedAt!.Value, Started);
        public SessionQueuedMessage Compact() => new()
        {
            Id = Guid.NewGuid(), AgentSessionId = Harness.SessionId, Origin = QueuedMessageOrigin.Delegation,
            Status = QueuedMessageStatus.Pending, Sequence = 1, CreatedAt = Task.DispatchedAt!.Value,
            Body = CompactBody, ConversationKey = Key, ExecutionTaskId = Task.Id,
            ExecutionDeadlineAt = Task.ExecutionDeadlineAt,
        };
        public async Task<List<SessionQueuedMessage>> RowsAsync()
        {
            await using var db = Harness.CreateObserverContext();
            return await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == Harness.SessionId)
                .OrderBy(m => m.Sequence).ToListAsync();
        }
        public async Task AssertReceiptAsync()
        {
            var brief = (await RowsAsync()).Single(m => m.Body.Contains(Marker));
            await using var db = Harness.CreateObserverContext();
            var prompts = await db.TranscriptEntries.Where(e => e.AgentSessionId == Harness.SessionId
                && e.Kind == TranscriptKinds.UserPrompt && e.Text != null && e.Text.Contains(Marker)).ToListAsync();
            var prompt = prompts.ShouldHaveSingleItem();
            PromptSubmissionMatch.IsCompleteIn(brief.Body, prompt.Text!).ShouldBeTrue();
            prompt.Sequence.ShouldBeGreaterThan(brief.LastDeliveryBaselineSequence ?? 0);
            brief.Status.ShouldBe(QueuedMessageStatus.Sent);
        }
        public static async Task<Fixture> CreateAsync(bool cut = false)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var fault = new PersistCut { Armed = cut };
            FakeAgentProtocolAdapter? adapter = null;
            var h = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString, AlwaysOn = false,
                ConfigureDbContext = o => o.AddInterceptors(fault),
                ConfigureServices = services =>
                {
                    services.AddSingleton<IAgentProtocolAdapterFactory>(new AdapterFactory(() => adapter!));
                    services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddDelegationWorktreeGraph();
                    services.AddScoped<AgentTaskService>();
                    services.AddScoped<AgentTaskDispatcher>();
                },
            });
            h.AfterDispose = schema;
            adapter = h.Adapter;
            adapter.RegisterOnStart = h.Runtime;
            var now = SessionGeneration.Normalize(DateTime.UtcNow.AddSeconds(-1));
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "New unrelated work", Goal = "Preserve this exact refocus goal.",
                Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = h.TempRoot, AgentId = h.AgentId,
                AgentSessionId = h.SessionId, Status = AgentTaskStatus.Dispatched, Attempt = 1,
                CreatedAt = now, DispatchedAt = now, ExecutionDeadlineAt = now.AddMinutes(10), ReplyTo = AgentTaskReplyTo.None,
            };
            await using var db = h.CreateObserverContext();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.AgentKind = AgentKind.ClaudeCode;
            session.StartedAt = now.AddHours(-1);
            var prior = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask { Id = prior, RootTaskId = prior, Title = "Old work", Goal = "Old work",
                AgentSessionId = h.SessionId, AgentId = h.AgentId, Status = AgentTaskStatus.Succeeded,
                CreatedAt = now.AddMinutes(-10), DispatchedAt = now.AddMinutes(-9), CompletedAt = now.AddMinutes(-5) });
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            h.Runtime.SetTestAcceptedStartedAt(h.SessionId, session.StartedAt);
            adapter.DialogGuardEnabled = true;
            adapter.AcceptedStartedAt = session.StartedAt;
            adapter.ClaudeComposerChrome = true;
            var f = new Fixture { Harness = h, Task = task, Started = session.StartedAt, Cut = fault };
            h.Adapter.OnSubmitted = body => StoreAwareTranscript.WriteAsync(h.Provider, h.SessionId, async () =>
            {
                var now = DateTime.UtcNow;
                await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt, body,
                    timestamp: now, connectionString: h.ConnectionString);
                // Manual compaction ends without a TurnEnd; the boundary must release the following brief.
                await BridgeQueueHarness.InsertEntryAsync(h.SessionId,
                    body.StartsWith("/compact", StringComparison.Ordinal) ? TranscriptKinds.CompactBoundary : TranscriptKinds.TurnEnd,
                    body.StartsWith("/compact", StringComparison.Ordinal) ? "Context compacted (manual)" : null,
                    stopReason: "end_turn", connectionString: h.ConnectionString,
                    timestamp: body.StartsWith("/compact", StringComparison.Ordinal) ? now.AddMilliseconds(2) : null);
                if (body.StartsWith("/compact", StringComparison.Ordinal))
                {
                    await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt,
                        "This session is being continued from a previous conversation that ran out of context. The conversation is summarized below:",
                        timestamp: now.AddMilliseconds(1), connectionString: h.ConnectionString);
                    await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt,
                        "<command-name>/compact</command-name>", timestamp: now, connectionString: h.ConnectionString);
                    await BridgeQueueHarness.InsertEntryAsync(h.SessionId, TranscriptKinds.UserPrompt,
                        "<local-command-stdout>Compacted</local-command-stdout>", timestamp: now.AddMilliseconds(2),
                        connectionString: h.ConnectionString);
                }
            });
            return f;
        }
        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }

    private sealed class AdapterFactory(Func<IAgentProtocolAdapter> create) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => create();
    }

    private sealed class PersistCut : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Throws { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                    .Any(e => e.Entity.Body.StartsWith("/compact", StringComparison.Ordinal)))
            {
                Armed = false;
                Throws++;
                throw new IOException("C1529 compact persisted before probe failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
