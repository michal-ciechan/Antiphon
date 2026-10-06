using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedTaskParkReclaimTests
{
    [Test]
    public async Task C1065_LegacySweepRequiresFreshPublicationAndIdleWindow()
    {
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: true))
        {
            f.Wire.Qualified = Idle(TimeSpan.Zero, null);
            await f.EditAsync((task, _) => task.Role = AgentTaskRole.Code);
            await f.CreateSourceAsync();
            var tip = await CommitTipAsync(f);
            var ancientAt = f.Now.AddHours(-30);
            await using (var db = f.Db())
            {
                await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.CompletedAt, ancientAt));
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Blocked)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.At, ancientAt));
            }

            var shared = "/reclaim/shared-" + Guid.NewGuid().ToString("N");
            var unpushed = await SeedBlockedAsync(f, "unpushed");
            var dirty = await SeedBlockedAsync(f, "dirty");
            var working = await SeedBlockedAsync(f, "working");
            var unknown = await SeedBlockedAsync(f, "unknown");
            var missing = await SeedBlockedAsync(f, "missing", result: null);
            var ambiguous = await SeedBlockedAsync(f, "ambiguous");
            var poison = await SeedBlockedAsync(f, "poison");
            var partner = await SeedBlockedAsync(f, "partner", status: AgentTaskStatus.Working, completed: false);
            await using (var db = f.Db())
            {
                await db.AgentTasks.Where(t => t.Id == ambiguous.TaskId || t.Id == partner.TaskId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.WorktreePath, shared));
            }
            var unpushedPath = await f.CreateReclaimSourceAsync(unpushed.TaskId, unpushed.SessionId);
            var dirtyPath = await f.CreateReclaimSourceAsync(dirty.TaskId, dirty.SessionId, dirty: true);
            var workingPath = await f.CreateReclaimSourceAsync(working.TaskId, working.SessionId);
            var unknownPath = await f.CreateReclaimSourceAsync(unknown.TaskId, unknown.SessionId);
            var verifier = new RunnerWorkspaceParkService();
            f.Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: { } receipt }
                && (await verifier.VerifySessionCheckoutAsync(receipt, receipt.Request.Path, default)).Receipt is not null;

            var poisonedOnce = false;
            Func<string, CancellationToken, Task> boundary = (name, _) =>
            {
                if (name != "ReclaimList:" + poison.TaskId.ToString("D") || poisonedOnce)
                    return Task.CompletedTask;
                poisonedOnce = true;
                throw new InvalidOperationException("injected reclaim failure");
            };

            var visited = await f.ReclaimAsync(2, 3, boundary);
            visited.ShouldBe(6, "G-174");
            var cursor = await CursorAsync(f);
            cursor.ShouldNotBeNull("G-174");
            var marked = cursor.AfterTaskId.ShouldNotBeNull("G-174");
            await f.RestartAsync();
            var restored = await CursorAsync(f);
            restored.ShouldNotBeNull("G-174");
            restored.AfterTaskId.ShouldBe(marked, "G-174");

            var detail = await DetailAsync(f, f.TaskId);
            detail.Summary.Status.ShouldBe(AgentTaskStatus.Blocked, "V-23");
            (await f.AttentionAsync()).ShouldNotBeNull("V-23");
            var slots = await SlotsAsync(f);
            slots.RunnerId.ShouldBe(PhoneHomeRunnerDirectory.LocalRunnerId, "V-23");
            await using (var db = f.Db())
            {
                (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running, "V-23");
                (await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId)).Status.ShouldBe(AgentTaskStatus.Blocked, "V-23");
            }
            (await DetailAsync(f, f.TaskId)).Summary.Status.ShouldBe(AgentTaskStatus.Blocked, "V-23");

            for (var pass = 0; pass < 8 && await ParkCountAsync(f, poison.TaskId) == 0; pass++)
                await f.ReclaimAsync(32, 1, boundary);
            poisonedOnce.ShouldBeTrue("G-174");
            foreach (var id in new[] { f.TaskId, unpushed.TaskId, dirty.TaskId, working.TaskId, unknown.TaskId,
                missing.TaskId, ambiguous.TaskId, poison.TaskId })
                (await ParkCountAsync(f, id)).ShouldBe(1, "G-174");
            (await ParkOfAsync(f, f.TaskId)).LegacyDiscovery.ShouldBeTrue("V-23");

            var ancientPark = await ParkOfAsync(f, f.TaskId);
            f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromHours(30), AsUtc(ancientAt));
            await f.ReclaimAsync(32, 1);
            Released(f, f.SessionId).ShouldBeFalse("G-171");
            await SessionRunningAsync(f, f.SessionId, "G-171");

            var unpushedPark = await ParkOfAsync(f, unpushed.TaskId);
            var windowAnchor = ancientPark.CreatedAt >= unpushedPark.CreatedAt
                ? ancientPark.CreatedAt : unpushedPark.CreatedAt;
            AdvanceTo(f, windowAnchor, TimeSpan.FromMilliseconds(119_999));
            f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(ancientPark.CreatedAt));
            f.Wire.BySession[unpushed.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(unpushedPark.CreatedAt));
            await f.ReclaimAsync(32, 1);
            Released(f, f.SessionId).ShouldBeFalse("G-171");
            Released(f, unpushed.SessionId).ShouldBeFalse("G-171");
            await SessionRunningAsync(f, f.SessionId, "G-171");

            AdvanceTo(f, windowAnchor, TimeSpan.FromMilliseconds(120_001));
            f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(ancientPark.CreatedAt));
            f.Wire.BySession[unpushed.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(unpushedPark.CreatedAt));
            f.Wire.BySession[dirty.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc((await ParkOfAsync(f, dirty.TaskId)).CreatedAt));
            f.Wire.BySession[unknown.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc((await ParkOfAsync(f, unknown.TaskId)).CreatedAt),
                TerminalSeatQualificationStatus.Unknown);
            f.Wire.BySession[working.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc((await ParkOfAsync(f, working.TaskId)).CreatedAt),
                TerminalSeatQualificationStatus.Working);
            f.Wire.BySession[missing.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc((await ParkOfAsync(f, missing.TaskId)).CreatedAt));
            f.Wire.BySession[ambiguous.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc((await ParkOfAsync(f, ambiguous.TaskId)).CreatedAt));
            f.Wire.BySession[poison.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc((await ParkOfAsync(f, poison.TaskId)).CreatedAt));
            var releasing = await f.ReclaimResultAsync(32, 1);
            await using (var ledger = f.Db())
            {
                var confirmed = (await ledger.RunnerSeatReleases.AsNoTracking().ToListAsync())
                    .Count(TerminalRunnerSeatReleaseService.IsConfirmed);
                releasing.Released.ShouldBe(confirmed, "G-7");
                releasing.Released.ShouldBe(2, "V-23");
            }

            f.Wire.Requests.Count(r => r.ParkVersion == 2).ShouldBe(2, "V-23");
            Released(f, f.SessionId).ShouldBeTrue("V-23");
            Released(f, unpushed.SessionId).ShouldBeTrue("V-23");
            Released(f, dirty.SessionId).ShouldBeFalse("G-172");
            Released(f, unknown.SessionId).ShouldBeFalse("G-172");
            Released(f, working.SessionId).ShouldBeFalse("V-23");
            Released(f, missing.SessionId).ShouldBeFalse("G-173");
            Released(f, ambiguous.SessionId).ShouldBeFalse("V-23");
            Released(f, poison.SessionId).ShouldBeFalse("V-23");
            f.Wire.ForceCommands.ShouldBe(0, "V-23");
            (await ParkOfAsync(f, dirty.TaskId)).ReasonCode.ShouldBe("park_dirty", "G-172");
            (await ParkOfAsync(f, missing.TaskId)).ReasonCode.ShouldBe("park_binding_missing", "G-173");
            (await ParkOfAsync(f, ambiguous.TaskId)).ReasonCode.ShouldBe("park_ownership_ambiguous", "V-23");
            await SessionRunningAsync(f, missing.SessionId, "G-173");
            await SessionRunningAsync(f, working.SessionId, "V-23");
            await SessionRunningAsync(f, unknown.SessionId, "G-172");
            Directory.Exists(dirtyPath).ShouldBeTrue("V-23");
            Directory.Exists(workingPath).ShouldBeTrue("V-23");
            Directory.Exists(unknownPath).ShouldBeTrue("V-23");
            Directory.Exists(unpushedPath).ShouldBeTrue("V-23");

            var parked = await f.ParkAsync();
            parked.State.ShouldBe(AgentTaskParkState.Parked, "V-23");
            parked.SourceSha.ShouldBe(tip, "V-23");
            var fullRef = parked.FullRef.ShouldNotBeNull("V-23");
            await StampAsync(f);
            await DriveAsync(f, f.AnswerAsync("continue from the parked tip"));
            Directory.Delete(f.SourcePath, recursive: true);
            await f.DispatchAsync();
            Directory.Exists(f.SourcePath).ShouldBeTrue("V-23");
            (await f.GitAsync(f.SourcePath, "rev-parse", "HEAD")).ShouldBe(tip, "V-23");
            (await f.GitAsync(f.SourcePath, "symbolic-ref", "-q", "HEAD")).ShouldBe(fullRef, "V-23");
            (await f.TaskAsync()).AgentId.ShouldBe(f.AgentId, "V-23");
            f.Launches.Calls.Count.ShouldBe(1, "V-23");
            Directory.Exists(dirtyPath).ShouldBeTrue("V-23");
            Directory.Exists(unpushedPath).ShouldBeTrue("V-23");
        }

        await using (var job = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: true))
        {
            job.Wire.Qualified = Idle(TimeSpan.Zero, null);
            await job.JobAsync();
            var parks = await ParksAsync(job);
            parks.Count.ShouldBe(1, "V-23");
            parks.Single().LegacyDiscovery.ShouldBeTrue("V-23");
            job.Wire.ConditionalCommands.ShouldBe(0, "V-23");
        }

        await using (var sweep = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: true))
        {
            sweep.Wire.Qualified = Idle(TimeSpan.Zero, null);
            await sweep.SweepAsync();
            var parks = await ParksAsync(sweep);
            parks.Count.ShouldBe(1, "V-23");
            parks.Single().LegacyDiscovery.ShouldBeTrue("V-23");
            sweep.Wire.ConditionalCommands.ShouldBe(0, "V-23");
        }

        await using (var closed = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: false))
        {
            (await closed.ReclaimAsync()).ShouldBe(0, "legacy discovery requires ReclaimExisting");
            await closed.JobAsync();
            await closed.SweepAsync();
            (await ParksAsync(closed)).Count.ShouldBe(0, "legacy discovery requires ReclaimExisting");
        }
    }

    [Test]
    public async Task C1108_LegacyWindowIsServerAnchored()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: true);
        f.Wire.Qualified = Idle(TimeSpan.Zero, null);
        await f.EditAsync((task, _) => task.Role = AgentTaskRole.Code);
        await f.CreateSourceAsync();
        await CommitTipAsync(f);
        var ancientAt = f.Now.AddHours(-30);
        await using (var db = f.Db())
        {
            await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.CompletedAt, ancientAt));
            await db.AgentTaskEvents.Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Blocked)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.At, ancientAt));
        }

        var second = await SeedBlockedAsync(f, "duration");
        await f.CreateReclaimSourceAsync(second.TaskId, second.SessionId);
        var verifier = new RunnerWorkspaceParkService();
        f.Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: { } receipt }
            && (await verifier.VerifySessionCheckoutAsync(receipt, receipt.Request.Path, default)).Receipt is not null;

        (await f.ReclaimAsync(32, 1)).ShouldBeGreaterThan(0, "V-1");
        var park = await ParkOfAsync(f, f.TaskId);
        park.LegacyDiscovery.ShouldBeTrue("V-1");
        var secondPark = await ParkOfAsync(f, second.TaskId);
        secondPark.LegacyDiscovery.ShouldBeTrue("V-1");

        f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromHours(30), AsUtc(ancientAt));
        await f.ReclaimAsync(32, 1);
        Released(f, f.SessionId).ShouldBeFalse("G-1");
        await SessionRunningAsync(f, f.SessionId, "G-1");

        AdvanceTo(f, park.CreatedAt, TimeSpan.FromSeconds(60));
        f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(park.CreatedAt).AddHours(10));
        await f.ReclaimAsync(32, 1);
        Released(f, f.SessionId).ShouldBeFalse("G-2");
        await SessionRunningAsync(f, f.SessionId, "G-2");

        AdvanceTo(f, park.CreatedAt, TimeSpan.FromMilliseconds(119_999));
        f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(park.CreatedAt).AddHours(-10));
        await f.ReclaimAsync(32, 1);
        Released(f, f.SessionId).ShouldBeFalse("G-1");
        await SessionRunningAsync(f, f.SessionId, "G-1");

        AdvanceTo(f, park.CreatedAt, TimeSpan.FromMilliseconds(120_001));
        await f.ReclaimAsync(32, 1);
        Released(f, f.SessionId).ShouldBeTrue("G-2");
        f.Wire.Requests.Count(r => r.ParkVersion == 2).ShouldBe(1, "V-1");
        f.Wire.ForceCommands.ShouldBe(0, "V-1");

        (f.Clock.GetUtcNow() - AsUtc(secondPark.CreatedAt)).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(120), "V-1");
        (await ParkOfAsync(f, second.TaskId)).State.ShouldBe(AgentTaskParkState.Published, "V-1");
        f.Wire.BySession[second.SessionId] = Idle(TimeSpan.FromMilliseconds(119_999), AsUtc(secondPark.CreatedAt).AddHours(-10));
        await f.ReclaimAsync(32, 1);
        Released(f, second.SessionId).ShouldBeFalse("V-1");
        await SessionRunningAsync(f, second.SessionId, "V-1");
        f.Wire.Requests.Count(r => r.ParkVersion == 2).ShouldBe(1, "V-1");
        f.Wire.ForceCommands.ShouldBe(0, "V-1");
    }

    [Test]
    public async Task C1108_ScheduledSweepIsGatedAndBoundedPerRun()
    {
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: true))
        {
            f.Wire.Qualified = Idle(TimeSpan.Zero, null);
            var extra = new Row[4];
            for (var i = 0; i < extra.Length; i++)
                extra[i] = await SeedBlockedAsync(f, "gate-" + i);
            var blocked = new[] { f.TaskId, extra[0].TaskId, extra[1].TaskId, extra[2].TaskId, extra[3].TaskId };
            var working = await SeedBlockedAsync(f, "working-seat", status: AgentTaskStatus.Working, completed: false);

            var first = await f.ReclaimScheduledAsync();
            first.Visited.ShouldBe(5, "G-4");
            first.Eligible.ShouldBe(5, "G-4");
            first.Cap.ShouldBe(5, "G-4");
            first.Registered.ShouldBe(5, "V-2");
            first.Released.ShouldBe(0, "G-7");
            foreach (var id in blocked)
                (await ParkCountAsync(f, id)).ShouldBe(1, "V-2");
            (await ParkCountAsync(f, working.TaskId)).ShouldBe(0, "V-2");
            await SessionRunningAsync(f, working.SessionId, "V-2");
            await using (var db = f.Db())
                (await db.AgentTasks.SingleAsync(t => t.Id == working.TaskId)).Status
                    .ShouldBe(AgentTaskStatus.Working, "V-2");

            var cursor = await CursorAsync(f);
            cursor.ShouldNotBeNull("G-3");
            var marked = cursor.AfterTaskId;
            var second = await f.ReclaimScheduledAsync();
            second.ShouldBe(LegacyReclaimResult.None, "G-3");
            (await CursorAsync(f))!.AfterTaskId.ShouldBe(marked, "G-3");
            foreach (var id in blocked)
                (await ParkCountAsync(f, id)).ShouldBe(1, "G-3");

            f.Clock.Advance(TimeSpan.FromSeconds(120));
            var third = await f.ReclaimScheduledAsync();
            third.Visited.ShouldBe(5, "V-2");
            third.Eligible.ShouldBe(5, "V-2");
            foreach (var id in blocked)
                (await ParkCountAsync(f, id)).ShouldBe(1, "V-2");

            var beforeHold = (await CursorAsync(f))!.AfterTaskId;
            (await f.State.LegacyGate.WaitAsync(0)).ShouldBeTrue("G-3");
            try
            {
                var held = await f.ReclaimScheduledAsync();
                held.ShouldBe(LegacyReclaimResult.None, "G-3");
                (await CursorAsync(f))!.AfterTaskId.ShouldBe(beforeHold, "G-3");
            }
            finally { f.State.LegacyGate.Release(); }
        }

        await using (var rapid = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true, reclaimIntervalSeconds: 0))
        {
            rapid.Wire.Qualified = Idle(TimeSpan.Zero, null);
            for (var i = 0; i < 4; i++)
                await SeedBlockedAsync(rapid, "rapid-" + i);
            var opened = await rapid.ReclaimScheduledAsync();
            var again = await rapid.ReclaimScheduledAsync();
            opened.Visited.ShouldBe(5, "V-2");
            again.Visited.ShouldBe(5, "V-2");
            again.Released.ShouldBe(0, "G-7");
        }

        await using (var ungated = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true))
        {
            ungated.Wire.Qualified = Idle(TimeSpan.Zero, null);
            for (var i = 0; i < 7; i++)
                await SeedBlockedAsync(ungated, "budget-" + i);
            var budget = await ungated.ReclaimResultAsync(2, 3);
            budget.Visited.ShouldBe(6, "V-23");
            budget.Eligible.ShouldBe(8, "V-23");
            budget.Cap.ShouldBe(6, "V-23");
        }

        await using (var crash = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true))
        {
            crash.Wire.Qualified = Idle(TimeSpan.Zero, null);
            var extras = new Row[4];
            for (var i = 0; i < extras.Length; i++)
                extras[i] = await SeedBlockedAsync(crash, "crash-" + i);
            var poisoned = false;
            var first = await crash.ReclaimScheduledAsync((name, _) =>
            {
                if (name != "ReclaimList:" + crash.TaskId.ToString("D") || poisoned)
                    return Task.CompletedTask;
                poisoned = true;
                throw new InvalidOperationException("injected reclaim failure");
            });
            poisoned.ShouldBeTrue("G-174");
            first.Visited.ShouldBe(5, "G-174");
            first.Released.ShouldBe(0, "G-174");
            (await ParkCountAsync(crash, crash.TaskId)).ShouldBe(0, "G-174");
            foreach (var row in extras)
                (await ParkCountAsync(crash, row.TaskId)).ShouldBe(1, "G-174");
            (await CursorAsync(crash)).ShouldNotBeNull("G-174");
            await SessionRunningAsync(crash, crash.SessionId, "G-174");

            crash.Clock.Advance(TimeSpan.FromSeconds(120));
            var recovered = await crash.ReclaimScheduledAsync();
            recovered.Visited.ShouldBe(5, "G-174");
            (await ParkCountAsync(crash, crash.TaskId)).ShouldBe(1, "G-174");
        }
    }

    [Test]
    public async Task C1108_ReconcileJobCountsOnlyReleases()
    {
        await using var job = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true);
        job.Wire.Qualified = Idle(TimeSpan.Zero, null);
        var returned = await job.JobAsync();
        returned.ShouldBe(0, "G-6");
        var record = job.State.LastLegacyReclaim.ShouldNotBeNull("V-3");
        record.Visited.ShouldBe(1, "V-3");
        record.Released.ShouldBe(0, "G-7");
        var parks = await ParksAsync(job);
        parks.Count.ShouldBe(1, "V-3");
        parks.Single().LegacyDiscovery.ShouldBeTrue("V-3");
        await using var db = job.Db();
        (await db.RunnerSeatReleases.AsNoTracking().ToListAsync())
            .Count(TerminalRunnerSeatReleaseService.IsConfirmed).ShouldBe(0, "G-7");
    }

    [Test]
    public async Task C1108_DispatcherHookRunsTheGatedSweep()
    {
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true))
        {
            f.Wire.Qualified = Idle(TimeSpan.Zero, null);
            var now = f.Clock.GetUtcNow();
            await f.SweepAsync();
            var parks = await ParksAsync(f);
            parks.Count.ShouldBe(1, "V-4");
            parks.Single().LegacyDiscovery.ShouldBeTrue("V-4");
            f.State.LegacySweptAt.ShouldBe(now, "V-4");
            var recorded = f.State.LastLegacyReclaim.ShouldNotBeNull("V-4");
            recorded.Visited.ShouldBe(1, "V-4");
            recorded.Registered.ShouldBe(1, "V-4");
            recorded.Released.ShouldBe(0, "G-7");

            await f.SweepAsync();
            (await ParksAsync(f)).Count.ShouldBe(1, "G-3");
            f.State.LegacySweptAt.ShouldBe(now, "G-3");
            f.State.LastLegacyReclaim.ShouldBe(recorded, "G-3");
        }

        await using (var reclaimOff = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: false))
        {
            reclaimOff.Wire.Qualified = Idle(TimeSpan.Zero, null);
            await reclaimOff.SweepAsync();
            (await ParksAsync(reclaimOff)).Count.ShouldBe(0, "G-5");
            reclaimOff.State.LegacySweptAt.ShouldBeNull("G-5");
            reclaimOff.State.NextLegacySweepAt.ShouldBeNull("G-5");
            reclaimOff.State.LastLegacyReclaim.ShouldBeNull("G-5");
            (await CursorAsync(reclaimOff)).ShouldBeNull("G-5");
        }

        await using (var disabled = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: false, reclaim: false))
        {
            disabled.Wire.Qualified = Idle(TimeSpan.Zero, null);
            await disabled.SweepAsync();
            (await ParksAsync(disabled)).Count.ShouldBe(0, "G-5");
            disabled.State.LegacySweptAt.ShouldBeNull("G-5");
            disabled.State.NextLegacySweepAt.ShouldBeNull("G-5");
            disabled.State.LastLegacyReclaim.ShouldBeNull("G-5");
            (await CursorAsync(disabled)).ShouldBeNull("G-5");
        }
    }

    [Test]
    public async Task C1065_ClaimAndReplyInvalidateLegacyCandidate()
    {
        await InvalidateAsync("G-175", (f, _) => DriveAsync(f, f.AnswerAsync("reply invalidates the legacy candidate")), list: true);
        await InvalidateAsync("G-175", (f, _) => DriveAsync(f, f.Harness.Provider.GetRequiredService<AgentTaskService>()
            .CancelAsync(f.TaskId, CancellationToken.None)), list: true, cancel: true);
        await InvalidateAsync("G-175", (f, _) => SetStatusAsync(f, AgentTaskStatus.Working), reserve: true);
        await InvalidateAsync("G-175", (f, _) => SetStatusAsync(f, AgentTaskStatus.Working), send: true);
        await InvalidateAsync("G-176", (f, _) => BumpAttemptAsync(f), reserve: true);
        await InvalidateAsync("G-176", (f, _) => BumpAttemptAsync(f), send: true);
        await InvalidateAsync("G-177", (f, _) => NewerBlockAsync(f), reserve: true);
        await InvalidateAsync("G-177", (f, _) => NewerBlockAsync(f), send: true);
        await InvalidateAsync("G-178", (f, _) => DirtyAsync(f), reserve: true);
        await InvalidateAsync("G-178", (f, _) => DirtyAsync(f), send: true);
        await InvalidateAsync("G-179", (f, _) => ReplaceGenerationAsync(f), reserve: true);
        await InvalidateAsync("G-179", (f, _) => ReplaceGenerationAsync(f), send: true);
        await InvalidateAsync("G-180", (f, _) => ClaimSessionAsync(f), reserve: true);
        await InvalidateAsync("G-180", (f, _) => ClaimSessionAsync(f), send: true);
        await InvalidateAsync("G-181", (f, _) => ClaimAgentAsync(f), reserve: true);
        await InvalidateAsync("G-181", (f, _) => ClaimAgentAsync(f), send: true);
    }

    private static async Task InvalidateAsync(string label, Func<RunnerSeatReleaseFixture, CancellationToken, Task> mutate,
        bool list = false, bool reserve = false, bool send = false, bool cancel = false)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: true);
        f.Wire.Qualified = Idle(TimeSpan.Zero, null);
        await f.CreateSourceAsync();
        (await f.ReclaimAsync(2, 1)).ShouldBe(1, label);
        var park = await f.ParkAsync();
        park.LegacyDiscovery.ShouldBeTrue(label);
        park.State.ShouldNotBe(AgentTaskParkState.Parked, label);
        AdvanceTo(f, park.CreatedAt, TimeSpan.FromMilliseconds(120_001));
        f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(park.CreatedAt));
        Exception? error = null;
        var hit = 0;
        var barrier = list ? "ReclaimList:" + f.TaskId.ToString("D")
            : reserve ? "BeforeReservation" : "BeforeDispatch";
        if (!list && !reserve && !send)
            throw new InvalidOperationException("A reclaim barrier is required for " + label);
        var visited = await f.ReclaimAsync(2, 1, async (name, ct) =>
        {
            if (name != barrier) return;
            hit++;
            try { await mutate(f, ct); }
            catch (Exception ex) { error = ex; }
        });
        visited.ShouldBeGreaterThan(0, label);
        hit.ShouldBe(1, label);
        error.ShouldBeNull(label);
        f.Wire.Requests.ShouldNotContain(r => r.ParkVersion == 2, label);
        f.Wire.ForceCommands.ShouldBe(0, label);
        Directory.Exists(f.SourcePath).ShouldBeTrue(label);
        if (!cancel)
            await SessionRunningAsync(f, f.SessionId, label);
        else
            f.RecordedStops.Killed.ShouldContain(f.SessionId, label);
        if (label == "G-175" && list && !cancel)
            (await f.ParkAsync()).ReasonCode.ShouldBe("park_reply_before_reserve", label);
    }

    // Reply and cancel deliver through the session queue, which settles on the fixture clock.
    // Advance that clock while the call is in flight; a plain await never wakes the delay.
    private static async Task DriveAsync(RunnerSeatReleaseFixture f, Task work)
    {
        while (!work.IsCompleted)
        {
            await Task.WhenAny(work, Task.Delay(10));
            f.Clock.Advance(TimeSpan.FromMilliseconds(20));
        }
        await work;
    }

    private static async Task<Row> SeedBlockedAsync(RunnerSeatReleaseFixture f, string name,
        string? result = "completed report", AgentTaskStatus status = AgentTaskStatus.Blocked, bool completed = true)
    {
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var slug = "reclaim-" + name + "-" + Guid.NewGuid().ToString("N")[..8];
        await using var db = f.Db();
        db.Agents.Add(new Agent
        {
            Id = agentId, Name = slug, Slug = slug, WorkingDirectory = "/tmp",
            CreatedAt = f.Now, UpdatedAt = f.Now, IsPoolDelegate = true, AlwaysOn = false
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, StartedAt = f.Now.AddHours(-1),
            Cwd = "/reclaim/" + name, RunnerId = "fixture", RunnerStoreId = f.Directory.StoreId,
            RunnerCwd = "/reclaim/" + name, CreatedAt = f.Now, LastSeenAt = f.Now
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, AgentId = agentId, AgentSessionId = sessionId,
            RunnerId = "fixture", Workspace = WorkspaceMode.Worktree, Attempt = 1, Status = status,
            CompletedAt = completed ? f.Now.AddMinutes(-3) : null, CreatedAt = f.Now.AddHours(-1),
            Ephemeral = true, Goal = name, Result = result,
            ReportEvidence = result is null ? default : AgentTaskReportEvidence.Marked
        });
        if (status == AgentTaskStatus.Blocked)
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Blocked,
                At = f.Now.AddMinutes(-3), Detail = name
            });
        }
        await db.SaveChangesAsync();
        return new Row(taskId, sessionId);
    }

    private static async Task SetStatusAsync(RunnerSeatReleaseFixture f, AgentTaskStatus status)
    {
        await using var db = f.Db();
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
    }

    private static async Task BumpAttemptAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Attempt, t => t.Attempt + 1));
    }

    private static async Task NewerBlockAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = f.TaskId, Type = AgentTaskEventType.Blocked,
            At = f.Now, Detail = "newer block"
        });
        await db.SaveChangesAsync();
    }

    private static Task DirtyAsync(RunnerSeatReleaseFixture f) =>
        File.WriteAllTextAsync(Path.Combine(f.SourcePath, "raced.txt"), "dirty");

    private static async Task ReplaceGenerationAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.StartedAt, f.Now));
    }

    private static async Task ClaimSessionAsync(RunnerSeatReleaseFixture f)
    {
        var sessionId = Guid.NewGuid();
        await using var db = f.Db();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, StartedAt = f.Now, Cwd = "/reclaim/claim",
            RunnerId = "fixture", RunnerStoreId = f.Directory.StoreId, RunnerCwd = "/reclaim/claim",
            CreatedAt = f.Now, LastSeenAt = f.Now
        });
        await db.SaveChangesAsync();
        await db.AgentTasks.Where(t => t.Id == f.TaskId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.AgentSessionId, sessionId));
    }

    private static async Task ClaimAgentAsync(RunnerSeatReleaseFixture f)
    {
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using var db = f.Db();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running, StartedAt = f.Now, Cwd = "/reclaim/agent-claim",
            RunnerId = "fixture", RunnerStoreId = Guid.NewGuid(), RunnerCwd = "/reclaim/agent-claim",
            CreatedAt = f.Now, LastSeenAt = f.Now
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, AgentId = f.AgentId, AgentSessionId = sessionId,
            RunnerId = "fixture", Status = AgentTaskStatus.Working, Attempt = 1, CreatedAt = f.Now,
            Goal = "same agent, other session", Workspace = WorkspaceMode.Worktree
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> CommitTipAsync(RunnerSeatReleaseFixture f)
    {
        var branch = (await f.TaskAsync()).WorktreeBranch.ShouldNotBeNull();
        await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "parked-tip.txt"), "parked tip");
        await f.GitAsync(f.SourcePath, "add", "parked-tip.txt");
        await f.GitAsync(f.SourcePath, "commit", "-m", "parked tip");
        await f.GitAsync(f.SourcePath, "push", "--no-follow-tags", "origin", $"HEAD:refs/heads/{branch}");
        return await f.GitAsync(f.SourcePath, "rev-parse", "HEAD");
    }

    private static Task StampAsync(RunnerSeatReleaseFixture f) => f.EditAsync((task, _) =>
    {
        task.RepliedAtSequence = 9;
        task.ReportNudgedAt = f.Now;
        task.ReportNudgeMessageId = Guid.NewGuid();
        task.NextCheckAt = f.Now.AddHours(1);
        task.CheckCount = 4;
        task.RemoteWorktreePath = task.WorktreePath;
    });

    private static TerminalSeatObservation Idle(TimeSpan stable, DateTimeOffset? first,
        TerminalSeatQualificationStatus status = TerminalSeatQualificationStatus.Qualified) => new(status,
        new(TerminalTranscriptReadStatus.Success, TerminalTranscriptVerdict.Idle, "binding", "file", 100, 12, 12, 11),
        "issued-token", stable, first);

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static void AdvanceTo(RunnerSeatReleaseFixture f, DateTime createdAt, TimeSpan elapsed)
    {
        var delta = AsUtc(createdAt) + elapsed - f.Clock.GetUtcNow();
        if (delta > TimeSpan.Zero)
            f.Clock.Advance(delta);
    }

    private static bool Released(RunnerSeatReleaseFixture f, Guid sessionId) =>
        f.Wire.Calls.Any(path => path.Contains(sessionId.ToString("D"), StringComparison.Ordinal)
            && path.EndsWith("/release-terminal-seat", StringComparison.Ordinal));

    private static async Task SessionRunningAsync(RunnerSeatReleaseFixture f, Guid sessionId, string label)
    {
        await using var db = f.Db();
        (await db.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running, label);
    }

    private static async Task<BlockedTaskParkReclaimCursor?> CursorAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.BlockedTaskParkReclaimCursors.AsNoTracking().SingleOrDefaultAsync(c => c.Id == 1);
    }

    private static async Task<int> ParkCountAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        await using var db = f.Db();
        return await db.AgentTaskParks.CountAsync(p => p.TaskId == taskId);
    }

    private static async Task<AgentTaskPark> ParkOfAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        await using var db = f.Db();
        return await db.AgentTaskParks.AsNoTracking().SingleAsync(p => p.TaskId == taskId);
    }

    private static async Task<List<AgentTaskPark>> ParksAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.AgentTaskParks.AsNoTracking().ToListAsync();
    }

    private static async Task<AgentTaskDetailDto> DetailAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var credential = Guid.NewGuid().ToString("N");
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Test-Token"] != credential)
            { context.Response.StatusCode = 401; return; }
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "fixture-operator")], "fixture"));
            await next(context);
        });
        app.MapGet("/api/agent-tasks/{id:guid}", async (Guid id, CancellationToken ct) =>
        {
            using var scope = f.Harness.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetAsync(id, ct);
        });
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        (await http.GetAsync($"/api/agent-tasks/{taskId}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Add("X-Test-Token", credential);
        return (await http.GetFromJsonAsync<AgentTaskDetailDto>($"/api/agent-tasks/{taskId}"))!;
    }

    private static async Task<RunnerSlotsDto> SlotsAsync(RunnerSeatReleaseFixture f)
    {
        f.Wire.SessionsList = [];
        var directory = new PhoneHomeRunnerDirectory(f.Directory.Client, Options.Create(new PhoneHomeRunnerSettings()),
            f.Harness.Provider.GetRequiredService<IServiceScopeFactory>(), f.Clock);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var credential = Guid.NewGuid().ToString("N");
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Test-Token"] != credential)
            { context.Response.StatusCode = 401; return; }
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "fixture-operator")], "fixture"));
            await next(context);
        });
        app.MapGet("/api/session-runners/{runnerId}/slots", async (string runnerId, CancellationToken ct) =>
        {
            await using var db = f.Db();
            return await RunnerSlotService.ListAsync(directory, db, runnerId, ct);
        });
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        (await http.GetAsync($"/api/session-runners/{PhoneHomeRunnerDirectory.LocalRunnerId}/slots"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Add("X-Test-Token", credential);
        var slots = (await http.GetFromJsonAsync<RunnerSlotsDto>(
            $"/api/session-runners/{PhoneHomeRunnerDirectory.LocalRunnerId}/slots"))!;
        f.Wire.SessionsList = null;
        return slots;
    }

    private sealed record Row(Guid TaskId, Guid SessionId);
}
