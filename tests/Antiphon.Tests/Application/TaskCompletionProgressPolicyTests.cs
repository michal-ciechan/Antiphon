using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class TaskCompletionProgressPolicyTests
{
    private const string Bl = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Br = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string C = "cccccccccccccccccccccccccccccccccccccccc";
    private const string D = "dddddddddddddddddddddddddddddddddddddddd";
    private const string X = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
    private const string Other = "1111111111111111111111111111111111111111";

    [Test]
    public async Task C499_V09_ClaimedChildOfBothBaselinesOnTheRepairSourceIsProgress()
    {
        var (task, git, svc) = World(repairLocal: C, repairRemote: C, claim: C);
        git.AddCommit(C, Bl);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.Single(s => s.Origin == ProgressOrigin.RepairSource).VerifiedSha.ShouldBe(C);
        ev.Evidence.Sources!.First(s => s.Assessment == CompletionProgressAssessment.ProgressObserved)
            .Origin.ShouldBe(ProgressOrigin.RepairSource);
        ev.Evidence.Sources!.First(s => s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote)
            .OwnerTaskId.ShouldNotBeNull();
        ev.Evidence.Sources!.First(s => s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote)
            .LocalObserved.ShouldBe(C);
        ev.Evidence.Sources!.First(s => s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote)
            .RemoteObserved.ShouldBe(C);
    }

    [Test]
    public async Task C499_V10_RemoteOnlyClaimedCommitIsProgress()
    {
        var (task, git, svc) = World(repairLocal: Bl, repairRemote: C, claim: C);
        git.AddCommit(C, Bl);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.Any(s => s.Origin == ProgressOrigin.RepairSourceRemote).ShouldBeTrue();
        git.Fetches.ShouldContain(f => f.StartsWith($"{TaskProgressGit.ProgressRefPrefix}{task.Id:N}/", StringComparison.Ordinal));
    }

    [Test]
    public async Task C499_V11_ClaimedAncestorOfALaterTipIsProgress()
    {
        var (task, git, svc) = World(repairLocal: D, repairRemote: D, claim: C);
        git.AddCommit(C, Bl);
        git.AddCommit(D, C);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.First(s => s.Assessment == CompletionProgressAssessment.ProgressObserved)
            .VerifiedSha.ShouldBe(C);
    }

    [Test]
    public async Task C499_V12_BackdatedAndDeepCommitsQualifyByAncestry()
    {
        var tip = Bl;
        var git = new FakeTaskProgressGit();
        git.AddCommit(Bl);
        for (var i = 0; i < 60; i++)
        {
            var next = $"{i:x40}"[..40];
            git.AddCommit(next, tip);
            tip = next;
        }
        git.AddCommit(C, tip);
        var (task, _, svc) = World(repairLocal: C, repairRemote: C, claim: C, git: git);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        git.Log50Called.ShouldBeFalse();
    }

    [Test]
    public async Task C499_V13_PrimaryNewCommitIsProgressWithoutAClaim()
    {
        var (task, git, svc) = World(primaryLocal: C, primaryRemote: Bl);
        git.AddCommit(C, Bl);
        var ev = await svc.EvaluateAsync(task, "done without a claim.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.First(s => s.Assessment == CompletionProgressAssessment.ProgressObserved)
            .Origin.ShouldBe(ProgressOrigin.Primary);
    }

    [Test]
    public async Task C499_V14_BothSourcesAreRecorded()
    {
        var (task, git, svc) = World(primaryLocal: C, repairLocal: D, repairRemote: D, claim: D);
        git.AddCommit(C, Bl);
        git.AddCommit(D, Bl);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, D), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.Count(s => s.Assessment == CompletionProgressAssessment.ProgressObserved).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task C499_V15_UnchangedTipsWithCompleteQueriesIsNoAttributedProgress()
    {
        var (task, _, svc) = World();
        var ev = await svc.EvaluateAsync(task, "nothing moved.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("no_movement");
        ev.Evidence.Sources!.ShouldAllBe(s => s.Complete);
    }

    [Test]
    [Arguments("Missing")]
    [Arguments("NotConfigured")]
    public async Task C499_V16_MissingRemoteAndNoRemoteAreCompleteNegatives(string state)
    {
        var remote = Enum.Parse<ProgressRemoteState>(state);
        var (task, git, svc) = World(repairRemoteState: remote, primaryRemoteState: remote);
        if (remote == ProgressRemoteState.NotConfigured) git.OriginConfigured = false;
        git.RemoteRefs.Clear();
        var ev = await svc.EvaluateAsync(task, "nothing.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Assessment.ShouldNotBe(CompletionProgressAssessment.Indeterminate);
    }

    [Test]
    public async Task C499_R01_ClaimOfTheBaselineTipIsNotNovel()
    {
        var (task, _, svc) = World(claim: Bl);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, Bl), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("claimed_commit_not_novel");
    }

    [Test]
    public async Task C499_R02_ClaimOfAnAncestorOfTheBaselineIsNotNovel()
    {
        var (task, git, svc) = World(claim: Other);
        git.AddCommit(Bl, Other);
        git.AddCommit(Other);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, Other), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
    }

    [Test]
    public async Task C499_R03_RemoteAheadAtDispatchIsNotProgress()
    {
        var (task, git, svc) = World(repairRemote: Br, claim: Br);
        git.AddCommit(Br, Bl);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, Br), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        git.Trace.ShouldContain(a => a.Length >= 3 && a[0] == "merge-base" && a[2] == Br);
    }

    [Test]
    public async Task C499_R04_CommitCopiedLocallyAfterDispatchIsNotProgress()
    {
        var (task, git, svc) = World(repairLocal: Br, repairRemote: Br, claim: Br);
        git.AddCommit(Br, Bl);
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, Br), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
    }

    [Test]
    public async Task C499_R05_MovementOfAnotherRefIsNotProgress()
    {
        var (task, git, svc) = World();
        git.LocalRefs["refs/heads/other"] = C;
        git.AddCommit(C, Bl);
        var ev = await svc.EvaluateAsync(task, "other branch moved.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("no_movement");
        git.Trace.ShouldNotContain(a => a.Any(x => x.Contains("refs/heads/other", StringComparison.Ordinal)));
    }

    [Test]
    public async Task C499_R06_UnclaimedSourceMovementIsNotProgress()
    {
        var (task, git, svc) = World(repairLocal: X);
        git.AddCommit(X, Bl);
        var ev = await svc.EvaluateAsync(task, "no claim line.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("unclaimed_or_unmatched_commit");
        ev.Evidence.Sources!.First(s => s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote)
            .LocalObserved.ShouldBe(X);
    }

    [Test]
    public async Task C499_R07_RewrittenHistoryIsIndeterminate()
    {
        var (task, git, svc) = World(repairLocal: C, claim: C);
        git.AddCommit(C); // C does not contain Bl
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe("baseline_lineage_broken");
        ev.Assessment.ShouldNotBe(CompletionProgressAssessment.NoAttributedProgress);
    }

    [Test]
    public async Task C499_R09_QuotedAndArbitraryHexAreNotClaims()
    {
        var (task, git, svc) = World(repairLocal: C);
        git.AddCommit(C, Bl);
        var body = $"""
            quoted:
            ```
            [antiphon-progress:{task.Id:D} commit={C}]
            ```
            and a bare {C}
            """;
        var ev = await svc.EvaluateAsync(task, body, default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.ClaimWarning.ShouldBe("claim_malformed_or_quoted");
    }

    [Test]
    public async Task C499_R10_ClaimMustNameThisTask()
    {
        var (task, git, svc) = World(repairLocal: C);
        git.AddCommit(C, Bl);
        var other = Guid.NewGuid();
        var ev = await svc.EvaluateAsync(task, Claim(other, C), default);
        ev.Claim.ShouldBeNull();
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
    }

    [Test]
    [Arguments("conflict")]
    [Arguments("repeat")]
    public async Task C499_R11_ConflictingClaimsConferNoCreditAndRepeatsAreIdempotent(string mode)
    {
        var (task, git, svc) = World(repairLocal: C, repairRemote: C);
        git.AddCommit(C, Bl);
        string body;
        if (mode == "conflict")
        {
            body = Claim(task.Id, C) + "\n" + Claim(task.Id, D);
            var ev = await svc.EvaluateAsync(task, body, default);
            ev.Claim.ShouldBeNull();
            ev.ClaimWarning.ShouldBe("claim_conflicting");
            ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        }
        else
        {
            body = Claim(task.Id, C) + "\n" + Claim(task.Id, C);
            var ev = await svc.EvaluateAsync(task, body, default);
            ev.Claim.ShouldBe(C);
            ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        }
    }

    [Test]
    public async Task C499_R12_ClaimNotReachableFromTheTipIsRefused()
    {
        var (task, git, svc) = World(repairLocal: Bl, claim: C);
        git.AddCommit(C, Other);
        git.LocalRefs["refs/heads/unrelated"] = C;
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        ev.Reason.ShouldBe("claimed_commit_unreachable");
    }

    [Test]
    public async Task C499_R13_ABranchSwitchInThePrimaryTreeIsNotIsolatedEvidence()
    {
        var (task, git, svc) = World(primaryLocal: C, withRepair: false);
        git.AddCommit(C, Bl);
        git.SymbolicHeads["HEAD"] = "refs/heads/other";
        git.SymbolicHeads[@"C:\repo\repair"] = "refs/heads/other";
        git.LocalRefs["refs/heads/other"] = C;
        var ev = await svc.EvaluateAsync(task, "switched.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
    }

    [Test]
    [Arguments("[antiphon-progress:{0} commit=abc]")]
    [Arguments("[antiphon-progress:{0} commit=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA]")]
    [Arguments("[antiphon-progress:{0} sha={1}]")]
    [Arguments("[antiphon-progress:{0} commit={1}] extra")]
    public async Task C499_R14_MalformedClaimsAreIgnored(string template)
    {
        var (task, _, svc) = World();
        var line = string.Format(template, task.Id.ToString("D"), C);
        var ev = await svc.EvaluateAsync(task, line, default);
        ev.Claim.ShouldBeNull();
        ev.ClaimWarning.ShouldBe("claim_malformed_or_quoted");
    }

    [Test]
    [Arguments("endpoint")]
    [Arguments("ref")]
    [Arguments("repository")]
    public async Task C499_R15_ChangedEndpointRefOrRepositoryIsIndeterminate(string kind)
    {
        var (task, git, svc) = World(claim: C);
        git.AddCommit(C, Bl);
        if (kind == "endpoint") git.EndpointFingerprint = new string('b', 64);
        if (kind == "ref") git.LocalRefs.Remove("refs/heads/feat/card-task-repair");
        if (kind == "repository") git.CommonDirectory = @"C:\other";
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe(kind switch
        {
            "endpoint" => "source_remote_endpoint_changed",
            "ref" => "source_ref_changed",
            _ => "source_repository_changed",
        });
    }

    [Test]
    public async Task C499_R17_CancellationPropagates()
    {
        var (task, git, svc) = World();
        git.Inject = (cmd, _) => cmd == "ls-remote" ? new OperationCanceledException() : null;
        await Should.ThrowAsync<OperationCanceledException>(() => svc.EvaluateAsync(task, "x", default));
    }

    [Test]
    public async Task C499_R18_APrimaryPositiveSurvivesARepairProbeFailure()
    {
        var (task, git, svc) = World(primaryLocal: C);
        git.AddCommit(C, Bl);
        git.BeforeCommand = (_, args) => args.Contains("refs/heads/feat/owner")
            ? new LandingGitResult(128, "", "git_exit_128") : null;
        var ev = await svc.EvaluateAsync(task, "primary commit.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        ev.Evidence.Sources!.Any(s => s.Origin == ProgressOrigin.Primary).ShouldBeTrue();
    }

    [Test]
    public async Task C499_R19_AnUnknownRequiredArmMakesTheAggregateIndeterminate()
    {
        var (task, git, svc) = World();
        git.BeforeCommand = (_, args) => args.Contains("refs/heads/feat/owner")
            ? new LandingGitResult(128, "", "git_exit_128") : null;
        var ev = await svc.EvaluateAsync(task, "primary quiet.", default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Assessment.ShouldNotBe(CompletionProgressAssessment.NoAttributedProgress);
    }

    [Test]
    public async Task C499_R20_UnavailableBaselineRemoteCannotCreditARemoteClaim()
    {
        var (task, git, svc) = World(repairLocal: Bl, repairRemote: C, repairRemoteState: ProgressRemoteState.Unavailable, claim: C);
        git.AddCommit(C, Bl);
        git.RemoteRefs["refs/heads/feat/owner"] = C;
        var ev = await svc.EvaluateAsync(task, Claim(task.Id, C), default);
        ev.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        ev.Reason.ShouldBe("baseline_remote_unavailable");
    }

    private static string Claim(Guid id, string sha) => $"[antiphon-progress:{id:D} commit={sha}]";

    private static (AgentTask Task, FakeTaskProgressGit Git, TaskCompletionProgressService Svc) World(
        string primaryLocal = Bl,
        string primaryRemote = Bl,
        string repairLocal = Bl,
        string repairRemote = Bl,
        ProgressRemoteState primaryRemoteState = ProgressRemoteState.Present,
        ProgressRemoteState repairRemoteState = ProgressRemoteState.Present,
        string? claim = null,
        bool withRepair = true,
        FakeTaskProgressGit? git = null)
    {
        git ??= new FakeTaskProgressGit();
        git.AddCommit(Bl);
        git.CanonicalRepository = @"C:\repo";
        git.CommonDirectory = @"C:\repo";
        git.LocalRefs["refs/heads/feat/card-task-repair"] = primaryLocal;
        git.LocalRefs["HEAD"] = primaryLocal;
        git.SymbolicHeads["HEAD"] = "refs/heads/feat/card-task-repair";
        git.SymbolicHeads[@"C:\repo\repair"] = "refs/heads/feat/card-task-repair";
        if (primaryRemoteState == ProgressRemoteState.Present)
            git.RemoteRefs["refs/heads/feat/card-task-repair"] = primaryRemote;
        git.LocalRefs["refs/heads/feat/owner"] = repairLocal;
        if (repairRemoteState == ProgressRemoteState.Present)
            git.RemoteRefs["refs/heads/feat/owner"] = repairRemote;
        if (primaryLocal != Bl) git.Parents.TryAdd(primaryLocal, new HashSet<string> { Bl });
        if (repairLocal != Bl) git.Parents.TryAdd(repairLocal, new HashSet<string> { Bl });
        if (repairRemote != Bl) git.Parents.TryAdd(repairRemote, new HashSet<string> { Bl });

        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var primary = new ProgressSourceBaseline(
            @"C:\repo", @"C:\repo", id, @"C:\repo\repair", "refs/heads/feat/card-task-repair", Bl,
            new ProgressRemoteBaseline(primaryRemoteState, primaryRemoteState == ProgressRemoteState.Present ? Bl : null,
                git.EndpointFingerprint, primaryRemoteState == ProgressRemoteState.Unavailable ? "source_remote_unreadable" : null));
        ProgressSourceBaseline? repair = withRepair
            ? new ProgressSourceBaseline(
                @"C:\repo", @"C:\repo", ownerId, @"C:\repo\owner", "refs/heads/feat/owner", Bl,
                new ProgressRemoteBaseline(repairRemoteState, repairRemoteState == ProgressRemoteState.Present ? Br == repairRemote ? Br : Bl : null,
                    git.EndpointFingerprint, repairRemoteState == ProgressRemoteState.Unavailable ? "source_remote_unreadable" : null))
            : null;
        if (withRepair && repairRemoteState == ProgressRemoteState.Present)
            repair = repair! with { Remote = new ProgressRemoteBaseline(ProgressRemoteState.Present, repairRemote == Br ? Br : Bl, git.EndpointFingerprint) };

        var baseline = new ProgressBaselineSnapshot(1, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-10), primary, repair);
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "repair",
            Goal = "fix",
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = @"C:\repo\repair",
            RepoPath = @"C:\repo",
            WorktreePath = @"C:\repo\repair",
            WorktreeBranch = "feat/card-task-repair",
            RepairSourceTaskId = withRepair ? ownerId : null,
            ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline),
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = DateTime.UtcNow.AddMinutes(-11),
            DispatchedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        var files = new StubWorkspaceProgressProbe(new WorkspaceProgressArm(true, null, null, false));
        return (task, git, new TaskCompletionProgressService(git, files));
    }
}
