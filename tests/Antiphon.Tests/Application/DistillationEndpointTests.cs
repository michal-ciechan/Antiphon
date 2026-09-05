using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0330 S4 — feedback 409/overwrite and stats over the real host.</summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class DistillationEndpointTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private readonly List<Guid> _taskIds = [];
    private readonly List<Guid> _sessionIds = [];
    private readonly List<Guid> _queuedIds = [];

    public DistillationEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        if (_taskIds.Count == 0 && _sessionIds.Count == 0)
            return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_taskIds.Count > 0)
        {
            await db.OutputDistillations.Where(d => _taskIds.Contains(d.TaskId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _taskIds.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
        }
        if (_queuedIds.Count > 0)
            await db.SessionQueuedMessages.Where(m => _queuedIds.Contains(m.Id)).ExecuteDeleteAsync();
        if (_taskIds.Count > 0)
            await db.AgentTasks.Where(t => _taskIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_sessionIds.Count > 0)
            await db.AgentSessions.Where(s => _sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        _taskIds.Clear();
        _queuedIds.Clear();
        _sessionIds.Clear();
    }

    [Test]
    public async Task feedback_on_a_task_without_a_row_is_409()
    {
        var task = await SeedTaskAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/distillation/feedback",
            new { verdict = "Good" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task second_feedback_overwrites_with_a_new_FeedbackAt()
    {
        var task = await SeedTaskAsync();
        await SeedLedgerAsync(task.Id, DistillationOutcome.Shadowed);
        using var client = _factory.CreateClient();

        (await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/distillation/feedback",
            new { verdict = "Lost", note = "first" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        DateTime? firstAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.OutputDistillations.SingleAsync(d => d.TaskId == task.Id);
            row.Feedback.ShouldBe(DistillationFeedback.LostInformation);
            row.FeedbackNote.ShouldBe("first");
            firstAt = row.FeedbackAt;
            firstAt.ShouldNotBeNull();
        }

        await Task.Delay(20);
        (await client.PostAsJsonAsync(
            $"/api/agent-tasks/{task.Id}/distillation/feedback",
            new { verdict = "Noisy" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.OutputDistillations.SingleAsync(d => d.TaskId == task.Id);
            row.Feedback.ShouldBe(DistillationFeedback.Noisy);
            row.FeedbackAt.ShouldNotBeNull();
            row.FeedbackAt.Value.ShouldBeGreaterThan(firstAt!.Value);
        }
    }

    [Test]
    public async Task stats_counts_match_seeded_rows()
    {
        var a = await SeedTaskAsync();
        var b = await SeedTaskAsync();
        await SeedLedgerAsync(a.Id, DistillationOutcome.Shadowed, DistillationFeedback.Good);
        await SeedLedgerAsync(b.Id, DistillationOutcome.RejectedOverCompressed, DistillationFeedback.LostInformation);
        using var client = _factory.CreateClient();

        var stats = await client.GetFromJsonAsync<DistillationStatsDto>(
            "/api/distillations/stats", Json);

        stats.ShouldNotBeNull();
        stats.Total.ShouldBeGreaterThanOrEqualTo(2);
        stats.ByOutcome.ShouldContainKey("Shadowed");
        stats.ByFeedback.ShouldContainKey("Good");
        stats.ByFeedback.ShouldContainKey("LostInformation");
        stats.ByBundleStamp.ShouldContainKey("output-distiller vtest");
    }

    [Test]
    public async Task since_window_excludes_older_rows()
    {
        var oldTask = await SeedTaskAsync();
        var newTask = await SeedTaskAsync();
        await SeedLedgerAsync(oldTask.Id, DistillationOutcome.Shadowed, createdAt: DateTime.UtcNow.AddDays(-10));
        await SeedLedgerAsync(newTask.Id, DistillationOutcome.Applied, createdAt: DateTime.UtcNow);
        using var client = _factory.CreateClient();

        var since = DateTime.UtcNow.AddDays(-1).ToString("o");
        var rows = await client.GetFromJsonAsync<List<DistillationDto>>(
            $"/api/distillations?since={Uri.EscapeDataString(since)}", Json);

        rows.ShouldNotBeNull();
        rows.ShouldContain(r => r.TaskId == newTask.Id);
        rows.ShouldNotContain(r => r.TaskId == oldTask.Id);
    }

    [Test]
    public async Task full_read_at_is_set_only_by_a_parent_poll_after_SentAt()
    {
        var parentId = Guid.NewGuid();
        var applied = await SeedTaskAsync(parentId);
        var shadowed = await SeedTaskAsync(parentId);
        var unsent = await SeedTaskAsync(parentId);
        await SeedSessionAsync(parentId);
        await SeedSentNoteAsync(applied.Id, parentId, sentAt: DateTime.UtcNow.AddMinutes(-1));
        await SeedSentNoteAsync(shadowed.Id, parentId, sentAt: DateTime.UtcNow.AddMinutes(-1));
        await SeedPendingNoteAsync(unsent.Id, parentId);
        await SeedLedgerAsync(applied.Id, DistillationOutcome.Applied);
        await SeedLedgerAsync(shadowed.Id, DistillationOutcome.Shadowed);
        await SeedLedgerAsync(unsent.Id, DistillationOutcome.Applied);

        using (var scope = _factory.Services.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
            await tasks.GetAsync(applied.Id, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.OutputDistillations.SingleAsync(d => d.TaskId == applied.Id))
                .FullReadAt.ShouldBeNull();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
            await tasks.GetAsync(applied.Id, CancellationToken.None, parentId);
            await tasks.GetAsync(shadowed.Id, CancellationToken.None, parentId);
            await tasks.GetAsync(unsent.Id, CancellationToken.None, parentId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.OutputDistillations.SingleAsync(d => d.TaskId == applied.Id))
                .FullReadAt.ShouldNotBeNull();
            (await db.OutputDistillations.SingleAsync(d => d.TaskId == shadowed.Id))
                .FullReadAt.ShouldBeNull();
            (await db.OutputDistillations.SingleAsync(d => d.TaskId == unsent.Id))
                .FullReadAt.ShouldBeNull();
        }
    }

    private async Task<AgentTask> SeedTaskAsync(Guid? parentSessionId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = parentSessionId,
            Title = "distill api seed",
            Goal = "g",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded,
            Result = new string('y', 1_500),
            ReplyTo = AgentTaskReplyTo.Session,
            CreatedAt = now,
            CompletedAt = now,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        _taskIds.Add(id);
        return task;
    }

    private async Task SeedSessionAsync(Guid sessionId)
    {
        if (_sessionIds.Contains(sessionId))
            return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        _sessionIds.Add(sessionId);
    }

    private async Task SeedSentNoteAsync(Guid taskId, Guid sessionId, DateTime sentAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = id,
            AgentSessionId = sessionId,
            Body = "note",
            Status = QueuedMessageStatus.Sent,
            Sequence = _queuedIds.Count + 1,
            Origin = QueuedMessageOrigin.Delegation,
            SourceTaskId = taskId,
            CreatedAt = sentAt.AddMinutes(-2),
            SentAt = sentAt,
        });
        await db.SaveChangesAsync();
        _queuedIds.Add(id);
    }

    private async Task SeedPendingNoteAsync(Guid taskId, Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = id,
            AgentSessionId = sessionId,
            Body = "note",
            Status = QueuedMessageStatus.Pending,
            Sequence = _queuedIds.Count + 1,
            Origin = QueuedMessageOrigin.Delegation,
            SourceTaskId = taskId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        _queuedIds.Add(id);
    }

    private async Task SeedLedgerAsync(
        Guid taskId,
        DistillationOutcome outcome,
        DistillationFeedback feedback = DistillationFeedback.None,
        DateTime? createdAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.OutputDistillations.Add(new OutputDistillationRecord
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            BundleStamp = "output-distiller vtest",
            Mode = OutputDistillerMode.Shadow,
            RawChars = 1500,
            DistilledChars = 200,
            WaitMs = 10,
            Outcome = outcome,
            Feedback = feedback,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
