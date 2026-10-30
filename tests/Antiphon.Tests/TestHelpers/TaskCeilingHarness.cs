using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Tests.TestHelpers;

internal sealed class TaskCeilingHarness : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;
    private readonly OverdueSweepHarness.Built _built;
    public FakeTimeProvider Clock { get; }
    public Guid TaskId { get; } = Guid.NewGuid();
    public Guid SessionId { get; } = Guid.NewGuid();
    public Guid ParentId { get; } = Guid.NewGuid();
    public AgentTaskDispatcher Dispatcher => _built.Dispatcher;
    public RecordingSessionStopper Stopper => _built.Stopper;
    public ServiceProvider Services => _built.Provider;
    public string ConnectionString => _schema.ConnectionString;
    public AgentTaskReplyService Replies => Services.GetRequiredService<AgentTaskReplyService>();

    private TaskCeilingHarness(IsolatedTestSchema schema, FakeTimeProvider clock)
    {
        _schema = schema;
        Clock = clock;
        _built = OverdueSweepHarness.Create(s =>
        {
            s.DefaultTimeoutMinutes = 240;
            s.ModelWaitDeadlineMinutes = 20;
            s.LocalExecutionDeadlineMinutes = 90;
            s.BootModelWaitDeadlineMinutes = 8;
            s.RolePolicy.Clear();
        }, clock: clock, connectionString: schema.ConnectionString);
    }

    public static async Task<TaskCeilingHarness> CreateAsync(AgentTaskRole role = AgentTaskRole.Code,
        int ageMinutes = 241, string kind = TranscriptKinds.ToolCall, int lastAgeMinutes = 6)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var h = new TaskCeilingHarness(schema, new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)));
        await using var db = h.Context();
        var now = h.Clock.GetUtcNow().UtcDateTime;
        db.AgentSessions.AddRange(new AgentSession { Id = h.SessionId, StartedAt = now.AddMinutes(-ageMinutes),
            Status = SessionStatus.Running, AgentKind = AgentKind.ClaudeCode, LastSeenAt = now, Cwd = Path.GetTempPath() },
            new AgentSession { Id = h.ParentId, StartedAt = now.AddMinutes(-ageMinutes - 1),
                Status = SessionStatus.Running, AgentKind = AgentKind.ClaudeCode, LastSeenAt = now, Cwd = Path.GetTempPath() });
        db.AgentTasks.Add(new AgentTask { Id = h.TaskId, RootTaskId = h.TaskId, Role = role, Title = "Ceiling witness",
            Goal = "keep working", Status = AgentTaskStatus.Working, AgentSessionId = h.SessionId,
            ParentSessionId = h.ParentId, ReplyTo = AgentTaskReplyTo.Session,
            WorkingDirectory = Path.GetTempPath(), DispatchedAt = now.AddMinutes(-ageMinutes), CreatedAt = now.AddMinutes(-ageMinutes) });
        db.TranscriptEntries.AddRange(new TranscriptEntry { AgentSessionId = h.SessionId, Sequence = 1,
            Kind = TranscriptKinds.UserPrompt, Text = DelegationReportFormatter.TaskMarker(h.TaskId),
            Timestamp = now.AddMinutes(-ageMinutes).AddSeconds(1), CreatedAt = now.AddMinutes(-ageMinutes).AddSeconds(1) },
            new TranscriptEntry { AgentSessionId = h.SessionId, Sequence = 2, Kind = TranscriptKinds.AssistantText,
                Text = "working", Timestamp = now.AddMinutes(-lastAgeMinutes - 1), CreatedAt = now.AddMinutes(-lastAgeMinutes - 1) },
            new TranscriptEntry { AgentSessionId = h.SessionId, Sequence = 3, Kind = kind,
                Text = kind == TranscriptKinds.CompactBoundary ? "(auto)" : "work", Timestamp = now.AddMinutes(-lastAgeMinutes), CreatedAt = now.AddMinutes(-lastAgeMinutes) });
        await db.SaveChangesAsync();
        return h;
    }

    public AppDbContext Context() => new(TestDbFixture.CreateDbContextOptions(ConnectionString));
    public async Task<AgentTask> TaskAsync()
    {
        await using var db = Context();
        return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
    }
    public Task<int> SweepAsync() => Dispatcher.FailOverdueTasksAsync(CancellationToken.None);
    public async Task MutateAsync(Action<AgentTask> change)
    {
        await using var db = Context();
        change(await db.AgentTasks.SingleAsync(t => t.Id == TaskId));
        await db.SaveChangesAsync();
    }
    public async Task AppendAsync(string kind, string? text = null, string? apiCallId = null, string? stopReason = null)
    {
        await using var db = Context();
        var sequence = await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).MaxAsync(t => t.Sequence) + 1;
        var now = Clock.GetUtcNow().UtcDateTime;
        db.TranscriptEntries.Add(new TranscriptEntry { AgentSessionId = SessionId, Sequence = sequence,
            Kind = kind, Text = text, Timestamp = now, CreatedAt = now, ApiCallId = apiCallId, StopReason = stopReason });
        await db.SaveChangesAsync();
    }
    public async Task FinishAsync(string verdict = "done")
    {
        await AppendAsync(TranscriptKinds.AssistantText, "Completed the authorized work.\n--- next stage ---\nnext: review\nhandoff: inspect\nartifact: none\n"
            + DelegationReportFormatter.ReportToken(TaskId, verdict), "finish");
        await AppendAsync(TranscriptKinds.TurnEnd, apiCallId: "finish", stopReason: "end_turn");
        await Replies.OnTurnEndAsync(SessionId, CancellationToken.None);
    }
    public async Task<AgentTaskSummaryDto> DecideAsync(CeilingAction action = CeilingAction.Continue,
        int? minutes = null, bool operatorExtend = false, bool authenticated = false, string? reason = null,
        Guid? episode = null)
    {
        var task = await TaskAsync();
        return await Replies.AnswerCeilingAsync(TaskId, new ReplyToAgentTaskRequest(CeilingAction: action,
            EpisodeId: episode ?? task.CeilingHoldEpisodeId, ExtendMinutes: minutes, OperatorExtend: operatorExtend,
            CeilingReason: reason), authenticated ? new CeilingOperatorAuthorization("test-operator") : null, CancellationToken.None);
    }
    public async ValueTask DisposeAsync()
    {
        await _built.Provider.DisposeAsync();
        await _schema.DisposeAsync();
    }
}
