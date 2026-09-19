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
/// CARD-0546 S1 (V-1..V-5): the service-level status filter behind <c>GET /api/agent-tasks</c>.
///
/// The card reported <c>?status=Working</c> returning <c>[]</c> for a task that
/// <c>GET /{id}</c> said was Working. The plan found the filter correct at HEAD and at the SHA
/// that served the observation; the empty list was a bare <c>Invoke-RestMethod | Select-Object</c>
/// pipeline printing an array as one blank row. Nothing had ever pinned
/// <see cref="AgentTaskService.ListAsync(Guid?, IReadOnlyCollection{AgentTaskStatus}?, bool, DateTime?, CancellationToken)"/>
/// with a status, so this class does: the <c>IN</c> predicate, the union of a comma list, the
/// history window that never trims non-settled rows, and the specialist hiding that is the only
/// thing which can legitimately drop a Working row.
///
/// Every test seeds under its own fresh <c>RootTaskId</c> and lists by that root, so each
/// assertion is exact on the shared fixture database and no global count is ever asserted.
/// </summary>
[Category("Integration")]
public class AgentTaskListStatusFilterTests
{
    /// <summary>Spelled out rather than <c>Enum.GetValues</c> so V-1 stays "seven, one each".</summary>
    private static readonly AgentTaskStatus[] AllStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
        AgentTaskStatus.Blocked,
        AgentTaskStatus.Succeeded,
        AgentTaskStatus.Failed,
        AgentTaskStatus.Canceled,
    ];

    // ---- V-1 -------------------------------------------------------------------------------

    [Test]
    public async Task each_status_value_returns_exactly_its_own_row_with_the_status_passed_through()
    {
        var root = Guid.NewGuid();
        var seeded = new Dictionary<AgentTaskStatus, Guid>();
        await using (var seed = CreateContext())
        {
            foreach (var status in AllStatuses)
                seeded[status] = (await SeedAsync(seed, root, status)).Id;
        }

        await using var db = CreateContext();
        var service = CreateService(db);

        foreach (var status in AllStatuses)
        {
            var rows = await service.ListAsync(
                root, [status], includeChecks: false, since: null, CancellationToken.None);

            rows.Items.Select(r => r.Id).ShouldBe([seeded[status]], $"status={status}");
            rows.Items.Single().Status.ShouldBe(status, "ToSummary must pass task.Status through unchanged");
        }
    }

    // ---- V-2 -------------------------------------------------------------------------------

    [Test]
    public async Task a_comma_list_is_a_union_and_a_single_value_is_exact()
    {
        var root = Guid.NewGuid();
        var seeded = new Dictionary<AgentTaskStatus, Guid>();
        await using (var seed = CreateContext())
        {
            foreach (var status in AllStatuses)
                seeded[status] = (await SeedAsync(seed, root, status)).Id;
        }

        await using var db = CreateContext();
        var service = CreateService(db);

        var occupancy = await service.ListAsync(
            root,
            [AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked],
            includeChecks: false,
            since: null,
            CancellationToken.None);
        occupancy.Items.Select(r => r.Id).ShouldBe(
            [seeded[AgentTaskStatus.Dispatched], seeded[AgentTaskStatus.Working], seeded[AgentTaskStatus.Blocked]],
            ignoreOrder: true);

        var working = await service.ListAsync(
            root, [AgentTaskStatus.Working], includeChecks: false, since: null, CancellationToken.None);
        working.Items.Select(r => r.Id).ShouldBe([seeded[AgentTaskStatus.Working]]);
    }

    // ---- V-3 -------------------------------------------------------------------------------

    [Test]
    public async Task the_since_window_trims_only_settled_rows()
    {
        var root = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Guid oldWorking, oldSucceeded, recentSucceeded;
        await using (var seed = CreateContext())
        {
            oldWorking = (await SeedAsync(
                seed, root, AgentTaskStatus.Working, createdAt: now.AddDays(-10))).Id;
            oldSucceeded = (await SeedAsync(
                seed, root, AgentTaskStatus.Succeeded, createdAt: now.AddDays(-10), completedAt: now.AddDays(-10))).Id;
            recentSucceeded = (await SeedAsync(
                seed, root, AgentTaskStatus.Succeeded, createdAt: now.AddHours(-2), completedAt: now.AddHours(-1))).Id;
        }

        await using var db = CreateContext();
        var service = CreateService(db);

        var rows = await service.ListAsync(
            root, statuses: null, includeChecks: false, since: now.AddDays(-1), CancellationToken.None);

        rows.Items.Select(r => r.Id).ShouldBe([oldWorking, recentSucceeded], ignoreOrder: true,
            "a Working row created 10 days ago is kept; a Succeeded row completed 10 days ago is trimmed");
        rows.Items.Select(r => r.Id).ShouldNotContain(oldSucceeded);
    }

    // ---- V-4 -------------------------------------------------------------------------------

    [Test]
    public async Task specialist_hiding_is_the_only_thing_that_drops_a_working_row()
    {
        var root = Guid.NewGuid();
        Guid codeWorking, checkWorking;
        await using (var seed = CreateContext())
        {
            codeWorking = (await SeedAsync(seed, root, AgentTaskStatus.Working, role: AgentTaskRole.Code)).Id;
            checkWorking = (await SeedAsync(seed, root, AgentTaskStatus.Working, role: AgentTaskRole.Check)).Id;
        }

        await using var db = CreateContext();
        var service = CreateService(db);

        var hidden = await service.ListAsync(
            root, [AgentTaskStatus.Working], includeChecks: false, since: null, CancellationToken.None);
        hidden.Items.Select(r => r.Id).ShouldBe([codeWorking], "includeChecks:false hides the Check row and nothing else");

        var shown = await service.ListAsync(
            root, [AgentTaskStatus.Working], includeChecks: true, since: null, CancellationToken.None);
        shown.Items.Select(r => r.Id).ShouldBe([codeWorking, checkWorking], ignoreOrder: true);
    }

    // ---- V-5 -------------------------------------------------------------------------------

    [Test]
    public async Task a_null_or_empty_status_list_matches_every_row()
    {
        var root = Guid.NewGuid();
        var seededIds = new List<Guid>();
        await using (var seed = CreateContext())
        {
            foreach (var status in AllStatuses)
                seededIds.Add((await SeedAsync(seed, root, status)).Id);
        }

        await using var db = CreateContext();
        var service = CreateService(db);

        var unfiltered = await service.ListAsync(
            root, statuses: null, includeChecks: false, since: null, CancellationToken.None);
        unfiltered.Items.Select(r => r.Id).ShouldBe(seededIds, ignoreOrder: true, "null means no status predicate");

        var empty = await service.ListAsync(
            root, statuses: [], includeChecks: false, since: null, CancellationToken.None);
        empty.Items.Select(r => r.Id).ShouldBe(seededIds, ignoreOrder: true, "an empty list means no status predicate, never match-nothing");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static AgentTaskService CreateService(AppDbContext db) =>
        new(db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings()),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

    /// <summary>
    /// The minimal row shape (as <c>AgentTaskConcurrencyLimitTests</c> seeds it), parented to
    /// the test's own root so a list by that root is exact on the shared database.
    /// </summary>
    private static async Task<AgentTask> SeedAsync(
        AppDbContext db,
        Guid root,
        AgentTaskStatus status,
        AgentTaskRole role = AgentTaskRole.Code,
        DateTime? createdAt = null,
        DateTime? completedAt = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = root,
            Title = $"status-filter {status} {id:N}",
            Goal = $"status-filter {status}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            Status = status,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = @"C:\tmp\status-filter",
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CompletedAt = completedAt,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}
