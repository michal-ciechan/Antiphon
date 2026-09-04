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
    public async Task Three_Plan_Hard_and_two_Code_Hard_with_no_cells_are_one_Hard_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var planIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
            planIds.Add(await SeedRoutingBlockedAsync(db, $"plan-{i}", AgentTaskRole.Plan, minutesAgo: 10 - i));
        for (var i = 0; i < 2; i++)
            await SeedRoutingBlockedAsync(db, $"code-{i}", AgentTaskRole.Code, minutesAgo: 5 - i);

        var items = await Service(db).GetAsync(CancellationToken.None);
        var row = items.Items.Single(i => i.Kind == AttentionKind.RoutingExhausted);

        row.Title.ShouldBe("Hard chain exhausted");
        row.Headline.ShouldContain("5 tasks waiting");
        row.TaskId.ShouldBe(planIds[0]);
    }

    [Test]
    public async Task Adding_a_Plan_Hard_cell_splits_Plan_and_any_role_into_two_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        for (var i = 0; i < 3; i++)
            await SeedRoutingBlockedAsync(db, $"plan-{i}", AgentTaskRole.Plan, minutesAgo: 10 - i);
        for (var i = 0; i < 2; i++)
            await SeedRoutingBlockedAsync(db, $"code-{i}", AgentTaskRole.Code, minutesAgo: 5 - i);
        await SeedCellAsync(db, AgentTaskRole.Plan, TaskComplexity.Hard,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier));

        var items = await Service(db).GetAsync(CancellationToken.None);
        var rows = items.Items
            .Where(i => i.Kind == AttentionKind.RoutingExhausted)
            .OrderBy(i => i.Title)
            .ToList();

        rows.Count.ShouldBe(2);
        rows.Select(r => r.Title).ShouldBe([
            "Hard chain exhausted",
            "Plan/Hard chain exhausted",
        ]);
        rows.Single(r => r.Title == "Plan/Hard chain exhausted").Headline.ShouldContain("3 tasks waiting");
        rows.Single(r => r.Title == "Hard chain exhausted").Headline.ShouldContain("2 tasks waiting");
    }

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

    private static async Task<Guid> SeedRoutingBlockedAsync(
        AppDbContext db,
        string title,
        AgentTaskRole role = AgentTaskRole.Plan,
        int minutesAgo = 5)
    {
        var id = Guid.NewGuid();
        db.AgentTasks.Add(Task(
            id,
            title,
            AgentTaskStatus.Blocked,
            ComplexityRoutingService.RoutingExhaustedPrefix + "Hard chain — all held",
            TaskComplexity.Hard,
            role,
            DateTime.UtcNow.AddMinutes(-minutesAgo)));
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedCellAsync(
        AppDbContext db,
        AgentTaskRole? role,
        TaskComplexity complexity,
        params (AgentKind Kind, AgentModelLevel Level)[] pairs)
    {
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = role,
            Complexity = complexity,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                pairs.Select(p => new ComplexityCandidatePair(p.Kind, p.Level)).ToList()),
            Provenance = RoutingPinProvenance.Human,
            Reason = "test cell",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static AgentTask Task(
        Guid id,
        string title,
        AgentTaskStatus status,
        string? failure,
        TaskComplexity? complexity,
        AgentTaskRole role = AgentTaskRole.Plan,
        DateTime? createdAt = null) =>
        new()
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Complexity = complexity,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = status,
            FailureReason = failure,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-5),
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
