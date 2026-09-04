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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0288 S1 — restart catch-up must settle an open task whose report-boundary TurnEnd only
/// ever arrived via backfill. Settlement is its own try/catch and is not gated on
/// <c>AddedTurnBoundary</c>.
/// </summary>
[Category("Integration")]
public class AgentTaskCatchUpSettlementTests
{
    [Test]
    public async Task catch_up_of_a_report_boundary_settles_a_dispatched_task_as_marked()
    {
        await using var scenario = new Scenario();
        var seeded = await scenario.SeedDispatchedAsync();
        var runtime = scenario.RuntimeFor(MarkedReportSnapshot(seeded));

        await runtime.SyncTranscriptAsync(seeded.SessionId, CancellationToken.None);

        var settled = await scenario.ReadTaskAsync(seeded.TaskId);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
    }

    [Test]
    public async Task catch_up_settles_even_when_channel_reply_dispatch_throws()
    {
        await using var scenario = new Scenario();
        var seeded = await scenario.SeedDispatchedAsync();
        var runtime = scenario.RuntimeFor(MarkedReportSnapshot(seeded), throwOnChannelReply: true);

        await runtime.SyncTranscriptAsync(seeded.SessionId, CancellationToken.None);

        var settled = await scenario.ReadTaskAsync(seeded.TaskId);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
    }

