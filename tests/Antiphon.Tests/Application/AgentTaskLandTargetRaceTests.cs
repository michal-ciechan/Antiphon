using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0711: a push that races a schema-3 land is rebased again inside the same request.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandTargetRaceTests
{
    [Test]
    public async Task C711_PushWindowRaceLandsInOneRequest()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        await MoveRemoteMasterAsync(f);
        var pushes = 0;
        string? intruder = null;
        int? retryRebase = null;
        var barrierHit = false;
        f.Git.BeforeCommand = async (_, a) =>
        {
            if (a.Count > 0 && a[0] == "push" && pushes++ < 1)
                intruder = await f.PushIndependentAsync("intruder-1");
            // The raced attempt's own push is already in the trace. Commands from B's rebase onward
            // are the retry: the repository directory is also where `git push origin` runs, so A's
            // rejected push must not be read as a local master mutation.
            if (retryRebase is null && pushes > 0 && a.Contains("rebase") && !a.Contains("--abort"))
                retryRebase = f.Git.Commands.Count - 1;
            return null;
        };
        h.Verifier.Barrier = async () =>
        {
            if (h.Verifier.Calls < 2) return;
            (await Read(f, f.Source, "rev-parse", "HEAD")).ShouldBe(reviewed);
            (await Read(f, f.Repository, "rev-parse", f.TargetRef)).ShouldBe(f.SeedSha);
            retryRebase.ShouldNotBeNull();
            foreach (var command in f.Git.Commands.Skip(retryRebase!.Value))
                MutatesLocalBranch(f, command.Directory, command.Arguments).ShouldBeFalse(string.Join(' ', command.Arguments));
            barrierHit = true;
        };

        var requested = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        barrierHit.ShouldBeTrue();
        intruder.ShouldNotBeNull();
        await using var db = h.CreateContext();
        var events = await EventsAsync(db, requested.RequestId);
        events.Select(e => e.Type).ShouldBe(new[] { AgentTaskEventType.LandRequested, AgentTaskEventType.Warning, AgentTaskEventType.Landed });
        var ops = await OperationsAsync(db, f.TaskId);
        ops.Count.ShouldBe(2);
        var warning = events[1];
        warning.Detail.ShouldContain("raced with a push to origin:refs/heads/master (retry 1 of 2)");
        warning.LandRequestId.ShouldBe(requested.RequestId);
        warning.LandingOperationId.ShouldBe(ops[0].Id);
        AssertRaced(ops[0], pushExit: 1);
        AssertLanded(ops[1], ops[0], intruder!, reviewed, requested.RequestId);
        h.Verifier.Calls.ShouldBe(2);
        h.Verifier.Invocations[^1].ShouldBe((ops[1].LandWorktreePath, null));
        (await Read(f, f.Remote, "rev-parse", f.TargetRef)).ShouldBe(ops[1].VerifiedSourceSha);
        var ancestor = await new LandingGitFixture.FixtureGit(Path.Combine(f.Root, "home"), f.TaskId)
            .RunAsync(f.Remote, ["merge-base", "--is-ancestor", intruder!, ops[1].VerifiedSourceSha!], CancellationToken.None);
        ancestor.ExitCode.ShouldBe(0);
        (await Read(f, f.Repository, "rev-parse", f.TargetRef)).ShouldBe(ops[1].VerifiedSourceSha);
        events[2].Detail.ShouldContain("canonical=advanced");
        var notes = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.RequestId == requested.RequestId).ToListAsync();
        notes.Count.ShouldBe(1);
        notes[0].Kind.ShouldBe(LandNotificationKind.Outcome);
        notes[0].SourceEventId.ShouldBe(events[2].Id);
    }

    [Test]
    public async Task C711_PreObservationRaceLandsInOneRequest()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        var reads = 0;
        string? intruder = null;
        f.Git.BeforeCommand = async (_, a) =>
        {
            if (a.Count > 0 && a[0] == "ls-remote" && a[^1] == f.TargetRef && ++reads == 2)
                intruder = await f.PushIndependentAsync("intruder-pre");
            return null;
        };

        var requested = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        intruder.ShouldNotBeNull();
        f.Git.Trace.Count(a => a.Length > 0 && a[0] == "push").ShouldBe(1);
        await using var db = h.CreateContext();
        var events = await EventsAsync(db, requested.RequestId);
        events.Select(e => e.Type).ShouldBe(new[] { AgentTaskEventType.LandRequested, AgentTaskEventType.Warning, AgentTaskEventType.Landed });
        events[1].Detail.ShouldContain("raced with a push to origin:refs/heads/master (retry 1 of 2)");
        var ops = await OperationsAsync(db, f.TaskId);
        ops.Count.ShouldBe(2);
        AssertRaced(ops[0], pushExit: null);
        ops[0].PushStartedAt.ShouldBeNull();
        AssertLanded(ops[1], ops[0], intruder!, reviewed, requested.RequestId);
    }

    [Test]
    [Arguments(2)]
    [Arguments(0)]
    public async Task C711_BudgetSpentRefusesAndNewRequestLands(int budget)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        h.LandSettings.LandTargetRaceRetries = budget;
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        var pushes = 0;
        f.Git.BeforeCommand = async (_, a) =>
        {
            if (a.Count > 0 && a[0] == "push" && pushes++ < budget + 1)
                await f.PushIndependentAsync($"intruder-{pushes}");
            return null;
        };

        var first = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var terminal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == first.RequestId && e.IsLandTerminal);
        terminal.Type.ShouldBe(AgentTaskEventType.LandRefused);
        terminal.Detail.ShouldContain("remote_changed_before_push");
        if (budget == 0) terminal.Detail.ShouldNotContain("automatic rebases");
        else terminal.Detail.ShouldContain($"remote_changed_before_push; after {budget} automatic rebases (Delegation:LandTargetRaceRetries={budget})");
        var ops = await OperationsAsync(db, f.TaskId);
        ops.Count.ShouldBe(budget + 1);
        ops.ShouldAllBe(o => o.Phase == LandPhase.Refused && o.LastReason == "remote_changed_before_push");
        ops.Count(o => o.Active).ShouldBe(1);
        ops[^1].Active.ShouldBeTrue();
        var warnings = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.LandRequestId == first.RequestId && e.Type == AgentTaskEventType.Warning)
            .OrderBy(e => e.At).ToListAsync();
        warnings.Count.ShouldBe(budget);
        for (var i = 0; i < budget; i++)
            warnings[i].Detail.ShouldContain($"retry {i + 1} of {budget}");
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId);
        task.LandAttempt.ShouldBe(1);
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId)).Attempt.ShouldBe(1);
        (await Read(f, f.Source, "rev-parse", "HEAD")).ShouldBe(reviewed);
        (await Read(f, f.Repository, "rev-parse", f.TargetRef)).ShouldBe(f.SeedSha);

        f.Git.BeforeCommand = null;
        var second = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        await using var after = h.CreateContext();
        var landed = await after.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == second.RequestId && e.IsLandTerminal);
        landed.Type.ShouldBe(AgentTaskEventType.Landed);
        var all = await OperationsAsync(after, f.TaskId);
        all.Count.ShouldBe(budget + 2);
        all[^1].Publication.ShouldBe(LandPublicationOutcome.Landed);
        all[^1].ApprovalLandRequestId.ShouldBe(first.RequestId);
        (await after.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId)).LandAttempt.ShouldBe(1);
        (await after.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == second.RequestId)).Attempt.ShouldBe(1);
    }

    [Test]
    public async Task C711_PushRejectedWithoutMovementStaysResumable()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        // A rebase onto the seed is a no-op and skips verification. Move master first so the candidate is verified once.
        await f.RequiredAsync(f.Repository, "commit", "--allow-empty", "-m", "base moved");
        await f.RequiredAsync(f.Repository, "push", "origin", f.TargetRef);
        var pushes = 0;
        f.Git.BeforeCommand = (_, a) =>
        {
            if (a.Count > 0 && a[0] == "push" && pushes++ == 0)
                return Task.FromResult<LandingGitResult?>(new LandingGitResult(1, "", "! [remote rejected] master -> master (pre-receive hook declined)"));
            return Task.FromResult<LandingGitResult?>(null);
        };

        var first = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var refused = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == first.RequestId && e.IsLandTerminal);
        refused.Detail.ShouldContain("push_rejected");
        // Every refusal already writes "Landing not confirmed". The race retry is the warning that must be absent.
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("raced with a push"))).ShouldBe(0);
        var op = (await OperationsAsync(db, f.TaskId)).ShouldHaveSingleItem();
        op.Phase.ShouldBe(LandPhase.PushStarted);
        op.Active.ShouldBeTrue();
        op.PushExitCode.ShouldBe(1);
        op.Publication.ShouldBe(LandPublicationOutcome.Unconfirmed);
        h.Verifier.Calls.ShouldBe(1);

        var second = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        await using var after = h.CreateContext();
        var events = await EventsAsync(after, second.RequestId);
        events.ShouldContain(e => e.Type == AgentTaskEventType.Landed);
        var same = (await OperationsAsync(after, f.TaskId)).ShouldHaveSingleItem();
        same.Id.ShouldBe(op.Id);
        h.Verifier.Calls.ShouldBe(1);
        (await Read(f, f.Remote, "rev-parse", f.TargetRef)).ShouldBe(same.VerifiedSourceSha);
    }

    [Test]
    public async Task C711_ConflictOnRetryRefusesAsConflict()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        var pushes = 0;
        string? intruder = null;
        f.Git.BeforeCommand = async (_, a) =>
        {
            if (a.Count > 0 && a[0] == "push" && pushes++ < 1)
                intruder = await f.PushIndependentAsync("intruder-conflict", "feature.txt", "intruder content\n");
            return null;
        };

        var requested = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        intruder.ShouldNotBeNull();
        await using var db = h.CreateContext();
        var ops = await OperationsAsync(db, f.TaskId);
        ops.Count.ShouldBe(2);
        AssertRaced(ops[0], pushExit: 1);
        ops[1].Phase.ShouldBe(LandPhase.Refused);
        ops[1].LastReason.ShouldBe("rebase_conflict");
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requested.RequestId);
        request.State.ShouldBe(LandRequestState.NeedsResolution);
        var conflict = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == requested.RequestId && e.Type == AgentTaskEventType.Conflicted);
        conflict.Detail.ShouldContain("feature.txt");
        f.Git.Trace.Count(a => a.Length > 0 && a[0] == "push").ShouldBe(1);
        (await Read(f, f.Remote, "rev-parse", f.TargetRef)).ShouldBe(intruder);
        (await Read(f, f.Source, "rev-parse", "HEAD")).ShouldBe(reviewed);
        (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == requested.RequestId && e.Type == AgentTaskEventType.Warning)).ShouldBe(1);
    }

    [Test]
    [Arguments("remote")]
    [Arguments("local")]
    public async Task C711_SourceMovedBeforeRetryRefuses(string where)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        await MoveRemoteMasterAsync(f);
        var pushes = 0;
        string? intruder = null;
        f.Git.BeforeCommand = async (_, a) =>
        {
            if (a.Count == 0 || a[0] != "push" || pushes++ >= 1) return null;
            intruder = await f.PushIndependentAsync($"intruder-{where}");
            var reader = new LandingGitFixture.FixtureGit(Path.Combine(f.Root, "home"), f.TaskId);
            if (where == "remote")
            {
                (await reader.RunAsync(f.Observer, ["fetch", "origin", f.SourceRef + ":refs/heads/race-source"], CancellationToken.None)).Succeeded.ShouldBeTrue();
                (await reader.RunAsync(f.Observer, ["checkout", "race-source"], CancellationToken.None)).Succeeded.ShouldBeTrue();
                (await reader.RunAsync(f.Observer, ["commit", "--allow-empty", "-m", "source moved"], CancellationToken.None)).Succeeded.ShouldBeTrue();
                (await reader.RunAsync(f.Observer, ["push", "origin", "HEAD:" + f.SourceRef], CancellationToken.None)).Succeeded.ShouldBeTrue();
            }
            else
            {
                (await reader.RunAsync(f.Source, ["commit", "--allow-empty", "-m", "local moved"], CancellationToken.None)).Succeeded.ShouldBeTrue();
            }
            return null;
        };

        var requested = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        intruder.ShouldNotBeNull();
        await using var db = h.CreateContext();
        var ops = await OperationsAsync(db, f.TaskId);
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requested.RequestId);
        if (where == "remote")
        {
            ops.Count.ShouldBe(2);
            ops[1].Phase.ShouldBe(LandPhase.Refused);
            ops[1].LastReason.ShouldBe("source_remote_changed");
        }
        else
        {
            ops.Count.ShouldBe(1);
            request.SourceRefusalReason.ShouldBe("source_changed");
        }
        ops[0].Phase.ShouldBe(LandPhase.Refused);
        ops[0].LastReason.ShouldBe("remote_changed_before_push");
        ops[0].PushExitCode.ShouldBe(1);
        ops[0].Active.ShouldBe(where == "local");
        var terminal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == requested.RequestId && e.IsLandTerminal);
        terminal.Type.ShouldBe(AgentTaskEventType.LandRefused);
        terminal.Detail.ShouldContain(where == "remote" ? "source_remote_changed" : "source_changed");
        (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == requested.RequestId && e.Type == AgentTaskEventType.Warning)).ShouldBe(1);
        f.Git.Trace.Count(a => a.Length > 0 && a[0] == "push").ShouldBe(1);
        (await Read(f, f.Remote, "rev-parse", f.TargetRef)).ShouldBe(intruder);
        h.Verifier.Calls.ShouldBe(1);
    }

    [Test]
    public void C711_RaceRetryBudgetIsValidated()
    {
        new DelegationSettings().LandTargetRaceRetries.ShouldBe(2);
        Validate(-1).Failed.ShouldBeTrue();
        Validate(-1).Failures.ShouldContain(f => f.Contains("LandTargetRaceRetries", StringComparison.Ordinal));
        Validate(6).Failed.ShouldBeTrue();
        Validate(0).Failed.ShouldBeFalse();
        Validate(5).Failed.ShouldBeFalse();
    }

    [Test]
    public async Task C711_CrashResumeAfterRaceRetries()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        await MoveRemoteMasterAsync(f);
        h.Fault.Phase = LandPhase.Verified;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        var intruder = await f.PushIndependentAsync("intruder-crash");
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RestartServicesAsync();

        await h.RunAsync();

        await using var db = h.CreateContext();
        var ops = await OperationsAsync(db, f.TaskId);
        ops.Count.ShouldBe(2);
        AssertRaced(ops[0], pushExit: null);
        ops[0].PushStartedAt.ShouldBeNull();
        ops[1].Publication.ShouldBe(LandPublicationOutcome.Landed);
        ops[1].TargetBeforeSha.ShouldBe(intruder);
        h.Verifier.Calls.ShouldBe(2);
        var requestId = (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == f.TaskId)).CurrentLandRequestId;
        var warnings = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.LandRequestId == requestId && e.Type == AgentTaskEventType.Warning).ToListAsync();
        warnings.Count.ShouldBe(1);
        warnings[0].Detail.ShouldContain("retry 1 of 2");
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requestId)).Attempt.ShouldBe(2);
        (await Read(f, f.Source, "rev-parse", "HEAD")).ShouldBe(reviewed);
    }

    [Test]
    public async Task C711_LocalMasterAheadNamesTheOperatorReset()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var f = h.Fixture;
        var reviewed = await h.AddSourceAsync();
        await f.RequiredAsync(f.Repository, "commit", "--allow-empty", "-m", "unpushed local");

        var requested = await h.RequestAsync(expectedSourceSha: reviewed);
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var terminal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.LandRequestId == requested.RequestId && e.IsLandTerminal);
        terminal.Type.ShouldBe(AgentTaskEventType.LandRefused);
        terminal.Detail.ShouldContain("target_local_ahead");
        terminal.Detail.ShouldContain("git fetch origin && git reset --hard origin/master");
        terminal.Detail.ShouldNotContain("pull --rebase");
        (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == requested.RequestId))
            .SourceRefusalReason.ShouldBe("target_local_ahead");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == f.TaskId)).ShouldBe(0);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(int value) =>
        new DelegationSettingsValidator().Validate(null, new DelegationSettings { LandTargetRaceRetries = value });

    private static void AssertRaced(AgentTaskLanding op, int? pushExit)
    {
        op.SchemaVersion.ShouldBe(3);
        op.Phase.ShouldBe(LandPhase.Refused);
        op.Publication.ShouldBe(LandPublicationOutcome.Refused);
        op.LastReason.ShouldBe("remote_changed_before_push");
        op.PushExitCode.ShouldBe(pushExit);
        op.Active.ShouldBeFalse();
    }

    private static void AssertLanded(AgentTaskLanding landed, AgentTaskLanding raced, string intruder, string reviewed, Guid requestId)
    {
        landed.Publication.ShouldBe(LandPublicationOutcome.Landed);
        landed.Phase.ShouldBe(LandPhase.Complete);
        landed.Active.ShouldBeTrue();
        landed.TargetBeforeSha.ShouldBe(intruder);
        landed.VerifiedSourceSha.ShouldBe(landed.RebasedSourceSha);
        landed.VerificationPassed.ShouldBeTrue();
        landed.VerificationSkipReason.ShouldBeNull();
        landed.OriginalSourceSha.ShouldBe(reviewed);
        landed.ReviewedSourceSha.ShouldBe(reviewed);
        landed.ApprovalLandRequestId.ShouldBe(raced.ApprovalLandRequestId);
        raced.ApprovalLandRequestId.ShouldBe(requestId);
        landed.ReviewEvidenceId.ShouldBe(raced.ReviewEvidenceId);
    }

    private static bool MutatesLocalBranch(LandingGitFixture f, string directory, string[] args) =>
        args.Length > 0
        && (LandingGit.PathsEqual(directory, f.Source) || LandingGit.PathsEqual(directory, f.Repository))
        && (args[0] is "rebase" or "merge" or "push" or "reset"
            || args.Contains("update-ref") && args.Contains(f.TargetRef));

    /// <summary>A rebase onto the seed skips verification. Publish a new tip, then put local master back.</summary>
    private static async Task MoveRemoteMasterAsync(LandingGitFixture f)
    {
        var reader = new LandingGitFixture.FixtureGit(Path.Combine(f.Root, "home"), f.TaskId);
        (await reader.RunAsync(f.Repository, ["commit", "--allow-empty", "-m", "base moved"], CancellationToken.None)).Succeeded.ShouldBeTrue();
        (await reader.RunAsync(f.Repository, ["push", "origin", "HEAD:" + f.TargetRef], CancellationToken.None)).Succeeded.ShouldBeTrue();
        (await reader.RunAsync(f.Repository, ["reset", "--hard", f.SeedSha], CancellationToken.None)).Succeeded.ShouldBeTrue();
    }

    private static async Task<string> Read(LandingGitFixture f, string path, params string[] args)
    {
        var reader = new LandingGitFixture.FixtureGit(Path.Combine(f.Root, "home"), f.TaskId);
        var result = await reader.RunAsync(path, args, CancellationToken.None);
        result.Succeeded.ShouldBeTrue(result.Diagnostic);
        return result.Output.Trim();
    }

    private static async Task<List<AgentTaskEvent>> EventsAsync(AppDbContext db, Guid requestId) =>
        await db.AgentTaskEvents.AsNoTracking().Where(e => e.LandRequestId == requestId).OrderBy(e => e.At).ToListAsync();

    private static async Task<List<AgentTaskLanding>> OperationsAsync(AppDbContext db, Guid taskId) =>
        await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == taskId).OrderBy(o => o.CreatedAt).ToListAsync();
}
