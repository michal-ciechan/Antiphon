using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0272 S1: one-shot derivation of StageOutcome rows from land events. Isolated schema so
/// the sweep cannot see another suite's LandRequested rows.
/// </summary>
[Category("Integration")]
public class StageOutcomeBackfillTests
{
    [Test]
    public async Task conflicted_is_rebase_found_with_the_merge_cost_attached()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, request, outcome) = await SeedLandAsync(db, AgentTaskEventType.Conflicted, "Conflicts: AgentTaskDispatcher.cs");
        var merge = await SeedMergeAsync(db, task.Id, 2.51m);

        var written = await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None);

        written.ShouldBe(1);
        var row = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.SubjectTaskId == task.Id);
        row.Stage.ShouldBe(OrchestrationStage.Rebase);
        row.Outcome.ShouldBe(StageOutcomeKind.Found);
        row.Source.ShouldBe(StageOutcomeSource.Backfill);
        row.ResolutionTaskId.ShouldBe(merge.Id);
        row.ResolutionCostUsd.ShouldBe(2.51m);
        row.Ref.ShouldBe(outcome.Id.ToString("D"));
        row.DurationSeconds.ShouldBe(12);
        row.Detail.ShouldContain("duration=request-to-outcome");
        _ = request;
    }

    [Test]
    public async Task landed_build_skipped_is_rebase_clean_verify_skipped_cleanup_clean()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, _, _) = await SeedLandAsync(db, AgentTaskEventType.Landed,
            "landed feat/x -> master as abc, pushed (origin/master=abc), verify: build skipped (base unchanged), worktree removed");

        await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None);

        (await StagesAsync(db, task.Id)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Skipped),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Clean),
        ]);
    }

    [Test]
    public async Task landed_build_ok_is_verify_clean()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, _, _) = await SeedLandAsync(db, AgentTaskEventType.Landed,
            "landed feat/x -> master as abc, pushed (origin/master=abc), verify: build OK, worktree removed");

        await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None);

        (await StagesAsync(db, task.Id)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Clean),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Clean),
        ]);
    }

    [Test]
    public async Task landed_with_residue_is_rebase_clean_verify_from_head_cleanup_failed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, _, _) = await SeedLandAsync(db, AgentTaskEventType.LandedWithResidue,
            "landed feat/x -> master as abc, pushed (origin/master=abc), verify: build OK, cleanup incomplete: directory C:\\trees\\card-task-x still exists");

        await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None);

        (await StagesAsync(db, task.Id)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Clean),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Failed),
        ]);
    }

    [Test]
    public async Task land_refused_could_not_delete_is_verify_unreported_cleanup_failed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, _, _) = await SeedLandAsync(db, AgentTaskEventType.LandRefused,
            "land refused: Landed and pushed, but could not delete feat/card-task-abc: branch is used by worktree");

        await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None);

        (await StagesAsync(db, task.Id)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Unreported),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Failed),
        ]);
    }

    [Test]
    public async Task land_refused_build_failed_is_verify_failed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, _, _) = await SeedLandAsync(db, AgentTaskEventType.LandRefused,
            "land refused: build failed:\nMSB1003: Specify a project or solution file.");

        await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None);

        (await StagesAsync(db, task.Id)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Failed),
        ]);
    }

    [Test]
    public async Task land_refused_push_rejected_is_cleanup_failed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, _, _) = await SeedLandAsync(db, AgentTaskEventType.LandRefused,
            "land refused: git push origin master rejected: non-fast-forward");

        await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None);

        (await StagesAsync(db, task.Id)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Unreported),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Failed),
        ]);
    }

    [Test]
    public async Task a_second_run_writes_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (task, _, _) = await SeedLandAsync(db, AgentTaskEventType.Landed,
            "landed feat/x -> master as abc, pushed (origin/master=abc), verify: build skipped (base unchanged), worktree removed");

        (await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None)).ShouldBe(3);
        (await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None)).ShouldBe(0);
        (await db.StageOutcomes.CountAsync(o => o.SubjectTaskId == task.Id)).ShouldBe(3);
    }

    [Test]
    public async Task hosted_service_writes_the_same_rows_as_run_async()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using (var seed = CreateContext(schema))
        {
            await SeedLandAsync(seed, AgentTaskEventType.Landed,
                "landed feat/x -> master as abc, pushed (origin/master=abc), verify: build OK, worktree removed");
        }

        var service = new StageOutcomeBackfillService(
            new BackfillScopeFactory(schema.ConnectionString),
            NullLogger<StageOutcomeBackfillService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        await using var verify = CreateContext(schema);
        (await verify.StageOutcomes.CountAsync()).ShouldBe(3);
    }

    private static async Task<(AgentTask Task, AgentTaskEvent Request, AgentTaskEvent Outcome)> SeedLandAsync(
        AppDbContext db, AgentTaskEventType outcomeType, string detail)
    {
        var id = Guid.NewGuid();
        var requested = new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc);
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "backfill land",
            Goal = "Land.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = requested.AddMinutes(-30),
            CompletedAt = requested.AddMinutes(-1),
        };
        var request = new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.LandRequested,
            Detail = "Land requested (build verification only when rebase replays commits).",
            At = requested,
        };
        var outcome = new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = outcomeType,
            Detail = detail,
            At = requested.AddSeconds(12),
        };
        db.AgentTasks.Add(task);
        db.AgentTaskEvents.AddRange(request, outcome);
        await db.SaveChangesAsync();
        return (task, request, outcome);
    }

    private static async Task<AgentTask> SeedMergeAsync(AppDbContext db, Guid parentId, decimal cost)
    {
        var id = Guid.NewGuid();
        var merge = new AgentTask
        {
            Id = id,
            RootTaskId = parentId,
            ParentTaskId = parentId,
            Title = "Resolve merge conflict",
            Goal = "Rebase.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Merge,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Succeeded,
            CostUsd = cost,
            CreatedAt = new DateTime(2026, 9, 1, 13, 5, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 9, 1, 13, 21, 0, DateTimeKind.Utc),
        };
        db.AgentTasks.Add(merge);
        await db.SaveChangesAsync();
        return merge;
    }

    private static async Task<List<(OrchestrationStage Stage, StageOutcomeKind Outcome)>> StagesAsync(
        AppDbContext db, Guid taskId)
    {
        var rows = await db.StageOutcomes.AsNoTracking()
            .Where(o => o.SubjectTaskId == taskId)
            .OrderBy(o => o.Stage)
            .ToListAsync();
        return rows.Select(o => (o.Stage, o.Outcome)).ToList();
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class BackfillScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        public BackfillScopeFactory(string connectionString)
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() => _provider.Dispose();
    }
}
