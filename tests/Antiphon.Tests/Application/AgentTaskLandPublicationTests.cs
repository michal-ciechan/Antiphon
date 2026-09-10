using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandPublicationTests
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
        op.LocalTargetAfterSha.ShouldBe(source);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("--force") || a.Contains("--mirror"));
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
        h.Fixture.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(fault switch
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
        h.Fixture.Git.Trace.Clear();
        if (fault is "timeout" or "canceled")
        {
            var error = fault == "timeout"
                ? (Exception)await Should.ThrowAsync<TimeoutException>(() => h.RunAsync())
                : await Should.ThrowAsync<OperationCanceledException>(() => h.RunAsync());
            await h.FailAsync(error);
        }
        else await h.RunAsync();
        var operation = (await h.OperationAsync()).ShouldNotBeNull();
        operation.RemoteConfirmedAt.ShouldBeNull();
        // Drain-side timeout/cancellation settlement can retain a null operation reason.
        (operation.LastReason ?? "").ShouldNotContain("synthetic-private-marker");
        await using (var db = h.CreateContext())
        {
            var terminal = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
                && e.Type == AgentTaskEventType.LandRefused);
            terminal.Detail.ShouldNotContain("synthetic-private-marker");
            terminal.LandingOperationId.ShouldBe(operation.Id);
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
        var operation = (await h.OperationAsync()).ShouldNotBeNull();
        operation.Publication.ShouldBe(containsSource ? LandPublicationOutcome.AlreadyPresent : LandPublicationOutcome.Refused);
        Directory.Exists(h.Fixture.Source).ShouldBe(!containsSource);
        if (!containsSource) operation.LastReason.ShouldBe("remote_ahead_or_diverged");
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(localBefore);
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
        var sentinel = Path.Combine(h.Fixture.Source, "bin-private", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "keep");
        await h.RunAsync();
        var operation = (await h.OperationAsync()).ShouldNotBeNull();
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
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandRefused))
            .Detail.ShouldContain(change switch { "detached" => "detached_head", "switched" => "source_branch_mismatch", _ => "source_dirty" });
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.local.json")]
    [Arguments("bin-private/data.txt")]
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
        op.LocalTargetAfterSha.ShouldBe(source);
        op.RemoteConfirmedAt.ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        h.Fixture.Git.BeforeCommand = null;
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim()
            .ShouldBe(pushAccepted ? source : h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
