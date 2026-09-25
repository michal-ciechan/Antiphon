using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class AgentTaskLandPublicationTests
{
    [Test]
    [Arguments("non-ff-before-push")]
    [Arguments("rewrite-after-push")]
    [Arguments("destination-before-push")]
    [Arguments("destination-after-push")]
    public async Task C448_V09_PushAndConfirmationPreserveCompetingRemoteState(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var rival = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree", h.Fixture.SeedSha + "^{tree}", "-p", h.Fixture.SeedSha, "-m", "other remote writer")).Trim();
        var other = Path.Combine(h.Fixture.Root, "other-endpoint.git");
        await h.Fixture.RequiredAsync(h.Fixture.Root, "clone", "--bare", h.Fixture.Remote, other);
        var fired = false;
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] == "push" && change == "non-ff-before-push")
            {
                fired = true;
                await h.Fixture.RequiredAsync(h.Fixture.Remote, "fetch", h.Fixture.Repository, rival + ":" + h.Fixture.TargetRef);
            }
            return null;
        };
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (!fired && phase == LandPhase.PushStarted && change == "destination-before-push")
            {
                fired = true;
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "config", "remote.origin.pushurl", other);
            }
        };
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] != "push" || !result.Succeeded || fired) return;
            if (change == "rewrite-after-push")
            { fired = true; await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", h.Fixture.TargetRef, h.Fixture.SeedSha); }
            if (change == "destination-after-push")
            { fired = true; await h.Fixture.RequiredAsync(h.Fixture.Repository, "config", "remote.origin.pushurl", other); }
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue("competing remote state or changed endpoint must never authorize cleanup");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldBeNull();
        // CARD-0688 D-4: the push comes first, so an unconfirmed publication never advanced the local target.
        op.LocalTargetAfterSha.ShouldBeNull();
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        // The land worktree's own heal path is `worktree add --force --force`; no other forced or mirrored command runs.
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("--mirror")
            || a.Contains("--force") && !(a[0] == "worktree" && a[1] == "add"));
        h.Fixture.Git.BeforeCommand = null;
        h.Fixture.Git.AfterCommand = null;
        h.Fault.AfterAcknowledged = null;
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(change switch
        { "non-ff-before-push" => rival, "destination-after-push" => source, _ => h.Fixture.SeedSha });
        (await h.Fixture.RequiredAsync(other, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", op.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", op.RecoveryRefPrefix + "/prepared")).Trim().ShouldBe(source);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V26_OriginalAndPreparedPinsSurviveCleanupAndGarbageCollection()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new base");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        var otherPrefix = $"refs/antiphon/land/{Guid.NewGuid():N}/{Guid.NewGuid():N}";
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", otherPrefix + "/source", h.Fixture.SeedSha);
        await h.RunAsync();
        var op = (await h.OperationAsync())!;
        foreach (var required in new[] { "source", "target-before", "prepared" })
        {
            var requiredPin = await h.Fixture.Git.RunAsync(h.Fixture.Repository,
                ["show-ref", "--verify", "--hash", op.RecoveryRefPrefix + "/" + required], CancellationToken.None);
            requiredPin.Succeeded.ShouldBeTrue("normal publication must retain its required " + required + " recovery pin");
        }
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        op.VerifiedSourceSha.ShouldNotBe(source);
        h.Verifier.Calls.ShouldBe(1);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "reflog", "expire", "--expire=now", "--all");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "gc", "--prune=now");
        foreach (var (name, sha) in new[] { (op.RecoveryRefPrefix + "/source", source),
                     (op.RecoveryRefPrefix + "/prepared", op.VerifiedSourceSha!), (otherPrefix + "/source", h.Fixture.SeedSha) })
        {
            var pin = await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["show-ref", "--verify", "--hash", name], CancellationToken.None);
            pin.Succeeded.ShouldBeTrue("cleanup and maintenance must retain every operation's recovery pin");
            pin.Output.Trim().ShouldBe(sha);
            var commit = await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["cat-file", "-t", sha], CancellationToken.None);
            commit.Succeeded.ShouldBeTrue("the recovery ref must retain its commit independently of reflogs");
            commit.Output.Trim().ShouldBe("commit");
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V08_RejectedPushWithIndependentContainmentIsAlreadyPresent()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] != "push") return null;
            // A separate fixture publisher puts the exact commit on the push endpoint.
            await h.Fixture.RequiredAsync(h.Fixture.Remote, "fetch", h.Fixture.Repository, sha + ":" + h.Fixture.TargetRef);
            return new LandingGitResult(1, "", "rejected acknowledgement");
        };
        await h.RunAsync();
        var operation = (await h.OperationAsync()).ShouldNotBeNull();
        operation.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        operation.PushExitCode.ShouldBe(1);
        operation.RemoteConfirmedAt.ShouldNotBeNull();
        operation.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("read-error")]
    [Arguments("empty")]
    [Arguments("malformed")]
    [Arguments("missing")]
    [Arguments("fetch-error")]
    [Arguments("ancestry-error")]
    [Arguments("timeout")]
    [Arguments("canceled")]
    public async Task C448_V05_RemoteErrorsCannotUseCachedOrLocalContainment(string fault)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "merge", "--ff-only", sha);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "refs/remotes/origin/master", sha);
        // CARD-0488 observes the remote *source* branch (ls-remote/fetch/merge-base against
        // SourceRef) in the resolver before an operation exists, and rechecks it in the protocol.
        // Only the target-branch observation is faulted: that is the read whose failure must not
        // be replaced by cached (refs/remotes/origin/*) or local containment. The resolver's
        // ancestry checks share merge-base's argument shape but always precede the first target
        // ls-remote, and the trace records a command before BeforeCommand sees it.
        bool TargetObservation(IReadOnlyList<string> args) => args[0] switch
        {
            "ls-remote" => args.Contains(h.Fixture.TargetRef),
            "fetch" => args.Any(a => a.StartsWith(h.Fixture.TargetRef + ":", StringComparison.Ordinal)),
            "merge-base" => h.Fixture.Git.Trace.Any(t => t[0] == "ls-remote" && t.Contains(h.Fixture.TargetRef)),
            _ => false,
        };
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (!TargetObservation(args)) return Task.FromResult<LandingGitResult?>(null);
            return Task.FromResult<LandingGitResult?>(fault switch
            {
                "timeout" when args[0] == "ls-remote" => throw new TimeoutException("fixture timeout"),
                "canceled" when args[0] == "ls-remote" => throw new OperationCanceledException("fixture cancel"),
                "read-error" when args[0] == "ls-remote" => new(128, "", "synthetic-private-marker"),
                "empty" when args[0] == "ls-remote" => new(0, "", ""),
                "malformed" when args[0] == "ls-remote" => new(0, "invalid\\trefs/heads/master\\n", ""),
                "missing" when args[0] == "ls-remote" => new(2, "", ""),
                "fetch-error" when args[0] == "fetch" => new(128, "", "synthetic-private-marker"),
                "ancestry-error" when args[0] == "merge-base" => new(128, "", "synthetic-private-marker"),
                _ => null,
            });
        };
        h.Fixture.Git.Trace.Clear();
        if (fault is "timeout" or "canceled")
        {
            var error = fault == "timeout"
                ? (Exception)await Should.ThrowAsync<TimeoutException>(() => h.RunAsync())
                : await Should.ThrowAsync<OperationCanceledException>(() => h.RunAsync());
            await h.FailAsync(error);
        }
        else await h.RunAsync();
        h.Fixture.Git.Trace.ShouldContain(a => a[0] == "ls-remote" && a.Contains(h.Fixture.SourceRef),
            "the remote source observation must run unfaulted before the target observation");
        // CARD-0688 D-4: the target is observed while the operation is created (it becomes the rebase base), so a
        // failed observation refuses before any operation, pin or land-worktree mutation exists.
        (await h.OperationAsync()).ShouldBeNull();
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            request.RemoteSourceSha.ShouldBe(h.Fixture.SeedSha);
            if (fault is not ("timeout" or "canceled"))
                request.SourceRefusalReason.ShouldBe(fault switch
                {
                    "read-error" or "missing" => "remote_read_failed",
                    "empty" or "malformed" => "remote_response_invalid",
                    "fetch-error" => "remote_fetch_failed",
                    _ => "remote_ancestry_error",
                });
            // Drain-side timeout/cancellation settlement can retain a null refusal reason.
            (request.SourceRefusalReason ?? "").ShouldNotContain("synthetic-private-marker");
            var terminal = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
                && e.Type == AgentTaskEventType.LandRefused);
            terminal.Detail.ShouldNotContain("synthetic-private-marker");
            terminal.LandingOperationId.ShouldBeNull();
        }
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase") || a.Contains("remove"));
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.BeforeCommand = null;
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(sha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task C448_V06_RemoteAheadRefusesOnlyWhenSourceIsNotContained(bool containsSource, bool divergent)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (!containsSource) await h.AddSourceAsync();
        if (divergent) await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "local divergent base");
        var localBefore = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim();
        var remoteTip = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit-tree", h.Fixture.SeedSha + "^{tree}",
            "-p", h.Fixture.SeedSha, "-m", "remote advanced")).Trim();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", remoteTip + ":" + h.Fixture.TargetRef);
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        // CARD-0688 D-4 / I-10 (inverted): the rebase base is the observed remote target, so a remote ahead of a
        // stale local target is normal; only a local target that is not an ancestor of the remote (unpushed or
        // divergent local commits) refuses, as target_local_ahead before any operation exists.
        var operation = await h.OperationAsync();
        if (divergent)
        {
            operation.ShouldBeNull();
            await using var db = h.CreateContext();
            (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId)).SourceRefusalReason.ShouldBe("target_local_ahead");
            Directory.Exists(h.Fixture.Source).ShouldBeTrue();
            h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase"));
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(localBefore);
        }
        else
        {
            operation.ShouldNotBeNull().Publication.ShouldBe(containsSource ? LandPublicationOutcome.AlreadyPresent : LandPublicationOutcome.Landed);
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            operation.TargetBeforeSha.ShouldBe(remoteTip);
            if (containsSource) h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase"));
            // A landed commit fast-forwards the stale main checkout through the remote tip; an already-present source
            // is already in the local target, so the canonical step leaves it where it was ("already").
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim()
                .ShouldBe(containsSource ? localBefore : operation.VerifiedSourceSha);
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V33_CleanupRetriesDoNotEmitAnotherPublication(bool alreadyPresent)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (!alreadyPresent) await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, "bin-private", "settings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "keep");
        await h.RunAsync();
        var operation = (await h.OperationAsync()).ShouldNotBeNull();
        operation.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        operation.LastReason.ShouldBe("ignored_content_preserved");
        (await File.ReadAllTextAsync(sentinel)).ShouldBe("keep");
        var publicationTime = operation.RemoteConfirmedAt;
        File.Delete(sentinel); // This test owns these exact bytes; this is the explicit cleanup remedy.
        await h.RepostAsync();
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        await using var observer = h.CreateContext();
        var events = await observer.AgentTaskEvents.Where(e => e.AgentTaskId == h.Fixture.TaskId
            && e.LandingOperationId != null).OrderBy(e => e.At).ToListAsync();
        events.Count.ShouldBe(2);
        events.Count(e => e.Type is AgentTaskEventType.Landed or AgentTaskEventType.LandedWithResidue or AgentTaskEventType.AlreadyPresent).ShouldBe(1);
        events.Last().Type.ShouldBe(AgentTaskEventType.LandingCleanup);
        events.Last().LandingOperationId.ShouldBe(operation.Id);
        events.Last().LandingMode.ShouldBe(LandOperationMode.CleanupRetry);
        events.Last().LandingCleanup.ShouldBe(LandCleanupStatus.Complete);
        events.Last().LandingPublication.ShouldBe(operation.Publication);
        (await h.OperationAsync())!.RemoteConfirmedAt.ShouldBe(publicationTime);
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V22_ConfirmedPublicationUsesSavedReceipt(bool alreadyPresent)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = alreadyPresent ? h.Fixture.SeedSha : await h.AddSourceAsync();
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.Publication.ShouldBe(alreadyPresent ? LandPublicationOutcome.AlreadyPresent : LandPublicationOutcome.Landed);
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        op.VerifiedSourceSha.ShouldBe(sha);
        op.ObservedRemoteTargetSha.ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim());
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", op.RecoveryRefPrefix + "/source")).Trim().ShouldBe(sha);
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(alreadyPresent ? 0 : 1);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("detached")]
    [Arguments("switched")]
    [Arguments("dirty")]
    [Arguments("untracked")]
    public async Task C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (change == "detached") await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "--detach");
        if (change == "switched") await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "-b", "other");
        var sentinel = Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt");
        if (change is "dirty" or "untracked") await File.WriteAllTextAsync(sentinel, "changed\n");
        var bytes = await File.ReadAllBytesAsync(sentinel);
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        (await File.ReadAllBytesAsync(sentinel)).ShouldBe(bytes);
        h.Fixture.Git.Trace.ShouldNotContain(a => (a[0] == "push" || a[0] == "rebase") || a.Contains("remove"));
        // CARD-0688 D-2 / I-3..I-5: the source is the branch ref, so the already-present shortcut is taken from it;
        // the task worktree's state is guarded cleanup's concern, which refuses and preserves it (these were
        // pre-shortcut landing refusals).
        var reason = change switch { "detached" => "detached_head", "switched" => "source_branch_mismatch", _ => "source_dirty" };
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe(reason);
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.AlreadyPresent))
            .Detail.ShouldContain(reason);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.local.json")]
    [Arguments(".antiphon/inbox/photo.png")]
    public async Task C448_V04_IgnoredFilesSurviveConfirmedPublication(string relative)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var path = Path.Combine(h.Fixture.Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "irreplaceable\n");
        await h.RunAsync();
        File.Exists(path).ShouldBeTrue("explicit ignored-content guard must run before real git worktree remove");
        (await File.ReadAllTextAsync(path)).ShouldBe("irreplaceable\n");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe("ignored_content_preserved");
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    // CARD-0665 V-4: end-to-end land through the real remover and gate.
    [Test]
    public async Task C665_DisposableOnlyIgnoredContentIsRemovedOnLand()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var shortId = DelegationReportFormatter.Short(h.Fixture.TaskId);
        foreach (var relative in new[] { Path.Combine("obj", "project.assets.json"), Path.Combine("bin-c665", "out.dll"),
                     Path.Combine(".antiphon", "inbox", "brief.md"), Path.Combine(".antiphon", $"task-{shortId}-brief.md") })
        {
            var path = Path.Combine(h.Fixture.Source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "rebuildable\n");
        }
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete, op.LastReason);
        op.LastReason.ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.Landed))
            .Detail.ShouldContain("cleanup=Complete");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C665_ProtectedIgnoredRefusalNamesPaths()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var secret = Path.Combine(h.Fixture.Source, ".claude", "settings.local.json");
        var build = Path.Combine(h.Fixture.Source, "obj", "x");
        foreach (var path in new[] { secret, build })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "irreplaceable\n");
        }
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe("ignored_content_preserved");
        await using var db = h.CreateContext();
        var detail = (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.LandedWithResidue)).Detail;
        detail.ShouldContain("cleanup=Refused: ignored_content_preserved; protected: .claude/settings.local.json");
        detail.ShouldNotContain("obj/x");
        (await File.ReadAllTextAsync(secret)).ShouldBe("irreplaceable\n");
        (await File.ReadAllTextAsync(build)).ShouldBe("irreplaceable\n");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V09_PushExitCannotReplaceRemoteConfirmation(bool pushAccepted)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var pushed = false;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "push")
            {
                pushed = true;
                if (!pushAccepted) return Task.FromResult<LandingGitResult?>(new(1, "", "fixture rejection"));
            }
            return Task.FromResult<LandingGitResult?>(pushed && args[0] == "ls-remote" ? new(128, "", "fixture read failure") : null);
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LocalTargetAfterSha.ShouldBeNull("CARD-0688 D-4: the local target advances only after a confirmed publication");
        op.RemoteConfirmedAt.ShouldBeNull();
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        h.Fixture.Git.BeforeCommand = null;
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim()
            .ShouldBe(pushAccepted ? source : h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C688_LandNeverRunsGitInTheSourceWorktree()
    {
        // CARD-0688 V-4 (D-2): before cleanup the task worktree is read once, for its git directory,
        // besides the admission index-lock probe D-10 keeps; nothing inspects or mutates it.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await AdvanceRemoteTargetAsync(h);
        var logger = new RecordingLogger<AgentTaskLandService>();
        h.Logger = logger;
        var cleanupAt = -1;
        h.Fault.AfterAcknowledged = phase =>
        {
            if (phase == LandPhase.CleanupStarted && cleanupAt < 0) cleanupAt = h.Fixture.Git.Commands.Count;
            return Task.CompletedTask;
        };
        await h.RequestAsync(); // the harness reads the source HEAD for the expected SHA; the land does not
        h.Fixture.Git.Commands.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        cleanupAt.ShouldBeGreaterThan(0);
        var inSource = h.Fixture.Git.Commands.Take(cleanupAt)
            .Where(c => Antiphon.Server.Infrastructure.Git.LandingGit.PathsEqual(c.Directory, h.Fixture.Source)).Select(c => string.Join(' ', c.Arguments)).ToList();
        inSource.Count(c => c == "rev-parse --absolute-git-dir").ShouldBe(1, string.Join(" | ", inSource));
        inSource.ShouldAllBe(c => c == "rev-parse --absolute-git-dir" || c == "rev-parse --path-format=absolute --git-path index.lock");
        var profile = logger.Entries.Single(e => e.Message.StartsWith("Land git profile ", StringComparison.Ordinal));
        Convert.ToInt32(profile.State["Inspections"]).ShouldBe(2, "only the guarded cleanup inspects the task worktree");
    }

    [Test]
    public async Task C688_RebaseAndVerifyRunInTheLandWorktree()
    {
        // CARD-0688 V-5 (D-3/D-9): the task branch never moves; the rebased commit lives detached in the land worktree.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        await AdvanceRemoteTargetAsync(h);
        string? branchBeforeCleanup = null;
        var reader = new LandingGitFixture.FixtureGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId);
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (phase == LandPhase.PublicationConfirmed && branchBeforeCleanup is null)
                branchBeforeCleanup = (await reader.RunAsync(h.Fixture.Repository, ["rev-parse", h.Fixture.SourceRef], CancellationToken.None)).Output.Trim();
        };
        h.Fixture.Git.Commands.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.SchemaVersion.ShouldBe(3);
        op.LandWorktreePath.ShouldNotBeNull();
        op.SourceLocalSha.ShouldBe(source);
        op.OriginalSourceSha.ShouldBe(source);
        op.RebasedSourceSha.ShouldNotBe(source);
        var rebase = h.Fixture.Git.Commands.Single(c => c.Arguments.Contains("rebase") && !c.Arguments.Contains("--abort"));
        Antiphon.Server.Infrastructure.Git.LandingGit.PathsEqual(rebase.Directory, op.LandWorktreePath!).ShouldBeTrue(rebase.Directory);
        h.Verifier.Invocations.ShouldHaveSingleItem();
        Antiphon.Server.Infrastructure.Git.LandingGit.PathsEqual(h.Verifier.Invocations[0].Worktree, op.LandWorktreePath!).ShouldBeTrue();
        branchBeforeCleanup.ShouldBe(source, "publication never moves the task branch");
        (await reader.RunAsync(op.LandWorktreePath!, ["rev-parse", "HEAD"], CancellationToken.None)).Output.Trim().ShouldBe(op.RebasedSourceSha);
        (await reader.RunAsync(op.LandWorktreePath!, ["symbolic-ref", "-q", "HEAD"], CancellationToken.None)).ExitCode.ShouldBe(1);
    }

    [Test]
    public async Task C688_PushPrecedesCanonicalAdvance()
    {
        // CARD-0688 V-6 (D-4): push first; the main checkout fast-forwards after publication.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fixture.Git.Commands.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        var push = h.Fixture.Git.Commands.FindIndex(c => c.Arguments[0] == "push");
        var merge = h.Fixture.Git.Commands.FindIndex(c => c.Arguments.Contains("merge") && c.Arguments.Contains("--ff-only")
            && Antiphon.Server.Infrastructure.Git.LandingGit.PathsEqual(c.Directory, h.Fixture.Repository));
        push.ShouldBeGreaterThan(0);
        merge.ShouldBeGreaterThan(push, "the canonical checkout advances only after publication");
        op.CanonicalAdvancedAt.ShouldNotBeNull();
        op.CanonicalAdvanceReason.ShouldBeNull();
        op.LocalTargetBeforeSha.ShouldBe(h.Fixture.SeedSha);
        op.LocalTargetAfterSha.ShouldBe(op.VerifiedSourceSha);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(op.VerifiedSourceSha);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(op.VerifiedSourceSha);
        (await TerminalAsync(h)).Detail.ShouldContain("canonical=advanced");
    }

    [Test]
    public async Task C688_DirtyCanonicalCheckoutLandsWithResidue()
    {
        // CARD-0688 V-7: a dirty main checkout is an activation residue, never a publication refusal.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Repository, "operator-scratch.txt"), "untracked operator bytes\n");
        h.Fixture.Git.Commands.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(op.VerifiedSourceSha);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        var push = h.Fixture.Git.Commands.FindIndex(c => c.Arguments[0] == "push");
        h.Fixture.Git.Commands.Skip(push + 1).ShouldNotContain(c => c.Arguments.Contains("merge")
            || c.Arguments[0] == "update-ref" && c.Arguments.Contains(h.Fixture.TargetRef));
        op.CanonicalAdvanceReason.ShouldBe("canonical_checkout_dirty");
        var terminal = await TerminalAsync(h);
        terminal.Type.ShouldBe(AgentTaskEventType.LandedWithResidue);
        terminal.Detail.ShouldContain("canonical=canonical_checkout_dirty");
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.Where(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.Warning).ToListAsync())
            .ShouldContain(e => e.Detail.Contains(h.Fixture.Repository) && e.Detail.Contains("git pull --rebase"));
        File.Exists(Path.Combine(h.Fixture.Repository, "operator-scratch.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task C688_TargetCheckedOutElsewhereIsResidueNotRefusal()
    {
        // CARD-0688 V-8 (D-5): a hand-made second checkout of master is a named residue.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var alias = Path.Combine(h.Fixture.Root, "alias");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--force", alias, "master");
        h.Fixture.Git.Commands.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.CanonicalAdvanceReason.ShouldBe("target_checked_out_elsewhere");
        h.Fixture.Git.Commands.ShouldNotContain(c => c.Arguments.Contains("merge")
            || c.Arguments[0] == "update-ref" && c.Arguments.Contains(h.Fixture.TargetRef));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        (await TerminalAsync(h)).Detail.ShouldContain("canonical=target_checked_out_elsewhere");
    }

    [Test]
    [Arguments("missing", false)]
    [Arguments("malformed", false)]
    [Arguments("empty-ref", false)]
    [Arguments("directory", false)]
    [Arguments("missing", true)]
    [Arguments("malformed", true)]
    [Arguments("locked", false)]
    public async Task C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(string fault, bool atMutation)
    {
        if (fault == "locked" && !OperatingSystem.IsWindows())
            Skip.Test("Windows sharing denial is required for the locked HEAD row.");
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var alias = Path.Combine(h.Fixture.Root, "alias");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", alias, h.Fixture.SeedSha);
        var admin = (await h.Fixture.RequiredAsync(alias, "rev-parse", "--absolute-git-dir")).Trim();
        var head = Path.Combine(admin, "HEAD");
        var original = await File.ReadAllTextAsync(head);
        FileStream? holder = null;
        var published = false;
        var injected = false;
        void Corrupt()
        {
            injected = true;
            if (fault == "locked") holder = new FileStream(head, FileMode.Open, FileAccess.Read, FileShare.None);
            else if (fault == "missing") File.Delete(head);
            else if (fault == "directory") { File.Delete(head); Directory.CreateDirectory(head); }
            else File.WriteAllText(head, fault == "empty-ref" ? "ref: \n" : "unreadable HEAD\n");
        }
        void Restore()
        {
            holder?.Dispose();
            holder = null;
            if (Directory.Exists(head)) Directory.Delete(head);
            File.WriteAllText(head, original);
        }
        h.Fault.AfterAcknowledged = phase =>
        {
            if (phase == LandPhase.PublicationConfirmed)
            {
                published = true;
                if (!atMutation && !injected) Corrupt();
            }
            if (phase == LandPhase.CleanupStarted) Restore();
            return Task.CompletedTask;
        };
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (atMutation && published && !injected && args[0] == "merge-base") Corrupt();
            return Task.FromResult<LandingGitResult?>(null);
        };
        try
        {
            await h.RunAsync();
            injected.ShouldBeTrue();
            var op = (await h.OperationAsync()).ShouldNotBeNull();
            new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
            op.CanonicalAdvanceReason.ShouldBe("canonical_checkout_unknown");
            op.LocalTargetAfterSha.ShouldBeNull();
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
            h.Fixture.Git.Commands.ShouldNotContain(c => c.Arguments.Contains("merge")
                || c.Arguments[0] == "update-ref" && c.Arguments.Contains(h.Fixture.TargetRef));
            (await TerminalAsync(h)).Type.ShouldBe(AgentTaskEventType.LandedWithResidue);
        }
        finally { Restore(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C688_Repair_AlreadyCanonicalRecordsLocalIdentity(bool detached)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "merge", "--ff-only", sha);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        if (detached) await h.Fixture.RequiredAsync(h.Fixture.Repository, "checkout", "--detach");
        h.Fixture.Git.Commands.Clear();

        await h.RunAsync();

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        op.LocalTargetAfterSha.ShouldBe(sha);
        op.CanonicalAdvancedAt.ShouldNotBeNull();
        h.Fixture.Git.Commands.ShouldNotContain(c => c.Arguments.Contains("merge")
            || c.Arguments[0] == "update-ref" && c.Arguments.Contains(h.Fixture.TargetRef));
    }

    [Test]
    public async Task C688_LocalMasterBehindOriginRebasesOntoOrigin()
    {
        // CARD-0688 V-12 (D-4): a stale main checkout no longer refuses; the base is the observed origin/master.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var foreign = await PushFromObserverAsync(h, "foreign.txt");
        h.Fixture.Git.Commands.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.TargetBeforeSha.ShouldBe(foreign);
        h.Fixture.Git.Commands.Single(c => c.Arguments.Contains("rebase") && !c.Arguments.Contains("--abort")).Arguments[^1].ShouldBe(foreign);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(op.VerifiedSourceSha);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "HEAD")).Trim().ShouldBe(op.VerifiedSourceSha);
        (await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["merge-base", "--is-ancestor", foreign, "HEAD"], CancellationToken.None))
            .ExitCode.ShouldBe(0, "the canonical fast-forward carries the foreign commit");
        File.Exists(Path.Combine(h.Fixture.Repository, "foreign.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task C688_LocalMasterAheadOfOriginRefuses()
    {
        // CARD-0688 V-13 (I-10): unpushed local master commits are the operator's, not a land's.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "unpushed local");
        var local = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim();
        h.Fixture.Git.Commands.Clear();

        await h.RunAsync();

        (await TerminalAsync(h)).Detail.ShouldContain("target_local_ahead");
        (await h.OperationAsync()).ShouldBeNull("the refusal happens before an operation exists");
        var pins = await h.Fixture.RequiredAsync(h.Fixture.Repository, "for-each-ref", "--format=%(refname)", $"refs/antiphon/land/{h.Fixture.TaskId:N}/");
        pins.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldNotContain(r => r.EndsWith("/source") || r.EndsWith("/target-before"));
        Directory.Exists(Path.Combine(h.Fixture.Root, "trees", "land")).ShouldBeFalse();
        h.Fixture.Git.Commands.ShouldNotContain(c => c.Arguments.Contains("reset") || c.Arguments.Contains("rebase")
            || c.Arguments[0] == "push" || c.Arguments[0] == "worktree" && c.Arguments[1] == "add");
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(local);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private static async Task AdvanceRemoteTargetAsync(LandingSafetyHarness h)
    {
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new base");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
    }

    private static async Task<string> PushFromObserverAsync(LandingSafetyHarness h, string file)
    {
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Observer, file), "foreign\n");
        await h.Fixture.RequiredAsync(h.Fixture.Observer, "add", file);
        await h.Fixture.RequiredAsync(h.Fixture.Observer, "commit", "-m", "foreign writer");
        await h.Fixture.RequiredAsync(h.Fixture.Observer, "push", "origin", "HEAD:" + h.Fixture.TargetRef);
        return (await h.Fixture.RequiredAsync(h.Fixture.Observer, "rev-parse", "HEAD")).Trim();
    }

    private static async Task<Antiphon.Server.Domain.Entities.AgentTaskEvent> TerminalAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        return await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == h.Fixture.TaskId && e.IsLandTerminal)
            .OrderBy(e => e.At).LastAsync();
    }

    [Test]
    public async Task C642_ProtocolInspectionsAreIdentityAndStatus()
    {
        // CARD-0642 V-6: the resolver and protocol never ask for the ignored listing.
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        var push = h.Git.Trace.FindIndex(a => a[0] == "push");
        push.ShouldBeGreaterThan(0, "the land must publish");
        // CARD-0688 D-2 supersedes the CARD-0642 scope split: the resolver and protocol never inspect the task
        // worktree, so no inspection precedes the push; guarded cleanup's are the only ones, and they are Full.
        h.Git.InspectionScopes.Where(i => i.TraceIndex <= push).ShouldBeEmpty();
        var cleanup = h.Git.InspectionScopes.Select(i => i.Scope).ToList();
        cleanup.Count.ShouldBe(2);
        cleanup.ShouldAllBe(scope => scope == LandInspectionScope.Full);
    }
}
