using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0613 D-4..D-8. Continuation attribution: what happens when the delegate's real work is in
/// the task's OWN registered checkout but NOT on its expected ref with descendant history. The
/// policy here decides only whether a false no-progress failure is avoided; every positive it can
/// produce is <see cref="ProgressOrigin.PrimaryAlternate"/>, which never authorizes merge-back.
/// </summary>
[Category("Unit")]
public class TaskCompletionContinuationTests
{
    private const string Root = "1111111111111111111111111111111111111111";
    private const string Bl = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Rem = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string C = "cccccccccccccccccccccccccccccccccccccccc";
    private const string D = "dddddddddddddddddddddddddddddddddddddddd";
    private const string E = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string Unrelated = "2222222222222222222222222222222222222222";

    private const string ExpectedRef = "refs/heads/feat/card-task-own";
    private const string OtherRef = "refs/heads/feat/card-task-sibling";
    private const string Repo = @"C:\repo";
    private const string Checkout = @"C:\repo\own";

    private static readonly DateTime Captured = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Captured.AddMinutes(30);
    private static readonly DateTime AfterCapture = Captured.AddMinutes(5);
    private static readonly DateTime BeforeCapture = Captured.AddMinutes(-5);
    private static readonly DateTime Future = Now.AddMinutes(5);

    // ---- V-4 -------------------------------------------------------------------------------

    [Test]
    [Arguments("sibling-branch")]
    [Arguments("detached")]
    [Arguments("claim-is-ancestor-of-later-tip")]
    public async Task C613_OffBranchClaimQualifiesActualHead(string shape)
    {
        var head = shape == "claim-is-ancestor-of-later-tip" ? D : C;
        var w = World(headRef: shape == "detached" ? null : OtherRef, headSha: head);
        w.Git.AddCommit(C, Root);
        w.Git.AddCommit(D, C);
        w.Git.CommitTimes[C] = AfterCapture;
        w.Git.CommitTimes[D] = AfterCapture.AddMinutes(1);

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var positive = ev.Evidence.Sources!.Single(s => s.Assessment == CompletionProgressAssessment.ProgressObserved);
        positive.Origin.ShouldBe(ProgressOrigin.PrimaryAlternate);
        positive.VerifiedSha.ShouldBe(C);
        positive.ClaimedSha.ShouldBe(C);
        positive.LocalObserved.ShouldBe(head);
        positive.RegisteredPath.ShouldBe(Checkout);
        positive.Reason.ShouldBe("primary_off_branch_claim");
        positive.ObservedRef.ShouldBe(shape == "detached" ? null : OtherRef);

        // The whole point of the alternate origin: this prevents a false failure and nothing more.
        ev.PrimaryDirectProgress.ShouldBeFalse();
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
        TaskCompletionProgressService.AllowsAutomaticWorkspaceMutation(ev.Evidence).ShouldBeFalse();
    }

    // ---- V-5 -------------------------------------------------------------------------------

