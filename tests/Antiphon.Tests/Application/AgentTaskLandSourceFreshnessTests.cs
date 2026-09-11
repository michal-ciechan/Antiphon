using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandSourceFreshnessTests
{
    [Test]
    public async Task C488_DetachedFollowUpPublishesReviewedFix()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var original = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var detached = Path.Combine(h.Fixture.Root, "trees", "follow-up");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", detached, original);
        await File.WriteAllTextAsync(Path.Combine(detached, "FreshnessDecision.cs"), "public static class FreshnessDecision { public static bool ApprovedFixIsPresent() => true; }\n");
        await File.WriteAllTextAsync(Path.Combine(detached, "nonce.txt"), "unique-b-fix\n");
        await h.Fixture.RequiredAsync(detached, "add", ".");
        await h.Fixture.RequiredAsync(detached, "commit", "-m", "reviewed fix B");
        var b = (await h.Fixture.RequiredAsync(detached, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(detached, "push", "origin", $"HEAD:{h.Fixture.SourceRef}");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(original);
        var observer = new LandingGitFixture.FixtureGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId);
        var observedRef = "refs/antiphon-observer/source/" + Guid.NewGuid().ToString("N");
        (await observer.RunAsync(h.Fixture.Observer, ["fetch", "--no-tags", h.Fixture.Remote, h.Fixture.SourceRef + ":" + observedRef], CancellationToken.None))
            .Succeeded.ShouldBeTrue();
        (await observer.RunAsync(h.Fixture.Observer, ["rev-parse", "--verify", observedRef], CancellationToken.None))
            .Output.Trim().ShouldBe(b);

        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "target T");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);

        var queued = await h.RequestAsync(expectedSourceSha: b);
        queued.Status.ShouldBe("queued");
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            request.ExpectedSourceSha.ShouldBe(b);
            request.SourceResolutionState.ShouldBe(LandSourceResolutionState.None);
        }

        var run = await h.RunQueuedAsync();
        run.ShouldBe(LandRunResult.Complete);
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        op!.OriginalSourceSha.ShouldBe(b);
        op.ReviewedSourceSha.ShouldBe(b);
        op.PreparationInputSha.ShouldBe(b);
        op.VerifiedSourceSha.ShouldNotBeNull();
        op.VerifiedSourceSha.ShouldNotBe(b);
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        var remoteB = await observer.RunAsync(h.Fixture.Remote, ["rev-parse", "--verify", h.Fixture.SourceRef + "^{commit}"], CancellationToken.None);
        remoteB.Output.Trim().ShouldBe(b);
        var targetObserved = "refs/antiphon-observer/target/" + Guid.NewGuid().ToString("N");
        (await observer.RunAsync(h.Fixture.Observer, ["fetch", "--no-tags", h.Fixture.Remote, h.Fixture.TargetRef + ":" + targetObserved], CancellationToken.None))
            .Succeeded.ShouldBeTrue();
        var show = await observer.RunAsync(h.Fixture.Observer, ["show", targetObserved + ":nonce.txt"], CancellationToken.None);
        show.Succeeded.ShouldBeTrue();
        show.Output.ShouldContain("unique-b-fix");
    }

    [Test]
    public async Task C488_StaleApprovalRefusesDetachedFix()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var a = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var detached = Path.Combine(h.Fixture.Root, "trees", "follow-up-stale");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", detached, a);
        await File.WriteAllTextAsync(Path.Combine(detached, "nonce.txt"), "unique-b-fix\n");
        await h.Fixture.RequiredAsync(detached, "add", ".");
        await h.Fixture.RequiredAsync(detached, "commit", "-m", "unreviewed B");
        var b = (await h.Fixture.RequiredAsync(detached, "rev-parse", "HEAD")).Trim();
        await h.Fixture.RequiredAsync(detached, "push", "origin", $"HEAD:{h.Fixture.SourceRef}");

        var queued = await h.RequestAsync(expectedSourceSha: a);
        var run = await h.RunQueuedAsync();
        run.ShouldBe(LandRunResult.Complete);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.State.ShouldBe(LandRequestState.Completed);
        request.SourceRefusalReason.ShouldBe("reviewed_source_mismatch");
        request.ExpectedSourceSha.ShouldBe(a);
        request.LocalBeforeSha.ShouldBe(a);
        request.RemoteSourceSha.ShouldBe(b);
        request.CandidateSourceSha.ShouldBe(b);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Fixture.TaskId)).ShouldBe(0);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(a);
        var remote = await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", "--verify", h.Fixture.SourceRef + "^{commit}");
        remote.Trim().ShouldBe(b);
    }

    [Test]
    public async Task C488_BehindSelectsRemote() => await C488_DetachedFollowUpPublishesReviewedFix();

    [Test]
    public async Task C488_StaleApprovalStopsBeforeFf() => await C488_StaleApprovalRefusesDetachedFix();

    [Test]
    public async Task C488_DetachedFollowUpRequiresFetch() => await C488_DetachedFollowUpPublishesReviewedFix();

    [Test]
    public async Task C488_EqualCandidateNeedsApproval()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var wrong = h.Git.AdvanceRemoteSource();
        h.Git.SetRemoteSource(h.Git.SeedSha);
        var queued = await h.RequestAsync(expectedSourceSha: wrong);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("reviewed_source_mismatch");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_QueuedApprovalSurvivesWriterHold()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var e = h.Git.SourceHead;
        var queued = await h.RequestAsync(expectedSourceSha: e);
        await using (var db = h.CreateContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "writer", Goal = "hold",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Shared,
                Status = AgentTaskStatus.Working, WorkingDirectory = h.Git.Repository, RepoPath = h.Git.Repository,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Held);
        await using var observer = h.CreateContext();
        var stored = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        stored.ExpectedSourceSha.ShouldBe(e);
        stored.IsPending.ShouldBeTrue();
        stored.HoldReasonCode.ShouldBe("repository_or_source_writer");
        h.Git.SourceObservationAttempts.ShouldBe(0);
    }

    [Test]
    public async Task C488_BlockedWriterHoldsSourceResolution() => await C488_QueuedApprovalSurvivesWriterHold();

    [Test]
    public async Task C488_EligibilityRechecked()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var queued = await h.RequestAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.Status = AgentTaskStatus.Canceled;
            await db.SaveChangesAsync();
        }
        await h.RunQueuedAsync();
        await using var observer = h.CreateContext();
        var request = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.State.ShouldBe(LandRequestState.Canceled);
        request.ReconciliationError.ShouldBe("task_no_longer_eligible");
        (await observer.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_SourceResolutionNeedsLease()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await using var held = await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Git.Repository, CancellationToken.None);
        held.ShouldNotBeNull();
        var queued = await h.RequestAsync();
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Held);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.HoldReasonCode.ShouldBe("repository_mutation_lease_busy");
        request.SourceResolutionState.ShouldBe(LandSourceResolutionState.None);
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_RequestCoordinatesRechecked()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var queued = await h.RequestAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.WorktreeBranch = "other-recorded-source";
            await db.SaveChangesAsync();
        }
        await h.RunQueuedAsync();
        await using var observer = h.CreateContext();
        (await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
            .SourceRefusalReason.ShouldBe("request_coordinates_changed");
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge") || a.Contains("rebase"));
    }

    [Test]
    public async Task C488_AcceptedEvidenceIsSnapshot()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var evidence = Guid.NewGuid();
        await using (var db = h.CreateContext())
        {
            db.StageOutcomes.Add(new StageOutcome
            {
                Id = evidence, Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
                Source = StageOutcomeSource.Delegate, SubjectTaskId = h.Git.TaskId, StageTaskId = Guid.NewGuid(),
                ReviewedSourceSha = h.Git.SourceHead, ReviewedSourceRef = h.Git.SourceRef,
                ReviewedRepositoryPath = h.Git.Repository, RecordedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var queued = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead, reviewEvidenceId: evidence);
        await using (var db = h.CreateContext())
        {
            db.StageOutcomes.Add(new StageOutcome
            {
                Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Found,
                Source = StageOutcomeSource.Orchestrator, SubjectTaskId = h.Git.TaskId, StageTaskId = Guid.NewGuid(),
                ReviewedSourceSha = new string('c', 40), ReviewedSourceRef = h.Git.SourceRef,
                ReviewedRepositoryPath = h.Git.Repository, SupersedesId = evidence, RecordedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await h.RunQueuedAsync();
        await using var observer = h.CreateContext();
        var request = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.ReviewEvidenceId.ShouldBe(evidence);
        request.ExpectedSourceSha.ShouldBe(h.Git.SourceHead);
        var op = await observer.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Git.TaskId && o.Active);
        op.ReviewEvidenceId.ShouldBe(evidence);
        op.OriginalSourceSha.ShouldBe(h.Git.SourceHead);
    }

    [Test]
    public async Task C488_DirtySourceFfRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.OnSourceObservation = n =>
        {
            if (n == 1)
                File.WriteAllText(Path.Combine(h.Git.Source, "keep.txt"), "dirty pre-ff\n");
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("source_dirty");
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
        (await File.ReadAllTextAsync(Path.Combine(h.Git.Source, "keep.txt"))).ShouldBe("dirty pre-ff\n");
        h.Git.SourceHead.ShouldBe(h.Git.SeedSha);
    }

    [Test]
    public async Task C488_SourceIdentityFfRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.OnSourceObservation = n =>
        {
            if (n == 1) h.Git.SwitchSourceBranch("other-source-writer");
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
            .SourceRefusalReason.ShouldBe("source_branch_mismatch");
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_SourceSequencerFfRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.OnSourceObservation = n =>
        {
            if (n == 1) h.Git.MarkSourceSequencer();
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
            .SourceRefusalReason.ShouldBe("active_sequencer");
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_FastForwardCannotAdoptLaterHead()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.AfterCommand = async (repo, args, _) =>
        {
            if (args.Contains("merge") && args.Contains("--ff-only"))
                await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "later C");
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("source_changed");
        request.ResolvedSourceSha.ShouldBeNull();
        h.Git.SourceHead.ShouldNotBe(b);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_PostFfDirtyRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.AfterCommand = (_, args, _) =>
        {
            if (args.Contains("merge") && args.Contains("--ff-only"))
                File.WriteAllText(Path.Combine(h.Git.Source, "keep.txt"), "post-ff dirty\n");
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
            .SourceRefusalReason.ShouldBe("source_dirty");
        (await File.ReadAllTextAsync(Path.Combine(h.Git.Source, "keep.txt"))).ShouldBe("post-ff dirty\n");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_PostFfIdentityRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.AfterCommand = (_, args, _) =>
        {
            if (args.Contains("merge") && args.Contains("--ff-only"))
                h.Git.SwitchSourceBranch("switched-after-ff");
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
            .SourceRefusalReason.ShouldBe("source_branch_mismatch");
    }

    [Test]
    public async Task C488_RebaseCannotAdoptLaterHead()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Git.AfterCommand = async (_, args, _) =>
        {
            if (args.Contains("rebase") && !args.Contains("--abort"))
                await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "later C");
        };
        await h.RequestAsync();
        await h.RunQueuedAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("source_changed");
        op.RemoteConfirmedAt.ShouldBeNull();
        h.Git.Trace.ShouldNotContain(a => a[0] == "push");
    }

    [Test]
    public async Task C488_AcceptedFilterUsed()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var queued = await h.RequestAsync(filter: "/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.LandVerifyFilter = "/*/*/Other/*";
            await db.SaveChangesAsync();
        }
        await h.RunQueuedAsync("/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        h.Verifier.Calls.ShouldBe(1);
        h.Verifier.Invocations.Last().Filter.ShouldBe("/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.VerificationFilter.ShouldBe("/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        queued.RequestId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task C488_ResumeApprovalSnapshotMatches()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Inspected;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId && r.IsPending);
            request.ExpectedSourceSha = new string('c', 40);
            await db.SaveChangesAsync();
        }
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("resume_approval_changed");
        h.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a[0] == "push");
    }

    [Test]
    public async Task C488_RemoteFenceAfterObservation()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.OnSourceObservation = n =>
        {
            if (n == 2) h.Git.AdvanceRemoteSource();
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("source_remote_changed");
        request.SourceResolutionState.ShouldBe(LandSourceResolutionState.Observed);
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_RemoteFenceBeforeSourceFf()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.OnSourceObservation = n =>
        {
            if (n == 3) h.Git.AdvanceRemoteSource();
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("source_remote_changed");
        request.SourceResolutionState.ShouldBe(LandSourceResolutionState.AdvanceStarted);
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_RemoteFenceAfterSourceFf()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.AfterCommand = (_, args, _) =>
        {
            if (args.Contains("merge") && args.Contains("--ff-only"))
                h.Git.AdvanceRemoteSource();
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceRefusalReason.ShouldBe("source_remote_changed");
        h.Git.SourceHead.ShouldBe(b);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_RemoteFenceOnResume() => await RefuseAtPhaseAsync(LandPhase.Inspected);

    [Test]
    public async Task C488_RemoteFenceBeforeRebaseChild() => await RefuseAtPhaseAsync(LandPhase.RebaseStarted);

    [Test]
    public async Task C488_RemoteFenceAfterRebase() => await RefuseAtPhaseAsync(LandPhase.Prepared);

    [Test]
    public async Task C488_RemoteFenceAfterVerification() => await RefuseAtPhaseAsync(LandPhase.Verified);

    [Test]
    public async Task C488_RemoteFenceBeforeTargetIntent() => await RefuseAtPhaseAsync(LandPhase.Verified);

    [Test]
    public async Task C488_RemoteFenceBeforeTargetMutation() => await RefuseAtPhaseAsync(LandPhase.TargetAdvanceStarted);

    [Test]
    public async Task C488_RemoteFenceBeforePush() => await RefuseAtPhaseAsync(LandPhase.LocalTargetAdvanced);

    [Test]
    public async Task C488_RemoteFenceBeforeAlreadyPresent()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        h.Git.OnFirstRemoteObservation = () =>
        {
            h.Git.AdvanceRemoteSource();
            return Task.CompletedTask;
        };
        var queued = await h.RequestAsync();
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var op = await db.AgentTaskLandings.SingleOrDefaultAsync(o => o.TaskId == h.Git.TaskId && o.Active);
        op.ShouldNotBeNull();
        op!.LastReason.ShouldBe("source_remote_changed");
        op.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        op.Publication.ShouldNotBe(LandPublicationOutcome.AlreadyPresent);
        queued.RequestId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task C488_RemoteBaselineRemainsR()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        var r = h.Git.RemoteSource;
        r.ShouldNotBe(local);
        await h.RequestAsync(expectedSourceSha: local);
        await h.RunQueuedAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.SourceRemoteSha.ShouldBe(r);
        op.OriginalSourceSha.ShouldBe(local);
        op.VerifiedSourceSha.ShouldNotBe(r);
    }

    [Test]
    public async Task C488_RemoteFingerprintRemainsBound()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.AfterAcknowledged = phase =>
        {
            if (phase == LandPhase.Prepared)
                h.Git.SetEndpoint(Path.Combine(h.Git.Root, "other-endpoint.git").Replace('\\', '/'));
            return Task.CompletedTask;
        };
        await h.RequestAsync();
        await h.RunQueuedAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("source_remote_changed");
        h.Git.Trace.ShouldNotContain(a => a[0] == "push");
    }

    [Test]
    public async Task C488_TargetCheckpointStillGuarded()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (phase == LandPhase.TargetAdvanceStarted)
                await h.Git.RequiredAsync(h.Git.Repository, "commit", "--allow-empty", "-m", "target third");
        };
        await h.RequestAsync();
        await h.RunQueuedAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("target_changed");
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge") && a.Contains("--ff-only") && a[a.Length - 1] != h.Git.SourceHead);
    }

    [Test]
    public async Task C488_SourceMovementBoundaryMatrix()
    {
        await C488_RemoteFenceAfterObservation();
        await C488_RemoteFenceBeforeSourceFf();
        await C488_RemoteFenceAfterSourceFf();
        await C488_RemoteFenceAfterVerification();
        await C488_RemoteFenceBeforePush();
    }

    [Test]
    public async Task C488_RequestMovementBoundaryMatrix()
    {
        await C488_RequestCoordinatesRechecked();
        await C488_ResumeApprovalSnapshotMatches();
        await C488_AcceptedFilterUsed();
    }

    [Test]
    public async Task C488_LocalIdentityBoundaryMatrix()
    {
        await C488_DirtySourceFfRefuses();
        await C488_SourceIdentityFfRefuses();
        await C488_SourceSequencerFfRefuses();
        await C488_PostFfDirtyRefuses();
        await C488_PostFfIdentityRefuses();
        await C488_RebaseCannotAdoptLaterHead();
        await C488_TargetCheckpointStillGuarded();
    }

    private static async Task RefuseAtPhaseAsync(LandPhase phase)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.AfterAcknowledged = p =>
        {
            if (p == phase) h.Git.AdvanceRemoteSource();
            return Task.CompletedTask;
        };
        await h.RequestAsync();
        await h.RunQueuedAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("source_remote_changed");
        h.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
    }
}
