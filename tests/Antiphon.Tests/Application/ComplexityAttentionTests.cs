using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0090 S3: RoutingExhausted attention grouping and BlockedQuestion carve-out.
/// Isolated schema so the fleet-global projection only sees this test's rows.
/// </summary>
[Category("Integration")]
public class ComplexityAttentionTests
{
    [Test]
    public async Task Three_blocked_Hard_tasks_are_one_RoutingExhausted_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add(await SeedRoutingBlockedAsync(db, $"hard-{i}"));

        var items = await Service(db).GetAsync(CancellationToken.None);
        var row = items.Items.Single(i => i.Kind == AttentionKind.RoutingExhausted);

        row.Severity.ShouldBe(AlertSeverity.Error);
        row.Title.ShouldBe("Hard chain exhausted");
        row.Headline.ShouldContain("3 tasks waiting");
        row.TaskId.ShouldBe(ids[0]);
        row.Actions.ShouldBe([AttentionAction.OpenDrawer, AttentionAction.OpenCard]);
        items.Items.ShouldNotContain(i =>
            i.Kind == AttentionKind.BlockedQuestion && i.TaskId != null && ids.Contains(i.TaskId.Value));
    }

    [Test]
    public async Task A_question_Blocked_task_is_still_BlockedQuestion()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var id = Guid.NewGuid();
        db.AgentTasks.Add(Task(id, "question", AgentTaskStatus.Blocked, "Which branch?", complexity: null));
        await db.SaveChangesAsync();

        var items = await Service(db).GetAsync(CancellationToken.None);
        items.Items.Single(i => i.TaskId == id).Kind.ShouldBe(AttentionKind.BlockedQuestion);
        items.Items.ShouldNotContain(i => i.Kind == AttentionKind.RoutingExhausted);
    }

    [Test]
    public async Task The_row_disappears_when_the_last_blocked_task_is_requeued()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var id = await SeedRoutingBlockedAsync(db, "only");
        (await Service(db).GetAsync(CancellationToken.None)).Items
            .ShouldContain(i => i.Kind == AttentionKind.RoutingExhausted);

        var task = await db.AgentTasks.FindAsync(id);
        task!.Status = AgentTaskStatus.Queued;
        task.FailureReason = null;
        await db.SaveChangesAsync();

        (await Service(db).GetAsync(CancellationToken.None)).Items
            .ShouldNotContain(i => i.Kind == AttentionKind.RoutingExhausted);
    }

    private static async Task<Guid> SeedRoutingBlockedAsync(AppDbContext db, string title)
    {
        var id = Guid.NewGuid();
        db.AgentTasks.Add(Task(
            id,
            title,
            AgentTaskStatus.Blocked,
            ComplexityRoutingService.RoutingExhaustedPrefix + "Hard chain — all held",
            TaskComplexity.Hard));
        await db.SaveChangesAsync();
        return id;
    }

    private static AgentTask Task(
        Guid id, string title, AgentTaskStatus status, string? failure, TaskComplexity? complexity) =>
        new()
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = AgentTaskRole.Plan,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Complexity = complexity,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = status,
            FailureReason = failure,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        };

    private static AttentionService Service(AppDbContext db) =>
        new(
            db,
            new RefusingSessionRunnerClient(),
            Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<AttentionService>.Instance);

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
