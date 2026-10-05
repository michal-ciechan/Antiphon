using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1074: the ordinary dispatcher retains one deliverable brief across launch interruption.</summary>
[Category("Integration")]
public class DelegationBriefRecoveryTests
{
    [Test]
    public async Task Interrupted_dispatch_backfills_the_missing_brief_on_resume()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var fixture = await BriefFixture.CreateAsync(schema.ConnectionString, dispatched: true);
        // A marker from an older attempt cannot suppress this attempt's missing brief.
        await fixture.InsertAsync(TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(fixture.Task.Id) + " stale attempt",
            fixture.Task.DispatchedAt!.Value.AddMinutes(-1));
        await fixture.InsertAsync(TranscriptKinds.TurnEnd);
        var boundary = await fixture.BoundaryAsync();

        await fixture.ResumeAsync();
        await fixture.ResumeAsync();

        await fixture.AssertDeliveredAsync(boundary);
        await using var db = fixture.Db();
        var warnings = await db.AgentTaskEvents.Where(e => e.AgentTaskId == fixture.Task.Id
            && e.Type == AgentTaskEventType.Warning).ToListAsync();
        warnings.Count.ShouldBe(2);
        warnings.Count(e => e.Detail!.Contains("brief re-queued")).ShouldBe(1);
    }

    [Test]
    public async Task Launch_enqueue_refusal_after_the_committed_claim_keeps_the_task_dispatched()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var fixture = await BriefFixture.CreateAsync(schema.ConnectionString, dispatched: false);
        await using (var scope = fixture.Harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        await using (var db = fixture.Db())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == fixture.Task.Id);
            fixture.Sink.Calls.ShouldBe(1, "the actual launch enqueue boundary must refuse");
            task.Status.ShouldBe(AgentTaskStatus.Dispatched);
            task.FailureReason.ShouldBeNull();
            task.AgentSessionId.ShouldNotBeNull();
            fixture.SessionId = task.AgentSessionId.Value;
            (await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId)).Status.ShouldBe(SessionStatus.Starting);
            var brief = (await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == task.Id).ToListAsync()).ShouldHaveSingleItem();
            brief.Status.ShouldBe(QueuedMessageStatus.Pending);
            fixture.Adapter.SubmittedBodies.ShouldBeEmpty();
            var warning = (await db.AgentTaskEvents.Where(e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("launch enqueue refused")).ToListAsync()).ShouldHaveSingleItem();
            warning.Detail.ShouldContain("Starting session awaits interrupted-launch recovery");
        }

        var boundary = await fixture.BoundaryAsync();
        await fixture.ResumeAsync();
        await fixture.ResumeAsync();
        await fixture.AssertDeliveredAsync(boundary);
        await using var verify = fixture.Db();
        (await verify.AgentTasks.CountAsync(t => t.Id == fixture.Task.Id)).ShouldBe(1);
        (await verify.AgentSessions.CountAsync(s => s.Cwd == fixture.Task.WorkingDirectory)).ShouldBe(1);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == fixture.Task.Id
            && e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("brief re-queued"))).ShouldBe(0);
    }

    [Test]
    public async Task Resume_never_duplicates_an_existing_brief()
    {
        // Normal dispatch, rules-bootstrap legacy key, and a consumed/pruned queue row.
        foreach (var existing in new[] { "execution", "rules", "transcript" })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var fixture = await BriefFixture.CreateAsync(schema.ConnectionString, dispatched: true);
            var body = AgentTaskDispatcher.FitBriefForTyping(fixture.Task, fixture.Harness.Delegation);
            var boundary = await fixture.BoundaryAsync();
            if (existing == "transcript")
            {
                await fixture.InsertAsync(TranscriptKinds.UserPrompt, body, DateTime.UtcNow);
                await fixture.InsertAsync(TranscriptKinds.TurnEnd);
            }
            else
                await fixture.Harness.Queue.EnqueueAsync(fixture.SessionId, body, MessageSendMode.WhenIdle,
                    CancellationToken.None, QueuedMessageOrigin.Delegation, deliverIfIdle: false,
                    executionDeadlineAt: fixture.Task.ExecutionDeadlineAt,
                    executionTaskId: existing == "execution" ? fixture.Task.Id : null,
                    sourceTaskId: existing == "rules" ? fixture.Task.Id : null);

            await fixture.ResumeAsync();
            await fixture.ResumeAsync();

            await using var db = fixture.Db();
            var rows = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == fixture.SessionId
                && m.Origin == QueuedMessageOrigin.Delegation).ToListAsync();
            rows.Count.ShouldBe(existing == "transcript" ? 0 : 1, existing);
            if (rows.Count == 1) rows[0].Status.ShouldBe(QueuedMessageStatus.Sent);
            (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == fixture.SessionId
                && e.Kind == TranscriptKinds.UserPrompt && e.Text == body && e.Sequence > boundary)).ShouldBe(1, existing);
            fixture.Adapter.SubmittedBodies.Count.ShouldBe(existing == "transcript" ? 0 : 1, existing);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == fixture.Task.Id
                && e.Type == AgentTaskEventType.Warning)).ShouldBe(1, existing);
        }
    }

    private sealed class BriefFixture : IAsyncDisposable
    {
        public required BridgeQueueHarness Harness { get; init; }
        public required FakeAgentProtocolAdapter Adapter { get; init; }
        public required RefusingLaunchSink Sink { get; init; }
        public required AgentTask Task { get; init; }
        public Guid SessionId { get; set; }
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Harness.ConnectionString));

        public static async Task<BriefFixture> CreateAsync(string connection, bool dispatched)
        {
            var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
            var sink = new RefusingLaunchSink();
            var settings = new DelegationSettings { MaxConcurrentTasks = 16 };
            var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = connection, AlwaysOn = false, Delegation = settings,
                ConfigureServices = services =>
                {
                    services.AddSingleton<IAgentProtocolAdapterFactory>(new AdapterFactory(adapter));
                    services.AddSingleton<IAgentTaskLaunchSink>(sink);
                    services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddDelegationWorktreeGraph();
                    services.AddScoped<AgentTaskService>();
                    services.AddScoped<AgentTaskDispatcher>();
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                        new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                        {
                            DefaultDefinition = "claude",
                            Definitions = { ["claude"] = new AgentDefinition
                                { Kind = "ClaudeCode", Exe = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh" } },
                        }));
                },
            });
            settings.AllowedRoots = [harness.TempRoot];
            settings.BriefInlineMaxBytes.ShouldBe(900);
            harness.Provider.GetRequiredService<IOptions<ChannelOutboundSettings>>().Value.UnifiedRecoveryEnabled.ShouldBeFalse();
            var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "worker")).FullName;
            var agentId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var task = new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "Retain the ordinary delegate brief",
                Goal = "Recover this exact frozen goal: " + new string('x', 1200),
                AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                Role = AgentTaskRole.Custom, Workspace = WorkspaceMode.Shared,
                ReplyTo = AgentTaskReplyTo.None, WorkingDirectory = directory, AgentId = agentId,
                Status = dispatched ? AgentTaskStatus.Dispatched : AgentTaskStatus.Queued,
                AgentSessionId = dispatched ? sessionId : null, CreatedAt = now.AddMinutes(-2),
                DispatchedAt = dispatched ? now.AddMinutes(-1) : null,
                ExecutionDeadlineAt = now.AddMinutes(10),
            };
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
            {
                var boardId = await db.Agents.Where(a => a.Id == harness.AgentId).Select(a => a.BoardId).SingleAsync();
                db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "brief worker",
                    Slug = "brief-" + agentId.ToString("N"), Kind = AgentKind.ClaudeCode,
                    WorkingDirectory = directory, AlwaysOn = false,
                    PersistentSessionId = dispatched ? sessionId.ToString("D") : null });
                if (dispatched)
                    db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "claude",
                        AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Starting, Cwd = directory,
                        CreatedAt = now.AddMinutes(-1), StartedAt = now.AddMinutes(-1), LastSeenAt = now.AddMinutes(-1) });
                db.AgentTasks.Add(task);
                await db.SaveChangesAsync();
            }
            var fixture = new BriefFixture { Harness = harness, Adapter = adapter, Sink = sink, Task = task, SessionId = sessionId };
            adapter.RegisterOnStart = harness.Runtime;
            adapter.OnSubmitted = async body =>
            {
                await fixture.InsertAsync(TranscriptKinds.UserPrompt, body, DateTime.UtcNow);
                await fixture.InsertAsync(TranscriptKinds.TurnEnd);
            };
            return fixture;
        }

        public Task InsertAsync(string kind, string? body = null, DateTime? timestamp = null) =>
            BridgeQueueHarness.InsertEntryAsync(SessionId, kind, body, timestamp: timestamp,
                stopReason: kind == TranscriptKinds.TurnEnd ? "end_turn" : null, connectionString: Harness.ConnectionString);

        public async Task<long> BoundaryAsync()
        {
            await using var db = Db();
            return await db.TranscriptEntries.Where(e => e.AgentSessionId == SessionId).MaxAsync(e => (long?)e.Sequence) ?? 0;
        }

        public async Task ResumeAsync()
        {
            await using var scope = Harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(SessionId, Task.AgentId!.Value, CancellationToken.None);
        }

        public async Task AssertDeliveredAsync(long boundary)
        {
            Adapter.Attached.ShouldBeTrue();
            await using var db = Db();
            (await db.AgentSessions.SingleAsync(s => s.Id == SessionId)).Status.ShouldBe(SessionStatus.Running);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == Task.Id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched);
            var brief = (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId
                && m.ExecutionTaskId == Task.Id && m.Origin == QueuedMessageOrigin.Delegation).ToListAsync()).ShouldHaveSingleItem();
            brief.Status.ShouldBe(QueuedMessageStatus.Sent);
            brief.ExecutionDeadlineAt.ShouldBe(task.ExecutionDeadlineAt);
            var path = Path.Combine(task.WorkingDirectory, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
            var fullBrief = await File.ReadAllTextAsync(path);
            fullBrief.ShouldContain(task.Goal);
            brief.Body.ShouldBe(DelegationReportFormatter.BuildBriefPointer(task, Harness.Delegation, path, fullBrief.Length, task.AgentKind));
            brief.Body.ShouldContain(path);
            brief.Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
            Adapter.SubmittedBodies.ShouldHaveSingleItem().ShouldBe(brief.Body);
            (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == SessionId
                && e.Kind == TranscriptKinds.UserPrompt && e.Text == brief.Body && e.Sequence > boundary)).ShouldBe(1);
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = Db())
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == Task.Id).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == Task.Id).ExecuteDeleteAsync();
                await db.AgentIncidents.Where(i => i.AgentId == Task.AgentId).ExecuteDeleteAsync();
                await db.Alerts.Where(a => a.AgentId == Task.AgentId).ExecuteDeleteAsync();
                await db.Agents.Where(a => a.Id == Task.AgentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.PersistentSessionId, (string?)null));
            }
            await Harness.DisposeAsync();
        }
    }

    private sealed class AdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RefusingLaunchSink : IAgentTaskLaunchSink
    {
        public int Calls { get; private set; }
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        {
            Calls++;
            throw new IOException("Injected launch enqueue refusal after the committed claim");
        }
    }
}
