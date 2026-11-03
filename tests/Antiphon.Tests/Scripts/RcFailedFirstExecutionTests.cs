using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFailedFirstExecutionTests
{
    // Provisional full replay bound uses S2's measured 156-launch cost (CARD-1514).
    [Test, Timeout(1_140_000)]
    public Task ActualLoopFollowsSchedule(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(ActualLoopFollowsSchedule), 18,
            ["FF ActualLoopFollowsSchedule.actualTrace", "FF ActualLoopFollowsSchedule.permutation",
             "FF ActualLoopFollowsSchedule.oneBuildDiscovery", "FF ActualLoopFollowsSchedule.historyBinding",
             "FF ActualLoopFollowsSchedule.allChunksVerdict", "FF ActualLoopFollowsSchedule.finalProjection",
             "FF ActualLoopFollowsSchedule.fallback", "FF ActualLoopFollowsSchedule.progressOrder",
             "FF ActualLoopFollowsSchedule.heartbeatCollision", "FF ActualLoopFollowsSchedule.heartbeatLegacy",
             "FF ActualLoopFollowsSchedule.heartbeatRefusals", "FF ActualLoopFollowsSchedule.heartbeatIo",
             "FF ActualLoopFollowsSchedule.heartbeatLiveness",
             "FF ActualLoopFollowsSchedule.chainProducer", "FF ActualLoopFollowsSchedule.chainPreviousOrder",
             "FF ActualLoopFollowsSchedule.chainRegrouped",
             "FF ActualLoopFollowsSchedule.chainDigestMissing", "FF ActualLoopFollowsSchedule.chainIngress"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(1080), TimeSpan.FromSeconds(10)), cancellationToken);

    // Actual entrypoint plus S2's measured ~55s D-15 matrix, shared setup (CARD-1514).
    // R5 (CARD-1638): execution budget 2x the 240.257s TRX duration in checkpoint run
    // 20261102-133640-5f80 (server2, load 32-38 on 24 cores; the card's 163s has no receipt);
    // method timeout keeps the 60s cleanup margin. Provisional: n=2 (163s, 240.3s).
    [Test, Timeout(541_000)]
    public Task JournalCustodySurvivesInterleaving(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(JournalCustodySurvivesInterleaving), 27,
            ["FF JournalCustodySurvivesInterleaving.interleaving", "FF JournalCustodySurvivesInterleaving.hostDiscovery",
             "FF JournalCustodySurvivesInterleaving.prefixState", "FF JournalCustodySurvivesInterleaving.journalBindings",
             "FF JournalCustodySurvivesInterleaving.oneContextClose", "FF JournalCustodySurvivesInterleaving.timeoutPermutation",
             "FF JournalCustodySurvivesInterleaving.custody", "FF JournalCustodySurvivesInterleaving.fieldPolicies",
             "FF JournalCustodySurvivesInterleaving.activeIdentity",
             "FF JournalCustodySurvivesInterleaving.terminalMatrix", "FF JournalCustodySurvivesInterleaving.stickyHeader",
             "FF JournalCustodySurvivesInterleaving.settlementOrder", "FF JournalCustodySurvivesInterleaving.reasonSequence",
             "FF JournalCustodySurvivesInterleaving.timelines", "FF JournalCustodySurvivesInterleaving.phaseSummariesAndIndex",
             "FF JournalCustodySurvivesInterleaving.schedulePolicy", "FF JournalCustodySurvivesInterleaving.lazyPreparation",
             "FF JournalCustodySurvivesInterleaving.preparationBarrier", "FF JournalCustodySurvivesInterleaving.e2ePreparation",
             "FF JournalCustodySurvivesInterleaving.diagnosticWriteFailure", "FF JournalCustodySurvivesInterleaving.authorityBytes",
             "FF JournalCustodySurvivesInterleaving.authorityBinding", "FF JournalCustodySurvivesInterleaving.originalManifestBytes",
             "FF JournalCustodySurvivesInterleaving.artifactSchemas", "FF JournalCustodySurvivesInterleaving.phaseBindings"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(481), TimeSpan.FromSeconds(10)), cancellationToken);

    // CARD-1590: the retained archive keeps the authority fixture inputs but never the
    // fixture checkout's Git admin files, so no nested repository lands in the worktree.
    // Provisional budget: the DiagnosticsOnly tail of JournalCustodySurvivesInterleaving.
    [Test, Timeout(541_000)]
    public async Task C1590_ArchiveHoldsNoGitRepository(CancellationToken cancellationToken)
    {
        var result = await ScriptHarnessProcess.RunAsync("test-nightly-failed-first.ps1", "FF", "ConservationDiagnostics",
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-nightly-failed-first.ps1"),
            new ScriptHarnessOptions(TimeSpan.FromSeconds(481), TimeSpan.FromSeconds(10)), cancellationToken);
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        var archive = output.ReplaceLineEndings("\n").Split('\n')
            .Single(line => line.StartsWith("FF EVIDENCE ", StringComparison.Ordinal))["FF EVIDENCE ".Length..].Trim();
        Directory.Exists(archive).ShouldBeTrue(output);
        Directory.EnumerateFileSystemEntries(archive, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(archive, path).Split('/', '\\').Contains(".git", StringComparer.OrdinalIgnoreCase))
            .ShouldBeEmpty();
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(archive, "inventory.json")));
        var skipped = inventory.RootElement.GetProperty("files").EnumerateArray()
            .Where(file => file.GetProperty("kind").GetString() == "git-dir").ToList();
        skipped.ShouldNotBeEmpty("the authority fixture checkout has a .git directory to skip");
        skipped.ShouldAllBe(file => !file.GetProperty("copied").GetBoolean() &&
            file.GetProperty("path").GetString()!.Contains(".git", StringComparison.Ordinal));
    }
}
