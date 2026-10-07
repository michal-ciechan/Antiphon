using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1079 V-13 through V-20. Rows are read from a published snapshot and never write.</summary>
[Category("Integration")]
public sealed class SeatOccupancyAttentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task C1079_Seat_idle_is_absent_below_the_warning_age()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = Guid.NewGuid();
        var state = State(Host("server2", seats:
        [
            Seat(sessionId, SeatClass.IdleBlocked, Ago(29), occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked),
        ]));

        var rows = await ReadAsync(schema, state);
        rows.Items.Where(i => i.Kind == AttentionKind.SeatIdle).ShouldBeEmpty();
        var orphan = rows.Items.Where(i => i.Kind == AttentionKind.SlotOrphan).ShouldHaveSingleItem();
        orphan.ConditionKey.ShouldBe($"slot-orphan:server2:{sessionId:N}");
    }

    [Test]
    public async Task C1079_Seat_idle_warning_names_runner_session_task_status_age_and_pushed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var idleSince = Ago(30);
        var state = State(Host("server2", seats:
        [
            Seat(sessionId, SeatClass.IdleBlocked, idleSince, occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked, taskId: taskId, cardId: cardId, boardId: boardId,
                attempt: 4, pushed: "unknown"),
        ]));

        var row = (await ReadAsync(schema, state)).Items
            .Where(i => i.Kind == AttentionKind.SeatIdle).ShouldHaveSingleItem();
        row.Severity.ShouldBe(AlertSeverity.Warning);
        row.ConditionKey.ShouldBe($"seat-idle:server2:{sessionId:N}");
        row.TaskId.ShouldBe(taskId);
        row.SessionId.ShouldBe(sessionId);
        row.CardId.ShouldBe(cardId);
        row.BoardId.ShouldBe(boardId);
        row.SinceUtc.ShouldBe(idleSince);
        row.Evidence.ShouldContain("runner=server2");
        row.Evidence.ShouldContain($"session={sessionId:D}");
        row.Evidence.ShouldContain($"task={taskId:D}");
        row.Evidence.ShouldContain("attempt=4");
        row.Evidence.ShouldContain("status=Blocked");
        // S3 review: the evidence age was a bare number while the headline already says min.
        row.Evidence.ShouldContain("age=30min");
        row.Evidence.ShouldContain("pushed=unknown");
        row.Evidence.ShouldContain($"observedAt={Now.UtcDateTime:O}");
        row.Actions.ShouldBe([AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.OpenDrawer]);
    }

    [Test]
    public async Task C1079_Seat_idle_becomes_error_at_the_error_age()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = Guid.NewGuid();
        var state = new SeatOccupancyState();
        state.Publish(Snapshot(Host("server2", seats:
        [
            Seat(sessionId, SeatClass.IdleBlocked, Ago(179), occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked),
        ])));
        SeatIdle(await ReadAsync(schema, state)).Severity.ShouldBe(AlertSeverity.Warning);

        state.Publish(Snapshot(Host("server2", seats:
        [
            Seat(sessionId, SeatClass.IdleBlocked, Ago(180), occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked),
        ])));
        SeatIdle(await ReadAsync(schema, state)).Severity.ShouldBe(AlertSeverity.Error);
    }

    [Test]
    public async Task C1079_Seat_idle_clears_when_the_bound_task_is_working_again()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = Guid.NewGuid();
        var state = new SeatOccupancyState();
        state.Publish(Snapshot(Host("server2", inFlight: 1, dispatchedWorking: 0, idleSeats: 1, seats:
        [
            Seat(sessionId, SeatClass.IdleBlocked, Ago(40), occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked),
        ])));
        var key = $"seat-idle:server2:{sessionId:N}";
        (await ReadAsync(schema, state)).Items.ShouldContain(i => i.ConditionKey == key);

        state.Publish(Snapshot(Host("server2", inFlight: 1, dispatchedWorking: 1, idleSeats: 0, seats:
        [
            Seat(sessionId, SeatClass.Active, Ago(40), occupies: true, orphan: false,
                status: AgentTaskStatus.Working),
        ])));
        var cleared = await ReadAsync(schema, state);
        cleared.Items.ShouldNotContain(i => i.ConditionKey == key);
        cleared.Items.ShouldNotContain(i => i.ConditionKey == $"slot-orphan:server2:{sessionId:N}");
    }

    [Test]
    public async Task C1079_Occupancy_divergence_is_one_row_per_host_and_needs_an_idle_seat()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var oldest = Ago(200);
        var state = State(
            Host("A", inFlight: 10, dispatchedWorking: 2, sessions: 8, pendingLaunch: 1, mirrors: 1,
                idleSeats: 2, oldest: oldest, seats:
                [
                    Seat(older, SeatClass.IdleBlocked, oldest, occupies: true, orphan: true,
                        status: AgentTaskStatus.Blocked, runnerId: "A"),
                    Seat(newer, SeatClass.IdleTerminal, Ago(40), occupies: true, orphan: true,
                        status: AgentTaskStatus.Failed, runnerId: "A"),
                ]),
            // Age would qualify. IdleSeats is the gate (PC-12). PendingLaunch alone is not a row.
            Host("B", inFlight: 3, dispatchedWorking: 2, sessions: 2, pendingLaunch: 1, idleSeats: 0,
                oldest: Ago(200)));

        var rows = (await ReadAsync(schema, state)).Items
            .Where(i => i.Kind == AttentionKind.OccupancyDivergence).ToList();
        var row = rows.ShouldHaveSingleItem();
        row.Severity.ShouldBe(AlertSeverity.Error);
        row.ConditionKey.ShouldBe("occupancy-divergence:A");
        row.Headline.ShouldContain("10");
        row.Headline.ShouldContain("2");
        row.SinceUtc.ShouldBe(oldest);
        row.Evidence.ShouldContain("inFlight=10");
        row.Evidence.ShouldContain("dispatchedWorking=2");
        row.Evidence.ShouldContain("pendingLaunch=1");
        row.Evidence.ShouldContain("mirrors=1");
        row.Evidence.ShouldContain($"seat={older:D}");
        row.Evidence.ShouldContain($"seat={newer:D}");
        rows.ShouldNotContain(i => i.ConditionKey == "occupancy-divergence:B");
    }

    [Test]
    public async Task C1079_Slot_orphan_is_immediate_for_occupying_orphans_only()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var idleId = Guid.NewGuid();
        var warmId = Guid.NewGuid();
        var exitedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var idleSince = Ago(1);
        var started = Ago(50);
        var state = State(Host("server2", inFlight: 2, dispatchedWorking: 1, idleSeats: 1, oldest: idleSince, seats:
        [
            Seat(idleId, SeatClass.IdleBlocked, idleSince, occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked, started: Ago(80)),
            Seat(warmId, SeatClass.PooledWarm, Ago(10), occupies: true, orphan: false, pooledWarm: true),
            Seat(exitedId, SeatClass.Exited, Ago(10), occupies: false, orphan: true),
            Seat(activeId, SeatClass.Active, Ago(1), occupies: true, orphan: true,
                status: AgentTaskStatus.Working, agentId: agentId, started: started),
        ]));

        var rows = (await ReadAsync(schema, state)).Items
            .Where(i => i.Kind == AttentionKind.SlotOrphan).ToList();
        rows.Count.ShouldBe(2);
        var idle = rows.Single(i => i.SessionId == idleId);
        idle.Severity.ShouldBe(AlertSeverity.Warning);
        idle.ConditionKey.ShouldBe($"slot-orphan:server2:{idleId:N}");
        idle.SinceUtc.ShouldBe(idleSince);
        idle.Evidence.ShouldContain("age=1min");
        idle.Actions.ShouldBe([AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.OpenDrawer]);
        var active = rows.Single(i => i.SessionId == activeId);
        active.SinceUtc.ShouldBe(started);
        active.Evidence.ShouldContain("age=50min");
        active.Actions.ShouldBe([AttentionAction.OpenDrawer, AttentionAction.OpenAgent]);
        rows.ShouldNotContain(i => i.SessionId == warmId || i.SessionId == exitedId);
    }

    [Test]
    public async Task C1124_Seat_evidence_names_the_park_state()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var blockedId = Guid.NewGuid();
        var unboundId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var idleSince = Ago(40);
        var state = State(Host("server2", inFlight: 2, dispatchedWorking: 0, idleSeats: 2, oldest: idleSince, seats:
        [
            Seat(blockedId, SeatClass.IdleBlocked, idleSince, occupies: true, orphan: false,
                status: AgentTaskStatus.Blocked, taskId: taskId, attempt: 4,
                parkState: "Held", parkReason: "park_dirty"),
            Seat(unboundId, SeatClass.IdleUnbound, idleSince, occupies: true, orphan: true,
                status: null, taskId: null, parkState: "none"),
        ]));

        var rows = (await ReadAsync(schema, state)).Items;
        var idle = rows.Where(i => i.Kind == AttentionKind.SeatIdle && i.SessionId == blockedId)
            .ShouldHaveSingleItem();
        idle.Severity.ShouldBe(AlertSeverity.Warning);
        idle.Evidence.ShouldContain("park=Held:park_dirty");
        idle.Evidence.ShouldContain("pushed=unknown");
        idle.Actions.ShouldBe([AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.OpenDrawer]);
        rows.ShouldNotContain(i => i.ConditionKey == $"slot-orphan:server2:{blockedId:N}");

        var orphan = rows.Where(i => i.Kind == AttentionKind.SlotOrphan).ShouldHaveSingleItem();
        orphan.SessionId.ShouldBe(unboundId);
        orphan.Evidence.ShouldContain("park=none");
        orphan.ConditionKey.ShouldBe($"slot-orphan:server2:{unboundId:N}");
    }

    [Test]
    public async Task C1079_No_snapshot_or_unavailable_inventory_yields_no_seat_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        (await ReadAsync(schema, seats: null)).Items.Where(IsSeatKind).ShouldBeEmpty();
        (await ReadAsync(schema, new SeatOccupancyState())).Items.Where(IsSeatKind).ShouldBeEmpty();

        var state = State(Host("server2", inFlight: 4, dispatchedWorking: 1, idleSeats: 2, oldest: Ago(200),
            inventory: "unavailable", reason: "connection dropped"));
        var rows = await ReadAsync(schema, state);
        rows.Items.Where(i => i.Kind is AttentionKind.SeatIdle or AttentionKind.SlotOrphan).ShouldBeEmpty();
        var divergence = rows.Items.Where(i => i.Kind == AttentionKind.OccupancyDivergence).ShouldHaveSingleItem();
        divergence.ConditionKey.ShouldBe("occupancy-divergence:server2");
        divergence.Severity.ShouldBe(AlertSeverity.Error);
        divergence.Evidence.ShouldContain("idleSeats=2");
        divergence.Evidence.ShouldContain("inventory=unavailable");
    }

    [Test]
    public async Task C1079_Summary_counts_the_three_kinds_open()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var baseline = await ReadAsync(schema);
        baseline.Items.Where(IsSeatKind).ShouldBeEmpty();
        var state = State(Host("server2", inFlight: 2, dispatchedWorking: 0, idleSeats: 1, oldest: Ago(40), seats:
        [
            Seat(Guid.NewGuid(), SeatClass.IdleBlocked, Ago(40), occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked),
        ]));

        var withRows = await ReadAsync(schema, state);
        var added = withRows.Items.Where(IsSeatKind).ToList();
        added.Count.ShouldBe(3);
        added.Select(i => i.Kind).OrderBy(k => k).ShouldBe(
        [
            AttentionKind.SeatIdle,
            AttentionKind.OccupancyDivergence,
            AttentionKind.SlotOrphan,
        ]);
        AttentionSummaryDto.From(withRows).Open.ShouldBe(AttentionSummaryDto.From(baseline).Open + added.Count);
    }

    [Test]
    public async Task C1079_Disabled_watch_hides_seat_idle_divergence_and_orphan_rows()
    {
        // S3 review: V-11 proves the sampler stops when SeatWatchEnabled is false.
        // This is the projection gate: the same snapshot yields none of the three kinds.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var state = State(Host("server2", inFlight: 2, dispatchedWorking: 0, idleSeats: 1, oldest: Ago(40), seats:
        [
            Seat(Guid.NewGuid(), SeatClass.IdleBlocked, Ago(40), occupies: true, orphan: true,
                status: AgentTaskStatus.Blocked),
        ]));

        (await ReadAsync(schema, state)).Items.Where(IsSeatKind).Count().ShouldBe(3);
        (await ReadAsync(schema, state, new AttentionSettings { SeatWatchEnabled = false }))
            .Items.Where(IsSeatKind).ShouldBeEmpty();
    }

    private static bool IsSeatKind(AttentionItemDto item) =>
        item.Kind is AttentionKind.SeatIdle or AttentionKind.OccupancyDivergence or AttentionKind.SlotOrphan;

    private static AttentionItemDto SeatIdle(AttentionDto attention) =>
        attention.Items.Where(i => i.Kind == AttentionKind.SeatIdle).ShouldHaveSingleItem();

    private static DateTime Ago(int minutes) => Now.AddMinutes(-minutes).UtcDateTime;

    private static SeatOccupancyState State(params HostOccupancyObservation[] hosts)
    {
        var state = new SeatOccupancyState();
        state.Publish(Snapshot(hosts));
        return state;
    }

    private static SeatOccupancySnapshot Snapshot(params HostOccupancyObservation[] hosts) =>
        new(Now.UtcDateTime, hosts);

    private static HostOccupancyObservation Host(
        string hostId,
        int inFlight = 1,
        int dispatchedWorking = 0,
        int sessions = 1,
        int pendingLaunch = 0,
        int mirrors = 0,
        int idleSeats = 1,
        DateTime? oldest = null,
        string inventory = "listed",
        string? reason = null,
        params SeatObservation[] seats) =>
        new(hostId, "runner", inventory, reason, inFlight, dispatchedWorking, sessions, pendingLaunch, mirrors,
            idleSeats, 0, seats.Count(s => s.Orphan && s.Occupies), 10, null, oldest, seats);

    private static SeatObservation Seat(
        Guid sessionId,
        SeatClass seatClass,
        DateTime idleSince,
        bool occupies,
        bool orphan,
        AgentTaskStatus? status = null,
        Guid? taskId = null,
        Guid? cardId = null,
        Guid? boardId = null,
        Guid? agentId = null,
        int? attempt = 1,
        string pushed = "unknown",
        bool pooledWarm = false,
        DateTime? started = null,
        string runnerId = "server2",
        string parkState = "none",
        string? parkReason = null) =>
        new(runnerId, sessionId, occupies ? "Running" : "Exited", 42, started ?? idleSince, occupies, orphan,
            pooledWarm, "Running", taskId, status, attempt, AgentTaskRole.Code, cardId, boardId, agentId,
            seatClass, idleSince, pushed, parkState, parkReason);

    private static async Task<AttentionDto> ReadAsync(
        IsolatedTestSchema schema, SeatOccupancyState? seats = null, AttentionSettings? attention = null)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        return await new AttentionService(
            db, new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            new FakeTimeProvider(Now), NullLogger<AttentionService>.Instance,
            seats: seats,
            attention: attention is null ? null : Options.Create(attention))
            .GetAsync(CancellationToken.None, includeProgressProbe: false);
    }
}
