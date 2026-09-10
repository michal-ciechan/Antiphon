using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class LandingProtocolHarnessTests
{
    private static readonly Regex Arguments = new(@"\[Arguments\((.*)\)\]", RegexOptions.Compiled);
    private static readonly HashSet<(string Boundary, string Change, bool Contained)> RealV10Capstones =
    [
        ("remote", "advance", false), ("remote", "advance", true), ("remote", "switch", true),
        ("BeforeRebaseIntent", "dirty", false), ("RebaseStarted", "staged", false),
        ("Prepared", "untracked", false), ("Prepared", "metadata", false),
        ("Verified", "switch", false), ("Verified", "metadata-path", false),
        ("TargetAdvanceStarted", "advance", false), ("LocalTargetAdvanced", "dirty", false),
        ("BeforePushIntent", "staged", false), ("BeforePushIntent", "metadata-target", false),
        ("PushStarted", "untracked", false), ("PushStarted", "metadata-repository", false),
    ];

    [Test]
    public void C475_CoverageAllocationMatchesTheLegacyTupleManifest()
    {
        C475LegacyLandTuples.All.Count.ShouldBe(169);
        var controlledV10 = AttributeTuples("AgentTaskLandBoundaryControlledTests.cs", "C448_V10_EachAcknowledgedBoundaryRechecksSource");
        var controlledFreeze = AttributeTuples("AgentTaskLandBoundaryControlledTests.cs", "C448_V10_VerificationCannotFreezeOldTaskCoordinates");
        var realV10 = AttributeTuples("AgentTaskLandBoundaryTests.cs", "C448_V10_EachAcknowledgedBoundaryRechecksSource");
        var admissionControlled = AttributeTuples("AgentTaskLandAdmissionControlledTests.cs", "C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther");
        var admissionReal = AttributeTuples("AgentTaskLandAdmissionTests.cs", "C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther");
        var concV14c = AttributeTuples("AgentTaskLandConcurrencyControlledTests.cs", "C448_V14_EveryModeHonoursWriterAndLeaseHolds");
        var concV10c = AttributeTuples("AgentTaskLandConcurrencyControlledTests.cs", "C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget");
        var concV14r = AttributeTuples("AgentTaskLandConcurrencyTests.cs", "C448_V14_EveryModeHonoursWriterAndLeaseHolds");
        var concV10r = AttributeTuples("AgentTaskLandConcurrencyTests.cs", "C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget");

        controlledV10.Count.ShouldBe(90);
        controlledFreeze.Count.ShouldBe(4);
        realV10.Count.ShouldBe(15);
        admissionControlled.Count.ShouldBe(24);
        admissionReal.Count.ShouldBe(8);
        concV14c.Count.ShouldBe(12);
        concV10c.Count.ShouldBe(10);
        concV14r.Count.ShouldBe(4);
        concV10r.Count.ShouldBe(4);

        (controlledV10.Count + controlledFreeze.Count).ShouldBe(94);
        (admissionControlled.Count).ShouldBe(24);
        (concV14c.Count + concV10c.Count).ShouldBe(22);

        var realBoundaryOther = CountMethods("AgentTaskLandBoundaryTests.cs",
            "C448_V11_TargetMutationAfterFastForwardCannotBeAcknowledged",
            "C448_V32_EachRecoveryPinFailureStopsDependentMutation",
            "C448_V11_TargetAdvanceUsesTheCapturedCheckoutAndOldSha",
            "C448_V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs",
            "C448_V36_CreationUsesTheRepositoryLeaseAndThreadsNestedOwnership",
            "C448_V11_TargetSequencerBlocksPreparation",
            "C448_V26_RecoveryPinsRemainPrerequisitesAfterVerification");
        (realV10.Count + realBoundaryOther + admissionReal.Count + concV14r.Count + concV10r.Count
            + CountMethods("AgentTaskLandConcurrencyTests.cs",
                "C448_V36_SettlementLeasePrecedesItsFirstMutation",
                "C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders")).ShouldBe(60);

        foreach (var cap in RealV10Capstones)
            realV10.ShouldContain($"{cap.Boundary},{cap.Change},{cap.Contained}");
    }

    [Test]
    public async Task C475_AllServicesUseTheRegisteredGit()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        ReferenceEquals(h.RegisteredGit, h.Git).ShouldBeTrue();
        h.Git.NativeProcessStarts.ShouldBe(0);
    }

    [Test]
    public async Task C475_AcknowledgedHookReadsCommittedPhase()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        LandPhase? seen = null;
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (phase != LandPhase.Prepared) return;
            await using var db = h.CreateContext();
            var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == h.Git.TaskId && o.Active);
            seen = op.Phase;
        };
        await h.RunAsync();
        seen.ShouldBe(LandPhase.Prepared);
    }

    [Test]
    public async Task C475_SourceAliasContendsOnRepositoryLease()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
        await using var first = await leases.TryAcquireAsync(h.Git.Repository, CancellationToken.None);
        first.ShouldNotBeNull();
        (await leases.TryAcquireAsync(h.Git.Source, CancellationToken.None)).ShouldBeNull();
        leases.Owns(first, await h.Git.CommonDirectoryAsync(h.Git.Source, CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task C475_CleanupUsesGuardedRemoval()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Git.Source, ".antiphon", "owned.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "keep");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        File.ReadAllText(sentinel).ShouldBe("keep");
        Directory.Exists(h.Git.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public async Task C475_RecreationPreservesCommittedModeAndAttempts(string mode)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        if (mode != "AlreadyPresent") await h.AddSourceAsync();
        if (mode == "ResumePublication")
        {
            h.Fault.Phase = LandPhase.LocalTargetAdvanced;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        }
        if (mode == "CleanupRetry")
        {
            var sentinel = Path.Combine(h.Git.Source, ".antiphon", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            await File.WriteAllTextAsync(sentinel, "preserve");
            await h.RunAsync();
            await h.RepostAsync();
        }
        var before = await h.OperationAsync();
        int attempt;
        await using (var db = h.CreateContext())
            attempt = await db.AgentTasks.Where(t => t.Id == h.Git.TaskId).Select(t => t.LandAttempt).SingleAsync();
        await h.RestartServicesAsync();
        if (mode is "ResumePublication" or "CleanupRetry")
        {
            if (mode == "CleanupRetry") File.Delete(Path.Combine(h.Git.Source, ".antiphon", "report.md"));
            await h.RunAsync();
        }
        else await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        if (before is not null) after.Id.ShouldBe(before.Id);
        await using var check = h.CreateContext();
        (await check.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId)).LandAttempt.ShouldBeGreaterThanOrEqualTo(attempt);
    }

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public async Task C475_ModesAreReachedThroughProtocol(string mode)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        if (mode != "AlreadyPresent") await h.AddSourceAsync();
        if (mode == "ResumePublication")
        {
            h.Fault.Phase = LandPhase.LocalTargetAdvanced;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
            (await h.OperationAsync())!.Phase.ShouldBe(LandPhase.LocalTargetAdvanced);
            return;
        }
        if (mode == "CleanupRetry")
        {
            var sentinel = Path.Combine(h.Git.Source, ".antiphon", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            await File.WriteAllTextAsync(sentinel, "preserve");
            await h.RunAsync();
            (await h.OperationAsync())!.Cleanup.ShouldBe(LandCleanupStatus.Refused);
            await h.RepostAsync();
            File.Delete(sentinel);
        }
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new Antiphon.Server.Application.Services.AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
    }

    [Test]
    public async Task C475_CleanupRetryRequiresFreshRemoteContainment()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Git.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "preserve");
        await h.RunAsync();
        await h.RepostAsync();
        h.Git.RewriteRemoteAwayFromSource();
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        File.Exists(sentinel).ShouldBeTrue();
    }

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public async Task C475_LandingKeepsLeaseThroughProtocol(string mode)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        if (mode != "AlreadyPresent") await h.AddSourceAsync();
        var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
        var contended = false;
        h.Git.BeforeCommand = async (_, args) =>
        {
            if (!contended && args[0] == "show-ref")
            {
                contended = true;
                var stolen = await leases.TryAcquireAsync(h.Git.Repository, CancellationToken.None);
                stolen.ShouldBeNull("landing must still hold the repository lease at first model query");
            }
            return null;
        };
        if (mode == "ResumePublication")
        {
            h.Fault.Phase = LandPhase.LocalTargetAdvanced;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        }
        try { await h.RunAsync(); } catch (LandingProtocolHarness.InjectedSaveFailure) { }
        contended.ShouldBeTrue();
    }

    private static List<string> AttributeTuples(string file, string method)
    {
        var path = Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "Application", file);
        var lines = File.ReadAllLines(path);
        var tuples = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("public async Task " + method, StringComparison.Ordinal)) continue;
            for (var j = i - 1; j >= 0; j--)
            {
                var m = Arguments.Match(lines[j]);
                if (m.Success)
                {
                    tuples.Insert(0, Normalize(m.Groups[1].Value));
                    continue;
                }
                if (lines[j].Contains("[Test]", StringComparison.Ordinal)) break;
            }
            break;
        }
        return tuples;
    }

    private static int CountMethods(string file, params string[] methods)
        => methods.Sum(m => Math.Max(1, AttributeTuples(file, m).Count));

    private static string Normalize(string raw) =>
        raw.Replace("\"", "").Replace(" ", "").Replace("false", "False").Replace("true", "True");

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        }
    }
}
