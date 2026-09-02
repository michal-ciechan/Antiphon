using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0272 S1: hit rate is Found / (Found + Clean); latest-per-(task, stage) is the report grain.
/// Isolated schema so empty-fleet and count assertions do not collide.
/// </summary>
[Category("Integration")]
public class StageOutcomeSummaryTests
{
    [Test]
    public async Task hit_rate_excludes_skipped_failed_and_unreported()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = Guid.NewGuid();
        var at = DateTime.UtcNow;
        db.StageOutcomes.AddRange(
            Row(task, OrchestrationStage.Verify, StageOutcomeKind.Found, at),
            Row(Guid.NewGuid(), OrchestrationStage.Verify, StageOutcomeKind.Clean, at.AddSeconds(1)),
            Row(Guid.NewGuid(), OrchestrationStage.Verify, StageOutcomeKind.Skipped, at.AddSeconds(2)),
            Row(Guid.NewGuid(), OrchestrationStage.Verify, StageOutcomeKind.Failed, at.AddSeconds(3)),
            Row(Guid.NewGuid(), OrchestrationStage.Verify, StageOutcomeKind.Unreported, at.AddSeconds(4)));
        await db.SaveChangesAsync();

        var dto = await new StageOutcomeService(db).ListAsync(null, null, "Verify", null, latestOnly: true, CancellationToken.None);

        var row = dto.Summary.ShouldHaveSingleItem();
        row.Runs.ShouldBe(5);
        row.Found.ShouldBe(1);
        row.Clean.ShouldBe(1);
        row.Skipped.ShouldBe(1);
        row.Failed.ShouldBe(1);
        row.Unreported.ShouldBe(1);
        row.HitPercent.ShouldBe(50.0m);
        dto.Rows.Count.ShouldBe(5);
    }

    [Test]
    public async Task latest_per_task_stage_keeps_the_override_only()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var task = Guid.NewGuid();
        var first = Row(task, OrchestrationStage.Review, StageOutcomeKind.Clean, DateTime.UtcNow.AddMinutes(-5),
            source: StageOutcomeSource.Delegate, cost: 7.48m);
        var overrideRow = Row(task, OrchestrationStage.Review, StageOutcomeKind.Found, DateTime.UtcNow,
            source: StageOutcomeSource.Orchestrator, cost: 0m, supersedes: first.Id);
        db.StageOutcomes.AddRange(first, overrideRow);
        await db.SaveChangesAsync();

        var latest = await new StageOutcomeService(db).ListAsync(null, null, null, null, latestOnly: true, CancellationToken.None);
        latest.Rows.ShouldHaveSingleItem();
        latest.Rows[0].Id.ShouldBe(overrideRow.Id);
        latest.Rows[0].Outcome.ShouldBe(StageOutcomeKind.Found);
        latest.Summary.ShouldHaveSingleItem().HitPercent.ShouldBe(100.0m);

        var all = await new StageOutcomeService(db).ListAsync(null, null, null, null, latestOnly: false, CancellationToken.None);
        all.Rows.Count.ShouldBe(2);
        all.Summary.ShouldHaveSingleItem().HitPercent.ShouldBe(50.0m);
    }

    [Test]
    public async Task usd_spent_includes_resolution_cost_and_usd_per_finding_is_null_when_none_found()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var rebase = Row(Guid.NewGuid(), OrchestrationStage.Rebase, StageOutcomeKind.Found, DateTime.UtcNow);
        rebase.ResolutionCostUsd = 2.51m;
        db.StageOutcomes.AddRange(
            rebase,
            Row(Guid.NewGuid(), OrchestrationStage.Rebase, StageOutcomeKind.Clean, DateTime.UtcNow.AddSeconds(1)),
            Row(Guid.NewGuid(), OrchestrationStage.Verify, StageOutcomeKind.Clean, DateTime.UtcNow.AddSeconds(2)));
        await db.SaveChangesAsync();

        var dto = await new StageOutcomeService(db).ListAsync(null, null, null, null, latestOnly: true, CancellationToken.None);
        var rebaseSummary = dto.Summary.Single(s => s.Stage == OrchestrationStage.Rebase);
        rebaseSummary.UsdSpent.ShouldBe(2.51m);
        rebaseSummary.UsdPerFinding.ShouldBe(2.51m);
        rebaseSummary.HitPercent.ShouldBe(50.0m);
        dto.Summary.Single(s => s.Stage == OrchestrationStage.Verify).UsdPerFinding.ShouldBeNull();
        dto.Summary.Single(s => s.Stage == OrchestrationStage.Verify).HitPercent.ShouldBe(0.0m);
    }

    [Test]
    public async Task attach_merge_resolution_fills_the_rebase_finding()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var parentId = Guid.NewGuid();
        db.StageOutcomes.Add(Row(parentId, OrchestrationStage.Rebase, StageOutcomeKind.Found, DateTime.UtcNow.AddMinutes(-10)));
        await db.SaveChangesAsync();

        var merge = new AgentTask
        {
            Id = Guid.NewGuid(),
            RootTaskId = parentId,
            ParentTaskId = parentId,
            Title = "Resolve merge conflict",
            Goal = "Rebase.",
            Role = AgentTaskRole.Merge,
            WorkingDirectory = Path.GetTempPath(),
            CostUsd = 2.51m,
            CreatedAt = DateTime.UtcNow,
        };
        await new StageOutcomeService(db).AttachMergeResolutionAsync(merge, CancellationToken.None);
        await db.SaveChangesAsync();

        var finding = await db.StageOutcomes.SingleAsync(o => o.SubjectTaskId == parentId);
        finding.ResolutionTaskId.ShouldBe(merge.Id);
        finding.ResolutionCostUsd.ShouldBe(2.51m);
    }

    [Test]
    public async Task an_unknown_stage_name_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var error = await Should.ThrowAsync<ValidationException>(() =>
            new StageOutcomeService(db).ListAsync(null, null, "PlanReview", null, true, CancellationToken.None));
        error.StatusCode.ShouldBe(422);
        error.Errors.ShouldContainKey("stage");
    }

    private static StageOutcome Row(
        Guid subject,
        OrchestrationStage stage,
        StageOutcomeKind outcome,
        DateTime at,
        StageOutcomeSource source = StageOutcomeSource.Server,
        decimal? cost = null,
        Guid? supersedes = null) => new()
    {
        Id = Guid.NewGuid(),
        Stage = stage,
        Outcome = outcome,
        Source = source,
        SubjectTaskId = subject,
        CostUsd = cost,
        DurationSeconds = 6,
        Detail = "",
        SupersedesId = supersedes,
        RecordedAt = DateTime.SpecifyKind(at, DateTimeKind.Utc),
    };

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
