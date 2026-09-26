using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0711: the race retry against the in-memory git double. No process spawns.</summary>
[Category("Integration")]
public sealed class AgentTaskLandTargetRaceFakeTests
{
    [Test]
    public async Task C711_RaceRetriesInTheFakeHarness()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var raced = false;
        h.Fault.AfterAcknowledged = phase =>
        {
            if (phase == LandPhase.Verified && !raced)
            {
                raced = true;
                h.Git.RewriteRemoteAwayFromSource();
            }
            return Task.CompletedTask;
        };

        await h.RequestAsync();
        await h.RunQueuedAsync();

        h.Git.OwnedTrace.Count(a => a.Contains("rebase") && !a.Contains("--abort")).ShouldBe(2);
        h.Git.Trace.Count(a => a.Length > 0 && a[0] == "push").ShouldBe(1);
        h.Verifier.Calls.ShouldBe(2);
        await using var db = h.CreateContext();
        var ops = await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == h.Git.TaskId).OrderBy(o => o.CreatedAt).ToListAsync();
        ops.Count.ShouldBe(2);
        ops[0].Phase.ShouldBe(LandPhase.Refused);
        ops[0].LastReason.ShouldBe("remote_changed_before_push");
        ops[1].Publication.ShouldBe(LandPublicationOutcome.Landed);
        h.Git.RemoteTarget.ShouldBe(ops[1].VerifiedSourceSha);
        var warning = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.AgentTaskId == h.Git.TaskId && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("retry 1 of 2");
    }

    [Test]
    [Arguments("remote")]
    [Arguments("branch")]
    [Arguments("push-rejected")]
    public async Task C711_LegacyAdvancedRowsBecomeTerminal(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "schema-2 rebase moved the branch here");
        var rebased = h.Git.SourceHead;
        var (seeded, _) = await AgentTaskLandRecoveryTests.SeedSchemaTwoAsync(h, "local-target-advanced", original, rebased);
        if (change == "remote") h.Git.RewriteRemoteAwayFromSource();
        if (change == "branch") h.Git.RewindSource(original);
        if (change == "push-rejected")
        {
            var pushes = 0;
            h.Git.BeforeCommand = (_, a) =>
            {
                if (a.Count > 0 && a[0] == "push" && pushes++ == 0)
                    return Task.FromResult<LandingGitResult?>(new LandingGitResult(1, "", "! [remote rejected] master -> master (pre-receive hook declined)"));
                return Task.FromResult<LandingGitResult?>(null);
            };
        }

        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == seeded.Id);
        if (change == "push-rejected")
        {
            op.Phase.ShouldBe(LandPhase.PushStarted);
            op.Active.ShouldBeTrue();
            op.LastReason.ShouldBe("push_rejected");
            return;
        }

        op.Phase.ShouldBe(LandPhase.Refused);
        op.Publication.ShouldBe(LandPublicationOutcome.Refused);
        op.LastReason.ShouldBe(change == "remote" ? "remote_changed_before_push" : "source_changed");
        h.Git.Commands.ShouldNotContain(c => c.Arguments.Length > 0 && c.Arguments[0] == "push");

        var ahead = await h.RequestAsync(expectedSourceSha: original);
        await h.RunQueuedAsync();
        await using var aheadDb = h.CreateContext();
        var refused = await aheadDb.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == ahead.RequestId && e.IsLandTerminal);
        refused.Detail.ShouldContain("target_local_ahead");
        refused.Detail.ShouldContain("git reset --hard origin/master");
        refused.Detail.ShouldNotContain("pull --rebase");
        (await aheadDb.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == ahead.RequestId))
            .SourceRefusalReason.ShouldBe("target_local_ahead");
        (await aheadDb.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(1);

        await h.Git.RequiredAsync(h.Git.Repository, "update-ref", h.Git.TargetRef, h.Git.RemoteTarget, rebased);
        var landed = await h.RequestAsync(expectedSourceSha: original);
        await h.RunQueuedAsync();
        await using var landedDb = h.CreateContext();
        var ops = await landedDb.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == h.Git.TaskId).OrderBy(o => o.CreatedAt).ToListAsync();
        ops.Count.ShouldBe(2);
        ops[1].SchemaVersion.ShouldBe(3);
        ops[1].Publication.ShouldBe(LandPublicationOutcome.Landed);
        if (change == "remote")
        {
            ops[1].PreparationInputSha.ShouldBe(rebased);
            ops[1].PreviousPreparationOperationId.ShouldBe(seeded.Id);
        }
        else
        {
            ops[1].PreparationInputSha.ShouldBe(original);
            ops[1].PreviousPreparationOperationId.ShouldBeNull();
        }
        _ = landed;
    }

    [Test]
    public async Task C711_RetryRestartsTheProgressClock()
    {
        var start = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        await using var h = new LandingProtocolHarness { Clock = clock };
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var aged = false;
        DateTimeOffset raceAt = default;
        var barrierHit = false;
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (phase != LandPhase.Verified || aged) return;
            aged = true;
            clock.Advance(TimeSpan.FromSeconds(h.LandSettings.LandWarningSeconds + 5));
            await SweepAsync(h, clock);
            await using var db = h.CreateContext();
            (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Git.TaskId && r.IsPending))
                .WarningAt.ShouldNotBeNull();
            h.Git.RewriteRemoteAwayFromSource();
            raceAt = clock.GetUtcNow();
        };
        h.Verifier.Barrier = async () =>
        {
            if (h.Verifier.Calls < 2) return;
            await using var db = h.CreateContext();
            var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Git.TaskId && r.IsPending);
            request.HighestProgress.ShouldBe((int)LandPhase.Prepared);
            request.LastProgressAt.ShouldBe(raceAt.UtcDateTime);
            request.WarningAt.ShouldBeNull();
            request.ErrorAt.ShouldBeNull();
            (await AgedAsync(h, request.Id)).ShouldBe(1);
            clock.Advance(TimeSpan.FromSeconds(h.LandSettings.LandWarningSeconds - 1));
            await SweepAsync(h, clock);
            (await AgedAsync(h, request.Id)).ShouldBe(1);
            clock.Advance(TimeSpan.FromSeconds(2));
            await SweepAsync(h, clock);
            (await AgedAsync(h, request.Id)).ShouldBe(2);
            barrierHit = true;
        };

        await h.RequestAsync();
        await h.RunQueuedAsync();

        barrierHit.ShouldBeTrue();
        (await h.OperationAsync())!.Publication.ShouldBe(LandPublicationOutcome.Landed);
    }

    [Test]
    public async Task C711_NonRaceRefusalIsNotReplacedBySameRequest()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Verifier.Passed = false;
        var requested = await h.RequestAsync(expectedSourceSha: source);
        await h.RunQueuedAsync();
        var refused = (await h.OperationAsync()).ShouldNotBeNull();
        refused.Phase.ShouldBe(LandPhase.Refused);
        refused.LastReason.ShouldBe("verification_failed");

        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == requested.RequestId);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            // The refusal and its terminal event commit together. Drop that event so the same request
            // can re-enter, which is the crash-before-terminal shape the guard describes.
            var notes = await db.AgentTaskLandNotifications.Where(n => n.RequestId == request.Id).ToListAsync();
            db.RemoveRange(notes);
            await db.SaveChangesAsync();
            var terminal = await db.AgentTaskEvents.SingleAsync(e => e.Id == request.TerminalEventId);
            db.Remove(terminal);
            request.IsPending = true;
            request.State = LandRequestState.Queued;
            request.TerminalEventId = null;
            task.CurrentLandRequestId = request.Id;
            task.LandRequestedAt = request.RequestedAt;
            task.LandAttempt = request.Attempt;
            task.Status = AgentTaskStatus.Succeeded;
            await db.SaveChangesAsync();
        }
        h.Verifier.Passed = true;

        await h.RunAsync();

        await using var after = h.CreateContext();
        var ops = await after.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == h.Git.TaskId).ToListAsync();
        ops.Count.ShouldBe(1);
        ops[0].Id.ShouldBe(refused.Id);
        ops[0].Phase.ShouldBe(LandPhase.Refused);
        ops[0].LastReason.ShouldBe("verification_failed");
        h.Verifier.Calls.ShouldBe(1);
        h.Git.Trace.ShouldNotContain(a => a.Length > 0 && a[0] == "push");
        (await after.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("raced with a push"))).ShouldBe(0);
        var terminal = await after.AgentTaskEvents.AsNoTracking()
            .Where(e => e.LandRequestId == requested.RequestId && e.IsLandTerminal).OrderBy(e => e.At).LastAsync();
        terminal.Detail.ShouldContain("verification_failed");
    }

    private static Task SweepAsync(LandingProtocolHarness h, TimeProvider clock) =>
        new AgentTaskLandMonitorService(h.CreateContext(), clock, Options.Create(h.LandSettings), new MockEventBus())
            .SweepAsync(CancellationToken.None);

    private static async Task<int> AgedAsync(LandingProtocolHarness h, Guid requestId)
    {
        await using var db = h.CreateContext();
        return await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == requestId && n.Kind == LandNotificationKind.Aged);
    }
}
