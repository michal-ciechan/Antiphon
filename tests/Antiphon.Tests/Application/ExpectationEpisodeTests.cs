using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0650 V-3 episodes: dedupe, cooldown, bypass, reminder, resolution and the concurrent
/// sweep race, on real PostgreSQL. The service is recreated for every scan.
/// </summary>
[Category("Integration")]
public sealed class ExpectationEpisodeTests
{
    [Test]
    public async Task C650_Repeated_sweeps_and_reason_flips_emit_one_nudge()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var taskId = Guid.NewGuid();
        var stopped = Guid.NewGuid();
        var dispatched = now.AddMinutes(-20);
        await using (var db = world.Db())
        {
            db.AgentSessions.Add(ExpectationTestWorld.Session(stopped, null, dispatched, SessionStatus.Stopped));
            db.AgentTasks.Add(world.Task(taskId, AgentTaskStatus.Dispatched, dispatched));
            await db.SaveChangesAsync();
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(1);

        // Reason flips from missing to terminal session: evidence update, same subject and episode.
        await using (var db = world.Db())
        {
            var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId);
            task.AgentSessionId = stopped;
            await db.SaveChangesAsync();
        }

        foreach (var minutes in new[] { 1, 2, 11, 29 })
        {
            clock.SetUtcNow(new DateTimeOffset(now.AddMinutes(minutes), TimeSpan.Zero));
            var scan = await Scan(world, clock);
            scan.Evaluation.SilentInFlight.ShouldHaveSingleItem().ReasonCode.ShouldBe("terminal-session");
            scan.NudgesCommitted.ShouldBe(0);
        }

        await using (var read = world.Db())
        {
            (await read.ExpectationNudges.CountAsync()).ShouldBe(1);
            var episode = await read.ExpectationEpisodes.AsNoTracking().SingleAsync();
            episode.ResolvedAt.ShouldBeNull();
            episode.FirstObservedAt.ShouldBe(now);
            episode.Evidence.ShouldContain("terminal-session");
            (await read.AgentTaskEvents.CountAsync(row => row.Type == AgentTaskEventType.Check)).ShouldBe(1);
        }

