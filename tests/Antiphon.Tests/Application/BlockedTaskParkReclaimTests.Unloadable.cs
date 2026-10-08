using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-1143: a due Held park whose episode can no longer be loaded records
// park_episode_changed with the ordinary Held backoff, and nothing else.
public sealed partial class BlockedTaskParkReclaimTests
{
    [Test]
    [Arguments("task-token")]
    [Arguments("runner-store")]
    [Arguments("accepted-start")]
    [Arguments("handoff-digest")]
    [Arguments("baseline-missing")]
    public async Task C1143_UnloadableHeldEpisodeRestampsAndSkipsNextSweep(string fact)
    {
        var counter = new FullCommandCounter();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter));
        var (row, path) = await HeldDirtyRowAsync(f, "unloadable-" + fact);
        var working = await SeedBlockedAsync(f, "working-seat", status: AgentTaskStatus.Working, completed: false);
        var held = await ParkOfAsync(f, row.TaskId);
        await BreakEpisodeAsync(f, row, fact);
        var git = f.SourceGit;
        var conditional = f.Wire.ConditionalCommands;
        var force = f.Wire.ForceCommands;
        var kills = f.RecordedStops.Killed.Count;

        AdvanceToDue(f, held);
        git.Commands.Clear();
        counter.Reset();
        (await f.ReclaimAsync(32, 1)).ShouldBe(1, fact);
        var due = counter.Commands;
        var dueNow = f.Now;
        var stamped = await ParkOfAsync(f, row.TaskId);
        stamped.Id.ShouldBe(held.Id, fact);
        stamped.State.ShouldBe(AgentTaskParkState.Held, fact);
        stamped.HeldFromState.ShouldBe(held.HeldFromState, fact);
        stamped.Revision.ShouldBe(held.Revision, fact);
        stamped.ReasonCode.ShouldBe("park_episode_changed", fact);
        SameInstant(stamped.UpdatedAt, dueNow, fact);
        SameInstant(stamped.NextAttemptAt, dueNow.AddSeconds(600), fact);
        stamped.PublicationReceiptId.ShouldBeNull(fact);
        stamped.RunnerSeatReleaseId.ShouldBeNull(fact);
        (await ReleaseCountAsync(f, row.TaskId)).ShouldBe(0, fact);
        StatusVisits(git).ShouldBe(0, fact);
        ParkReads(due).ShouldBe(4, fact + "\n" + counter.Roster());
        Updates(due, "AgentTaskParks").ShouldBe(1, fact + "\n" + counter.Roster());

        f.Clock.Advance(TimeSpan.FromSeconds(120));
        counter.Reset();
        (await f.ReclaimAsync(32, 1)).ShouldBe(1, fact);
        var follow = counter.Commands;
        var backedOff = await ParkOfAsync(f, row.TaskId);
        Metadata(backedOff).ShouldBe(Metadata(stamped), fact);
        ParkReads(follow).ShouldBe(3, fact + "\n" + counter.Roster());
        Reads(follow, "AgentSessions").ShouldBe(0, fact + "\n" + counter.Roster());
        Updates(follow, "AgentTaskParks").ShouldBe(0, fact + "\n" + counter.Roster());
        follow.Count.ShouldBeLessThan(due.Count, fact);
        Console.WriteLine($"C1143 V-1 fact={fact} due={due.Count} follow={follow.Count}");

        AdvanceToDue(f, backedOff);
        await f.ReclaimAsync(32, 1);
        var again = await ParkOfAsync(f, row.TaskId);
        again.Revision.ShouldBe(held.Revision, fact);
        again.State.ShouldBe(AgentTaskParkState.Held, fact);
        again.ReasonCode.ShouldBe("park_episode_changed", fact);
        SameInstant(again.UpdatedAt, f.Now, fact);
        SameInstant(again.NextAttemptAt, f.Now.AddSeconds(600), fact);

        StatusVisits(git).ShouldBe(0, fact);
        f.Wire.ConditionalCommands.ShouldBe(conditional, fact);
        f.Wire.ForceCommands.ShouldBe(force, fact);
        f.RecordedStops.Killed.Count.ShouldBe(kills, fact);
        Released(f, row.SessionId).ShouldBeFalse(fact);
        (await ReleaseCountAsync(f, row.TaskId)).ShouldBe(0, fact);
        (await ParkCountAsync(f, working.TaskId)).ShouldBe(0, fact);
        await SessionRunningAsync(f, working.SessionId, fact);
        await SessionRunningAsync(f, row.SessionId, fact);
        File.Exists(Path.Combine(path, "dirty.txt")).ShouldBeTrue(fact);
    }

    [Test]
    [Arguments("prepare")]
    [Arguments("capture")]
    public async Task C1143_UnloadablePreparationEntryPointsRestamp(string entry)
    {
        var counter = new FullCommandCounter();
        var cut = new AfterIntentCut();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter, cut));
        var (row, _) = await HeldDirtyRowAsync(f, "entry-" + entry);
        var held = await ParkOfAsync(f, row.TaskId);
        await BreakEpisodeAsync(f, row, "task-token");
        AdvanceToDue(f, held);
        f.SourceGit.Commands.Clear();

        counter.Reset();
        (await EntryAsync(f, entry, held.Id)).ShouldBe(("Held", "park_episode_changed"), entry);
        var commands = counter.Commands;
        commands.Count.ShouldBe(5, entry + "\n" + counter.Roster());
        ParkReads(commands).ShouldBe(1, entry + "\n" + counter.Roster());
        Updates(commands, "AgentTaskParks").ShouldBe(1, entry + "\n" + counter.Roster());
        var stamped = await ParkOfAsync(f, row.TaskId);
        stamped.State.ShouldBe(AgentTaskParkState.Held, entry);
        stamped.HeldFromState.ShouldBe(held.HeldFromState, entry);
        stamped.Revision.ShouldBe(held.Revision, entry);
        stamped.ReasonCode.ShouldBe("park_episode_changed", entry);
        SameInstant(stamped.UpdatedAt, f.Now, entry);
        SameInstant(stamped.NextAttemptAt, f.Now.AddSeconds(600), entry);

        // A repeat before the new deadline is the same refusal and moves nothing.
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        counter.Reset();
        (await EntryAsync(f, entry, held.Id)).ShouldBe(("Held", "park_episode_changed"), entry);
        Updates(counter.Commands, "AgentTaskParks").ShouldBe(0, entry + "\n" + counter.Roster());
        ParkReads(counter.Commands).ShouldBe(1, entry + "\n" + counter.Roster());
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(stamped), entry);

        counter.Reset();
        (await EntryAsync(f, entry, Guid.NewGuid())).ShouldBe(("Held", "park_episode_changed"), entry);
        counter.Commands.Count.ShouldBe(1, entry + "\n" + counter.Roster());
        Updates(counter.Commands, "AgentTaskParks").ShouldBe(0, entry);
        StatusVisits(f.SourceGit).ShouldBe(0, entry);

        if (entry != "prepare") return;
        // Prepare's reload after a committed intent: the row is Requested, never Held, so the
        // unloadable episode is refused without a stamp and stays Requested with no deadline.
        var fresh = await SeedBlockedAsync(f, "requested-reload");
        await f.CreateReclaimSourceAsync(fresh.TaskId, fresh.SessionId);
        var parkId = await RegisterAsync(f, fresh.TaskId);
        var registered = await ParkOfAsync(f, fresh.TaskId);
        registered.State.ShouldBe(AgentTaskParkState.Requested, entry);
        registered.RepositoryIdentity.ShouldBeNull(entry);
        cut.Arm(async () =>
        {
            await using var db = f.Db();
            await db.AgentTasks.Where(t => t.Id == fresh.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()));
        });
        f.SourceGit.Commands.Clear();
        (await EntryAsync(f, entry, parkId)).ShouldBe(("Held", "park_episode_changed"), entry);
        cut.Fired.ShouldBeTrue(entry);
        var reloaded = await ParkOfAsync(f, fresh.TaskId);
        reloaded.State.ShouldBe(AgentTaskParkState.Requested, entry);
        reloaded.ReasonCode.ShouldBe("park_publication_requested", entry);
        reloaded.NextAttemptAt.ShouldBeNull(entry);
        reloaded.HeldFromState.ShouldBeNull(entry);
        reloaded.Revision.ShouldBe(registered.Revision + 1, entry);
        reloaded.RepositoryIdentity.ShouldNotBeNull(entry);
        StatusVisits(f.SourceGit).ShouldBe(0, entry);
    }

    [Test]
    [Arguments("revision-race")]
    [Arguments("state-race")]
    [Arguments("already-restamped")]
    [Arguments("deleted-row")]
    public async Task C1143_RestampCasIsDueBoundedAndIdempotent(string race)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        Task<bool> StampAsync(AgentTaskPark observed, int backoff = 600, Guid? id = null) =>
            StampWithAsync(Db(), clock, backoff, id ?? observed.Id, observed.Revision, observed.NextAttemptAt);
        async Task<AgentTaskPark?> ReadAsync(Guid id)
        {
            await using var db = Db();
            return await db.AgentTaskParks.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
        }
        async Task<AgentTaskPark> InsertAsync(DateTime? due)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var park = new AgentTaskPark
            {
                Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), Attempt = 1, BlockEventId = Guid.NewGuid(),
                TaskConcurrencyToken = Guid.NewGuid(), Workspace = WorkspaceMode.Worktree,
                BlockedAt = now.AddMinutes(-30), State = AgentTaskParkState.Held,
                HeldFromState = AgentTaskParkState.Requested, ReasonCode = "park_dirty", Revision = 3,
                CreatedAt = now.AddMinutes(-30), UpdatedAt = now.AddMinutes(-10), NextAttemptAt = due
            };
            await using var db = Db();
            db.AgentTaskParks.Add(park);
            await db.SaveChangesAsync();
            return (await ReadAsync(park.Id))!;
        }

        var start = clock.GetUtcNow().UtcDateTime;
        var neighbor = await InsertAsync(start.AddSeconds(-1));
        var target = await InsertAsync(start.AddSeconds(-1));
        var observed = target;
        await using (var other = Db())
        {
            var rows = race switch
            {
                "revision-race" => await other.AgentTaskParks.Where(p => p.Id == target.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Revision, p => p.Revision + 1)),
                "state-race" => await other.AgentTaskParks.Where(p => p.Id == target.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.State, AgentTaskParkState.Requested)),
                "deleted-row" => await other.AgentTaskParks.Where(p => p.Id == target.Id).ExecuteDeleteAsync(),
                _ => 1
            };
            rows.ShouldBe(1, race);
        }
        if (race == "already-restamped")
        {
            // The first stale caller wins; a second caller holding the same old snapshot loses.
            (await StampAsync(observed)).ShouldBeTrue(race);
            var winner = (await ReadAsync(target.Id))!;
            SameInstant(winner.NextAttemptAt, start.AddSeconds(600), race);
            winner.Revision.ShouldBe(observed.Revision, race);
        }
        var raced = await ReadAsync(target.Id);
        clock.Advance(TimeSpan.FromSeconds(1));
        (await StampAsync(observed)).ShouldBeFalse(race);
        var after = await ReadAsync(target.Id);
        if (race == "deleted-row")
        {
            after.ShouldBeNull(race);
            await using var db = Db();
            (await db.AgentTaskParks.CountAsync(p => p.Id == target.Id || p.TaskId == target.TaskId)).ShouldBe(0, race);
        }
        else
            Metadata(after!).ShouldBe(Metadata(raced!), race);
        Metadata((await ReadAsync(neighbor.Id))!).ShouldBe(Metadata(neighbor), race);

        // Time boundary at a fixed clock: future is not due, equality and null are due.
        var now = clock.GetUtcNow().UtcDateTime;
        var future = await InsertAsync(now.AddTicks(10));
        (await StampAsync(future)).ShouldBeFalse(race + " just-before");
        Metadata((await ReadAsync(future.Id))!).ShouldBe(Metadata(future), race + " just-before");
        var exact = await InsertAsync(now);
        var unset = await InsertAsync(null);
        foreach (var (label, park) in new[] { ("exact-due", exact), ("null-due", unset) })
        {
            (await StampAsync(park)).ShouldBeTrue(race + " " + label);
            var stamped = (await ReadAsync(park.Id))!;
            stamped.State.ShouldBe(AgentTaskParkState.Held, race + " " + label);
            stamped.HeldFromState.ShouldBe(park.HeldFromState, race + " " + label);
            stamped.Revision.ShouldBe(park.Revision, race + " " + label);
            stamped.ReasonCode.ShouldBe("park_episode_changed", race + " " + label);
            SameInstant(stamped.UpdatedAt, now, race + " " + label);
            SameInstant(stamped.NextAttemptAt, now.AddSeconds(600), race + " " + label);
        }
        foreach (var backoff in new[] { 0, -1 })
        {
            var open = await InsertAsync(backoff == 0 ? null : now);
            (await StampAsync(open, backoff)).ShouldBeTrue(race + " backoff " + backoff);
            var stamped = (await ReadAsync(open.Id))!;
            stamped.NextAttemptAt.ShouldBeNull(race + " backoff " + backoff);
            stamped.State.ShouldBe(AgentTaskParkState.Held, race + " backoff " + backoff);
            stamped.Revision.ShouldBe(open.Revision, race + " backoff " + backoff);
            stamped.HeldFromState.ShouldBe(open.HeldFromState, race + " backoff " + backoff);
            stamped.ReasonCode.ShouldBe("park_episode_changed", race + " backoff " + backoff);
        }
        // Only the identity column differs: the neighbor's due row is not addressed.
        var current = (await ReadAsync(neighbor.Id))!;
        (await StampAsync(current, id: Guid.NewGuid())).ShouldBeFalse(race + " neighbor");
        Metadata((await ReadAsync(neighbor.Id))!).ShouldBe(Metadata(neighbor), race + " neighbor");
    }

    // F1: two overlapping callers capture a due cutoff, then the clock moves before either writes.
    // Each restamp is its own captured cutoff plus the backoff, so the later caller cannot move a
    // deadline the earlier caller made future for it, in either order. Offsets are seconds from
    // 02:00; the row starts due at 01:59:59, so the first caller always wins.
    [Test]
    [Arguments("rolled-back", 0.0, -3600.0, 0.0, -3599.0, false, 600.0, 0.0)]
    [Arguments("rolled-back-reversed", 0.0, -3599.0, 0.0, -3600.0, false, 600.0, 0.0)]
    [Arguments("rolled-forward-in-window", 0.0, 300.0, 0.0, 301.0, false, 600.0, 0.0)]
    [Arguments("rolled-forward-in-window-reversed", 0.0, 301.0, 0.0, 300.0, false, 600.0, 0.0)]
    [Arguments("exact-boundary", 0.0, -3600.0, 600.0, -3600.0, true, 1200.0, 600.0)]
    [Arguments("exact-boundary-reversed", 600.0, -3600.0, 0.0, -3600.0, false, 1200.0, 600.0)]
    [Arguments("before-boundary", 0.0, -3600.0, 599.999, -3600.0, false, 600.0, 0.0)]
    [Arguments("before-boundary-reversed", 599.999, -3600.0, 0.0, -3600.0, false, 1199.999, 599.999)]
    public async Task C1143_BackwardClockOverlapKeepsTheCapturedDeadline(string label,
        double firstCaptured, double firstLater, double secondCaptured, double secondLater,
        bool secondWins, double finalDue, double finalUpdated)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var origin = new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc);
        DateTime At(double seconds) => origin.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        async Task<AgentTaskPark> ReadAsync(Guid id)
        {
            await using var db = Db();
            return await db.AgentTaskParks.AsNoTracking().SingleAsync(p => p.Id == id);
        }
        var park = new AgentTaskPark
        {
            Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), Attempt = 1, BlockEventId = Guid.NewGuid(),
            TaskConcurrencyToken = Guid.NewGuid(), Workspace = WorkspaceMode.Worktree,
            BlockedAt = At(-1800), State = AgentTaskParkState.Held,
            HeldFromState = AgentTaskParkState.Requested, ReasonCode = "park_dirty", Revision = 3,
            CreatedAt = At(-1800), UpdatedAt = At(-600), NextAttemptAt = At(-1)
        };
        await using (var db = Db())
        {
            db.AgentTaskParks.Add(park);
            await db.SaveChangesAsync();
        }
        // Both callers hold the same pre-write snapshot.
        var observed = await ReadAsync(park.Id);
        var first = new SteppedClock(At(firstCaptured), At(firstLater));
        var second = new SteppedClock(At(secondCaptured), At(secondLater));

        (await StampWithAsync(Db(), first, 600, observed.Id, observed.Revision, observed.NextAttemptAt))
            .ShouldBeTrue(label + " first");
        var afterFirst = await ReadAsync(park.Id);
        SameInstant(afterFirst.NextAttemptAt, At(firstCaptured).AddSeconds(600), label + " first deadline");
        SameInstant(afterFirst.UpdatedAt, At(firstCaptured), label + " first updated");

        (await StampWithAsync(Db(), second, 600, observed.Id, observed.Revision, observed.NextAttemptAt))
            .ShouldBe(secondWins, label + " second");
        var after = await ReadAsync(park.Id);
        SameInstant(after.NextAttemptAt, At(finalDue), label + " final deadline");
        SameInstant(after.UpdatedAt, At(finalUpdated), label + " final updated");
        after.State.ShouldBe(AgentTaskParkState.Held, label);
        after.HeldFromState.ShouldBe(observed.HeldFromState, label);
        after.Revision.ShouldBe(observed.Revision, label);
        after.ReasonCode.ShouldBe("park_episode_changed", label);
        // The due cutoff and the deadline come from one captured instant.
        first.Reads.ShouldBe(1, label + " first clock reads");
        second.Reads.ShouldBe(1, label + " second clock reads");
    }

    [Test]
    public async Task C1143_UnloadableSweepStatementBudget()
    {
        var counter = new FullCommandCounter();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter));
        var (row, _) = await HeldDirtyRowAsync(f, "budget");
        (await CursorAsync(f)).ShouldNotBeNull().AfterTaskId.ShouldBe(row.TaskId, "warmed cursor");
        await BreakEpisodeAsync(f, row, "task-token");
        var git = f.SourceGit;
        git.Commands.Clear();
        var conditional = f.Wire.ConditionalCommands;
        var force = f.Wire.ForceCommands;

        async Task<IReadOnlyList<string>> MeasureAsync(string label, int expected, Func<Task> operation,
            int parkUpdates, int sessionReads, int locks)
        {
            counter.Reset();
            await operation();
            var commands = counter.Commands;
            Console.WriteLine($"C1143-BUDGET {label} commands={commands.Count} expected={expected}");
            var roster = label + "\n" + counter.Roster();
            commands.Count.ShouldBe(expected, roster);
            Updates(commands, "AgentTaskParks").ShouldBe(parkUpdates, roster);
            Reads(commands, "AgentSessions").ShouldBe(sessionReads, roster);
            Locks(commands).ShouldBe(locks, roster);
            return commands;
        }
        async Task DueAsync() => AdvanceToDue(f, await ParkOfAsync(f, row.TaskId));

        await DueAsync();
        var prepare = await MeasureAsync("prepare-due", 5, () => EntryAsync(f, "prepare", (Guid?)null, row.TaskId), 1, 1, 0);
        ParkReads(prepare).ShouldBe(1, counter.Roster());
        IsSelect(prepare[0], "AgentTaskParks").ShouldBeTrue(counter.Roster());
        IsSelect(prepare[1], "AgentTasks").ShouldBeTrue(counter.Roster());
        IsSelect(prepare[2], "AgentSessions").ShouldBeTrue(counter.Roster());
        IsSelect(prepare[3], "AgentTaskEvents").ShouldBeTrue(counter.Roster());
        Updates([prepare[4]], "AgentTaskParks").ShouldBe(1, counter.Roster());
        await DueAsync();
        var capture = await MeasureAsync("capture-due", 5, () => EntryAsync(f, "capture", (Guid?)null, row.TaskId), 1, 1, 0);
        ParkReads(capture).ShouldBe(1, counter.Roster());

        await DueAsync();
        var handle = await MeasureAsync("handle-due", 13, () => f.HandleAsync(row.TaskId), 1, 1, 1);
        ParkReads(handle).ShouldBe(3, counter.Roster());
        f.Clock.Advance(TimeSpan.FromSeconds(120));
        var handleBackedOff = await MeasureAsync("handle-backed-off", 7, () => f.HandleAsync(row.TaskId), 0, 0, 1);
        ParkReads(handleBackedOff).ShouldBe(2, counter.Roster());

        await DueAsync();
        var sweep = await MeasureAsync("sweep-due", 26, () => f.ReclaimResultAsync(32, 1), 1, 1, 2);
        Updates(sweep, "BlockedTaskParkReclaimCursors").ShouldBe(1, counter.Roster());
        var total = sweep.Count;
        for (var visit = 1; visit <= 4; visit++)
        {
            f.Clock.Advance(TimeSpan.FromSeconds(120));
            var next = await MeasureAsync("sweep-backed-off-" + visit, 20, () => f.ReclaimResultAsync(32, 1), 0, 0, 2);
            Updates(next, "BlockedTaskParkReclaimCursors").ShouldBe(1, counter.Roster());
            total += next.Count;
        }
        Console.WriteLine($"C1143-BUDGET five-default-visits total={total} expected=106");
        total.ShouldBe(106);
        StatusVisits(git).ShouldBe(0);
        f.Wire.ConditionalCommands.ShouldBe(conditional);
        f.Wire.ForceCommands.ShouldBe(force);
        (await ReleaseCountAsync(f, row.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C1143_LoadableEpisodeAndOtherRefusalsKeepTheirBehaviour()
    {
        var gate = new ReclaimReservationGate();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true, reservationGate: gate);
        f.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
        await f.EditAsync((task, _) => task.Status = AgentTaskStatus.Succeeded);
        var verifier = new Antiphon.SessionRunner.RunnerWorkspaceParkService();
        f.Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: { } receipt }
            && (await verifier.VerifySessionCheckoutAsync(receipt, receipt.Request.Path, default)).Receipt is not null;
        var dirty = await SeedBlockedAsync(f, "loadable-dirty");
        var writer = await SeedBlockedAsync(f, "other-writer");
        var reserved = await SeedBlockedAsync(f, "reserved");
        var dirtyPath = await f.CreateReclaimSourceAsync(dirty.TaskId, dirty.SessionId, dirty: true);
        var writerPath = await f.CreateReclaimSourceAsync(writer.TaskId, writer.SessionId, dirty: true);
        await f.CreateReclaimSourceAsync(reserved.TaskId, reserved.SessionId);
        gate.RefuseTaskId = reserved.TaskId;

        (await f.ReclaimAsync(32, 1)).ShouldBe(3, "V-5");
        var first = await ParkOfAsync(f, dirty.TaskId);
        first.State.ShouldBe(AgentTaskParkState.Held, "V-5");
        first.ReasonCode.ShouldBe("park_dirty", "V-5");
        first.HeldFromState.ShouldBe(AgentTaskParkState.Requested, "V-5");
        SameInstant(first.NextAttemptAt, f.Now.AddSeconds(600), "V-5");
        var writerFirst = await ParkOfAsync(f, writer.TaskId);
        writerFirst.ReasonCode.ShouldBe("park_dirty", "V-5");
        var reservedFirst = await ParkOfAsync(f, reserved.TaskId);
        reservedFirst.ReasonCode.ShouldBe("park_workspace_reserved", "V-5");
        reservedFirst.NextAttemptAt.ShouldBeNull("V-5");

        // The workspace reservation keeps its immediate retry; backed-off rows stay put.
        gate.RefuseTaskId = null;
        await f.ReclaimAsync(32, 1);
        var reservedAgain = await ParkOfAsync(f, reserved.TaskId);
        reservedAgain.Revision.ShouldBeGreaterThan(reservedFirst.Revision, "V-5");
        reservedAgain.ReasonCode.ShouldNotBe("park_workspace_reserved", "V-5");
        reservedAgain.ReasonCode.ShouldNotBe("park_episode_changed", "V-5");
        Metadata(await ParkOfAsync(f, dirty.TaskId)).ShouldBe(Metadata(first), "V-5");

        var squatter = await SeedBlockedAsync(f, "squatter", status: AgentTaskStatus.Working, completed: false);
        await using (var db = f.Db())
        {
            await db.AgentTasks.Where(t => t.Id == squatter.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkingDirectory, writerPath));
        }
        AdvanceToDue(f, first);
        await f.ReclaimAsync(32, 1);
        var dueNow = f.Now;
        // A loadable episode is re-requested (PersistIntent, revision + 1, Requested in memory)
        // and its ordinary inspection holds it again from Requested (revision + 2).
        var dirtyDue = await ParkOfAsync(f, dirty.TaskId);
        dirtyDue.State.ShouldBe(AgentTaskParkState.Held, "V-5 C1135 reset");
        dirtyDue.ReasonCode.ShouldBe("park_dirty", "V-5 C1135 reset");
        dirtyDue.HeldFromState.ShouldBe(AgentTaskParkState.Requested, "V-5 C1135 reset");
        dirtyDue.Revision.ShouldBe(first.Revision + 2, "V-5 C1135 reset");
        SameInstant(dirtyDue.UpdatedAt, dueNow, "V-5 C1135 reset");
        SameInstant(dirtyDue.NextAttemptAt, dueNow.AddSeconds(600), "V-5 C1135 reset");
        var writerDue = await ParkOfAsync(f, writer.TaskId);
        writerDue.State.ShouldBe(AgentTaskParkState.Held, "V-5 other writer");
        writerDue.ReasonCode.ShouldBe("park_other_writer", "V-5 other writer");
        writerDue.Revision.ShouldBe(writerFirst.Revision, "V-5 other writer");
        SameInstant(writerDue.NextAttemptAt, dueNow.AddSeconds(600), "V-5 other writer");

        // Heal the loadable row: the original publication and conditional release gates run.
        File.Delete(Path.Combine(dirtyPath, "dirty.txt"));
        f.Wire.BySession[dirty.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(dirtyDue.CreatedAt));
        AdvanceToDue(f, dirtyDue);
        await f.ReclaimAsync(32, 1);
        var healed = await ParkOfAsync(f, dirty.TaskId);
        healed.PublicationReceiptId.ShouldNotBeNull("V-5 publication");
        healed.State.ShouldNotBe(AgentTaskParkState.Held, "V-5 publication");
        Released(f, dirty.SessionId).ShouldBeTrue("V-5 release");
        Released(f, writer.SessionId).ShouldBeFalse("V-5 other writer");
        (await ParkOfAsync(f, writer.TaskId)).ReasonCode.ShouldBe("park_other_writer", "V-5 other writer");
        foreach (var park in await ParksAsync(f))
            park.ReasonCode.ShouldNotBe("park_episode_changed", "V-5 loadable rows");
        await SessionRunningAsync(f, writer.SessionId, "V-5");
        await SessionRunningAsync(f, squatter.SessionId, "V-5");
    }

    [Test]
    public async Task C1143_MissingAndReplacedEpisodesStayFailClosed()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true);
        f.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
        await f.EditAsync((task, _) => task.Status = AgentTaskStatus.Succeeded);
        var rows = new Dictionary<string, Row>();
        foreach (var name in new[] { "gone", "unevented", "sessionless", "replaced", "moved" })
        {
            rows[name] = await SeedBlockedAsync(f, name);
            await f.CreateReclaimSourceAsync(rows[name].TaskId, rows[name].SessionId, dirty: true);
        }
        (await f.ReclaimAsync(32, 1)).ShouldBe(5, "V-6");
        var before = new Dictionary<string, AgentTaskPark>();
        foreach (var (name, row) in rows)
        {
            before[name] = await ParkOfAsync(f, row.TaskId);
            before[name].State.ShouldBe(AgentTaskParkState.Held, name);
            before[name].ReasonCode.ShouldBe("park_dirty", name);
        }

        await using (var db = f.Db())
        {
            // Parks carry no foreign key: deleting the task (its events cascade) keeps the row.
            (await db.AgentTasks.Where(t => t.Id == rows["gone"].TaskId).ExecuteDeleteAsync()).ShouldBe(1);
            (await db.AgentTaskEvents.Where(e => e.AgentTaskId == rows["unevented"].TaskId
                && e.Type == AgentTaskEventType.Blocked).ExecuteDeleteAsync()).ShouldBe(1);
            (await db.AgentSessions.Where(s => s.Id == rows["sessionless"].SessionId).ExecuteDeleteAsync()).ShouldBe(1);
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = rows["replaced"].TaskId, Type = AgentTaskEventType.Blocked,
                At = f.Now, Detail = "newer block"
            });
            var project = new Project { Id = Guid.NewGuid(), Name = "c1143-moved", CreatedAt = f.Now, UpdatedAt = f.Now,
                LocalRepositoryPath = "/tmp", GitRepositoryUrl = "https://example.test/c1143.git" };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c1143-moved", CreatedAt = f.Now, UpdatedAt = f.Now };
            db.AddRange(project, board);
            await db.SaveChangesAsync();
            var movedAgent = await db.AgentTasks.Where(t => t.Id == rows["moved"].TaskId).Select(t => t.AgentId).SingleAsync();
            (await db.Agents.Where(a => a.Id == movedAgent)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.BoardId, board.Id))).ShouldBe(1);
        }
        (await ParkCountAsync(f, rows["gone"].TaskId)).ShouldBe(1, "no cascade");

        AdvanceToDue(f, before["gone"]);
        (await f.ReclaimAsync(32, 1)).ShouldBe(4, "V-6 the deleted task is not paged");
        var dueNow = f.Now;
        Metadata(await ParkOfAsync(f, rows["gone"].TaskId)).ShouldBe(Metadata(before["gone"]), "gone");
        Metadata(await ParkOfAsync(f, rows["unevented"].TaskId)).ShouldBe(Metadata(before["unevented"]), "unevented");
        var sessionless = await ParkOfAsync(f, rows["sessionless"].TaskId);
        sessionless.State.ShouldBe(AgentTaskParkState.Held, "sessionless");
        sessionless.Revision.ShouldBe(before["sessionless"].Revision, "sessionless");
        sessionless.ReasonCode.ShouldBe("park_episode_changed", "sessionless");
        SameInstant(sessionless.NextAttemptAt, dueNow.AddSeconds(600), "sessionless");
        var replaced = (await ParksAsync(f)).Where(p => p.TaskId == rows["replaced"].TaskId).ToList();
        replaced.Count.ShouldBe(2, "replaced: a newer Blocked event is a new park key");
        Metadata(replaced.Single(p => p.Id == before["replaced"].Id)).ShouldBe(Metadata(before["replaced"]), "replaced");
        var successor = replaced.Single(p => p.Id != before["replaced"].Id);
        successor.BlockEventId.ShouldNotBe(before["replaced"].BlockEventId, "replaced");
        successor.ReasonCode.ShouldBe("park_dirty", "replaced");
        var moved = await ParkOfAsync(f, rows["moved"].TaskId);
        moved.ReasonCode.ShouldBe("park_dirty", "moved: still loadable");
        moved.Revision.ShouldBe(before["moved"].Revision + 2, "moved: still loadable");

        // Direct preparation of the orphaned Held row is a refusal, never authority.
        (await EntryAsync(f, "prepare", before["gone"].Id)).ShouldBe(("Held", "park_episode_changed"), "orphan");
        (await EntryAsync(f, "capture", before["gone"].Id)).ShouldBe(("Held", "park_episode_changed"), "orphan");
        var orphan = await ParkOfAsync(f, rows["gone"].TaskId);
        orphan.State.ShouldBe(AgentTaskParkState.Held, "orphan");
        orphan.Revision.ShouldBe(before["gone"].Revision, "orphan");
        orphan.PublicationReceiptId.ShouldBeNull("orphan");
        orphan.RunnerSeatReleaseId.ShouldBeNull("orphan");
        await using (var db = f.Db())
            (await db.RunnerSeatReleases.CountAsync()).ShouldBe(0, "V-6");
        foreach (var row in rows.Values)
            Released(f, row.SessionId).ShouldBeFalse("V-6");
        f.Wire.ForceCommands.ShouldBe(0, "V-6");
    }

    [Test]
    public async Task C1143_DisabledBusyAndUnknownKeepExistingBehaviour()
    {
        var counter = new FullCommandCounter();
        var fault = new C1143Fault();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter, fault));
        var (row, _) = await HeldDirtyRowAsync(f, "gates");
        var loadable = await ParkOfAsync(f, row.TaskId);

        // A loadable Unknown inspection keeps its outcome, reason and HoldAsync policy.
        f.SourceGit.Before = arguments => arguments.Count > 0 && arguments[0] == "status"
            ? throw new IOException("CARD-1143 injected inspection failure") : Task.CompletedTask;
        AdvanceToDue(f, loadable);
        (await EntryAsync(f, "prepare", loadable.Id)).ShouldBe(("Unknown", "park_inspection_unavailable"), "unknown");
        f.SourceGit.Before = null;
        var held = await ParkOfAsync(f, row.TaskId);
        held.State.ShouldBe(AgentTaskParkState.Held, "unknown");
        held.ReasonCode.ShouldBe("park_inspection_unavailable", "unknown");
        held.HeldFromState.ShouldBe(AgentTaskParkState.Requested, "unknown");
        held.Revision.ShouldBe(loadable.Revision + 2, "unknown");
        SameInstant(held.NextAttemptAt, f.Now.AddSeconds(600), "unknown");

        await BreakEpisodeAsync(f, row, "task-token");
        AdvanceToDue(f, held);
        var parking = f.Harness.Provider.GetRequiredService<IOptions<BlockedTaskParkingOptions>>();
        parking.Value.Enabled = false;
        foreach (var entry in new[] { "prepare", "capture" })
        {
            counter.Reset();
            (await EntryAsync(f, entry, held.Id)).ShouldBe(("Held", "park_disabled_or_busy"), "disabled " + entry);
            counter.Total.ShouldBe(0, "disabled " + entry + "\n" + counter.Roster());
        }
        counter.Reset();
        (await f.ReclaimResultAsync(32, 1)).ShouldBe(LegacyReclaimResult.None, "disabled raw");
        (await f.ReclaimScheduledAsync()).ShouldBe(LegacyReclaimResult.None, "disabled scheduled");
        using (var scope = f.Harness.Provider.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>()
                .StampUnloadedHeldAttemptAsync(held.Id, held.Revision, held.NextAttemptAt, default)).ShouldBeFalse("disabled stamp");
        counter.Total.ShouldBe(0, "disabled\n" + counter.Roster());
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "disabled");
        parking.Value.Enabled = true;

        var release = f.Harness.Provider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>();
        release.Value.AutomaticEnabled = false;
        (await f.HandleAsync(row.TaskId)).ShouldBeFalse("automatic release off");
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "automatic release off");
        release.Value.AutomaticEnabled = true;

        using (var scope = f.Harness.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var publication = scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>();
            var parks = scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                counter.Reset();
                (await publication.PrepareAsync(held.Id, default)).Reason.ShouldBe("park_disabled_or_busy", "ambient tx");
                (await publication.CaptureSourceIdentityAsync(held.Id, default)).Reason.ShouldBe("park_disabled_or_busy", "ambient tx");
                (await parks.StampUnloadedHeldAttemptAsync(held.Id, held.Revision, held.NextAttemptAt, default)).ShouldBeFalse("ambient tx");
                counter.Total.ShouldBe(0, "ambient tx\n" + counter.Roster());
                await tx.RollbackAsync();
            }
            var sentinel = NewAgent(f, "c1143-busy");
            db.Agents.Add(sentinel);
            counter.Reset();
            (await publication.PrepareAsync(held.Id, default)).Reason.ShouldBe("park_disabled_or_busy", "dirty tracker");
            (await publication.CaptureSourceIdentityAsync(held.Id, default)).Reason.ShouldBe("park_disabled_or_busy", "dirty tracker");
            (await parks.StampUnloadedHeldAttemptAsync(held.Id, held.Revision, held.NextAttemptAt, default)).ShouldBeFalse("dirty tracker");
            counter.Total.ShouldBe(0, "dirty tracker\n" + counter.Roster());
            db.Entry(sentinel).State.ShouldBe(EntityState.Added, "dirty tracker");
            db.ChangeTracker.Entries().Count().ShouldBe(1, "dirty tracker");
        }
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "busy");

        // A failed or cancelled load is not durable absence: same exception, no stamp.
        fault.Throw = sql => IsSelect(sql, "AgentSessions");
        var failed = await CaughtAsync(() => EntryAsync(f, "prepare", held.Id));
        fault.Throw = null;
        Chain(failed).OfType<InjectedDbException>().ShouldNotBeEmpty("load failure");
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "load failure");
        using (var cts = new CancellationTokenSource())
        using (var scope = f.Harness.Provider.CreateScope())
        {
            fault.Cancel = (new Func<string, bool>(sql => IsSelect(sql, "AgentSessions")), cts);
            var canceled = await CaughtAsync(() => scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>()
                .PrepareAsync(held.Id, cts.Token));
            fault.Cancel = null;
            canceled.ShouldBeAssignableTo<OperationCanceledException>("cancellation");
        }
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "cancellation");
        fault.Throw = sql => IsSelect(sql, "AgentSessions");
        (await f.ReclaimAsync(32, 1)).ShouldBe(1, "sweep load failure is caught per row");
        fault.Throw = null;
        fault.Thrown.ShouldBe(2, "load failure");
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "sweep load failure");
        Released(f, row.SessionId).ShouldBeFalse("V-7");
    }

    [Test]
    public async Task C1143_WorkingSessionAndWarningsAreUntouched()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true);
        var (row, path) = await HeldDirtyRowAsync(f, "now-working");
        var held = await ParkOfAsync(f, row.TaskId);
        await using (var db = f.Db())
        {
            (await db.AgentTasks.Where(t => t.Id == row.TaskId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, AgentTaskStatus.Working)
                .SetProperty(t => t.CompletedAt, (DateTime?)null))).ShouldBe(1);
        }
        AdvanceToDue(f, held);
        var attention = Kinds(await f.AttentionAsync());
        var custody = await CustodyAsync(f, row);
        var kills = f.RecordedStops.Killed.Count;
        var launches = f.Launches.Calls.Count;

        (await f.ReclaimResultAsync(32, 1)).Visited.ShouldBe(0, "a Working task is not paged");
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "Working excluded");

        // Direct preparation may stamp only the historical park metadata.
        (await EntryAsync(f, "prepare", held.Id)).ShouldBe(("Held", "park_episode_changed"), "Working prepare");
        var stamped = await ParkOfAsync(f, row.TaskId);
        stamped.ReasonCode.ShouldBe("park_episode_changed", "Working prepare");
        stamped.Revision.ShouldBe(held.Revision, "Working prepare");
        f.Clock.Advance(TimeSpan.FromSeconds(120));
        (await EntryAsync(f, "capture", held.Id)).ShouldBe(("Held", "park_episode_changed"), "backed off");
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(stamped), "backed off");
        AdvanceToDue(f, stamped);
        (await EntryAsync(f, "prepare", held.Id)).ShouldBe(("Held", "park_episode_changed"), "due again");
        await f.ReclaimAsync(32, 1);

        (await CustodyAsync(f, row)).ShouldBe(custody, "task, session, queue, releases, events, incidents, notes");
        Kinds(await f.AttentionAsync()).ShouldBe(attention, "no new or duplicated attention");
        f.RecordedStops.Killed.Count.ShouldBe(kills, "no stop");
        f.Wire.ForceCommands.ShouldBe(0, "no force");
        f.Wire.ConditionalCommands.ShouldBe(0, "no release");
        f.Launches.Calls.Count.ShouldBe(launches, "no new child or attempt");
        File.Exists(Path.Combine(path, "dirty.txt")).ShouldBeTrue("source retained");

        // A Blocked owner restamped this way shows the stored reason in the slot projection.
        var blocked = await SeedBlockedAsync(f, "projected");
        await f.CreateReclaimSourceAsync(blocked.TaskId, blocked.SessionId, dirty: true);
        (await f.ReclaimAsync(32, 1)).ShouldBe(1, "projection");
        var projected = await ParkOfAsync(f, blocked.TaskId);
        attention = Kinds(await f.AttentionAsync());
        await BreakEpisodeAsync(f, blocked, "task-token");
        AdvanceToDue(f, projected);
        await f.ReclaimAsync(32, 1);
        await using (var db = f.Db())
        {
            var seat = (await SeatDesktopJoin.LoadAsync(db, [blocked.SessionId], default))[blocked.SessionId];
            seat.OpenTaskId.ShouldBe(blocked.TaskId, "projection");
            seat.Park.ShouldNotBeNull("projection").ReasonCode.ShouldBe("park_episode_changed", "projection");
            seat.Park.State.ShouldBe(AgentTaskParkState.Held, "projection");
        }
        Kinds(await f.AttentionAsync()).ShouldBe(attention, "the restamp adds no attention row");
    }

    [Test]
    public async Task C1143_ReadOnlyProofReadersNeverRestamp()
    {
        var counter = new FullCommandCounter();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter));
        f.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
        await f.EditAsync((task, _) => task.Status = AgentTaskStatus.Succeeded);
        var row = await SeedBlockedAsync(f, "proof");
        await f.CreateReclaimSourceAsync(row.TaskId, row.SessionId);
        (await f.ReclaimAsync(32, 1)).ShouldBe(1, "V-9");
        var published = await ParkOfAsync(f, row.TaskId);
        published.State.ShouldBe(AgentTaskParkState.Published, "V-9");
        published.PublicationReceiptId.ShouldNotBeNull("V-9");
        TaskParkPublicationEvidence evidence;
        using (var scope = f.Harness.Provider.CreateScope())
        {
            evidence = (await scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>()
                .ReadEvidenceAsync(published.Id, default)).ShouldNotBeNull("retained evidence");
            (await scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>().PersistStateAsync(
                published.Id, published.Revision, AgentTaskParkState.Published, AgentTaskParkState.Held,
                "park_proof_hold", default)).ShouldBeTrue("V-9");
        }
        var held = await ParkOfAsync(f, row.TaskId);
        held.State.ShouldBe(AgentTaskParkState.Held, "V-9");
        held.NextAttemptAt.ShouldBeNull("a null deadline is due");
        await BreakEpisodeAsync(f, row, "task-token");
        f.SourceGit.Commands.Clear();
        var calls = f.Wire.Calls.Count;

        using (var scope = f.Harness.Provider.CreateScope())
        {
            var publication = scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>();
            counter.Reset();
            (await publication.VerifyAsync(held.Id, default)).Reason.ShouldBe("park_receipt_missing", "verify");
            (await publication.ReadEvidenceAsync(held.Id, default)).ShouldBeNull("read evidence");
            (await publication.AcceptAsync(held.Id, evidence, default)).ShouldBeFalse("accept");
            Writes(counter.Commands).ShouldBe(0, "proof readers\n" + counter.Roster());
        }
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "proof readers");

        using (var scope = f.Harness.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var publication = scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>();
            await using var tx = await db.Database.BeginTransactionAsync();
            counter.Reset();
            (await publication.ReadEvidenceAsync(held.Id, default)).ShouldBeNull("read evidence in tx");
            (await publication.VerifyAsync(held.Id, default)).Reason.ShouldBe("park_busy", "verify in tx");
            (await publication.AcceptAsync(held.Id, evidence, default)).ShouldBeFalse("accept in tx");
            Writes(counter.Commands).ShouldBe(0, "proof readers in tx\n" + counter.Roster());
            await tx.RollbackAsync();
        }
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "proof readers in tx");
        StatusVisits(f.SourceGit).ShouldBe(0, "no Git inspection");
        f.Wire.Calls.Count.ShouldBe(calls, "no runner call");
        Released(f, row.SessionId).ShouldBeFalse("V-9");
    }

    [Test]
    public async Task C1143_RestampFailureLeavesNoTrackedWrite()
    {
        var fault = new C1143Fault();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(fault));
        var (row, _) = await HeldDirtyRowAsync(f, "write-failure");
        var held = await ParkOfAsync(f, row.TaskId);
        await BreakEpisodeAsync(f, row, "task-token");
        AdvanceToDue(f, held);
        int events;
        await using (var db = f.Db())
            events = await db.AgentTaskEvents.CountAsync();
        static bool RestampUpdate(string sql) => sql.TrimStart().StartsWith("UPDATE \"AgentTaskParks\"", StringComparison.Ordinal)
            && sql.Contains("park_episode_changed", StringComparison.Ordinal);

        var sentinel = NewAgent(f, "c1143-sentinel");
        using (var scope = f.Harness.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            fault.Throw = RestampUpdate;
            var failed = await CaughtAsync(() => scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>()
                .PrepareAsync(held.Id, default));
            fault.Throw = null;
            fault.Thrown.ShouldBe(1, "the restamp UPDATE was attempted");
            Chain(failed).OfType<InjectedDbException>().ShouldNotBeEmpty("the existing exception path");
            db.ChangeTracker.Entries<AgentTaskPark>().Count(e => e.State != EntityState.Unchanged && e.State != EntityState.Detached)
                .ShouldBe(0, "no tracked park write");
            db.Agents.Add(sentinel);
            await db.SaveChangesAsync();
        }
        await using (var db = f.Db())
        {
            (await db.Agents.AnyAsync(a => a.Id == sentinel.Id)).ShouldBeTrue("unrelated save");
            (await db.AgentTaskEvents.CountAsync()).ShouldBe(events, "no warning row");
            (await db.RunnerSeatReleases.CountAsync(r => r.TaskId == row.TaskId)).ShouldBe(0, "no release");
        }
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "failed restamp");

        fault.Throw = RestampUpdate;
        (await f.ReclaimAsync(32, 1)).ShouldBe(1, "the sweep keeps its per-row failure handling");
        fault.Throw = null;
        fault.Thrown.ShouldBe(2, "the sweep attempted the restamp");
        Metadata(await ParkOfAsync(f, row.TaskId)).ShouldBe(Metadata(held), "failed sweep restamp");
        Released(f, row.SessionId).ShouldBeFalse("V-10");

        await f.ReclaimAsync(32, 1);
        var stamped = await ParkOfAsync(f, row.TaskId);
        stamped.ReasonCode.ShouldBe("park_episode_changed", "recovered");
        stamped.Revision.ShouldBe(held.Revision, "recovered");
        SameInstant(stamped.NextAttemptAt, f.Now.AddSeconds(600), "recovered");
    }

    // A real dirty row reclaimed once: Held park_dirty with the configured backoff, cursor on it.
    private static async Task<(Row Row, string Path)> HeldDirtyRowAsync(RunnerSeatReleaseFixture f, string name)
    {
        f.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
        await f.EditAsync((task, _) => task.Status = AgentTaskStatus.Succeeded);
        var row = await SeedBlockedAsync(f, name);
        var path = await f.CreateReclaimSourceAsync(row.TaskId, row.SessionId, dirty: true);
        (await f.ReclaimAsync(32, 1)).ShouldBe(1, name);
        var park = await ParkOfAsync(f, row.TaskId);
        park.State.ShouldBe(AgentTaskParkState.Held, name);
        park.ReasonCode.ShouldBe("park_dirty", name);
        SameInstant(park.NextAttemptAt, f.Now.AddSeconds(600), name);
        return (row, path);
    }

    // Changes exactly one bound episode fact; attempt, Blocked event and park key stay.
    private static async Task BreakEpisodeAsync(RunnerSeatReleaseFixture f, Row row, string fact)
    {
        await using var db = f.Db();
        var tasks = db.AgentTasks.Where(t => t.Id == row.TaskId);
        var sessions = db.AgentSessions.Where(s => s.Id == row.SessionId);
        var changed = fact switch
        {
            "task-token" => await tasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.ConcurrencyToken, Guid.NewGuid())),
            "runner-store" => await sessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RunnerStoreId, Guid.NewGuid())),
            "accepted-start" => await sessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedAt, x => x.StartedAt.AddSeconds(-1))),
            "handoff-digest" => await tasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.Result, "rewritten report")),
            "baseline-missing" => await tasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.ProgressBaselineJson, (string?)null)),
            _ => throw new ArgumentOutOfRangeException(nameof(fact), fact, null)
        };
        changed.ShouldBe(1, fact);
    }

    private static Task<(string, string)> EntryAsync(RunnerSeatReleaseFixture f, string entry, Guid parkId) =>
        EntryAsync(f, entry, parkId, Guid.Empty);

    private static async Task<(string, string)> EntryAsync(RunnerSeatReleaseFixture f, string entry, Guid? parkId, Guid taskId)
    {
        var id = parkId ?? (await ParkOfAsync(f, taskId)).Id;
        using var scope = f.Harness.Provider.CreateScope();
        var publication = scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>();
        if (entry == "capture")
        {
            var captured = await publication.CaptureSourceIdentityAsync(id, default);
            return (captured.Outcome.ToString(), captured.Reason);
        }
        var prepared = await publication.PrepareAsync(id, default);
        return (prepared.Outcome.ToString(), prepared.Reason);
    }

    private static async Task<Guid> RegisterAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        await using var db = f.Db();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var block = await db.AgentTaskEvents.Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).Select(e => e.Id).FirstAsync();
        using var scope = f.Harness.Provider.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>()
            .RegisterAsync(taskId, task.Attempt, block, task.ConcurrencyToken, default)).ShouldNotBeNull();
    }

    private static async Task<bool> StampWithAsync(AppDbContext db, TimeProvider clock, int backoff,
        Guid parkId, long revision, DateTime? observedDue)
    {
        await using (db)
        {
            return await new BlockedTaskParkingService(db, clock, Options.Create(new BlockedTaskParkingOptions
            {
                Enabled = true, ReclaimHeldBackoffSeconds = backoff
            })).StampUnloadedHeldAttemptAsync(parkId, revision, observedDue, default);
        }
    }

    private static void AdvanceToDue(RunnerSeatReleaseFixture f, AgentTaskPark park)
    {
        var due = park.NextAttemptAt.ShouldNotBeNull("a Held row with a positive backoff has a deadline");
        AdvanceTo(f, due, TimeSpan.Zero);
    }

    private static async Task<int> ReleaseCountAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        await using var db = f.Db();
        return await db.RunnerSeatReleases.CountAsync(r => r.TaskId == taskId);
    }

    private static (AgentTaskParkState, AgentTaskParkState?, long, string, DateTime?, DateTime) Metadata(AgentTaskPark p) =>
        (p.State, p.HeldFromState, p.Revision, p.ReasonCode, p.NextAttemptAt, p.UpdatedAt);

    // A read whose primary FROM is the table; a subquery inside another table's read does not count.
    private static bool IsSelect(string sql, string table)
    {
        var text = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var from = text.IndexOf(" FROM \"", StringComparison.Ordinal);
        return text.StartsWith("SELECT", StringComparison.Ordinal) && !text.Contains("FOR UPDATE", StringComparison.Ordinal)
            && from >= 0 && text.AsSpan(from).StartsWith($" FROM \"{table}\"", StringComparison.Ordinal);
    }

    private static int Reads(IEnumerable<string> commands, string table) => commands.Count(c => IsSelect(c, table));

    private static int ParkReads(IEnumerable<string> commands) => Reads(commands, "AgentTaskParks");

    private static int Updates(IEnumerable<string> commands, string table) =>
        commands.Count(c => c.TrimStart().StartsWith($"UPDATE \"{table}\"", StringComparison.Ordinal));

    private static int Locks(IEnumerable<string> commands) =>
        commands.Count(c => c.Contains("FOR UPDATE", StringComparison.Ordinal));

    private static int Writes(IEnumerable<string> commands) => commands.Count(c =>
    {
        var text = c.TrimStart();
        return text.StartsWith("UPDATE", StringComparison.Ordinal) || text.StartsWith("INSERT", StringComparison.Ordinal)
            || text.StartsWith("DELETE", StringComparison.Ordinal);
    });

    private static Agent NewAgent(RunnerSeatReleaseFixture f, string name)
    {
        var slug = name + "-" + Guid.NewGuid().ToString("N")[..8];
        return new Agent
        {
            Id = Guid.NewGuid(), Name = slug, Slug = slug, WorkingDirectory = "/tmp",
            CreatedAt = f.Now, UpdatedAt = f.Now, IsPoolDelegate = true, AlwaysOn = false
        };
    }

    private static async Task<Exception?> CaughtAsync(Func<Task> work)
    {
        try { await work(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static IEnumerable<Exception> Chain(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            yield return ex;
    }

    private static string[] Kinds(AttentionDto attention) =>
        attention.Items.Select(i => i.Kind.ToString()).OrderBy(k => k, StringComparer.Ordinal).ToArray();

    // Task, session, queue, release and event custody as stored, for byte-equivalence checks.
    private static async Task<string> CustodyAsync(RunnerSeatReleaseFixture f, Row row)
    {
        await using var db = f.Db();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        };
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.TaskId),
            Session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == row.SessionId),
            Queue = await db.SessionQueuedMessages.AsNoTracking().OrderBy(m => m.Id).Select(m => m.Id).ToListAsync(),
            Releases = await db.RunnerSeatReleases.AsNoTracking().OrderBy(r => r.Id).ToListAsync(),
            Tasks = await db.AgentTasks.CountAsync(),
            Events = await db.AgentTaskEvents.CountAsync(),
            Incidents = await db.AgentIncidents.CountAsync(),
            Notes = await db.AgentTaskLandNotifications.CountAsync()
        }, options);
    }

    // Returns the captured instant on the first read and the moved clock on every later read.
    private sealed class SteppedClock(DateTime captured, DateTime later) : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow() => new(Reads++ == 0 ? captured : later, TimeSpan.Zero);
    }

    private sealed class InjectedDbException() : DbException("CARD-1143 injected command failure");

    /// <summary>Throws or cancels just before a matching command executes.</summary>
    private sealed class C1143Fault : DbCommandInterceptor
    {
        public Func<string, bool>? Throw { get; set; }
        public (Func<string, bool> When, CancellationTokenSource Source)? Cancel { get; set; }
        public int Thrown { get; private set; }

        private void Before(DbCommand command)
        {
            if (Throw is { } fail && fail(command.CommandText))
            {
                Thrown++;
                throw new InjectedDbException();
            }
            if (Cancel is { } cancel && cancel.When(command.CommandText))
            {
                Cancel = null;
                cancel.Source.Cancel();
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Before(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Before(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Before(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// After the intent UPDATE commits, runs one injected change just before the next park
    /// SELECT outside a transaction: Prepare's reload of the captured episode.
    /// </summary>
    private sealed class AfterIntentCut : DbCommandInterceptor
    {
        private Func<Task>? _change;
        private bool _intent;
        public bool Fired { get; private set; }

        public void Arm(Func<Task> change)
        {
            _change = change;
            _intent = false;
            Fired = false;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_change is not null && command.CommandText.TrimStart().StartsWith("UPDATE \"AgentTaskParks\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"RepositoryIdentity\"", StringComparison.Ordinal))
                _intent = true;
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_change is { } change && _intent && command.Transaction is null
                && IsSelect(command.CommandText, "AgentTaskParks"))
            {
                _change = null;
                Fired = true;
                await change();
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
