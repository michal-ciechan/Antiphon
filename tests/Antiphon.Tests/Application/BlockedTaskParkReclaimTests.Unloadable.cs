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
        var text = sql.TrimStart();
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
