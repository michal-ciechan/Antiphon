using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
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
public sealed partial class BlockedTaskParkReclaimTests
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

            f.Clock.Advance(TimeSpan.FromSeconds(119));
            var early = await f.ReclaimScheduledAsync();
            early.ShouldBe(LegacyReclaimResult.None, "G-3");
            (await CursorAsync(f))!.AfterTaskId.ShouldBe(marked, "G-3");

            f.Clock.Advance(TimeSpan.FromSeconds(1));
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
    public async Task C1108_HeldEpisodesBackOffUntilNextAttempt()
    {
        var gate = new ReclaimReservationGate();
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true, reservationGate: gate))
        {
            f.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
            await f.EditAsync((task, _) => task.Role = AgentTaskRole.Code);
            await f.CreateSourceAsync();
            await CommitTipAsync(f);
            var verifier = new RunnerWorkspaceParkService();
            f.Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: { } receipt }
                && (await verifier.VerifySessionCheckoutAsync(receipt, receipt.Request.Path, default)).Receipt is not null;

            var dirty = await SeedBlockedAsync(f, "dirty");
            var reserved = await SeedBlockedAsync(f, "reserved");
            var heal = await SeedBlockedAsync(f, "heal");
            var working = await SeedBlockedAsync(f, "working-seat", status: AgentTaskStatus.Working, completed: false);
            var dirtyPath = await f.CreateReclaimSourceAsync(dirty.TaskId, dirty.SessionId, dirty: true);
            await f.CreateReclaimSourceAsync(reserved.TaskId, reserved.SessionId);
            var healPath = await f.CreateReclaimSourceAsync(heal.TaskId, heal.SessionId, dirty: true);
            f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromMilliseconds(119_999), null);
            gate.RefuseTaskId = reserved.TaskId;

            (await f.ReclaimAsync(32, 1)).ShouldBeGreaterThan(0, "V-5");
            var dirtyPark = await ParkOfAsync(f, dirty.TaskId);
            dirtyPark.ReasonCode.ShouldBe("park_dirty", "G-8");
            dirtyPark.NextAttemptAt.ShouldNotBeNull("G-8");
            SameInstant(dirtyPark.NextAttemptAt, dirtyPark.UpdatedAt.AddSeconds(600), "G-8");
            var revision = dirtyPark.Revision;
            var waiting = await ParkOfAsync(f, f.TaskId);
            waiting.State.ShouldBe(AgentTaskParkState.Published, "G-10");
            Observations(f, f.SessionId).ShouldBe(1, "G-10");
            var reservedPark = await ParkOfAsync(f, reserved.TaskId);
            reservedPark.ReasonCode.ShouldBe("park_workspace_reserved", "G-9");
            reservedPark.NextAttemptAt.ShouldBeNull("G-9");
            var reservedRevision = reservedPark.Revision;
            (await ParkCountAsync(f, working.TaskId)).ShouldBe(0, "V-5");
            await SessionRunningAsync(f, working.SessionId, "V-5");

            gate.RefuseTaskId = null;
            var observed = Observations(f, f.SessionId);
            await f.ReclaimAsync(32, 1);
            (await f.HandleAsync(dirty.TaskId)).ShouldBeTrue("G-8");
            var dirtyAgain = await ParkOfAsync(f, dirty.TaskId);
            dirtyAgain.Revision.ShouldBe(revision, "G-8");
            dirtyAgain.ReasonCode.ShouldBe("park_dirty", "G-8");
            Observations(f, f.SessionId).ShouldBe(observed + 1, "G-10");
            var reservedAgain = await ParkOfAsync(f, reserved.TaskId);
            reservedAgain.Revision.ShouldBeGreaterThan(reservedRevision, "G-9");
            reservedAgain.ReasonCode.ShouldNotBe("park_workspace_reserved", "G-9");
            Released(f, f.SessionId).ShouldBeFalse("G-10");
            Released(f, heal.SessionId).ShouldBeFalse("V-5");

            File.Delete(Path.Combine(healPath, "dirty.txt"));
            var healPark = await ParkOfAsync(f, heal.TaskId);
            f.Wire.BySession[heal.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(healPark.CreatedAt));
            var due = dirtyAgain.NextAttemptAt ?? f.Now.AddSeconds(600);
            var delta = new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc)) - f.Clock.GetUtcNow();
            if (delta > TimeSpan.Zero)
                f.Clock.Advance(delta);
            await f.ReclaimAsync(32, 1);
            var dirtyAfter = await ParkOfAsync(f, dirty.TaskId);
            dirtyAfter.Revision.ShouldBeGreaterThan(revision, "G-8");
            dirtyAfter.ReasonCode.ShouldBe("park_dirty", "G-8");
            dirtyAfter.NextAttemptAt.ShouldNotBeNull("G-8");
            SameInstant(dirtyAfter.NextAttemptAt, f.Now.AddSeconds(600), "G-8");
            Released(f, heal.SessionId).ShouldBeTrue("V-5");
            Released(f, dirty.SessionId).ShouldBeFalse("G-8");
            Released(f, f.SessionId).ShouldBeFalse("G-10");
            Directory.Exists(dirtyPath).ShouldBeTrue("V-5");
            await SessionRunningAsync(f, working.SessionId, "V-5");
        }

        await using (var open = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true, reclaimHeldBackoffSeconds: 0))
        {
            open.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
            await open.EditAsync((task, _) => task.Status = AgentTaskStatus.Succeeded);
            var row = await SeedBlockedAsync(open, "open-dirty");
            await open.CreateReclaimSourceAsync(row.TaskId, row.SessionId, dirty: true);
            (await open.ReclaimAsync(32, 1)).ShouldBe(1, "V-5");
            var first = await ParkOfAsync(open, row.TaskId);
            first.ReasonCode.ShouldBe("park_dirty", "V-5");
            first.NextAttemptAt.ShouldBeNull("V-5");
            await open.ReclaimAsync(32, 1);
            var second = await ParkOfAsync(open, row.TaskId);
            second.Revision.ShouldBeGreaterThan(first.Revision, "V-5");
            second.ReasonCode.ShouldBe("park_dirty", "V-5");
        }

        var counter = new CountingCommandInterceptor();
        await using var measured = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter));
        measured.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
        await measured.EditAsync((task, _) => task.Status = AgentTaskStatus.Succeeded);
        var held = new Row[3];
        for (var i = 0; i < held.Length; i++)
        {
            held[i] = await SeedBlockedAsync(measured, "held-" + i);
            await measured.CreateReclaimSourceAsync(held[i].TaskId, held[i].SessionId, dirty: true);
        }
        var poison = await SeedBlockedAsync(measured, "poison");
        await measured.CreateReclaimSourceAsync(poison.TaskId, poison.SessionId, dirty: true);
        var poisoned = false;
        Func<string, CancellationToken, Task> boundary = (name, _) =>
        {
            if (name != "ReclaimList:" + poison.TaskId.ToString("D") || poisoned)
                return Task.CompletedTask;
            poisoned = true;
            throw new InvalidOperationException("injected reclaim failure");
        };
        var git = (TaskParkPublicationTests.ParkGit)measured.Harness.Provider.GetRequiredService<ITaskProgressGit>();
        git.Commands.Clear();
        var statementsAt = counter.Commands.Count;
        var firstSweep = await measured.ReclaimResultAsync(32, 1, boundary);
        var statusBefore = StatusVisits(git);
        var statementsBefore = counter.Commands.Count - statementsAt;
        poisoned.ShouldBeTrue("V-5");
        firstSweep.Visited.ShouldBe(4, "V-5");
        (await ParkCountAsync(measured, poison.TaskId)).ShouldBe(0, "V-5");
        var revisions = new long[held.Length];
        for (var i = 0; i < held.Length; i++)
        {
            var park = await ParkOfAsync(measured, held[i].TaskId);
            park.ReasonCode.ShouldBe("park_dirty", "V-5");
            revisions[i] = park.Revision;
        }
        git.Commands.Clear();
        statementsAt = counter.Commands.Count;
        await measured.ReclaimAsync(32, 1, boundary);
        var statusAfter = StatusVisits(git);
        var statementsAfter = counter.Commands.Count - statementsAt;
        Console.WriteLine(
            $"C1108 held visits rows=3 statusBefore={statusBefore} statusAfter={statusAfter} statementsBefore={statementsBefore} statementsAfter={statementsAfter}");
        statusAfter.ShouldBeLessThan(statusBefore,
            $"status visits before={statusBefore} after={statusAfter}; statements before={statementsBefore} after={statementsAfter}");
        statementsAfter.ShouldBeLessThan(statementsBefore,
            $"statements before={statementsBefore} after={statementsAfter}");
        (await ParkCountAsync(measured, poison.TaskId)).ShouldBe(1, "V-5");
        for (var i = 0; i < held.Length; i++)
            (await ParkOfAsync(measured, held[i].TaskId)).Revision.ShouldBe(revisions[i], "G-8");
    }

    [Test]
    public async Task C1135_HeldRefusalsRestampAndRecordTheirReason()
    {
        var gate = new ReclaimReservationGate();
        var counter = new CountingCommandInterceptor();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true, reservationGate: gate,
            configureDb: options => options.AddInterceptors(counter));
        f.Wire.Qualified = Idle(TimeSpan.FromMilliseconds(119_999), null);
        await f.EditAsync((task, _) => task.Status = AgentTaskStatus.Succeeded);

        var dirty = await SeedBlockedAsync(f, "dirty");
        var binding = await SeedBlockedAsync(f, "binding");
        var reserved = await SeedBlockedAsync(f, "reserved");
        var dirtyPath = await f.CreateReclaimSourceAsync(dirty.TaskId, dirty.SessionId, dirty: true);
        await f.CreateReclaimSourceAsync(binding.TaskId, binding.SessionId, dirty: true);
        await f.CreateReclaimSourceAsync(reserved.TaskId, reserved.SessionId);
        gate.RefuseTaskId = reserved.TaskId;
        var git = (TaskParkPublicationTests.ParkGit)f.Harness.Provider.GetRequiredService<ITaskProgressGit>();

        (await f.ReclaimAsync(32, 1)).ShouldBe(3, "V-1");
        var dirtyPark = await ParkOfAsync(f, dirty.TaskId);
        dirtyPark.State.ShouldBe(AgentTaskParkState.Held, "V-1");
        dirtyPark.ReasonCode.ShouldBe("park_dirty", "V-1");
        dirtyPark.HeldFromState.ShouldBe(AgentTaskParkState.Requested, "V-1");
        SameInstant(dirtyPark.NextAttemptAt, dirtyPark.UpdatedAt.AddSeconds(600), "V-1");
        var revision = dirtyPark.Revision;
        var heldFrom = dirtyPark.HeldFromState;
        var bindingPark = await ParkOfAsync(f, binding.TaskId);
        bindingPark.ReasonCode.ShouldBe("park_dirty", "V-2");
        var bindingRevision = bindingPark.Revision;
        var reservedPark = await ParkOfAsync(f, reserved.TaskId);
        reservedPark.ReasonCode.ShouldBe("park_workspace_reserved", "V-2");
        reservedPark.NextAttemptAt.ShouldBeNull("V-2");
        var reservedRevision = reservedPark.Revision;

        gate.RefuseTaskId = null;
        await f.ReclaimAsync(32, 1);
        var reservedAgain = await ParkOfAsync(f, reserved.TaskId);
        reservedAgain.Revision.ShouldBeGreaterThan(reservedRevision, "V-2");
        reservedAgain.ReasonCode.ShouldNotBe("park_workspace_reserved", "V-2");
        (await ParkOfAsync(f, dirty.TaskId)).Revision.ShouldBe(revision, "V-1");
        await using (var retire = f.Db())
        {
            await retire.AgentTasks.Where(t => t.Id == reserved.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        }

        // A shared WorktreePath is park_ownership_ambiguous before PrepareAsync. RefusalAsync
        // matches WorkingDirectory too, so this Working neighbor is park_other_writer first.
        var squatter = await SeedBlockedAsync(f, "squatter", status: AgentTaskStatus.Working, completed: false);
        await using (var db = f.Db())
        {
            await db.AgentTasks.Where(t => t.Id == squatter.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkingDirectory, dirtyPath));
            await db.AgentTaskParks.Where(p => p.TaskId == binding.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReportDigest, (string?)null));
        }

        f.Clock.Advance(TimeSpan.FromSeconds(600));
        git.Commands.Clear();
        var statementsAt = counter.Commands.Count;
        (await f.ReclaimAsync(32, 1)).ShouldBe(2, "V-1");
        var statusDue = StatusVisits(git);
        var statementsDue = counter.Commands.Count - statementsAt;
        var dueNow = f.Now;
        var dirtyDue = await ParkOfAsync(f, dirty.TaskId);
        var bindingDue = await ParkOfAsync(f, binding.TaskId);

        f.Clock.Advance(TimeSpan.FromSeconds(1));
        git.Commands.Clear();
        statementsAt = counter.Commands.Count;
        await f.ReclaimAsync(32, 1);
        var statusFollow = StatusVisits(git);
        var statementsFollow = counter.Commands.Count - statementsAt;
        var dirtyFollow = await ParkOfAsync(f, dirty.TaskId);
        var bindingFollow = await ParkOfAsync(f, binding.TaskId);
        Console.WriteLine(
            $"C1135 visits statusDue={statusDue} statementsDue={statementsDue} statusFollow={statusFollow} statementsFollow={statementsFollow}");

        dirtyDue.State.ShouldBe(AgentTaskParkState.Held, "G-1");
        dirtyDue.ReasonCode.ShouldBe("park_other_writer", "G-1");
        dirtyDue.Revision.ShouldBe(revision, "G-1");
        dirtyDue.HeldFromState.ShouldBe(heldFrom, "G-1");
        SameInstant(dirtyDue.UpdatedAt, dueNow, "G-1");
        SameInstant(dirtyDue.NextAttemptAt, dueNow.AddSeconds(600), "G-1");
        SameInstant(dirtyFollow.UpdatedAt, dirtyDue.UpdatedAt, "G-1");
        statusFollow.ShouldBe(0, "G-1");
        bindingDue.ReasonCode.ShouldBe("park_binding_missing", "G-2");
        bindingDue.Revision.ShouldBe(bindingRevision, "G-2");
        SameInstant(bindingDue.NextAttemptAt, dueNow.AddSeconds(600), "G-2");
        SameInstant(bindingDue.UpdatedAt, dueNow, "G-2");
        SameInstant(bindingFollow.UpdatedAt, bindingDue.UpdatedAt, "G-2");
        bindingFollow.Revision.ShouldBe(bindingRevision, "G-2");

        await using (var db = f.Db())
        {
            await db.AgentTasks.Where(t => t.Id == squatter.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        }
        f.Clock.Advance(TimeSpan.FromSeconds(600));
        await f.ReclaimAsync(32, 1);
        var dirtyAfter = await ParkOfAsync(f, dirty.TaskId);
        dirtyAfter.Revision.ShouldBeGreaterThan(revision, "V-1");
        dirtyAfter.ReasonCode.ShouldBe("park_dirty", "V-1");
        dirtyAfter.State.ShouldBe(AgentTaskParkState.Held, "V-1");
        Released(f, dirty.SessionId).ShouldBeFalse("V-1");
        Directory.Exists(dirtyPath).ShouldBeTrue("V-1");
        await SessionRunningAsync(f, dirty.SessionId, "V-1");
        await SessionRunningAsync(f, squatter.SessionId, "V-1");
        await SessionRunningAsync(f, binding.SessionId, "V-2");
    }

    [Test]
    public async Task C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce()
    {
        var shrinking = await MeasureVisitsAsync(shrink: true);
        var open = await MeasureVisitsAsync(shrink: false);
        var counts = await MeasureReleasesAsync();

        shrinking.Result.Eligible.ShouldBe(6, "V-4");
        shrinking.Result.Cap.ShouldBe(6, "V-4");
        shrinking.Result.Visited.ShouldBe(4, "V-4");
        shrinking.Listed.Count.ShouldBe(4, "V-4");
        shrinking.Listed.Distinct().Count().ShouldBe(shrinking.Listed.Count, "V-4");
        shrinking.Cursor.ShouldBe(shrinking.Listed[^1], "V-4");
        open.Result.Eligible.ShouldBe(6, "V-4");
        open.Result.Cap.ShouldBe(6, "V-4");
        open.Result.Visited.ShouldBe(6, "V-4");
        open.Listed.Count.ShouldBe(6, "V-4");
        open.Listed.Distinct().Count().ShouldBe(open.Listed.Count, "V-4");
        open.Cursor.ShouldBe(open.Listed[^1], "V-4");

        counts.Run1.Released.ShouldBe(2, "V-3");
        counts.Run1.Released.ShouldBe(counts.ConfirmedAfterRun1, "V-3");
        counts.Run2.Visited.ShouldBe(3, "V-3");
        counts.Run2.Registered.ShouldBe(3, "V-3");
        counts.Run2.Released.ShouldBe(0, "V-3");
        counts.ConfirmedAfterRun2.ShouldBe(2, "V-3");
        counts.Version2AfterRun2.ShouldBe(counts.Version2AfterRun1, "V-3");
        counts.ForceAfterRun2.ShouldBe(0, "V-3");
        counts.Negative.Released.ShouldBe(1, "V-3");
        counts.ConfirmedAfterNegative.ShouldBe(3, "V-3");
    }

    [Test]
