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
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1150 S2 repair F3: the dispatcher's post-claim producer, the interrupted-launch backfill and
/// a user follow-up race through the real queue to one complete brief prompt and one follow-up.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    public async Task C1150_Real_producers_race_to_one_brief_and_keep_the_followup()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var goal = "Race the real producers.\nLine two café \u2603.\n" + new string('y', 1200);
        const string followUp = "C1150 user follow-up racing the brief; deliver me exactly once.";
        var pause = new BackfillCheckPause(schema.ConnectionString);
        var sink = new RacingLaunchSink();
        var settings = new DelegationSettings { MaxConcurrentTasks = 16 };
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = schema.ConnectionString,
            AlwaysOn = false,
            Delegation = settings,
            ConfigureDbContext = options => options.AddInterceptors(pause),
            ConfigureServices = services =>
            {
                services.AddSingleton<IAgentProtocolAdapterFactory>(new LaunchAdapterFactory(adapter));
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
                        Definitions =
                        {
                            ["claude"] = new AgentDefinition
                            {
                                Kind = "ClaudeCode",
                                Exe = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                            },
                        },
                    }));
            },
        });
        settings.AllowedRoots = [harness.TempRoot];
        var directory = Directory.CreateDirectory(Path.Combine(harness.TempRoot, "worker")).FullName;
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var boardId = await db.Agents.Where(a => a.Id == harness.AgentId).Select(a => a.BoardId).SingleAsync();
            db.Agents.Add(new Agent
            {
                Id = agentId,
                BoardId = boardId,
                Name = "race worker",
                Slug = "race-" + agentId.ToString("N")[..12],
                Kind = AgentKind.ClaudeCode,
                WorkingDirectory = directory,
                AlwaysOn = false,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "Race the brief producers",
                Goal = goal,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Role = AgentTaskRole.Custom,
                Workspace = WorkspaceMode.Shared,
                ReplyTo = AgentTaskReplyTo.None,
                WorkingDirectory = directory,
                AgentId = agentId,
                Status = AgentTaskStatus.Queued,
                CreatedAt = now.AddMinutes(-2),
                ExecutionDeadlineAt = now.AddMinutes(10),
            });
            await db.SaveChangesAsync();
        }

        pause.TaskId = taskId;
        var sessionId = Guid.Empty;
        adapter.RegisterOnStart = harness.Runtime;
        adapter.OnSubmitted = async body =>
        {
            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.UserPrompt, body,
                timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.TurnEnd,
                stopReason: "end_turn", connectionString: schema.ConnectionString);
        };
        Task? resume = null;
        Task? user = null;
        var reached = false;
        // The claim and the Starting session are committed when the sink is called. The resumed
        // launch's backfill reads "no brief" first; only then does the normal producer continue.
        sink.OnEnqueue = (session, agent) =>
        {
            sessionId = session;
            resume = Task.Run(async () =>
            {
                BackfillCheckPause.Armed.Value = true;
                await using var scope = harness.Provider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentSessionService>()
                    .ResumeInterruptedLaunchAsync(session, agent, CancellationToken.None);
            });
            Task.WhenAny(pause.Checked.Task, resume).Wait(TimeSpan.FromSeconds(60));
            reached = pause.Checked.Task.IsCompleted;
            user = Task.Run(() => harness.Queue.EnqueueAsync(
                session, followUp, MessageSendMode.WhenIdle, CancellationToken.None, QueuedMessageOrigin.Ui));
        };

        await using (var scope = harness.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        sink.Calls.ShouldBe(1, "F3");
        reached.ShouldBeTrue("F3 the backfill reached its brief checks before the normal producer");
        await resume!;
        await user!;
        pause.Released.ShouldBeTrue("F3 the backfill resumed only after the normal brief committed");

        for (var round = 0; round < 5; round++)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            if (!await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId
                    && m.Status == QueuedMessageStatus.Pending))
                break;
            await harness.Queue.FlushSessionAsync(sessionId, CancellationToken.None);
        }

        var marker = DelegationReportFormatter.TaskMarker(taskId);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, "F3");
        task.AgentSessionId.ShouldBe(sessionId, "F3");
        (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running, "F3");
        var rows = await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).ToListAsync();
        var brief = rows.Where(m => m.ExecutionTaskId == taskId).ToList().ShouldHaveSingleItem("F3 one durable brief");
        brief.Status.ShouldBe(QueuedMessageStatus.Sent, "F3");
        var spill = Path.Combine(directory, ".antiphon", $"task-{DelegationReportFormatter.Short(taskId)}-brief.md");
        var full = await File.ReadAllTextAsync(spill);
        full.Contains(goal.Trim(), StringComparison.Ordinal).ShouldBeTrue("F3");
        brief.Body.ShouldBe(DelegationReportFormatter.BuildBriefPointer(
            task, harness.Delegation, spill, full.Length, AgentKind.ClaudeCode), "F3");
        var note = rows.Where(m => m.Origin == QueuedMessageOrigin.Ui).ToList().ShouldHaveSingleItem("F3 one follow-up row");
        note.Body.ShouldBe(followUp, "F3");
        note.Status.ShouldBe(QueuedMessageStatus.Sent, "F3");
        rows.Count.ShouldBe(2, "F3");

        var prompts = await UserPromptsAsync(schema.ConnectionString, sessionId);
        var briefPrompt = prompts.Where(p => p.Contains(marker, StringComparison.Ordinal)).ToList()
            .ShouldHaveSingleItem("F3 one brief prompt");
        briefPrompt.ShouldBe(brief.Body, "F3");
        PromptSubmissionMatch.IsCompleteIn(brief.Body, briefPrompt).ShouldBeTrue("F3");
        prompts.Count(p => p == followUp).ShouldBe(1, "F3");
        prompts.Count.ShouldBe(2, "F3");
        adapter.SubmittedBodies.Count(b => b.Contains(marker, StringComparison.Ordinal)).ShouldBe(1, "F3");
        adapter.SubmittedBodies.Count(b => b == followUp).ShouldBe(1, "F3");
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.Warning && e.Detail!.Contains("brief re-queued"))).ShouldBe(0, "F3");
    }

    private sealed class RacingLaunchSink : IAgentTaskLaunchSink
    {
        public int Calls { get; private set; }
        public Action<Guid, Guid>? OnEnqueue { get; set; }

        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec)
        {
            Calls++;
            OnEnqueue?.Invoke(sessionId, agentId);
        }
    }

    /// <summary>
    /// After the resumed launch's two brief reads (queue row, then received prompt) both found
    /// nothing, holds it until the normal producer's row is committed: the interleaving where a
    /// check outside the queue lock goes stale.
    /// </summary>
    private sealed class BackfillCheckPause(string connection) : DbCommandInterceptor
    {
        public static readonly AsyncLocal<bool> Armed = new();
        private int _sawRowCheck;
        private int _fired;
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid TaskId { get; set; }
        public bool Released { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var text = command.CommandText;
            if (!Armed.Value || !text.Contains("EXISTS", StringComparison.Ordinal))
                return result;
            if (text.Contains("\"SessionQueuedMessages\"", StringComparison.Ordinal)
                && text.Contains("\"ExecutionTaskId\"", StringComparison.Ordinal)
                && text.Contains("\"SourceTaskId\"", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref _sawRowCheck, 1);
                return result;
            }

            if (Volatile.Read(ref _sawRowCheck) == 1
                && text.Contains("\"TranscriptEntries\"", StringComparison.Ordinal)
                && text.Contains("COALESCE", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                Checked.TrySetResult();
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (true)
                {
                    await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection)))
                    {
                        if (await db.SessionQueuedMessages.AnyAsync(m => m.ExecutionTaskId == TaskId, cancellationToken))
                            break;
                    }

                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException("F3 the normal producer never committed its brief");
                    await Task.Delay(25, cancellationToken);
                }

                Released = true;
            }

            return result;
        }
    }
}