        // Settled: one clear scan is not enough; the second, a minute later, resolves.
        await using (var db = world.Db())
        {
            var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId);
            task.Status = AgentTaskStatus.Succeeded;
            task.Result = "done";
            await db.SaveChangesAsync();
        }

        clock.SetUtcNow(new DateTimeOffset(now.AddMinutes(30), TimeSpan.Zero));
        await Scan(world, clock);
        await using (var read = world.Db())
            (await read.ExpectationEpisodes.SingleAsync()).ResolvedAt.ShouldBeNull();
        clock.SetUtcNow(new DateTimeOffset(now.AddMinutes(31), TimeSpan.Zero));
        await Scan(world, clock);
        await using (var read = world.Db())
            (await read.ExpectationEpisodes.SingleAsync()).ResolvedAt.ShouldBe(now.AddMinutes(31));

        // A new dispatch stint is a new subject and a new episode, not the resolved one reopened.
        var redispatched = now.AddMinutes(20);
        await using (var db = world.Db())
        {
            var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId);
            task.Status = AgentTaskStatus.Dispatched;
            task.Result = null;
            task.AgentSessionId = null;
            task.DispatchedAt = redispatched;
            await db.SaveChangesAsync();
        }

        clock.SetUtcNow(new DateTimeOffset(now.AddMinutes(45), TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(1);
        await using (var read = world.Db())
        {
            var episodes = await read.ExpectationEpisodes.AsNoTracking().OrderBy(row => row.FirstObservedAt).ToListAsync();
            episodes.Count.ShouldBe(2);
            episodes[1].SubjectKey.ShouldBe(ExpectationSubjects.Silent(world.Directive.Id, taskId, redispatched));
            episodes[1].ResolvedAt.ShouldBeNull();
            episodes[1].FirstObservedAt.ShouldBe(now.AddMinutes(45));
        }
    }

    [Test]
    public async Task C650_Concurrent_sweeps_commit_one_audit_and_send_claim()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(first, AgentTaskStatus.Dispatched, now.AddMinutes(-30)));
            db.AgentTasks.Add(world.Task(second, AgentTaskStatus.Dispatched, now.AddMinutes(-25)));
            await db.SaveChangesAsync();
        }

        // Both sweeps decide to nudge, then meet before either commits.
        var rendezvous = new Rendezvous(2);
        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        await using var left = world.Db();
        await using var right = world.Db();
        var scans = await Task.WhenAll(
            Task.Run(() => world.Service(left, clock, rendezvous).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None)),
            Task.Run(() => world.Service(right, clock, rendezvous).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None)));

        rendezvous.Arrived.ShouldBe(2);
        scans.Sum(scan => scan.NudgesCommitted).ShouldBe(1);
        await using var read = world.Db();
        var nudge = await read.ExpectationNudges.AsNoTracking().SingleAsync();
        nudge.AttemptState.ShouldBe(ExpectationAttemptState.None);
        nudge.Ordinal.ShouldBe(1);
        JsonSerializer.Deserialize<List<Guid>>(nudge.EpisodeIdsJson)!.Count.ShouldBe(2);
        (await read.CardComments.CountAsync(row => row.Author == ExpectationLedger.AuditAuthor)).ShouldBe(1);
        var checks = await read.AgentTaskEvents.AsNoTracking()
            .Where(row => row.Type == AgentTaskEventType.Check)
            .Select(row => row.AgentTaskId)
            .ToListAsync();
        checks.OrderBy(id => id).ShouldBe(new[] { first, second }.OrderBy(id => id).ToArray());
        (await read.ExpectationEpisodes.CountAsync(row => row.ResolvedAt == null)).ShouldBe(2);
    }

    [Test]
    public async Task C650_Cooldown_recurrence_and_config_change_keep_correct_clocks()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString, localTarget: 1, remoteTarget: 1);
        var t0 = world.Now;
        var localSubject = ExpectationSubjects.Capacity(world.Directive.Id, null);
        var remoteSubject = ExpectationSubjects.Capacity(world.Directive.Id, "server2");
        await using (var db = world.Db())
        {
            db.Cards.Add(world.BacklogCard(Guid.NewGuid(), "C650R"));
            await db.SaveChangesAsync();
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(0);
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(5), TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(0);
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(10).AddSeconds(-1), TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(0);

        // Continuous deficit since t0 is due at exactly ten minutes.
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(10), TimeSpan.Zero));
        var due = await Scan(world, clock);
        due.NudgesCommitted.ShouldBe(1);
        due.NudgedSubjectKeys!.OrderBy(key => key, StringComparer.Ordinal)
            .ShouldBe(new[] { localSubject, remoteSubject }.OrderBy(key => key, StringComparer.Ordinal).ToArray());

        // A new ordinary condition inside the cooldown waits. Its runner has no target, so it
        // does not fill a lane and end the capacity episodes.
        var silent = Guid.NewGuid();
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(silent, AgentTaskStatus.Dispatched, t0, runnerId: "server3"));
            await db.SaveChangesAsync();
        }

        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(12), TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(0);

        // A new all-path repository fence bypasses the cooldown once and batches the waiting task.
        var fenced = await AddFencedAsync(world, "/src/antiphon", t0.AddMinutes(13), 2);
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(13), TimeSpan.Zero));
        var bypass = await Scan(world, clock);
        bypass.NudgesCommitted.ShouldBe(1);
        bypass.NudgedSubjectKeys!.ShouldContain(ExpectationSubjects.Fence(world.Directive.Id, "repo:/src/antiphon"));
        bypass.NudgedSubjectKeys!.ShouldContain(ExpectationSubjects.Silent(world.Directive.Id, silent, t0));
        bypass.NudgedSubjectKeys!.ShouldNotContain(localSubject);

        // A second fresh fence cannot bypass again inside the cooldown the bypass started.
        await using (var db = world.Db())
        {
            foreach (var task in await db.AgentTasks.Where(row => fenced.Contains(row.Id)).ToListAsync())
                task.Status = AgentTaskStatus.Canceled;
            await db.SaveChangesAsync();
        }

        var other = await AddFencedAsync(world, "/src/other", t0.AddMinutes(14), 1);
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(15), TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(0);
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(23), TimeSpan.Zero));
        var afterCooldown = await Scan(world, clock);
        afterCooldown.NudgesCommitted.ShouldBe(1);
        afterCooldown.NudgedSubjectKeys!.ShouldBe([ExpectationSubjects.Fence(world.Directive.Id, "repo:/src/other")]);
        await using (var db = world.Db())
        {
            foreach (var task in await db.AgentTasks.Where(row => other.Contains(row.Id)).ToListAsync())
                task.Status = AgentTaskStatus.Canceled;
            await db.SaveChangesAsync();
        }

        // Thirty minutes after the capacity nudge, the unresolved deficit is reminded once.
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(39), TimeSpan.Zero));
        (await Scan(world, clock)).NudgedSubjectKeys.ShouldBeNull();
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(40), TimeSpan.Zero));
        var reminder = await Scan(world, clock);
        reminder.NudgesCommitted.ShouldBe(1);
        reminder.NudgedSubjectKeys!.ShouldContain(localSubject);
        reminder.NudgedSubjectKeys!.ShouldContain(remoteSubject);

        await using (var read = world.Db())
        {
            var capacity = await read.ExpectationEpisodes.AsNoTracking()
                .Where(row => row.Kind == ExpectationEpisodeKind.CapacityDeficit)
                .ToListAsync();
            capacity.Count.ShouldBe(2);
            capacity.ShouldAllBe(row => row.FirstObservedAt == t0 && row.ResolvedAt == null);
            (await read.ExpectationWatchStates.SingleAsync()).NextNudgeAt.ShouldBe(t0.AddMinutes(50));
        }

        // A semantic config edit retires old episodes and starts the capacity clock afresh.
        var changed = Clone(world.Directive);
        changed.Targets[0].InFlightTarget = 2;
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(41), TimeSpan.Zero));
        (await ScanWith(world, changed, clock)).NudgesCommitted.ShouldBe(0);
        await using (var read = world.Db())
        {
            var episodes = await read.ExpectationEpisodes.AsNoTracking().ToListAsync();
            episodes.Where(row => row.ConfigDigest == world.Digest).ShouldAllBe(row => row.ResolvedAt != null);
            episodes.Where(row => row.ConfigDigest == world.Digest && row.Kind == ExpectationEpisodeKind.CapacityDeficit)
                .ShouldAllBe(row => row.ResolvedAt == t0.AddMinutes(41));
            var fresh = episodes.Where(row => row.ConfigDigest == ExpectationDirectiveDigest.Compute(changed)
                && row.Kind == ExpectationEpisodeKind.CapacityDeficit).ToList();
            fresh.Count.ShouldBe(2);
            fresh.ShouldAllBe(row => row.FirstObservedAt == t0.AddMinutes(41) && row.ResolvedAt == null);
        }
    }

    [Test]
    public async Task C650_Fence_batches_queue_and_capacity_subjects()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString, localTarget: 1, remoteTarget: 1);
        var now = world.Now;
        await using (var db = world.Db())
        {
            db.Cards.Add(world.BacklogCard(Guid.NewGuid(), "C650B"));
            await db.SaveChangesAsync();
        }

        var queued = await AddFencedAsync(world, "/src/antiphon", now.AddMinutes(-15), 4);
        var localSubject = ExpectationSubjects.Capacity(world.Directive.Id, null);
        await using (var db = world.Db())
        {
            var clockAtOpen = new FakeTimeProvider(new DateTimeOffset(now.AddMinutes(-10), TimeSpan.Zero));
            await new ExpectationLedger(db, clockAtOpen, new ExpectationTestWorld.QuietBus()).OpenEpisodeAsync(
                new ExpectationEpisodeOpen(
                    world.Directive.Id,
                    world.Digest,
                    ExpectationEpisodeKind.CapacityDeficit,
                    localSubject,
                    "deficit observed",
                    now.AddMinutes(-10)),
                CancellationToken.None);
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        var scan = await Scan(world, clock);
        scan.NudgesCommitted.ShouldBe(1);
        var fenceSubject = ExpectationSubjects.Fence(world.Directive.Id, "repo:/src/antiphon");
        var pipelineSubject = ExpectationSubjects.Pipeline(world.Directive.Id, "repo:/src/antiphon");
        scan.NudgedSubjectKeys!.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(
            new[] { fenceSubject, pipelineSubject, localSubject }.OrderBy(key => key, StringComparer.Ordinal).ToArray());

        await using var read = world.Db();
        var nudge = await read.ExpectationNudges.AsNoTracking().SingleAsync();
        JsonSerializer.Deserialize<List<Guid>>(nudge.EpisodeIdsJson)!.Count.ShouldBe(3);
        var lines = nudge.Body.Split('\n');
        var fenceLine = Array.FindIndex(lines, line => line.StartsWith("- DispatchFence", StringComparison.Ordinal));
        fenceLine.ShouldBe(1);
        lines.ShouldContain(line => line.StartsWith("- StalledPipeline stalled-pipeline (explained by the fence above)", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("- CapacityDeficit capacity-deficit (explained by the fence above)", StringComparison.Ordinal));
        nudge.Body.ShouldContain("[expectation-ack:" + nudge.Id.ToString("D") + "]");
        nudge.Body.Length.ShouldBeLessThanOrEqualTo(ExpectationPromptFormatter.DefaultCeilingChars);
        // At most three examples in the prompt; every task in the audit.
        queued.Count(id => lines[fenceLine].Contains(id.ToString("D"))).ShouldBe(3);
        var comment = await read.CardComments.AsNoTracking().SingleAsync(row => row.Id == nudge.AuditCommentId);
        comment.CardId.ShouldBe(world.CardId);
        foreach (var id in queued)
            comment.Body.ShouldContain(id.ToString("D"));
        foreach (var subject in new[] { fenceSubject, pipelineSubject, localSubject })
            comment.Body.ShouldContain(subject);
        var checks = await read.AgentTaskEvents.AsNoTracking()
            .Where(row => row.Type == AgentTaskEventType.Check)
            .Select(row => row.AgentTaskId)
            .ToListAsync();
        checks.OrderBy(id => id).ShouldBe(queued.OrderBy(id => id).ToArray());
    }

    [Test]
    public async Task C650_Unknown_scan_preserves_episode_and_fair_cursor()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        var t0 = world.Now;
        var taskId = Guid.NewGuid();
        await using (var db = world.Db())
        {
            db.AgentTasks.Add(world.Task(taskId, AgentTaskStatus.Dispatched, t0.AddMinutes(-20)));
            await db.SaveChangesAsync();
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(t0, TimeSpan.Zero));
        (await Scan(world, clock)).NudgesCommitted.ShouldBe(1);
        await using (var db = world.Db())
        {
            var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId);
            task.Status = AgentTaskStatus.Succeeded;
            task.Result = "done";
            await db.SaveChangesAsync();
        }

        var unavailable = new ExpectationProbeInput(
            null, [], new Dictionary<string, ExpectationRunnerProbe>(StringComparer.Ordinal), true, "registry timeout");
        foreach (var minutes in new[] { 2, 4 })
        {
            clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(minutes), TimeSpan.Zero));
            await using var db = world.Db();
            var scan = await world.Service(db, clock).ScanAsync(world.Directive, unavailable, CancellationToken.None);
            scan.Evaluation.ObservationUnknown.ShouldBeTrue();
        }

        await using (var read = world.Db())
        {
            (await read.ExpectationEpisodes.SingleAsync()).ResolvedAt.ShouldBeNull();
            var state = await read.ExpectationWatchStates.SingleAsync();
            state.LastSuccessfulScanAt.ShouldBe(t0);
            state.LastObservationError.ShouldBe("registry timeout");
        }

        // Unknown scans are not clear scans: the first good scan after them is only the first.
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(5), TimeSpan.Zero));
        await Scan(world, clock);
        await using (var read = world.Db())
            (await read.ExpectationEpisodes.SingleAsync()).ResolvedAt.ShouldBeNull();

        // Fair cursor: the never-scanned directive goes first, and its failure does not starve
        // the other directive, whose scan completes and resolves on its second clear scan.
        var other = Clone(world.Directive);
        other.Id = "other";
        var order = new List<string>();
        clock.SetUtcNow(new DateTimeOffset(t0.AddMinutes(6), TimeSpan.Zero));
        IReadOnlyDictionary<string, ExpectationScanResult?> results;
        await using (var db = world.Db())
        {
            results = await world.Service(db, clock).ScanAllAsync(
                [world.Directive, other],
                directive =>
                {
                    order.Add(directive.Id);
                    if (directive.Id == "other")
                        throw new InvalidOperationException("probe failed");
                    return ExpectationProbeInput.None;
                },
                CancellationToken.None);
        }

        order.ShouldBe(["other", world.Directive.Id]);
        results["other"].ShouldBeNull();
        results[world.Directive.Id].ShouldNotBeNull();
        await using (var read = world.Db())
        {
            (await read.ExpectationEpisodes.SingleAsync()).ResolvedAt.ShouldBe(t0.AddMinutes(6));
            var states = await read.ExpectationWatchStates.AsNoTracking().ToDictionaryAsync(row => row.DirectiveId);
            states["other"].LastSuccessfulScanAt.ShouldBeNull();
            states["other"].LastObservationError.ShouldBe("scan failed: InvalidOperationException");
            states[world.Directive.Id].LastSuccessfulScanAt.ShouldBe(t0.AddMinutes(6));
        }
    }

    private static async Task<ExpectationScanResult> Scan(ExpectationTestWorld world, FakeTimeProvider clock) =>
        await ScanWith(world, world.Directive, clock);

    private static async Task<ExpectationScanResult> ScanWith(
        ExpectationTestWorld world, ExpectationDirectiveSettings directive, FakeTimeProvider clock)
    {
        await using var db = world.Db();
        return await world.Service(db, clock).ScanAsync(directive, ExpectationProbeInput.None, CancellationToken.None);
    }

    private static async Task<List<Guid>> AddFencedAsync(ExpectationTestWorld world, string repo, DateTime createdAt, int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        await using var db = world.Db();
        foreach (var id in ids)
        {
            var task = world.Task(id, AgentTaskStatus.Queued, null, createdAt: createdAt);
            task.RepoPath = repo;
            db.AgentTasks.Add(task);
            db.AgentTaskEvents.Add(ExpectationTestWorld.Event(id, AgentTaskEventType.Created, createdAt, "created"));
            db.AgentTaskEvents.Add(ExpectationTestWorld.Event(
                id, AgentTaskEventType.Held, createdAt, DispatchHoldDetails.LeaseFenced("dead child journal")));
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private static ExpectationDirectiveSettings Clone(ExpectationDirectiveSettings directive) =>
        JsonSerializer.Deserialize<ExpectationDirectiveSettings>(JsonSerializer.Serialize(directive))!;

    private sealed class Rendezvous(int parties) : IExpectationCatchUp
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public int Arrived => Volatile.Read(ref _arrived);

        public async Task CatchUpAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) >= parties)
                _all.TrySetResult();
            await _all.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
    }
}
