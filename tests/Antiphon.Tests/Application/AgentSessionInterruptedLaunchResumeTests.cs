using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0340 S1: an interrupted Starting launch is resumed on the existing runner session for
/// delegate work, and failed loudly (kill + reason) when extras are not durable.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentSessionInterruptedLaunchResume")]
public class AgentSessionInterruptedLaunchResumeTests
{
    [Test]
    public async Task Delegate_Starting_row_attaches_becomes_Running_and_flushes_the_pending_brief()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = true };
        var runner = new RecordingKillRunner();
        await using var fixture = await ResumeFixture.CreateAsync(adapter, runner, dispatchedTask: true);
        adapter.RegisterOnStart = fixture.Runtime;
        adapter.OnSubmitted = async submitted =>
        {
            await fixture.InsertTranscriptAsync(TranscriptKinds.UserPrompt, submitted);
            await fixture.InsertTranscriptAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        };
        var queued = await fixture.SeedPendingBriefAsync();

        await fixture.ResumeAsync();

        adapter.Attached.ShouldBeTrue();
        adapter.Started.ShouldBeTrue("attach, not StartAsync, is what resumed the process");
        await using var db = ResumeFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        session.LaunchResumedAt.ShouldNotBeNull();
        (await db.SessionQueuedMessages.SingleAsync(m => m.Id == queued))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == fixture.TaskId && e.Type == AgentTaskEventType.Warning))
            .ShouldBe(1);
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.LaunchInterruptedByRestart);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        runner.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task Ready_false_fails_kills_the_adapter_and_the_agent()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var fixture = await ResumeFixture.CreateAsync(adapter, dispatchedTask: true);

        await Should.ThrowAsync<InvalidOperationException>(fixture.ResumeAsync());

        adapter.Attached.ShouldBeTrue();
        adapter.Lifecycle.ShouldBe(["Kill", "Dispose"]);
        await using var db = ResumeFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldContain("Resumed launch after a server restart failed");
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        var agent = await db.Agents.SingleAsync(a => a.Id == fixture.AgentId);
        agent.Status.ShouldBe(AgentStatus.Failed);
        (await db.AgentIncidents.SingleAsync(
            i => i.AgentId == fixture.AgentId && i.Kind == AgentIncidentKind.LaunchInterruptedByRestart))
            .Severity.ShouldBe(AlertSeverity.Error);
    }

    [Test]
    public async Task Sign_in_block_persists_LaunchBlock()
    {
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-grok-home-{Guid.NewGuid():N}");
        var reason = GrokSignInPromptDetector.BlockReason(grokHome);
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyResult = false,
            LaunchBlock = new AgentLaunchBlock(
                AgentLaunchBlockKind.ProviderSignInRequired, reason, grokHome),
        };
        await using var fixture = await ResumeFixture.CreateAsync(adapter, dispatchedTask: true);

        var ex = await Should.ThrowAsync<AgentLaunchBlockedException>(fixture.ResumeAsync());
        ex.Block.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);

        await using var db = ResumeFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.LaunchBlock.ShouldBe(SessionLaunchBlock.ProviderSignInRequired);
        session.FailureReason.ShouldContain("Resumed launch after a server restart failed");
    }

    [Test]
    public async Task Non_delegate_Starting_row_does_not_attach_and_kills_through_the_runner()
    {
        var adapter = new FakeAgentProtocolAdapter();
        var runner = new RecordingKillRunner();
        await using var fixture = await ResumeFixture.CreateAsync(adapter, runner, dispatchedTask: false);

        await fixture.ResumeAsync();

        adapter.Attached.ShouldBeFalse();
        runner.Killed.ShouldContain(fixture.SessionId);
        await using var db = ResumeFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldContain("launch notes, remote-control name and initial prompt are not durable");
        session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        var agent = await db.Agents.SingleAsync(a => a.Id == fixture.AgentId);
        agent.Status.ShouldBe(AgentStatus.Failed);
    }

    [Test]
    public async Task Already_Running_row_is_a_noop()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await ResumeFixture.CreateAsync(adapter, dispatchedTask: true);
        await using (var db = ResumeFixture.CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
            session.Status = SessionStatus.Running;
            await db.SaveChangesAsync();
        }

        await fixture.ResumeAsync();

        adapter.Attached.ShouldBeFalse();
        await using var verify = ResumeFixture.CreateContext();
        var row = await verify.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        row.Status.ShouldBe(SessionStatus.Running);
        row.LaunchResumedAt.ShouldBeNull();
        (await verify.AgentIncidents.CountAsync(
            i => i.SessionId == fixture.SessionId && i.Kind == AgentIncidentKind.LaunchInterruptedByRestart))
            .ShouldBe(0);
    }

    [Test]
    public async Task Adapter_without_attach_takes_the_not_resumable_arm()
    {
        var adapter = new NonAttachableAdapter();
        var runner = new RecordingKillRunner();
        await using var fixture = await ResumeFixture.CreateAsync(adapter, runner, dispatchedTask: true);

        await fixture.ResumeAsync();

        runner.Killed.ShouldContain(fixture.SessionId);
        await using var db = ResumeFixture.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == fixture.SessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        session.FailureReason.ShouldContain("not durable");
    }

    private sealed class ResumeFixture : IAsyncDisposable
    {
        public required BridgeQueueHarness Harness { private get; init; }
        public required IServiceScope LaunchScope { private get; init; }
        public required Guid SessionId { get; init; }
        public required Guid AgentId { get; init; }
        public required Guid? TaskId { get; init; }
        public AgentSessionRuntime Runtime => Harness.Runtime;
        public IServiceProvider Services => LaunchScope.ServiceProvider;

        public static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

        public static Task<ResumeFixture> CreateAsync(
            IAgentProtocolAdapter adapter,
            bool dispatchedTask) =>
            CreateAsync(adapter, runner: null, dispatchedTask);

        public static async Task<ResumeFixture> CreateAsync(
            IAgentProtocolAdapter adapter,
            RecordingKillRunner? runner,
            bool dispatchedTask)
        {
            var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                ConfigureServices = s =>
                {
                    s.AddSingleton<IAgentProtocolAdapterFactory>(new OneAdapterFactory(adapter));
                    if (runner is not null)
                        s.AddSingleton<ISessionRunnerClient>(runner);
                },
            });

            var workspace = Path.Combine(harness.TempRoot, "resume-workspace");
            Directory.CreateDirectory(workspace);
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            Guid? taskId = null;
            await using (var db = CreateContext())
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    CardId = null,
                    DefinitionName = "fake",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Starting,
                    Cwd = workspace,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now.AddMinutes(-2),
                    StartedAt = now.AddMinutes(-2),
                    LastSeenAt = now.AddMinutes(-2),
                });
                await db.SaveChangesAsync();
                await db.Agents.Where(a => a.Id == harness.AgentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Running)
                    .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));

                if (dispatchedTask)
                {
                    taskId = Guid.NewGuid();
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = taskId.Value,
                        RootTaskId = taskId.Value,
                        Title = "interrupted launch resume",
                        Goal = "Do the thing.",
                        Role = AgentTaskRole.Plan,
                        AgentKind = AgentKind.ClaudeCode,
                        ModelLevel = AgentModelLevel.Frontier,
                        Workspace = WorkspaceMode.Shared,
                        WorkingDirectory = workspace,
                        AgentSessionId = sessionId,
                        AgentId = harness.AgentId,
                        Status = AgentTaskStatus.Dispatched,
                        CreatedAt = now.AddMinutes(-2),
                        DispatchedAt = now.AddMinutes(-2),
                    });
                    await db.SaveChangesAsync();
                }
            }

            if (runner is not null)
                runner.SessionId = sessionId;

            return new ResumeFixture
            {
                Harness = harness,
                LaunchScope = harness.Provider.CreateScope(),
                SessionId = sessionId,
                AgentId = harness.AgentId,
                TaskId = taskId,
            };
        }

        public Task ResumeAsync() =>
            Services.GetRequiredService<AgentSessionService>()
                .ResumeInterruptedLaunchAsync(SessionId, AgentId, CancellationToken.None);

        public async Task<Guid> SeedPendingBriefAsync()
        {
            var marker = TaskId is Guid id
                ? DelegationReportFormatter.TaskMarker(id) + "\n\nDo the thing."
                : "queued while Starting";
            return await Harness.SeedPendingMessageAsync(marker, SessionId);
        }

        public Task InsertTranscriptAsync(string kind, string? text = null, string? stopReason = null) =>
            Harness.InsertTranscriptEntryAsync(kind, text, stopReason, SessionId);

        public async ValueTask DisposeAsync()
        {
            await using (var db = CreateContext())
            {
                var taskIds = await db.AgentTasks
                    .Where(t => t.AgentSessionId == SessionId)
                    .Select(t => t.Id)
                    .ToListAsync();
                if (taskIds.Count > 0)
                    await db.AgentTaskEvents.Where(e => taskIds.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentIncidents.Where(i => i.SessionId == SessionId).ExecuteDeleteAsync();
                await db.Alerts.Where(a => a.SessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            }

            LaunchScope.Dispose();
            await Harness.DisposeAsync();
        }
    }

    private sealed class OneAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RecordingKillRunner : ISessionRunnerClient
    {
        public Guid SessionId { get; set; }
        public List<Guid> Killed { get; } = [];

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([Running()]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(Running() with { SessionId = sessionId });

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Killed.Add(sessionId);
            return Task.FromResult(Running() with { SessionId = sessionId, Status = "Exited", ExitCode = 0 });
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }

        private SessionRunnerSessionDto Running() =>
            new(SessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddMinutes(-2),
                Status: "Running", ExitCode: null, ExitReason: AgentExitReason.Unknown, LastSequence: 1);
    }

    private sealed class NonAttachableAdapter : IAgentProtocolAdapter
    {
        public Task<int> Exited => Task.FromResult(0);
        public int? Pid => 1;
        public AgentExitReason ExitReason => AgentExitReason.Unknown;
        public string? AuditDirectory => null;
        public event Action<string>? OnTextDelta { add { } remove { } }
        public Task StartAsync(AgentLaunchSpec spec, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) => Task.FromResult(true);
        public Task SendPromptAsync(string prompt, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(true);
        public Task SendInputAsync(string input, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> WaitForReadyAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct) =>
            Task.FromResult(new AgentTurnResult(true, null, false, ""));
        public string SnapshotRawOutput() => "";
        public Task<string> SnapshotRawOutputAsync(CancellationToken ct) => Task.FromResult("");
        public string SnapshotRenderedScreen() => "";
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
