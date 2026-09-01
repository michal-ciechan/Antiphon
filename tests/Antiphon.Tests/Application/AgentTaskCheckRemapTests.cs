using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0302 S3: remap Role=Check + Blocked + non-empty Result to Succeeded. Isolated schema
/// because the sweep is fleet-global and must not rewrite another suite's Check rows.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskCheckRemapTests
{
    [Test]
    public async Task a_blocked_check_with_a_reading_is_remapped_to_succeeded_exempt()
    {
        await using var h = await Harness.CreateAsync();
        var seeded = await h.SeedBlockedCheckAsync("LOOKS STUCK — session idle 28m.");

        (await AgentTaskCheckService.RemapBlockedInterpretationsAsync(
            h.Db, h.Clock, CancellationToken.None)).ShouldBe(1);

        var row = await h.ReloadAsync(seeded.TaskId);
        row.Status.ShouldBe(AgentTaskStatus.Succeeded);
        row.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Exempt);
        row.Result.ShouldBe("LOOKS STUCK — session idle 28m.");
        row.CompletedAt.ShouldBe(seeded.CompletedAt);
        row.AgentSessionId.ShouldBe(seeded.SessionId);

        var events = await h.EventsAsync(seeded.TaskId);
        events.Count(e => e.Type == AgentTaskEventType.Completed && e.Detail.Contains("CARD-0302"))
            .ShouldBe(1);

        (await h.ReloadSessionAsync(seeded.SessionId)).Status.ShouldBe(SessionStatus.Running);
        (await h.ReloadAgentAsync(seeded.AgentId)).ShouldNotBeNull();

        (await AgentTaskCheckService.RemapBlockedInterpretationsAsync(
            h.Db, h.Clock, CancellationToken.None)).ShouldBe(0);
        (await h.EventsAsync(seeded.TaskId))
            .Count(e => e.Type == AgentTaskEventType.Completed && e.Detail.Contains("CARD-0302"))
            .ShouldBe(1);
    }

    [Test]
    public async Task remap_does_not_touch_a_non_check_blocked_row()
    {
        await using var h = await Harness.CreateAsync();
        var code = await h.SeedBlockedCodeAsync("Should negatives throw?");

        (await AgentTaskCheckService.RemapBlockedInterpretationsAsync(
            h.Db, h.Clock, CancellationToken.None)).ShouldBe(0);

        (await h.ReloadAsync(code)).Status.ShouldBe(AgentTaskStatus.Blocked);
    }

    [Test]
    public async Task remap_does_not_touch_a_check_blocked_row_with_empty_result()
    {
        await using var h = await Harness.CreateAsync();
        var empty = await h.SeedBlockedCheckAsync(result: "");
        var missing = await h.SeedBlockedCheckAsync(result: null);

        (await AgentTaskCheckService.RemapBlockedInterpretationsAsync(
            h.Db, h.Clock, CancellationToken.None)).ShouldBe(0);

        (await h.ReloadAsync(empty.TaskId)).Status.ShouldBe(AgentTaskStatus.Blocked);
        (await h.ReloadAsync(missing.TaskId)).Status.ShouldBe(AgentTaskStatus.Blocked);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;

        private Harness(IsolatedTestSchema schema, AppDbContext db, FakeTimeProvider clock)
        {
            _schema = schema;
            Db = db;
            Clock = clock;
        }

        public static async Task<Harness> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
            return new Harness(schema, db, clock);
        }

        public AppDbContext Db { get; }
        public FakeTimeProvider Clock { get; }

        public async Task<SeededCheck> SeedBlockedCheckAsync(string? result)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var sessionId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var completedAt = now.AddMinutes(-10);

            Db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = $"check-interp-{agentId:N}"[..24],
                Slug = $"check-interp-{agentId:N}"[..24],
                WorkingDirectory = Path.GetTempPath(),
                Details = "standing check interpreter",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Low,
                AlwaysOn = true,
                IsPoolDelegate = false,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
            });
            Db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now.AddHours(-1),
                StartedAt = now.AddHours(-1),
                LastSeenAt = now,
            });
            Db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = $"check #1 on {taskId:N}"[..24],
                Goal = "interpret this bundle",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Check,
                ReplyTo = AgentTaskReplyTo.None,
                ModelLevel = AgentModelLevel.Low,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Blocked,
                Result = result,
                ReportEvidence = AgentTaskReportEvidence.Marked,
                CreatedAt = now.AddMinutes(-30),
                DispatchedAt = now.AddMinutes(-30),
                CompletedAt = completedAt,
            });
            await Db.SaveChangesAsync();
            return new SeededCheck(taskId, agentId, sessionId, completedAt);
        }

        public async Task<Guid> SeedBlockedCodeAsync(string question)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var id = Guid.NewGuid();
            Db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"code-blocked-{id:N}"[..24],
                Goal = "needs an answer",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Blocked,
                ReplyTo = AgentTaskReplyTo.Session,
                Result = question,
                CreatedAt = now.AddMinutes(-20),
                DispatchedAt = now.AddMinutes(-20),
            });
            await Db.SaveChangesAsync();
            return id;
        }

        public async Task<AgentTask> ReloadAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<AgentSession> ReloadSessionAsync(Guid sessionId)
        {
            await using var db = NewDb();
            return await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        }

        public async Task<Agent> ReloadAgentAsync(Guid agentId)
        {
            await using var db = NewDb();
            return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == agentId);
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(Guid taskId)
        {
            await using var db = NewDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId)
                .OrderBy(e => e.At)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
        }

        private AppDbContext NewDb() =>
            new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
    }

    private sealed record SeededCheck(Guid TaskId, Guid AgentId, Guid SessionId, DateTime CompletedAt);
}
