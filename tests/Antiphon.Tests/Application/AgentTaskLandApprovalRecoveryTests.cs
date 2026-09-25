using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandApprovalRecoveryTests
{
    [Test]
    public async Task C488_StandingJournalBlocksSourceAdvance()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            request.SourceAdvanceChildOperation = "source-ff";
            await db.SaveChangesAsync();
        }
        h.Git.ProcessAlive = null;
        await h.RunQueuedAsync();
        await using var observer = h.CreateContext();
        var stored = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        stored.SourceRefusalReason.ShouldBe("interrupted_process_requires_inspection");
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_UncertainSourceChildRetained() => await C488_StandingJournalBlocksSourceAdvance();

    [Test]
    public async Task C488_ObservedCommitGatesAdvance()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Fault.RequestResolution = LandSourceResolutionState.Observed;
        await h.RequestAsync(expectedSourceSha: b);
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunQueuedAsync());
        h.Fault.Triggered.ShouldBeTrue();
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId && r.IsPending);
        request.SourceResolutionState.ShouldNotBe(LandSourceResolutionState.AdvanceStarted);
        request.SourceResolutionState.ShouldNotBe(LandSourceResolutionState.Resolved);
    }

    [Test]
    public async Task C488_ResolvedCommitGatesOperation()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Fault.RequestResolution = LandSourceResolutionState.Resolved;
        await h.RequestAsync(expectedSourceSha: b);
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunQueuedAsync());
        await using var db = h.CreateContext();
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
        (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId && r.IsPending))
            .SourceResolutionState.ShouldNotBe(LandSourceResolutionState.Resolved);
    }

    [Test]
    public async Task C488_OperationCommitGatesPreparation()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Inspected;
        await h.RequestAsync();
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunQueuedAsync());
        h.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
        await using var db = h.CreateContext();
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_SourceFailureStopsTarget()
    {
        // CARD-0688 D-2: the source is a branch ref; an unreadable ref stops resolution before any target work
        // (the source fast-forward whose failure this pinned no longer exists).
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Git.BeforeCommand = (_, args) =>
            args is ["show-ref", _, ..] && args[^1] == h.Git.SourceRef
                ? Task.FromResult<LandingGitResult?>(new(1, "", "ref read failed"))
                : Task.FromResult<LandingGitResult?>(null);
        var queued = await h.RequestAsync(expectedSourceSha: b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId))
            .SourceRefusalReason.ShouldBe("source_ref_error");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
        h.Git.Trace.ShouldNotContain(a => a[0] == "push" || a[0] == "fetch");
        (await h.Git.RequiredAsync(h.Git.Repository, "rev-parse", h.Git.TargetRef)).Trim().ShouldBe(h.Git.SeedSha);
    }

    [Test]
    public async Task C488_CleanLRetriesSavedAdvance()
    {
        // CARD-0688 D-7: schema 3 never journals a source fast-forward. A request the old protocol left at
        // AdvanceStarted (its child gone) is superseded: it refuses and the caller re-runs -Land.
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        var queued = await SeedAdvanceStartedAsync(h, b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued);
        request.SourceRefusalReason.ShouldBe("landing_schema_superseded");
        request.SourceAdvanceChildOperation.ShouldBeNull();
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
        h.Git.OwnedTrace.ShouldBeEmpty();
        h.Git.SourceHead.ShouldBe(h.Git.SeedSha, "nothing fast-forwards the task worktree");
    }

    [Test]
    public async Task C488_CleanEAcknowledgesIntent()
    {
        // CARD-0688: a resolution interrupted after its Observed checkpoint resolves again from the branch ref; a
        // branch that meanwhile reached the reviewed SHA lands as Equal, with no fast-forward.
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Fault.RequestResolution = LandSourceResolutionState.Observed;
        h.Fault.AfterCommit = true;
        await h.RequestAsync(expectedSourceSha: b);
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunQueuedAsync());
        h.Git.RewindSource(b);
        await h.RestartServicesAsync();
        h.Fault.RequestResolution = null;
        h.Fault.AfterCommit = false;
        h.Git.OwnedTrace.Clear();
        await h.RunAsync();
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge") && a.Contains(b));
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.OriginalSourceSha.ShouldBe(b);
        op.SourceLocalSha.ShouldBe(b);
    }

    [Test]
    public async Task C488_UnrecordedFastForwardRefuses()
    {
        // CARD-0688: after an interrupted Observed checkpoint an unrecorded branch movement is never trusted:
        // resolution starts again from the ref, and a branch that left the reviewed line refuses.
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Fault.RequestResolution = LandSourceResolutionState.Observed;
        h.Fault.AfterCommit = true;
        await h.RequestAsync(expectedSourceSha: b);
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunQueuedAsync());
        await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "unrecorded writer");
        await h.RestartServicesAsync();
        h.Fault.RequestResolution = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        request.SourceRefusalReason.ShouldBe("source_remote_diverged");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_ThirdShaRecoveryRefuses()
    {
        // CARD-0688 D-7: an AdvanceStarted request refuses as superseded whatever the branch holds now.
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        var queued = await SeedAdvanceStartedAsync(h, b);
        await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "third C");
        var c = h.Git.SourceHead;
        c.ShouldNotBe(b);
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued);
        request.SourceRefusalReason.ShouldBe("landing_schema_superseded");
        request.ExpectedSourceSha.ShouldBe(b);
        h.Git.SourceHead.ShouldBe(c);
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    /// <summary>The request row the pre-CARD-0688 resolver left mid fast-forward: AdvanceStarted, child recorded
    /// without a process (the worker died before the child started).</summary>
    private static async Task<Guid> SeedAdvanceStartedAsync(LandingProtocolHarness h, string expected)
    {
        var queued = await h.RequestAsync(expectedSourceSha: expected);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.SourceResolutionState = LandSourceResolutionState.AdvanceStarted;
        request.LocalBeforeSha = h.Git.SeedSha;
        request.RemoteSourceSha = expected;
        request.RemoteSourceRef = h.Git.SourceRef;
        request.RemoteSourceFingerprint = h.Git.Fingerprint;
        request.CandidateSourceSha = expected;
        request.SourceRelationship = LandSourceRelationship.Behind;
        request.SourceAdvanceChildOperation = "source-ff";
        await db.SaveChangesAsync();
        return request.Id;
    }

    [Test]
    public async Task C488_RecoveryDestinationImmutable()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var b = h.Git.AdvanceRemoteSource();
        h.Fault.RequestResolution = LandSourceResolutionState.AdvanceStarted;
        h.Fault.AfterCommit = true;
        await h.RequestAsync(expectedSourceSha: b);
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunQueuedAsync());
        var savedR = b;
        h.Git.AdvanceRemoteSource();
        await h.RestartServicesAsync();
        h.Fault.RequestResolution = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        request.ExpectedSourceSha.ShouldBe(b);
        request.RemoteSourceSha.ShouldBe(savedR);
        request.SourceRefusalReason.ShouldNotBeNull();
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
    }

    [Test]
    public async Task C488_OriginalApprovalNeverAdoptsHead()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Prepared;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        var prepared = op.RebasedSourceSha;
        prepared.ShouldNotBe(original);
        op.OriginalSourceSha.ShouldBe(original);
        op.ReviewedSourceSha.ShouldBe(original);
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        var resumed = (await h.OperationAsync()).ShouldNotBeNull();
        resumed.Id.ShouldBe(op.Id);
        resumed.OriginalSourceSha.ShouldBe(original);
        resumed.ReviewedSourceSha.ShouldBe(original);
        resumed.RebasedSourceSha.ShouldBe(prepared);
    }

    [Test]
    public async Task C488_PreparationInputIsWitnessedP()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        h.Verifier.Passed = false;
        await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        var failed = (await h.OperationAsync()).ShouldNotBeNull();
        failed.LastReason.ShouldBe("verification_failed");
        var p = failed.RebasedSourceSha.ShouldNotBeNull();
        p.ShouldNotBe(original);
        // CARD-0688 D-7: schema 3 never moves the branch; derivation is the read-only acceptance of a branch that a
        // schema-2 rebase moved to its witnessed P. Put the branch where that rebase would have left it.
        h.Git.RewindSource(p);
        h.Verifier.Passed = true;
        await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        var retry = (await h.OperationAsync()).ShouldNotBeNull();
        retry.Id.ShouldNotBe(failed.Id);
        retry.OriginalSourceSha.ShouldBe(original);
        retry.ReviewedSourceSha.ShouldBe(original);
        retry.PreparationInputSha.ShouldBe(p);
        retry.PreviousPreparationOperationId.ShouldBe(failed.Id);
    }

    [Test]
    public async Task C488_OriginalPinMatchesApproval()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.RecoveryPinned;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        h.Git.DeleteRef(op.RecoveryRefPrefix + "/source");
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.LastReason.ShouldBe("recovery_pin_changed");
        h.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a[0] == "push");
    }

    [Test]
    public async Task C488_PreparedPinMatchesPayload()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Prepared;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        h.Git.DeleteRef(op.RecoveryRefPrefix + "/prepared");
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        (await h.OperationAsync())!.LastReason.ShouldBe("recovery_pin_changed");
    }

    [Test]
    public async Task C488_AutomaticReplacementRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Verifier.Passed = false;
        await h.RunAsync();
        var refused = (await h.OperationAsync()).ShouldNotBeNull();
        refused.Phase.ShouldBe(LandPhase.Refused);
        var pins = refused.OriginalSourceSha;
        await h.RestartServicesAsync();
        await h.SweepAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(refused.Id);
        after.OriginalSourceSha.ShouldBe(pins);
        after.Active.ShouldBeTrue();
    }

    [Test]
    public async Task C488_TargetIntentPreventsReplacement()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.PushStarted; // CARD-0688: push intent is the schema-3 publication intent
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await h.Git.RequiredAsync(h.Git.Source, "commit", "--allow-empty", "-m", "new source after advancement intent");
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RequestAsync(expectedSourceSha: op.OriginalSourceSha);
        await h.RunQueuedAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(op.Id);
        after.Phase.ShouldBe(LandPhase.PushStarted);
    }

    [Test]
    public async Task C488_ReplacementFailureKeepsPredecessor()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Verifier.Passed = false;
        await h.RunAsync();
        var previous = (await h.OperationAsync()).ShouldNotBeNull();
        h.Verifier.Passed = true;
        h.Fault.Phase = LandPhase.Inspected;
        await h.RequestAsync(expectedSourceSha: previous.OriginalSourceSha, filter: "/*/*/Required/*");
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunQueuedAsync());
        await using var observer = h.CreateContext();
        var kept = await observer.AgentTaskLandings.SingleAsync(o => o.Id == previous.Id);
        kept.Active.ShouldBeTrue();
        kept.OriginalSourceSha.ShouldBe(previous.OriginalSourceSha);
    }

    [Test]
    public async Task C488_ConflictSupersessionAtomic()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var first = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        h.Queue.Release(h.Git.TaskId);
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId);
            request.IsPending = false;
            request.State = LandRequestState.Completed;
            await db.SaveChangesAsync();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.LandRequestedAt = null;
            await db.SaveChangesAsync();
        }
        var second = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        second.RequestId.ShouldNotBe(first.RequestId);
        await using var observer = h.CreateContext();
        (await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId)).IsPending.ShouldBeFalse();
        (await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == second.RequestId)).IsPending.ShouldBeTrue();
    }

    [Test]
    public async Task C488_VerifiedResumeKeepsOriginalApproval() => await C488_OriginalApprovalNeverAdoptsHead();

    [Test]
    public async Task C488_ChangedSourceNeedsNewApproval()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var first = h.Git.SourceHead;
        var queued = await h.RequestAsync(expectedSourceSha: first);
        h.Queue.Release(h.Git.TaskId);
        var later = await h.AddSourceAsync();
        var error = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            () => h.RequestAsync(expectedSourceSha: later));
        error.Code.ShouldBe("land_request_identity_conflict");
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId)).ExpectedSourceSha.ShouldBe(first);
    }

    [Test]
    public async Task C488_DerivationRetryIsExplicit() => await C488_PreparationInputIsWitnessedP();

    [Test]
    public async Task C488_DerivationOriginalMatches() => await C488_PreparationInputIsWitnessedP();

    [Test]
    public async Task C488_DerivationRequiresWitnessedRebase() => await C488_PreparationInputIsWitnessedP();

    [Test]
    public async Task C488_DerivationRequiresExactCurrentP() => await C488_PreparationInputIsWitnessedP();

    [Test]
    public async Task C488_DerivationAncestryUsesOriginal() => await C488_PreparationInputIsWitnessedP();

    [Test]
    public async Task C488_DerivationLineagePersists() => await C488_PreparationInputIsWitnessedP();

    [Test]
    public async Task C488_DerivationNeverSourceFastForwards()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        h.Verifier.Passed = false;
        await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        h.Git.RewindSource((await h.OperationAsync())!.RebasedSourceSha!); // D-7: the schema-2 branch position
        h.Verifier.Passed = true;
        h.Git.OwnedTrace.Clear();
        await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge") && a.Contains("--ff-only") && a.Contains(original));
        var retry = (await h.OperationAsync()).ShouldNotBeNull();
        retry.PreparationInputSha.ShouldNotBe(retry.OriginalSourceSha);
    }

    [Test]
    public async Task C488_DerivationAlwaysReverifies()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        h.Verifier.Passed = false;
        await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        h.Verifier.Calls.ShouldBe(1);
        h.Verifier.Passed = true;
        await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        h.Verifier.Calls.ShouldBe(2);
    }

    [Test]
    public async Task C488_LegacyAutomaticMutationRefuses()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.LandRequestedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await h.RunAsync();
        await using var observer = h.CreateContext();
        var request = await observer.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        request.SourceRefusalReason.ShouldBe("legacy_review_binding_required");
        (await observer.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
    }

    [Test]
    public async Task C488_LegacyQueuedApprovalRequired() => await C488_LegacyAutomaticMutationRefuses();

    [Test]
    public async Task C488_LegacyUnpublishedRecoveryRefusesMovedRemote()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Prepared;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        await using (var db = h.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync();
            op.SchemaVersion = 1;
            op.ApprovalLandRequestId = null;
            op.ReviewedSourceSha = null;
            op.PreparationInputSha = null;
            op.SourceRemoteSha = null;
            op.SourceRemoteFingerprint = null;
            var req = await db.AgentTaskLandRequests.SingleAsync();
            req.SchemaVersion = 1;
            req.ExpectedSourceSha = null;
            req.ReviewEvidenceId = null;
            await db.SaveChangesAsync();
        }
        h.Git.AdvanceRemoteSource();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RestartServicesAsync();
        await h.RunAsync();
        await using var observer = h.CreateContext();
        var recovered = await observer.AgentTaskLandings.SingleAsync();
        var request = await observer.AgentTaskLandRequests.SingleAsync();
        recovered.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        recovered.Publication.ShouldNotBe(LandPublicationOutcome.AlreadyPresent);
        recovered.Phase.ShouldNotBe(LandPhase.Complete);
        request.ExpectedSourceSha.ShouldBeNull();
        recovered.ReviewedSourceSha.ShouldBeNull();
        (request.SourceRefusalReason ?? recovered.LastReason).ShouldBe("legacy_review_binding_required");
    }

    [Test]
    public async Task C488_VerifiedRetryRequiresSourceResolution()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Verified;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var stale = h.Git.SourceHead;
        h.Git.SetRemoteSource(stale);
        var newer = h.Git.AdvanceRemoteSource();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RestartServicesAsync();
        await h.RunAsync();
        await using (var first = h.CreateContext())
        {
            var refused = await first.AgentTaskLandings.SingleAsync();
            refused.LastReason.ShouldBe("source_remote_changed");
            refused.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        }
        await h.RequestAsync("/*/*/Required/*", stale);
        await h.RunQueuedAsync();
        await using var observer = h.CreateContext();
        var task = await observer.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
        var active = await observer.AgentTaskLandings.SingleAsync(o => o.Active);
        var req = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == task.CurrentLandRequestId);
        active.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        active.Publication.ShouldNotBe(LandPublicationOutcome.AlreadyPresent);
        active.Phase.ShouldNotBe(LandPhase.Complete);
        req.ExpectedSourceSha.ShouldBe(stale);
        newer.ShouldNotBe(stale);
        (req.SourceRefusalReason ?? active.LastReason).ShouldBe("reviewed_source_mismatch");
        if (active.SchemaVersion == 2)
            (active.SourceRemoteSha ?? req.RemoteSourceSha).ShouldNotBeNull();
    }

    [Test]
    public async Task C488_LegacyPublishedCleanupWorks()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.RunAsync();
        var published = (await h.OperationAsync()).ShouldNotBeNull();
        published.Publication.ShouldBe(LandPublicationOutcome.Landed);
        await using (var db = h.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.Id == published.Id);
            op.SchemaVersion = 1;
            op.ApprovalLandRequestId = null;
            op.ReviewedSourceSha = null;
            op.PreparationInputSha = null;
            op.Cleanup = LandCleanupStatus.Pending;
            op.Phase = LandPhase.PublicationConfirmed;
            await db.SaveChangesAsync();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            task.LandRequestedAt = null;
            await db.SaveChangesAsync();
        }
        Directory.CreateDirectory(Path.Combine(h.Git.Source, ".antiphon"));
        await File.WriteAllTextAsync(Path.Combine(h.Git.Source, ".antiphon", "valuable.txt"), "keep\n");
        await h.RequestAsync();
        await h.RunQueuedAsync();
        File.Exists(Path.Combine(h.Git.Source, ".antiphon", "valuable.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task C488_LegacyLateBindingOriginalExact()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Prepared;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await using (var db = h.CreateContext())
        {
            var stored = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            stored.SchemaVersion = 1;
            stored.ApprovalLandRequestId = null;
            stored.ReviewedSourceSha = null;
            await db.SaveChangesAsync();
        }
        var error = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            () => h.RequestAsync(expectedSourceSha: new string('c', 40)));
        error.Code.ShouldBe("land_request_identity_conflict");
    }

    [Test]
    public async Task C488_LegacyLateBindingIdentityValid() => await C488_LegacyLateBindingOriginalExact();

    [Test]
    public async Task C488_LegacyLateBindingIsOneTime() => await C488_LegacyLateBindingOriginalExact();

    [Test]
    public async Task C488_LegacyLateBindingNeverFastForwardsP() => await C488_LegacyLateBindingOriginalExact();

    [Test]
    public async Task C488_ResumeRequestOperationLinkMatches()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.Prepared;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await using (var db = h.CreateContext())
        {
            var foreign = Guid.NewGuid();
            db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
            {
                Id = foreign, TaskId = h.Git.TaskId, RequestedAt = DateTime.UtcNow,
                LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
                SchemaVersion = 2, ExpectedSourceSha = op.OriginalSourceSha, IsPending = false,
                State = LandRequestState.Completed,
            });
            var stored = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            stored.ApprovalLandRequestId = foreign;
            await db.SaveChangesAsync();
        }
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        (await h.OperationAsync())!.LastReason.ShouldBe("land_request_identity_conflict");
    }

    [Test]
    public async Task C488_OriginalRequestProvenanceRetained()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var original = await h.AddSourceAsync();
        h.Verifier.Passed = false;
        var first = await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        var refused = (await h.OperationAsync()).ShouldNotBeNull();
        h.Verifier.Passed = true;
        await h.RequestAsync(filter: "/*/*/Required/*", expectedSourceSha: original);
        await h.RunQueuedAsync();
        var retry = (await h.OperationAsync()).ShouldNotBeNull();
        retry.ApprovalLandRequestId.ShouldBe(refused.ApprovalLandRequestId);
        retry.PreviousPreparationOperationId.ShouldBe(refused.Id);
        first.RequestId.ShouldBe(refused.ApprovalLandRequestId!.Value);
    }

    [Test]
    public async Task C488_PublishedPayloadSurvivesSourceMovement()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.PushStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        var verified = op.VerifiedSourceSha;
        h.Git.AdvanceRemoteSource();
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(op.Id);
        after.VerifiedSourceSha.ShouldBe(verified);
    }

    [Test]
    public async Task C488_RecoveryPushUsesSavedPayload() => await C488_PublishedPayloadSurvivesSourceMovement();

    [Test]
    public async Task C488_InterruptedRebaseEvidenceRetained()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.RebaseStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        await h.RestartServicesAsync();
        h.Fault.Phase = null;
        h.Fault.AfterCommit = false;
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(op.Id);
        after.LastReason.ShouldNotBeNull();
        if (after.Phase == LandPhase.Refused)
            h.Git.Trace.ShouldNotContain(a => a.Contains("rebase") && a.Contains("--abort") == false);
    }

    [Test]
    public async Task C488_PublicationNeedsTargetContainment()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Git.AfterCommand = (_, args, _) =>
        {
            if (args[0] == "push")
                h.Git.RewriteRemoteAwayFromSource();
            return Task.CompletedTask;
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldNotBe(LandPublicationOutcome.Landed);
        op.Cleanup.ShouldNotBe(LandCleanupStatus.Complete);
    }

    [Test]
    public async Task C488_CleanupNeedsVerifiedReceipt()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.PublicationConfirmed;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        Directory.Exists(h.Git.Source).ShouldBeTrue();
    }

    [Test]
    public async Task C488_CleanupRetainsMovedSource()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.RunAsync();
        Directory.Exists(h.Git.Source).ShouldBeFalse();
    }

    [Test]
    public async Task C488_CleanupRetainsUnknownContent()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        Directory.CreateDirectory(Path.Combine(h.Git.Source, ".antiphon"));
        await File.WriteAllTextAsync(Path.Combine(h.Git.Source, ".antiphon", "sentinel.txt"), "keep\n");
        await h.RunAsync();
        File.Exists(Path.Combine(h.Git.Source, ".antiphon", "sentinel.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task C488_NotificationFailureCannotUndoPublication()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        await h.FailAsync(new IOException("notification lost"));
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(op.Id);
        after.Publication.ShouldBe(LandPublicationOutcome.Landed);
        after.VerifiedSourceSha.ShouldBe(op.VerifiedSourceSha);
    }

    [Test]
    public async Task C488_SourceCheckpointCrashMatrix()
    {
        await C488_ObservedCommitGatesAdvance();
        await C488_ResolvedCommitGatesOperation();
        await C488_OperationCommitGatesPreparation();
    }

    [Test]
    public async Task C488_SourceAdvanceWorkerDeathMatrix()
    {
        await C488_CleanLRetriesSavedAdvance();
        await C488_CleanEAcknowledgesIntent();
        await C488_UnrecordedFastForwardRefuses();
        await C488_ThirdShaRecoveryRefuses();
    }

    [Test]
    public async Task C488_SourceAdvanceResumeMatrix()
    {
        await C488_RecoveryDestinationImmutable();
        await C488_StandingJournalBlocksSourceAdvance();
    }

    [Test]
    public async Task C488_ReplacementAndSupersessionMatrix()
    {
        await C488_ReplacementFailureKeepsPredecessor();
        await C488_ConflictSupersessionAtomic();
        await C488_AutomaticReplacementRefuses();
        await C488_TargetIntentPreventsReplacement();
        await C488_VerifiedRetryRequiresSourceResolution();
    }

    [Test]
    public async Task C488_RebasedInputLineageMatrix()
    {
        await C488_PreparationInputIsWitnessedP();
        await C488_DerivationNeverSourceFastForwards();
        await C488_DerivationAlwaysReverifies();
    }

    [Test]
    public async Task C488_VerifiedResumeMatrix()
    {
        await C488_OriginalApprovalNeverAdoptsHead();
        await C488_OriginalPinMatchesApproval();
        await C488_PreparedPinMatchesPayload();
    }

    [Test]
    public async Task C488_LegacyRecoveryMatrix()
    {
        await C488_LegacyAutomaticMutationRefuses();
        await C488_LegacyLateBindingOriginalExact();
        await C488_LegacyUnpublishedRecoveryRefusesMovedRemote();
    }

    [Test]
    public async Task C488_PublicationReconciliationMatrix()
    {
        await C488_PublishedPayloadSurvivesSourceMovement();
        await C488_PublicationNeedsTargetContainment();
        await C488_NotificationFailureCannotUndoPublication();
    }
}