    [Test]
    public async Task catch_up_settles_when_the_turn_end_was_already_persisted()
    {
        await using var scenario = new Scenario();
        var seeded = await scenario.SeedDispatchedAsync();
        var snapshot = MarkedReportSnapshot(seeded);
        await scenario.PersistSnapshotAsync(snapshot);
        (await scenario.ReadTaskAsync(seeded.TaskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);

        var runtime = scenario.RuntimeFor(snapshot);
        await runtime.SyncTranscriptAsync(seeded.SessionId, CancellationToken.None);

        var settled = await scenario.ReadTaskAsync(seeded.TaskId);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
    }

    [Test]
    public async Task catch_up_on_a_session_with_no_open_task_does_not_settle_anything()
    {
        await using var scenario = new Scenario();
        var seeded = await scenario.SeedDispatchedAsync();
        await scenario.MarkSucceededAsync(seeded.TaskId);
        var logs = new List<string>();
        var runtime = scenario.RuntimeFor(MarkedReportSnapshot(seeded), logs: logs);

        await runtime.SyncTranscriptAsync(seeded.SessionId, CancellationToken.None);

        logs.ShouldNotContain(l => l.Contains("Catch-up settlement", StringComparison.Ordinal));
        (await scenario.ReadTaskAsync(seeded.TaskId)).Status.ShouldBe(AgentTaskStatus.Succeeded);
    }

    [Test]
    public async Task a_mid_turn_catch_up_without_a_marked_report_does_not_settle()
    {
        await using var scenario = new Scenario();
        var seeded = await scenario.SeedDispatchedAsync();
        var runtime = scenario.RuntimeFor(MidTurnSnapshot(seeded));

        await runtime.SyncTranscriptAsync(seeded.SessionId, CancellationToken.None);

        (await scenario.ReadTaskAsync(seeded.TaskId)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    private static SessionRunnerTranscriptDto MarkedReportSnapshot(Seeded seeded)
    {
        var apiCallId = $"msg_{seeded.TaskId:N}";
        var body = "The work is done.\n" + DelegationReportFormatter.ReportToken(seeded.TaskId, "done");
        return new SessionRunnerTranscriptDto(seeded.SessionId,
        [
            Ev(seeded.SessionId, 1, TranscriptKinds.UserPrompt, seeded.PromptUuid,
                DelegationReportFormatter.TaskMarker(seeded.TaskId) + "\n\nDo the thing.",
                role: "user"),
            Ev(seeded.SessionId, 2, TranscriptKinds.AssistantText, seeded.TextUuid, body,
                role: "assistant", apiCallId: apiCallId),
            Ev(seeded.SessionId, 3, TranscriptKinds.TurnEnd, seeded.EndUuid, null,
                role: "assistant", stopReason: TranscriptKinds.StopReasons.EndTurn, apiCallId: apiCallId),
        ], 3);
    }

    private static SessionRunnerTranscriptDto MidTurnSnapshot(Seeded seeded) =>
        new(seeded.SessionId,
        [
            Ev(seeded.SessionId, 1, TranscriptKinds.UserPrompt, seeded.PromptUuid,
                DelegationReportFormatter.TaskMarker(seeded.TaskId) + "\n\nDo the thing.",
                role: "user"),
            Ev(seeded.SessionId, 2, TranscriptKinds.ToolCall, seeded.TextUuid, null,
                role: "assistant", toolName: "Read"),
        ], 2);

    private static SessionRunnerTranscriptEvent Ev(
        Guid sessionId, long sequence, string kind, string uuid, string? text,
        string? role = null, string? stopReason = null, string? apiCallId = null, string? toolName = null) =>
        new(sessionId, sequence, kind, uuid, null, DateTimeOffset.UtcNow, role, text,
            toolName, null, null, null, stopReason, apiCallId);

    private sealed record Seeded(Guid TaskId, Guid SessionId, string PromptUuid, string TextUuid, string EndUuid);

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly List<Guid> _tasks = [];
        private readonly List<Guid> _sessions = [];

        public async Task<Seeded> SeedDispatchedAsync()
        {
            var sessionId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var dispatched = DateTime.UtcNow.AddMinutes(-5);
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "catchup-test",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = dispatched,
                StartedAt = dispatched,
                LastSeenAt = dispatched,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "Catch-up settlement test",
                Goal = "Do the thing.",
                Role = AgentTaskRole.Plan,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            await db.SaveChangesAsync();
            _sessions.Add(sessionId);
            _tasks.Add(taskId);
            return new Seeded(
                taskId, sessionId,
                $"cu-prompt-{taskId:N}",
                $"cu-text-{taskId:N}",
                $"cu-end-{taskId:N}");
        }

        public async Task PersistSnapshotAsync(SessionRunnerTranscriptDto snapshot)
        {
            await using var db = CreateContext();
            var now = DateTime.UtcNow;
            foreach (var e in snapshot.Entries)
            {
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = snapshot.SessionId,
                    Sequence = e.Sequence,
                    Kind = e.Kind,
                    Uuid = e.Uuid,
                    Role = e.Role,
                    Text = e.Text,
                    ToolName = e.ToolName,
                    StopReason = e.StopReason,
                    ApiCallId = e.ApiCallId,
                    Timestamp = e.Timestamp?.UtcDateTime,
                    CreatedAt = now,
                });
            }
            await db.SaveChangesAsync();
        }

        public async Task MarkSucceededAsync(Guid taskId)
        {
            await using var db = CreateContext();
            await db.AgentTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, AgentTaskStatus.Succeeded)
                    .SetProperty(t => t.CompletedAt, DateTime.UtcNow));
        }

        public async Task<AgentTask> ReadTaskAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public AgentSessionRuntime RuntimeFor(
            SessionRunnerTranscriptDto snapshot, bool throwOnChannelReply = false, List<string>? logs = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitWorkspaceService();
            if (logs is not null)
                services.AddSingleton<ILogger<AgentSessionRuntime>>(new ListLogger<AgentSessionRuntime>(logs));
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<AgentTaskReplyService>();
            if (throwOnChannelReply)
            {
                services.AddSingleton<ChannelReplyDispatcher>(_ =>
                    throw new InvalidOperationException("channel reply boom"));
            }

            var provider = services.BuildServiceProvider();
            return new AgentSessionRuntime(
                new SnapshotRunnerClient(snapshot),
                provider.GetRequiredService<IEventBus>(),
                Options.Create(new AgentSessionSettings()),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                logs is null
                    ? NullLogger<AgentSessionRuntime>.Instance
                    : provider.GetRequiredService<ILogger<AgentSessionRuntime>>());
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => _sessions.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages.Where(m => _sessions.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => _sessions.Contains(s.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed class SnapshotRunnerClient(SessionRunnerTranscriptDto transcript) : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, string.Empty, 0));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(transcript.SessionId == sessionId
                ? transcript
                : new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add(formatter(state, exception));
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