#pragma warning disable EXTEXP0004 // SetUtcNow refuses a backward instant; AdjustTime is the step.
    public async Task C1145_ReleasedCountsConfirmationsThisRunProduces()
    {
        await using (var backward = await ReadyLegacyReleaseAsync())
        {
            var start = backward.Clock.GetUtcNow();
            var stepped = false;
            var result = await backward.ReclaimResultAsync(32, 1, (name, _) =>
            {
                if (name == "BeforeResponse" && !stepped)
                {
                    stepped = true;
                    // SetUtcNow throws on a backward instant. AdjustTime is the step.
                    backward.Clock.AdjustTime(start.AddSeconds(-1));
                }
                return Task.CompletedTask;
            });
            stepped.ShouldBeTrue("C1145-back");
            Released(backward, backward.SessionId).ShouldBeTrue("C1145-back");
            SameInstant(await ConfirmedStampAsync(backward, backward.TaskId), start.UtcDateTime.AddSeconds(-1), "C1145-back");
            result.Released.ShouldBe(1, "C1145-back");
        }

        await using (var equal = await ReadyLegacyReleaseAsync())
        {
            var start = equal.Clock.GetUtcNow();
            var result = await equal.ReclaimResultAsync(32, 1);
            result.Released.ShouldBe(1, "C1145-equal");
            Released(equal, equal.SessionId).ShouldBeTrue("C1145-equal");
            var stamp = await ConfirmedStampAsync(equal, equal.TaskId);
            SameInstant(stamp, start.UtcDateTime, "C1145-equal");

            equal.Clock.Advance(TimeSpan.FromSeconds(120));
            var normal = await equal.ReclaimResultAsync(32, 1);
            normal.Released.ShouldBe(0, "C1145-prior");
            normal.Visited.ShouldBeGreaterThan(0, "C1145-prior");
            normal.Registered.ShouldBeGreaterThan(0, "C1145-prior");
            (await ConfirmedCountAsync(equal)).ShouldBe(1, "C1145-prior");

            equal.Clock.AdjustTime(new DateTimeOffset(DateTime.SpecifyKind(stamp!.Value, DateTimeKind.Utc), TimeSpan.Zero));
            var equalPrior = await equal.ReclaimResultAsync(32, 1);
            equalPrior.Released.ShouldBe(0, "C1145-prior-equal");
            equalPrior.Visited.ShouldBeGreaterThan(0, "C1145-prior-equal");
            equalPrior.Registered.ShouldBeGreaterThan(0, "C1145-prior-equal");
            (await ConfirmedCountAsync(equal)).ShouldBe(1, "C1145-prior-equal");

            equal.Clock.AdjustTime(equal.Clock.GetUtcNow().AddSeconds(-1));
            var steppedPrior = await equal.ReclaimResultAsync(32, 1);
            steppedPrior.Released.ShouldBe(0, "C1145-prior-step");
            steppedPrior.Visited.ShouldBeGreaterThan(0, "C1145-prior-step");
            steppedPrior.Registered.ShouldBeGreaterThan(0, "C1145-prior-step");
            (await ConfirmedCountAsync(equal)).ShouldBe(1, "C1145-prior-step");
        }
    }
