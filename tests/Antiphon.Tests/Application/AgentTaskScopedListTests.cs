using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class AgentTaskScopedListTests
{
    [Test]
    public async Task Stored_project_wins_over_card_project()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);

        var listed = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, null, false, null,
            ScopedAgentTaskListFixture.ProjectXScope(),
            CancellationToken.None);
        listed.Items.Select(t => t.Id).ShouldNotContain(ScopedAgentTaskListFixture.Y3);

        var y = listed.Items.SingleOrDefault(t => t.Id == ScopedAgentTaskListFixture.Y3);
        y.ShouldBeNull();

        var yListed = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, null, false, null,
            new AgentTaskScopeRequest(ScopedAgentTaskListFixture.ProjectY, null, AgentTaskUnscopedMode.Exclude),
            CancellationToken.None);
        var y3 = yListed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.Y3);
        y3.ProjectId.ShouldBe(ScopedAgentTaskListFixture.ProjectY);
        y3.ScopeSource.ShouldBe(AgentTaskScopeSource.Task);
        y3.BoardId.ShouldBe(ScopedAgentTaskListFixture.BoardB1);
    }

    [Test]
    public async Task Card_project_fallback_remains_scoped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);

        var listed = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, null, false, null,
            ScopedAgentTaskListFixture.ProjectXScope(),
            CancellationToken.None);
        var x2 = listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X2);
        x2.ProjectId.ShouldBe(ScopedAgentTaskListFixture.ProjectX);
        x2.ScopeSource.ShouldBe(AgentTaskScopeSource.Card);

        var only = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, null, false, null,
            ScopedAgentTaskListFixture.ProjectXScope(AgentTaskUnscopedMode.Only),
            CancellationToken.None);
        only.Items.Select(t => t.Id).ShouldNotContain(ScopedAgentTaskListFixture.X2);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == ScopedAgentTaskListFixture.X2))
            .ProjectId.ShouldBeNull();
    }

    [Test]
    public async Task Project_labels_match_resolved_identity()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);
        var service = ScopedAgentTaskListFixture.CreateService(db);
        var listed = await service.ListAsync(null, null, false, null, CancellationToken.None);

        AssertRow(listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X1),
            ScopedAgentTaskListFixture.ProjectX, ScopedAgentTaskListFixture.AntiphonName,
            ScopedAgentTaskListFixture.BoardB1, ScopedAgentTaskListFixture.BoardB1Name, AgentTaskScopeSource.Task);
        AssertRow(listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X2),
            ScopedAgentTaskListFixture.ProjectX, ScopedAgentTaskListFixture.AntiphonName,
            ScopedAgentTaskListFixture.BoardB1, ScopedAgentTaskListFixture.BoardB1Name, AgentTaskScopeSource.Card);
        AssertRow(listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X3),
            ScopedAgentTaskListFixture.ProjectX, ScopedAgentTaskListFixture.AntiphonName,
            null, null, AgentTaskScopeSource.Task);
        AssertRow(listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.Y3),
            ScopedAgentTaskListFixture.ProjectY, ScopedAgentTaskListFixture.GymStatName,
            ScopedAgentTaskListFixture.BoardB1, ScopedAgentTaskListFixture.BoardB1Name, AgentTaskScopeSource.Task);

        var detail = await service.GetAsync(ScopedAgentTaskListFixture.X1, CancellationToken.None);
        detail.Summary.ProjectName.ShouldBe(ScopedAgentTaskListFixture.AntiphonName);
        detail.Summary.ScopeSource.ShouldBe(AgentTaskScopeSource.Task);
    }

    [Test]
    public async Task Board_labels_follow_only_bound_card()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);
        var listed = await ScopedAgentTaskListFixture.CreateService(db)
            .ListAsync(null, null, false, null, CancellationToken.None);

        var x2 = listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X2);
        x2.BoardId.ShouldBe(ScopedAgentTaskListFixture.BoardB1);
        x2.BoardName.ShouldBe(ScopedAgentTaskListFixture.BoardB1Name);
        listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X3).BoardId.ShouldBeNull();
        listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X3).BoardName.ShouldBeNull();

        var b1Card = listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.X1);
        var cCard = listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.Y1);
        b1Card.CardIdentifier.ShouldBe("CARD-0039");
        cCard.CardIdentifier.ShouldBe("CARD-0039");
        b1Card.BoardId.ShouldNotBe(cCard.BoardId);
        b1Card.ProjectId.ShouldNotBe(cCard.ProjectId);

        var archived = await db.AgentTasks.AsNoTracking()
            .SingleOrDefaultAsync(t => t.CardId == ScopedAgentTaskListFixture.CardArchived);
        archived.ShouldBeNull();
        db.AgentTasks.Add(ScopedAgentTaskListFixture.TaskRow(
            Guid.Parse("11111111-1111-1111-1111-111111111119"),
            ScopedAgentTaskListFixture.ProjectX,
            ScopedAgentTaskListFixture.CardArchived,
            ScopedAgentTaskListFixture.XPath,
            ScopedAgentTaskListFixture.XPath,
            ScopedAgentTaskListFixture.T.AddSeconds(20),
            Guid.Parse("11111111-1111-1111-1111-111111111119")));
        await db.SaveChangesAsync();
        var again = await ScopedAgentTaskListFixture.CreateService(db)
            .ListAsync(null, null, false, null, CancellationToken.None);
        var archivedRow = again.Items.Single(t => t.CardId == ScopedAgentTaskListFixture.CardArchived);
        archivedRow.BoardId.ShouldBe(ScopedAgentTaskListFixture.BoardArchived);
        archivedRow.BoardName.ShouldBe("archived Antiphon");
    }

    [Test]
    public async Task Paths_never_supply_scope()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);
        var listed = await ScopedAgentTaskListFixture.CreateService(db)
            .ListAsync(null, null, false, null, CancellationToken.None);

        foreach (var id in new[] { ScopedAgentTaskListFixture.N1, ScopedAgentTaskListFixture.N2 })
        {
            var row = listed.Items.Single(t => t.Id == id);
            row.ProjectId.ShouldBeNull();
            row.BoardId.ShouldBeNull();
            row.ScopeSource.ShouldBe(AgentTaskScopeSource.None);
        }

        var y2 = listed.Items.Single(t => t.Id == ScopedAgentTaskListFixture.Y2);
        y2.ProjectId.ShouldBe(ScopedAgentTaskListFixture.ProjectY);
        y2.ScopeSource.ShouldBe(AgentTaskScopeSource.Task);

        var stored = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Id == ScopedAgentTaskListFixture.N1 || t.Id == ScopedAgentTaskListFixture.N2)
            .ToListAsync();
        stored.ShouldAllBe(t => t.ProjectId == null);
    }

    [Test]
    public async Task Root_filter_precedes_scope_accounting()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);
        var listed = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            ScopedAgentTaskListFixture.RootShared, null, false, null,
            ScopedAgentTaskListFixture.ProjectXScope(),
            CancellationToken.None);

        listed.Items.Select(t => t.Id).ShouldBe(
            [ScopedAgentTaskListFixture.X1, ScopedAgentTaskListFixture.X2, ScopedAgentTaskListFixture.X4]);
        listed.Excluded.Total.ShouldBe(0);
        listed.Items.Count.ShouldBe(3);
    }

    [Test]
    public async Task Status_filter_precedes_scope_accounting()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);
        var terminal = ScopedAgentTaskListFixture.X4;
        var row = await db.AgentTasks.SingleAsync(t => t.Id == terminal);
        row.Status = AgentTaskStatus.Succeeded;
        row.CompletedAt = ScopedAgentTaskListFixture.T;
        await db.SaveChangesAsync();

        var listed = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled], false, null,
            ScopedAgentTaskListFixture.ProjectXScope(),
            CancellationToken.None);
        listed.Items.Select(t => t.Id).ShouldBe([terminal]);
        listed.Items.ShouldNotContain(t => t.Status == AgentTaskStatus.Working);
        listed.Excluded.Total.ShouldBe(0);
    }

    [Test]
    public async Task History_boundary_preserves_open_work()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);
        var t = ScopedAgentTaskListFixture.T;
        var micro = TimeSpan.FromTicks(10);
        var oldBlocked = Guid.Parse("55555555-5555-5555-5555-555555555551");
        var atT = Guid.Parse("55555555-5555-5555-5555-555555555552");
        var belowT = Guid.Parse("55555555-5555-5555-5555-555555555553");
        var aboveT = Guid.Parse("55555555-5555-5555-5555-555555555554");
        var nullCompleted = Guid.Parse("55555555-5555-5555-5555-555555555555");
        db.AddRange(
            ScopedAgentTaskListFixture.TaskRow(oldBlocked, ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, t.AddDays(-21), oldBlocked,
                AgentTaskStatus.Blocked),
            ScopedAgentTaskListFixture.TaskRow(atT, ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, t.AddDays(-2), atT,
                AgentTaskStatus.Succeeded, completedAt: t),
            ScopedAgentTaskListFixture.TaskRow(belowT, ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, t.AddDays(-2), belowT,
                AgentTaskStatus.Succeeded, completedAt: t - micro),
            ScopedAgentTaskListFixture.TaskRow(aboveT, ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, t.AddDays(-2), aboveT,
                AgentTaskStatus.Succeeded, completedAt: t + micro),
            ScopedAgentTaskListFixture.TaskRow(nullCompleted, ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, t.AddDays(-2), nullCompleted,
                AgentTaskStatus.Succeeded, completedAt: null));
        await db.SaveChangesAsync();

        var listed = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, null, false, t, ScopedAgentTaskListFixture.ProjectXScope(), CancellationToken.None);
        listed.Items.Select(row => row.Id).ShouldContain(oldBlocked);
        listed.Items.Select(row => row.Id).ShouldContain(atT);
        listed.Items.Select(row => row.Id).ShouldContain(aboveT);
        listed.Items.Select(row => row.Id).ShouldNotContain(belowT);
        listed.Items.Select(row => row.Id).ShouldNotContain(nullCompleted);
    }

    [Test]
    public async Task Specialists_require_include_checks()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = ScopedAgentTaskListFixture.CreateContext(schema.ConnectionString);
        await ScopedAgentTaskListFixture.SeedFleetAsync(db);
        var check = Guid.Parse("66666666-6666-6666-6666-666666666661");
        var distill = Guid.Parse("66666666-6666-6666-6666-666666666662");
        var diagnose = Guid.Parse("66666666-6666-6666-6666-666666666663");
        db.AddRange(
            ScopedAgentTaskListFixture.TaskRow(check, ScopedAgentTaskListFixture.ProjectX, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.T.AddSeconds(30), check,
                role: AgentTaskRole.Check),
            ScopedAgentTaskListFixture.TaskRow(distill, ScopedAgentTaskListFixture.ProjectY, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.T.AddSeconds(31), distill,
                role: AgentTaskRole.Distill),
            ScopedAgentTaskListFixture.TaskRow(diagnose, null, null,
                ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.XPath, ScopedAgentTaskListFixture.T.AddSeconds(32), diagnose,
                role: AgentTaskRole.Diagnose));
        await db.SaveChangesAsync();

        var hidden = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, null, false, null, ScopedAgentTaskListFixture.ProjectXScope(), CancellationToken.None);
        hidden.Items.Select(t => t.Id).ShouldNotContain(check);
        hidden.Items.Select(t => t.Id).ShouldNotContain(distill);
        hidden.Items.Select(t => t.Id).ShouldNotContain(diagnose);

        var shown = await ScopedAgentTaskListFixture.CreateService(db).ListAsync(
            null, null, true, null, ScopedAgentTaskListFixture.ProjectXScope(), CancellationToken.None);
        shown.Items.Select(t => t.Id).ShouldContain(check);
        shown.Items.Select(t => t.Id).ShouldNotContain(distill);
        shown.Items.Select(t => t.Id).ShouldNotContain(diagnose);
    }

    [Test]
    public async Task Scope_lookup_command_count_is_constant()
    {
        await using var schema1 = await TestDbFixture.CreateIsolatedSchemaAsync();
        var counter1 = new ScopeQueryCounter();
        await using (var seed = ScopedAgentTaskListFixture.CreateContext(schema1.ConnectionString))
            await ScopedAgentTaskListFixture.SeedDistinctKeysAsync(seed, 1);
        int oneTotal;
        await using (var measure = ScopedAgentTaskListFixture.CreateContext(schema1.ConnectionString, counter1))
        {
            counter1.Reset();
            var one = await ScopedAgentTaskListFixture.CreateService(measure)
                .ListAsync(null, null, false, null, CancellationToken.None);
            one.Items.Count.ShouldBe(1);
            one.Items[0].ProjectName.ShouldNotBeNull();
            one.Items[0].BoardName.ShouldNotBeNull();
            counter1.LabelSelects().ShouldBe(2);
            oneTotal = counter1.Commands.Count;
        }

        await using var schema200 = await TestDbFixture.CreateIsolatedSchemaAsync();
        var counter200 = new ScopeQueryCounter();
        await using (var seed = ScopedAgentTaskListFixture.CreateContext(schema200.ConnectionString))
            await ScopedAgentTaskListFixture.SeedDistinctKeysAsync(seed, 200);
        await using (var measure = ScopedAgentTaskListFixture.CreateContext(schema200.ConnectionString, counter200))
        {
            counter200.Reset();
            var twoHundred = await ScopedAgentTaskListFixture.CreateService(measure)
                .ListAsync(null, null, false, null, CancellationToken.None);
            twoHundred.Items.Count.ShouldBe(200);
            twoHundred.Items.ShouldAllBe(t => t.ProjectName != null && t.BoardName != null);
            counter200.LabelSelects().ShouldBe(2);
            counter200.Commands.Count.ShouldBe(oneTotal);
        }

        await using var emptySchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var emptyCounter = new ScopeQueryCounter();
        await using var empty = ScopedAgentTaskListFixture.CreateContext(emptySchema.ConnectionString, emptyCounter);
        emptyCounter.Reset();
        var none = await ScopedAgentTaskListFixture.CreateService(empty)
            .ListAsync(null, null, false, null, CancellationToken.None);
        none.Items.ShouldBeEmpty();
        emptyCounter.LabelSelects().ShouldBeLessThanOrEqualTo(2);

        await ScopedAgentTaskListFixture.SeedAllNoneAsync(empty, 3);
        emptyCounter.Reset();
        var noneRows = await ScopedAgentTaskListFixture.CreateService(empty)
            .ListAsync(null, null, false, null, CancellationToken.None);
        noneRows.Items.Count.ShouldBe(3);
        noneRows.Items.ShouldAllBe(t => t.ScopeSource == AgentTaskScopeSource.None);
        emptyCounter.LabelSelects().ShouldBeLessThanOrEqualTo(2);
    }

    private static void AssertRow(
        Antiphon.Server.Application.Dtos.AgentTaskSummaryDto row,
        Guid? projectId, string? projectName, Guid? boardId, string? boardName, AgentTaskScopeSource source)
    {
        row.ProjectId.ShouldBe(projectId);
        row.ProjectName.ShouldBe(projectName);
        row.BoardId.ShouldBe(boardId);
        row.BoardName.ShouldBe(boardName);
        row.ScopeSource.ShouldBe(source);
    }
}