    [Test]
    [Arguments("probe-available")]
    [Arguments("probe-unavailable")]
    public async Task C613_DivergentOwnTipIsProgress(string probe)
    {
        // The task is still on its own branch; that branch was reset into a lineage the baseline
        // is not part of. Before CARD-0613 this fell through the unclaimed_or_unmatched_commit arm.
        var files = probe == "probe-unavailable"
            ? new WorkspaceProgressArm(false, null, null, false)
            : new WorkspaceProgressArm(true, null, null, false);
        var w = World(headRef: ExpectedRef, headSha: C, expectedTip: C, files: files);
        w.Git.AddCommit(C, Root);
        w.Git.CommitTimes[C] = AfterCapture;

        var ev = await w.Svc.EvaluateAsync(w.Task, "reset the branch and committed.", default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var positive = ev.Evidence.Sources!.Single(s => s.Assessment == CompletionProgressAssessment.ProgressObserved);
        positive.Origin.ShouldBe(ProgressOrigin.PrimaryAlternate);
        positive.VerifiedSha.ShouldBe(C);
        positive.Reason.ShouldBe("primary_divergent_commit");
        positive.ObservedRef.ShouldBe(ExpectedRef);
        ev.Reason.ShouldBeNull();
        ev.Evidence.Sources!.ShouldNotContain(s => s.Reason == "baseline_lineage_broken");
        ev.Evidence.Sources!.ShouldNotContain(s => s.Reason == "unclaimed_or_unmatched_commit");
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
    }

    // ---- V-6 -------------------------------------------------------------------------------

    [Test]
    [Arguments("graph-negative")]
    [Arguments("metadata-unavailable")]
    public async Task C613_OffBranchFilesRemainProgress(string shape)
    {
        var w = World(
            headRef: OtherRef, headSha: C,
            files: new WorkspaceProgressArm(true, AfterCapture, null, false));
        w.Git.AddCommit(C, Root);
        // graph-negative: nothing names C, so the graph arm is a complete negative.
        // metadata-unavailable: a valid claim whose committer time cannot be read.
        if (shape == "metadata-unavailable") w.Git.CommitTimeFaults.Add(C);
        var body = shape == "metadata-unavailable" ? Claim(w.Task.Id, C) : "dirty files only.";

        var ev = await w.Svc.EvaluateAsync(w.Task, body, default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var positive = ev.Evidence.Sources!.Single(s => s.Assessment == CompletionProgressAssessment.ProgressObserved);
        positive.Origin.ShouldBe(ProgressOrigin.PrimaryAlternate);
        positive.Reason.ShouldBe("primary_off_branch_files");
        positive.RegisteredPath.ShouldBe(Checkout);
        positive.ObservedRef.ShouldBe(OtherRef);
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
    }

    [Test]
    public async Task C613_OffBranchFileArmNeedsAnActualFileChange()
    {
        // An empty file arm plus an unrelated recent COMMIT timestamp is not file evidence: the
        // probe's LastCommitAt scans other people's commits in a shared checkout.
        var w = World(
            headRef: OtherRef, headSha: C,
            files: new WorkspaceProgressArm(true, null, AfterCapture, false));
        w.Git.AddCommit(C, Root);

        var ev = await w.Svc.EvaluateAsync(w.Task, "no claim, nothing dirty.", default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Evidence.Sources!.ShouldNotContain(s => s.Origin == ProgressOrigin.PrimaryAlternate);
    }

    // ---- V-7 -------------------------------------------------------------------------------

    [Test]
    public async Task C613_AncestryFastPathStillWorks()
    {
        // A deep, backdated descendant of the baseline is ORDINARY progress: decided by ancestry,
        // never by the time fallback, and it keeps full Primary authority.
        var git = new FakeTaskProgressGit();
        var tip = Bl;
        for (var i = 0; i < 60; i++)
        {
            var next = $"{i:x40}"[..40];
            git.AddCommit(next, tip);
            tip = next;
        }
        git.AddCommit(E, tip);
        git.CommitTimes[E] = BeforeCapture;  // deliberately older than capture
        var w = World(headRef: ExpectedRef, headSha: E, expectedTip: E, git: git);

        var ev = await w.Svc.EvaluateAsync(w.Task, "ordinary commit on my own branch.", default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var positive = ev.Evidence.Sources!.Single(s => s.Assessment == CompletionProgressAssessment.ProgressObserved);
        positive.Origin.ShouldBe(ProgressOrigin.Primary);
        positive.VerifiedSha.ShouldBe(E);
        ev.PrimaryDirectProgress.ShouldBeTrue();
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeTrue();
        CommitTimeAsked(git).ShouldBeFalse();
        git.Log50Called.ShouldBeFalse();
    }

    [Test]
    public async Task C613_OnBranchFilePositiveStaysPrimary()
    {
        var w = World(
            headRef: ExpectedRef, headSha: Bl,
            files: new WorkspaceProgressArm(true, AfterCapture, null, false));

        var ev = await w.Svc.EvaluateAsync(w.Task, "uncommitted work on my own branch.", default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.Single(s => s.Assessment == CompletionProgressAssessment.ProgressObserved)
            .Origin.ShouldBe(ProgressOrigin.Primary);
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeTrue();
    }

    // ---- R-3 -------------------------------------------------------------------------------

    [Test]
    public async Task C613_LocalBaselineContainmentRejects()
    {
        // The candidate is already in the captured LOCAL history. A fresh committer date on it
        // must not buy credit: containment is excluded before the time fallback is consulted.
        var w = World(headRef: OtherRef, headSha: Bl);
        w.Git.AddCommit(Bl, Root);
        w.Git.CommitTimes[Root] = AfterCapture;

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, Root), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("claimed_commit_not_novel");
        ev.Evidence.Sources!.ShouldNotContain(s => s.Origin == ProgressOrigin.PrimaryAlternate);
    }

    [Test]
    public async Task C613_RemoteBaselineContainmentRejects()
    {
        // Local and remote baselines DIVERGE, and the candidate is contained only in the remote
        // one — so the local guard alone cannot reject it.
        var w = World(headRef: OtherRef, headSha: Rem, remoteTip: Rem);
        w.Git.AddCommit(Bl, Root);
        w.Git.AddCommit(Rem, Root);
        w.Git.CommitTimes[Rem] = AfterCapture;

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, Rem), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("claimed_commit_not_novel");
        ev.Evidence.Sources!.ShouldNotContain(s => s.Origin == ProgressOrigin.PrimaryAlternate);
    }

    // ---- R-4 -------------------------------------------------------------------------------

    [Test]
    [Arguments("clearly-older", CompletionProgressAssessment.NoAttributedProgress, "primary_commit_predates_dispatch")]
    [Arguments("same-second", CompletionProgressAssessment.Indeterminate, "primary_commit_time_overlaps_capture")]
    [Arguments("strictly-later", CompletionProgressAssessment.ProgressObserved, "primary_off_branch_claim")]
    public async Task C613_TimeLowerBound(string shape, CompletionProgressAssessment expected, string reason)
    {
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        w.Git.CommitTimes[C] = shape switch
        {
            "clearly-older" => BeforeCapture,
            "same-second" => Captured.AddMilliseconds(400),
            _ => AfterCapture,
        };

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(expected);
        if (expected == CompletionProgressAssessment.ProgressObserved)
        {
            ev.Evidence.Sources!.Single(s => s.Assessment == expected).Reason.ShouldBe(reason);
            ev.Evidence.Sources!.Single(s => s.Assessment == expected).Origin.ShouldBe(ProgressOrigin.PrimaryAlternate);
        }
        else
        {
            ev.Reason.ShouldBe(reason);
            ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
        }
    }

    [Test]
    public async Task C613_TimeLowerBoundUsesTheClaimNotTheLaterTip()
    {
        // The claim names an ancestor of a freshly dated HEAD. The CLAIM's own old timestamp
        // decides; borrowing the tip's would turn a re-checkout into fabricated progress.
        var w = World(headRef: OtherRef, headSha: D);
        w.Git.AddCommit(C, Root);
        w.Git.AddCommit(D, C);
        w.Git.CommitTimes[C] = BeforeCapture;
        w.Git.CommitTimes[D] = AfterCapture;

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("primary_commit_predates_dispatch");
    }

    [Test]
    public async Task C613_TimeUpperBound()
    {
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        w.Git.CommitTimes[C] = Future;

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe("primary_commit_time_unavailable");
        ev.Assessment.ShouldNotBe(CompletionProgressAssessment.ProgressObserved);
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
    }

    [Test]
    [Arguments("read-fault")]
    [Arguments("no-metadata")]
    public async Task C613_TimeReadUnavailable(string shape)
    {
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        if (shape == "read-fault") w.Git.CommitTimeFaults.Add(C);
        // "no-metadata" leaves CommitTimes empty: a missing fake time is UNAVAILABLE, never now.

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe("primary_commit_time_unavailable");
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
    }

    // ---- R-5 -------------------------------------------------------------------------------

    [Test]
    public async Task C613_ClaimMustBeReachable()
    {
        // A perfectly fresh claim that the observed HEAD cannot reach is not this checkout's work.
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        w.Git.AddCommit(Unrelated, Root);
        w.Git.CommitTimes[Unrelated] = AfterCapture;

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, Unrelated), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("claimed_commit_unreachable");
    }

    [Test]
    [Arguments("no-claim")]
    [Arguments("another-tasks-marker")]
    [Arguments("quoted-claim")]
    [Arguments("conflicting-claims")]
    public async Task C613_OffBranchRequiresClaim(string shape)
    {
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        w.Git.AddCommit(D, C);
        w.Git.CommitTimes[C] = AfterCapture;
        w.Git.CommitTimes[D] = AfterCapture;
        var body = shape switch
        {
            "no-claim" => "I switched to a sibling branch and did real work there, honest.",
            "another-tasks-marker" => Claim(Guid.NewGuid(), C),
            "quoted-claim" => "```\n" + Claim(w.Task.Id, C) + "\n```",
            _ => Claim(w.Task.Id, C) + "\n" + Claim(w.Task.Id, D),
        };

        var ev = await w.Svc.EvaluateAsync(w.Task, body, default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Evidence.Sources!.ShouldNotContain(s => s.Origin == ProgressOrigin.PrimaryAlternate);
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
    }

    [Test]
    public async Task C613_ClaimOnAnUnrelatedRefCannotRescue()
    {
        // The claim IS reachable from some other ref in the repository, but not from the ref this
        // task's registered checkout is actually on. Only the recorded path is probed.
        var w = World(headRef: OtherRef, headSha: Bl);
        w.Git.AddCommit(C, Root);
        w.Git.LocalRefs["refs/heads/someone-else"] = C;
        w.Git.CommitTimes[C] = AfterCapture;

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("claimed_commit_unreachable");
        w.Git.Trace.ShouldNotContain(a => a.Any(x => x.Contains("someone-else", StringComparison.Ordinal)));
    }

    [Test]
    public async Task C613_RegisteredCheckoutIdentity()
    {
        // The directory at the recorded path is a DIFFERENT repository now. Its history — however
        // fresh and however well it matches the claim — is not this task's evidence.
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        w.Git.CommitTimes[C] = AfterCapture;
        w.Git.CommonDirectoriesByPath[Checkout] = @"C:\some-other-repo";

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe("primary_checkout_identity_changed");
        ev.Assessment.ShouldNotBe(CompletionProgressAssessment.ProgressObserved);
    }

    [Test]
    [Arguments("sha-moved")]
    [Arguments("ref-moved")]
    public async Task C613_HeadChangeIsIndeterminate(string shape)
    {
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        w.Git.AddCommit(D, C);
        w.Git.CommitTimes[C] = AfterCapture;
        w.Git.CommitTimes[D] = AfterCapture;
        // Move the checkout out from under the evaluation, after its first HEAD observation.
        w.Git.OnHeadObserved = (git, n) =>
        {
            if (n != 1) return;
            if (shape == "sha-moved") git.HeadsByPath[Checkout] = D;
            else git.SymbolicHeads[Checkout] = "refs/heads/moved-again";
        };

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe("primary_head_changed");
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
    }

    // ---- R-6 -------------------------------------------------------------------------------

    [Test]
    [Arguments("ancestry-unknown")]
    [Arguments("status-unknown")]
    public async Task C613_IncompleteEvidenceCannotFail(string shape)
    {
        var files = shape == "status-unknown"
            ? new WorkspaceProgressArm(false, null, null, false)
            : new WorkspaceProgressArm(true, null, null, false);
        var w = World(headRef: OtherRef, headSha: C, files: files);
        w.Git.AddCommit(C, Root);
        w.Git.CommitTimes[C] = AfterCapture;
        if (shape == "ancestry-unknown")
        {
            // merge-base cannot answer: unknown ancestry is not a negative.
            w.Git.BeforeCommand = (_, _) => null;
            w.Git.CommitTimes.Remove(C);
            w.Git.CommitTimeFaults.Add(C);
        }

        var body = shape == "status-unknown" ? "quiet turn." : Claim(w.Task.Id, C);
        var ev = await w.Svc.EvaluateAsync(w.Task, body, default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Assessment.ShouldNotBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.AllowsAutomaticWorkspaceMutation.ShouldBeFalse();
    }

    [Test]
    public async Task C613_CancellationPropagates()
    {
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.Inject = (cmd, _) => cmd == "merge-base" ? new OperationCanceledException() : null;
        await Should.ThrowAsync<OperationCanceledException>(
            () => w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default));
    }

    [Test]
    public async Task C613_AlternateEvidenceRoundTripsWithoutAuthority()
    {
        var w = World(headRef: OtherRef, headSha: C);
        w.Git.AddCommit(C, Root);
        w.Git.CommitTimes[C] = AfterCapture;
        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);

        var json = TaskProgressJson.SerializeEvidence(ev.Evidence);
        var reloaded = TaskProgressJson.TryReadEvidence(json).ShouldNotBeNull();
        reloaded.SchemaVersion.ShouldBe(1);
        var positive = reloaded.Sources!.Single(s => s.Assessment == CompletionProgressAssessment.ProgressObserved);
        positive.Origin.ShouldBe(ProgressOrigin.PrimaryAlternate);
        positive.ObservedRef.ShouldBe(OtherRef);
        positive.VerifiedSha.ShouldBe(C);
        TaskCompletionProgressService.AllowsAutomaticWorkspaceMutation(reloaded).ShouldBeFalse();
        TaskProgressJson.ToDto(reloaded)!.Sources![0].ObservedRef.ShouldBe(OtherRef);
        TaskCompletionProgressService.ProgressWarning(reloaded).ShouldNotBeNull();

        // Evidence written before this card has no observedRef member and must still round-trip.
        var legacy = TaskProgressJson.TryReadEvidence("""
            {"schemaVersion":1,"assessment":"ProgressObserved","sources":[
              {"origin":"Primary","assessment":"ProgressObserved","verifiedSha":"cccccccccccccccccccccccccccccccccccccccc","complete":true}]}
            """).ShouldNotBeNull();
        legacy.Sources!.Single().ObservedRef.ShouldBeNull();
        legacy.Sources!.Single().Origin.ShouldBe(ProgressOrigin.Primary);
        TaskCompletionProgressService.AllowsAutomaticWorkspaceMutation(legacy).ShouldBeTrue();
    }

    // ---- R-7 / PC-18 -----------------------------------------------------------------------

    [Test]
    public async Task C613_RepairRewriteStaysIndeterminate()
    {
        // The time fallback is PRIMARY-LOCAL only. A repair source rewritten into another lineage
        // is still Indeterminate even with an explicit, freshly dated claim — applying the
        // fallback there would attribute someone else's branch surgery to this task.
        var git = new FakeTaskProgressGit();
        git.AddCommit(Bl, Root);
        git.AddCommit(C);  // C does not contain Bl
        git.CommitTimes[C] = AfterCapture;
        var w = World(headRef: ExpectedRef, headSha: Bl, git: git, withRepair: true, repairLocal: C);

        var ev = await w.Svc.EvaluateAsync(w.Task, Claim(w.Task.Id, C), default);

        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe("baseline_lineage_broken");
        ev.Assessment.ShouldNotBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.ShouldNotContain(s => s.Origin == ProgressOrigin.PrimaryAlternate);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string Claim(Guid id, string sha) => $"[antiphon-progress:{id:D} commit={sha}]";

    private static bool CommitTimeAsked(FakeTaskProgressGit git) =>
        git.Trace.Any(a => a.Length > 0 && a[0] == "show");

    private sealed record ContinuationWorld(
        AgentTask Task, FakeTaskProgressGit Git, TaskCompletionProgressService Svc);

    private static ContinuationWorld World(
        string? headRef = ExpectedRef,
        string? headSha = null,
        string expectedTip = Bl,
        string remoteTip = Bl,
        ProgressRemoteState remoteState = ProgressRemoteState.Present,
        WorkspaceProgressArm? files = null,
        FakeTaskProgressGit? git = null,
        bool withRepair = false,
        string repairLocal = Bl)
    {
        git ??= new FakeTaskProgressGit();
        git.AddCommit(Root);
        git.Parents.TryAdd(Bl, [Root]);
        git.CommonDirectory = Repo;
        git.CanonicalRepository = Repo;
        git.LocalRefs[ExpectedRef] = expectedTip;
        git.LocalRefs["HEAD"] = headSha ?? expectedTip;
        git.HeadsByPath[Checkout] = headSha ?? expectedTip;
        if (headRef is null) git.DetachedHeads.Add(Checkout);
        else git.SymbolicHeads[Checkout] = headRef;
        git.SymbolicHeads[Repo] = ExpectedRef;
        if (remoteState == ProgressRemoteState.Present)
            git.RemoteRefs[ExpectedRef] = remoteTip;

        var ownerRef = "refs/heads/feat/owner";
        if (withRepair)
        {
            git.LocalRefs[ownerRef] = repairLocal;
            git.RemoteRefs[ownerRef] = Bl;
        }

        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var primary = new ProgressSourceBaseline(
            Repo, Repo, id, Checkout, ExpectedRef, Bl,
            new ProgressRemoteBaseline(
                remoteState,
                remoteState == ProgressRemoteState.Present ? remoteTip == Rem ? Rem : Bl : null,
                git.EndpointFingerprint));
        ProgressSourceBaseline? repair = withRepair
            ? new ProgressSourceBaseline(
                Repo, Repo, ownerId, @"C:\repo\owner", ownerRef, Bl,
                new ProgressRemoteBaseline(ProgressRemoteState.Present, Bl, git.EndpointFingerprint))
            : null;

        var baseline = new ProgressBaselineSnapshot(1, Captured, Captured, primary, repair);
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "continuation",
            Goal = "continue the sibling's work",
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Checkout,
            RepoPath = Repo,
            WorktreePath = Checkout,
            WorktreeBranch = "feat/card-task-own",
            RepairSourceTaskId = withRepair ? ownerId : null,
            ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline),
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = Captured.AddMinutes(-1),
            DispatchedAt = Captured,
        };
        var probe = new StubWorkspaceProgressProbe(files ?? new WorkspaceProgressArm(true, null, null, false));
        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        return new ContinuationWorld(task, git, new TaskCompletionProgressService(git, probe, clock));
    }
}