#pragma warning restore EXTEXP0004

    [Test]
    public async Task C1147_RolledBackConfirmationIsNotThisRunsRelease()
    {
        // One condition flips per world: commit fault, then an independent recovery before the tally.
        ConfirmCommitWorld[] worlds =
        [
            new("c1147-control", Fault: false, Recover: false, Released: 3, Confirmed: 3, Commands: 3),
            new("c1147-rollback", Fault: true, Recover: false, Released: 2, Confirmed: 2, Commands: 3),
            new("c1147-recovered", Fault: true, Recover: true, Released: 2, Confirmed: 3, Commands: 4),
        ];
        var measured = new List<ConfirmCommitMeasurement>();
        foreach (var world in worlds)
            measured.Add(await MeasureConfirmCommitAsync(world));

        var control = measured[0];
        foreach (var m in measured)
        {
            var label = m.World.Label;
            m.Result.Visited.ShouldBe(3, label);
            m.Result.Registered.ShouldBe(3, label);
            m.Result.Eligible.ShouldBe(3, label);
            m.Result.Released.ShouldBe(m.World.Released, label);
            m.ConfirmedAfter.ShouldBe(m.World.Confirmed, label);
            m.ReleaseCommands.ShouldBe(m.World.Commands, label);
            m.ForceCommands.ShouldBe(0, label);
            // The commit fault follows every confirmation statement, so the sweep context sends
            // the same statements in every world. Recovery runs on its own context.
            m.SweepCommands.Count.ShouldBeGreaterThan(0, label);
            m.SweepCommands.ShouldBe(control.SweepCommands, label);
            m.Tallies.Count.ShouldBe(3, label);
            foreach (var tally in m.Tallies)
            {
                tally.Commands.Count.ShouldBe(2, label + " tally");
                tally.Commands[0].ShouldStartWith("SELECT", Case.Sensitive, label + " tally");
                tally.Commands[0].ShouldContain("FROM \"AgentTasks\"", Case.Sensitive, label + " tally");
                tally.Commands[1].ShouldStartWith("SELECT", Case.Sensitive, label + " tally");
                tally.Commands[1].ShouldContain("FROM \"RunnerSeatReleases\"", Case.Sensitive, label + " tally");
            }
        }
        control.ConfirmWrites.ShouldBe(0, "c1147-control");
        control.Rollbacks.ShouldBe(0, "c1147-control");
        control.RecoveryCommands.ShouldBeEmpty("c1147-control");
        foreach (var m in measured.Skip(1))
        {
            m.ConfirmWrites.ShouldBe(1, m.World.Label);
            m.Rollbacks.ShouldBe(1, m.World.Label);
        }
        measured[1].RecoveryCommands.ShouldBeEmpty("c1147-rollback");
        var recovered = measured[2];
        recovered.RecoveryCommands.Count(IsConfirmWrite).ShouldBe(1, "c1147-recovered");
        recovered.RecoveredBeforeTally.ShouldBeTrue("c1147-recovered");
    }

    private sealed record ConfirmCommitWorld(
        string Label, bool Fault, bool Recover, int Released, int Confirmed, int Commands);

    private sealed record ConfirmCommitMeasurement(
        ConfirmCommitWorld World, LegacyReclaimResult Result, int ConfirmedAfter, int ReleaseCommands,
        int ForceCommands, int ConfirmWrites, int Rollbacks, IReadOnlyList<string> SweepCommands,
        IReadOnlyList<string> RecoveryCommands, IReadOnlyList<ConfirmCommitFault.Tally> Tallies,
        bool RecoveredBeforeTally);

    private static async Task<ConfirmCommitMeasurement> MeasureConfirmCommitAsync(ConfirmCommitWorld world)
    {
        var label = world.Label;
        var fault = new ConfirmCommitFault();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true, reclaimIntervalSeconds: 0,
            configureDb: options => options.AddInterceptors(fault));
        f.Wire.Qualified = Idle(TimeSpan.Zero, null);
        await f.EditAsync((task, _) => task.Role = AgentTaskRole.Code);
        await f.CreateSourceAsync();
        await CommitTipAsync(f);
        await AncientBlockAsync(f, f.TaskId);
        ArmPathVerifier(f);
        var rows = new List<Row> { new(f.TaskId, f.SessionId) };
        for (var i = 0; i < 2; i++)
        {
            var row = await SeedBlockedAsync(f, "c1147-" + i);
            await f.CreateReclaimSourceAsync(row.TaskId, row.SessionId);
            await AncientBlockAsync(f, row.TaskId);
            // Link each agent to its seat like the fixture's own row, so the agent projection is live.
            await using (var db = f.Db())
            {
                var agentId = (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.TaskId)).AgentId;
                await db.Agents.Where(a => a.Id == agentId).ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.PersistentSessionId, row.SessionId.ToString("D")));
            }
            rows.Add(row);
        }

        var order = new List<Guid>();
        var registration = await f.ReclaimScheduledAsync((name, _) =>
        {
            if (name.StartsWith("ReclaimList:", StringComparison.Ordinal))
                order.Add(Guid.Parse(name["ReclaimList:".Length..]));
            return Task.CompletedTask;
        });
        registration.Visited.ShouldBe(3, label);
        registration.Released.ShouldBe(0, label);
        order.Distinct().Count().ShouldBe(3, label);
        var target = rows.Single(r => r.TaskId == order[1]);
        var anchor = new List<DateTime>();
        foreach (var row in rows)
            anchor.Add((await ParkOfAsync(f, row.TaskId)).CreatedAt);
        AdvanceTo(f, anchor.Max(), TimeSpan.FromMilliseconds(120_001));
        foreach (var row in rows)
            await ArmIdleAsync(f, row.TaskId, row.SessionId);
        var agentBefore = await TargetAgentAsync(f, target);
        agentBefore.Status.ShouldNotBe(AgentStatus.Stopped, label);
        var releaseCommandsBefore = f.Wire.ConditionalCommands;
        if (world.Fault) fault.Target = target.TaskId;

        var listed = new List<Guid>();
        var recovered = false;
        LegacyReclaimResult result;
        using (var scope = f.Harness.Provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>();
            var sweepDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            fault.Sweep = sweepDb;
            service.BoundaryAsync = async (name, ct) =>
            {
                if (name.StartsWith("ReclaimList:", StringComparison.Ordinal))
                {
                    listed.Add(Guid.Parse(name["ReclaimList:".Length..]));
                    return;
                }
                if (name != "BeforeAttentionPublish") return;
                if (world.Recover && fault.Rollbacks == 1 && !recovered)
                {
                    recovered = true;
                    await RecoverElsewhereAsync(f, fault, service, sweepDb, target, label, ct);
                }
                fault.OpenTally();
            };
            result = await service.ReclaimScheduledAsync(default);
        }
        fault.Target = null;
        fault.Sweep = null;

        listed.ShouldBe(order, label);
        (await CursorAsync(f))!.AfterTaskId.ShouldBe(listed[^1], label);
        foreach (var row in rows)
        {
            Released(f, row.SessionId).ShouldBeTrue(label);
            if (row == target) continue;
            (await ParkOfAsync(f, row.TaskId)).State.ShouldBe(AgentTaskParkState.Parked, label + " neighbor");
            TerminalRunnerSeatReleaseService.IsConfirmed(await ReleaseOfAsync(f, row.TaskId))
                .ShouldBeTrue(label + " neighbor");
        }
        var targetRelease = await ReleaseOfAsync(f, target.TaskId);
        var targetPark = await ParkOfAsync(f, target.TaskId);
        var agentAfter = await TargetAgentAsync(f, target);
        await using (var db = f.Db())
        {
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == target.SessionId);
            if (world.Fault && !world.Recover)
            {
                // Nothing the failed transaction wrote is visible to a fresh context.
                targetRelease.State.ShouldBe(RunnerSeatReleaseState.Unresolved, label);
                targetRelease.ConfirmedAt.ShouldBeNull(label);
                targetPark.State.ShouldBe(AgentTaskParkState.ReleasePending, label);
                session.Status.ShouldBe(SessionStatus.Running, label);
                agentAfter.Status.ShouldBe(agentBefore.Status, label);
                agentAfter.UpdatedAt.ShouldBe(agentBefore.UpdatedAt, label);
            }
            else
            {
                TerminalRunnerSeatReleaseService.IsConfirmed(targetRelease).ShouldBeTrue(label);
                targetRelease.OutcomeCode.ShouldBe(world.Recover
                    ? nameof(TerminalSeatReleaseOutcome.AlreadyAbsent) : nameof(TerminalSeatReleaseOutcome.Released), label);
                targetPark.State.ShouldBe(AgentTaskParkState.Parked, label);
                session.Status.ShouldBe(SessionStatus.Stopped, label);
                agentAfter.Status.ShouldBe(AgentStatus.Stopped, label);
            }
        }
        var tallies = fault.Tallies;
        var recoveredBeforeTally = recovered && tallies.Count == 3 && fault.RecoveryLast < tallies[1].First;
        Console.WriteLine(
            $"C1147 {label} visited={result.Visited} registered={result.Registered} released={result.Released} confirmed={await ConfirmedCountAsync(f)} confirmWrites={fault.ConfirmWrites} rollbacks={fault.Rollbacks} sweep={fault.SweepCommands.Count} recovery={fault.RecoveryCommands.Count} tallies={string.Join(',', tallies.Select(t => t.Commands.Count))}");
        return new ConfirmCommitMeasurement(world, result, await ConfirmedCountAsync(f),
            f.Wire.ConditionalCommands - releaseCommandsBefore, f.Wire.ForceCommands, fault.ConfirmWrites, fault.Rollbacks,
            fault.SweepCommands, fault.RecoveryCommands, tallies, recoveredBeforeTally);
    }

    /// <summary>
    /// Real accepted-answer recovery in another scope, after the failed transaction disposed and
    /// AdvanceAsync released the queue gate. It has no legacy accounting set of its own.
    /// </summary>
    private static async Task RecoverElsewhereAsync(RunnerSeatReleaseFixture f, ConfirmCommitFault fault,
        TerminalRunnerSeatReleaseService sweep, AppDbContext sweepDb, Row target, string label, CancellationToken ct)
    {
        var release = await ReleaseOfAsync(f, target.TaskId);
        release.State.ShouldBe(RunnerSeatReleaseState.Unresolved, label);
        sweepDb.Database.CurrentTransaction.ShouldBeNull(label);
        using var other = f.Harness.Provider.CreateScope();
        var recovery = other.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>();
        var recoveryDb = other.ServiceProvider.GetRequiredService<AppDbContext>();
        ReferenceEquals(recovery, sweep).ShouldBeFalse(label);
        ReferenceEquals(recoveryDb, sweepDb).ShouldBeFalse(label);
        recovery.BoundaryAsync.ShouldBeNull(label);
        fault.Recovery = recoveryDb;
        f.Directory.Inventory = () => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
        try { await recovery.ReconcileAcceptedAnswerAsync(release.Id, ct); }
        finally
        {
            f.Directory.Inventory = null;
            fault.Recovery = null;
        }
        TerminalRunnerSeatReleaseService.IsConfirmed(await ReleaseOfAsync(f, target.TaskId)).ShouldBeTrue(label);
    }

    private static async Task<RunnerSeatRelease> ReleaseOfAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        await using var db = f.Db();
        return await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.TaskId == taskId);
    }

    private static async Task<Agent> TargetAgentAsync(RunnerSeatReleaseFixture f, Row row)
    {
        await using var db = f.Db();
        var agentId = (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.TaskId)).AgentId;
        return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == agentId);
    }

    private static bool IsConfirmWrite(string sql) =>
        sql.StartsWith("UPDATE \"RunnerSeatReleases\"", StringComparison.Ordinal)
        && sql.Contains("\"ConfirmedAt\"", StringComparison.Ordinal);

    /// <summary>
    /// Rolls back, once, the sweep transaction that locked the target task and wrote its
    /// Confirmed row, then throws from TransactionCommitting. Reservation, Unresolved intent,
    /// cursor and fixture writes are never faulted. Records the sweep and recovery contexts'
    /// statements apart, and each tally window from BeforeAttentionPublish to the cursor write.
    /// </summary>
    private sealed class ConfirmCommitFault : DbCommandInterceptor, IDbTransactionInterceptor
    {
        internal sealed record Tally(int First, List<string> Commands);

        private readonly Lock _gate = new();
        private readonly List<string> _sweep = [];
        private readonly List<string> _recovery = [];
        private readonly List<Tally> _tallies = [];
        private Tally? _open;
        private DbTransaction? _locked;
        private DbTransaction? _confirm;

        public Guid? Target { get; set; }
        public DbContext? Sweep { get; set; }
        public DbContext? Recovery { get; set; }
        public int ConfirmWrites { get; private set; }
        public int Rollbacks { get; private set; }
        public int Sequence { get; private set; }
        public int RecoveryLast { get; private set; } = -1;
        public IReadOnlyList<string> SweepCommands { get { lock (_gate) return _sweep.ToArray(); } }
        public IReadOnlyList<string> RecoveryCommands { get { lock (_gate) return _recovery.ToArray(); } }
        public IReadOnlyList<Tally> Tallies { get { lock (_gate) return _tallies.ToArray(); } }

        public void OpenTally()
        {
            lock (_gate) _open = new Tally(Sequence, []);
        }

        private void Record(DbCommand command, CommandEventData data)
        {
            lock (_gate)
            {
                var sql = command.CommandText;
                if (data.Context is { } context && ReferenceEquals(context, Recovery))
                {
                    _recovery.Add(sql);
                    RecoveryLast = Sequence++;
                    return;
                }
                if (data.Context is null || !ReferenceEquals(data.Context, Sweep)) return;
                _sweep.Add(sql);
                Sequence++;
                if (_open is not null)
                {
                    if (sql.Contains("\"BlockedTaskParkReclaimCursors\"", StringComparison.Ordinal))
                    {
                        _tallies.Add(_open);
                        _open = null;
                    }
                    else
                        _open.Commands.Add(sql);
                }
                if (Target is not Guid target || Rollbacks > 0 || command.Transaction is not { } tx) return;
                if (sql.Contains("FROM \"AgentTasks\"", StringComparison.Ordinal)
                    && sql.Contains("FOR UPDATE", StringComparison.Ordinal)
                    && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == target))
                    _locked = tx;
                else if (IsConfirmWrite(sql) && ReferenceEquals(tx, _locked))
                {
                    ConfirmWrites++;
                    _confirm = tx;
                }
            }
        }

        public async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            bool fire;
            lock (_gate)
            {
                fire = _confirm is not null && ReferenceEquals(transaction, _confirm) && Rollbacks == 0;
                if (fire)
                {
                    _confirm = null;
                    _locked = null;
                }
            }
            if (!fire) return result;
            await transaction.RollbackAsync(cancellationToken);
            lock (_gate) Rollbacks++;
            throw new IOException("c1147 confirmation commit rolled back");
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command, eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command, eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Record(command, eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Record(command, eventData);
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<DateTime?> ConfirmedStampAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        await using var db = f.Db();
        return (await db.RunnerSeatReleases.AsNoTracking().SingleAsync(r => r.TaskId == taskId)).ConfirmedAt;
    }

    private sealed record VisitMeasurement(
        LegacyReclaimResult Result, List<Guid> Listed, Guid? Cursor, int Statements);

    private sealed record ReleaseMeasurement(
        LegacyReclaimResult Run1, LegacyReclaimResult Run2, LegacyReclaimResult Negative,
        int ConfirmedAfterRun1, int ConfirmedAfterRun2, int ConfirmedAfterNegative,
        int Version2AfterRun1, int Version2AfterRun2, int ForceAfterRun2, int StatementsRun2);

    private static async Task<VisitMeasurement> MeasureVisitsAsync(bool shrink)
    {
        var counter = new CountingCommandInterceptor();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter));
        f.Wire.Qualified = Idle(TimeSpan.Zero, null);
        for (var i = 0; i < 5; i++)
            await SeedBlockedAsync(f, (shrink ? "shrink-" : "open-") + i);
        var working = await SeedBlockedAsync(f, "working-seat", status: AgentTaskStatus.Working, completed: false);
        (await f.ReclaimResultAsync(2, 1)).Visited.ShouldBe(2, "V-4");

        var listed = new List<Guid>();
        var flipped = false;
        var statementsAt = counter.Commands.Count;
        var result = await f.ReclaimResultAsync(2, 3, async (name, _) =>
        {
            if (!name.StartsWith("ReclaimList:", StringComparison.Ordinal)) return;
            listed.Add(Guid.Parse(name["ReclaimList:".Length..]));
            if (!shrink || flipped) return;
            flipped = true;
            await using var db = f.Db();
            var highest = await db.AgentTasks.AsNoTracking()
                .Where(t => t.Status == AgentTaskStatus.Blocked)
                .OrderByDescending(t => t.Id).Select(t => t.Id).Take(2).ToListAsync();
            await db.AgentTasks.Where(t => highest.Contains(t.Id)).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        });
        var statements = counter.Commands.Count - statementsAt;
        var cursor = (await CursorAsync(f))?.AfterTaskId;
        Console.WriteLine(
            $"C1129 visits shrink={shrink} rows=6 visited={result.Visited} eligible={result.Eligible} cap={result.Cap} listed={listed.Count} distinct={listed.Distinct().Count()} statements={statements}");
        await SessionRunningAsync(f, working.SessionId, "V-4");
        await using (var db = f.Db())
            (await db.AgentTasks.SingleAsync(t => t.Id == working.TaskId)).Status
                .ShouldBe(AgentTaskStatus.Working, "V-4");
        listed.ShouldNotContain(working.TaskId, "V-4");
        return new VisitMeasurement(result, listed, cursor, statements);
    }

    private static async Task<ReleaseMeasurement> MeasureReleasesAsync()
    {
        var counter = new CountingCommandInterceptor();
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, reclaim: true,
            configureDb: options => options.AddInterceptors(counter));
        f.Wire.Qualified = Idle(TimeSpan.Zero, null);
        await f.EditAsync((task, _) => task.Role = AgentTaskRole.Code);
        await f.CreateSourceAsync();
        await CommitTipAsync(f);
        await AncientBlockAsync(f, f.TaskId);
        ArmPathVerifier(f);
        var second = await SeedBlockedAsync(f, "second");
        await f.CreateReclaimSourceAsync(second.TaskId, second.SessionId);
        await AncientBlockAsync(f, second.TaskId);
        var dirty = await SeedBlockedAsync(f, "dirty");
        var dirtyPath = await f.CreateReclaimSourceAsync(dirty.TaskId, dirty.SessionId, dirty: true);

        (await f.ReclaimResultAsync(32, 1)).Released.ShouldBe(0, "V-3");
        var anchor = new[]
        {
            (await ParkOfAsync(f, f.TaskId)).CreatedAt,
            (await ParkOfAsync(f, second.TaskId)).CreatedAt,
            (await ParkOfAsync(f, dirty.TaskId)).CreatedAt
        }.Max();
        AdvanceTo(f, anchor, TimeSpan.FromMilliseconds(120_001));
        await ArmIdleAsync(f, f.TaskId, f.SessionId);
        await ArmIdleAsync(f, second.TaskId, second.SessionId);
        await ArmIdleAsync(f, dirty.TaskId, dirty.SessionId);

        var run1 = await f.ReclaimResultAsync(32, 1);
        var confirmedAfterRun1 = await ConfirmedCountAsync(f);
        var version2AfterRun1 = f.Wire.Requests.Count(r => r.ParkVersion == 2);
        Released(f, f.SessionId).ShouldBeTrue("V-3");
        Released(f, second.SessionId).ShouldBeTrue("V-3");
        Released(f, dirty.SessionId).ShouldBeFalse("V-3");
        (await ParkOfAsync(f, dirty.TaskId)).ReasonCode.ShouldBe("park_dirty", "V-3");
        await SessionRunningAsync(f, dirty.SessionId, "V-3");
        Directory.Exists(dirtyPath).ShouldBeTrue("V-3");
        f.Wire.ForceCommands.ShouldBe(0, "V-3");

        f.Clock.Advance(TimeSpan.FromSeconds(120));
        var statementsAt = counter.Commands.Count;
        var run2 = await f.ReclaimResultAsync(32, 1);
        var statementsRun2 = counter.Commands.Count - statementsAt;
        var confirmedAfterRun2 = await ConfirmedCountAsync(f);
        var version2AfterRun2 = f.Wire.Requests.Count(r => r.ParkVersion == 2);
        Console.WriteLine(
            $"C1129 releases rows=3 run1Released={run1.Released} run2Visited={run2.Visited} run2Registered={run2.Registered} run2Released={run2.Released} statementsRun2={statementsRun2}");
        await SessionRunningAsync(f, dirty.SessionId, "V-3");

        var third = await SeedBlockedAsync(f, "third");
        await f.CreateReclaimSourceAsync(third.TaskId, third.SessionId);
        await AncientBlockAsync(f, third.TaskId);
        var registeredThird = await f.ReclaimResultAsync(32, 1);
        Console.WriteLine($"C1129 registerThird released={registeredThird.Released}");
        var thirdPark = await ParkOfAsync(f, third.TaskId);
        AdvanceTo(f, thirdPark.CreatedAt, TimeSpan.FromMilliseconds(120_001));
        await ArmIdleAsync(f, third.TaskId, third.SessionId);
        var negative = await f.ReclaimResultAsync(32, 1);
        var confirmedAfterNegative = await ConfirmedCountAsync(f);
        Released(f, third.SessionId).ShouldBeTrue("V-3");
        f.Wire.ForceCommands.ShouldBe(0, "V-3");
        Console.WriteLine(
            $"C1129 negative released={negative.Released} confirmed={confirmedAfterNegative} version2={f.Wire.Requests.Count(r => r.ParkVersion == 2)}");
        registeredThird.Released.ShouldBe(0, "V-3");
        return new ReleaseMeasurement(
            run1, run2, negative, confirmedAfterRun1, confirmedAfterRun2, confirmedAfterNegative,
            version2AfterRun1, version2AfterRun2, f.Wire.ForceCommands, statementsRun2);
    }

    private static void ArmPathVerifier(RunnerSeatReleaseFixture f)
    {
        var verifier = new RunnerWorkspaceParkService();
        f.Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: { } receipt }
            && (await verifier.VerifySessionCheckoutAsync(receipt, receipt.Request.Path, default)).Receipt is not null;
    }

    private static async Task AncientBlockAsync(RunnerSeatReleaseFixture f, Guid taskId)
    {
        var ancientAt = f.Now.AddHours(-30);
        await using var db = f.Db();
        await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.CompletedAt, ancientAt));
        await db.AgentTaskEvents.Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.At, ancientAt));
    }

    private static async Task ArmIdleAsync(RunnerSeatReleaseFixture f, Guid taskId, Guid sessionId)
    {
        var created = (await ParkOfAsync(f, taskId)).CreatedAt;
        f.Wire.BySession[sessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(created));
    }

    private static async Task<int> ConfirmedCountAsync(RunnerSeatReleaseFixture f)
    {
        await using var ledger = f.Db();
        return (await ledger.RunnerSeatReleases.AsNoTracking().ToListAsync())
            .Count(TerminalRunnerSeatReleaseService.IsConfirmed);
    }

    [Test]
    public async Task C1108_AdvanceVerifiesSourceOnceWithoutBoundary()
    {
        await using (var plain = await ReadyLegacyReleaseAsync())
        {
            await plain.ReclaimAsync(32, 1);
            // Two publication-gate verifies stay. AdvanceAsync adds one, or two when a boundary is installed.
            await AssertDispatchedReleaseAsync(plain, verifyCalls: 3, "G-11");
        }

        await using (var cut = await ReadyLegacyReleaseAsync())
        {
            await cut.ReclaimAsync(32, 1, (_, _) => Task.CompletedTask);
            await AssertDispatchedReleaseAsync(cut, verifyCalls: 4, "V-6");
        }
    }

    private static async Task AssertDispatchedReleaseAsync(RunnerSeatReleaseFixture f, int verifyCalls, string label)
    {
        Console.WriteLine($"C1108 verify label={label} calls={f.Wire.VerifyCalls}");
        Released(f, f.SessionId).ShouldBeTrue(label);
        f.Wire.Requests.Count(r => r.ParkVersion == 2).ShouldBe(1, label);
        f.Wire.ForceCommands.ShouldBe(0, label);
        f.Wire.VerifyCalls.ShouldBe(verifyCalls, label);
        (await ParkOfAsync(f, f.TaskId)).State.ShouldBe(AgentTaskParkState.Parked, label);
        await using var ledger = f.Db();
        (await ledger.RunnerSeatReleases.AsNoTracking().ToListAsync())
            .Count(TerminalRunnerSeatReleaseService.IsConfirmed).ShouldBe(1, label);
    }

    private static async Task<RunnerSeatReleaseFixture> ReadyLegacyReleaseAsync()
    {
        var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true, reclaim: true);
        try
        {
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
            var verifier = new RunnerWorkspaceParkService();
            f.Wire.VerifySource = async command => command is { ParkVersion: 2, Publication: { } receipt }
                && (await verifier.VerifySessionCheckoutAsync(receipt, receipt.Request.Path, default)).Receipt is not null;
            (await f.ReclaimAsync(32, 1)).ShouldBeGreaterThan(0, "V-6");
            var park = await ParkOfAsync(f, f.TaskId);
            park.LegacyDiscovery.ShouldBeTrue("V-6");
            park.State.ShouldNotBe(AgentTaskParkState.Parked, "V-6");
            AdvanceTo(f, park.CreatedAt, TimeSpan.FromMilliseconds(120_001));
            f.Wire.BySession[f.SessionId] = Idle(TimeSpan.FromMilliseconds(120_001), AsUtc(park.CreatedAt));
            f.Wire.VerifyCalls = 0;
            return f;
        }
        catch
        {
            await f.DisposeAsync();
            throw;
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

    private static int Observations(RunnerSeatReleaseFixture f, Guid sessionId) =>
        f.Wire.Calls.Count(path => path.Contains(sessionId.ToString("D"), StringComparison.Ordinal)
            && path.EndsWith("/terminal-seat-observation", StringComparison.Ordinal));

    private static int StatusVisits(TaskParkPublicationTests.ParkGit git) =>
        git.Commands.Count(command => command.Length > 0 && command[0] == "status");

    private static void SameInstant(DateTime? actual, DateTime expected, string label)
    {
        actual.ShouldNotBeNull(label);
        DateTime.SpecifyKind(actual.Value, DateTimeKind.Utc)
            .ShouldBe(DateTime.SpecifyKind(expected, DateTimeKind.Utc), label);
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
