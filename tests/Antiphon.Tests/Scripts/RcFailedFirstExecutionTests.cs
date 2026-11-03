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
    // Budget: 481 s is ~3x the 162 s red run on loaded server2 (2026-11-03, n=1; provisional).
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

    // CARD-1691 F1: only a whole .git segment is Git admin. Files whose names merely contain
    // .git are archived as regular copies, and every archived file is accounted for.
    [Test]
    public async Task C1590_ArchiveKeepsGitNamedFiles(CancellationToken cancellationToken)
    {
        var result = await ScriptHarnessProcess.RunAsync("test-nightly-failed-first.ps1", "FF", "C1590_ArchiveSegments",
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-nightly-failed-first.ps1"),
            ScriptHarnessOptions.Default, cancellationToken);
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        output.ShouldContain("PASS FF C1590_ArchiveSegments.fixture", Case.Sensitive, output);
        var archive = output.ReplaceLineEndings("\n").Split('\n')
            .Single(line => line.StartsWith("FF EVIDENCE ", StringComparison.Ordinal))["FF EVIDENCE ".Length..].Trim();
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(archive, "inventory.json")));
        var rows = inventory.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
            file => file.GetProperty("path").GetString()!.Replace('\\', '/'), file => file, StringComparer.Ordinal);
        foreach (var relative in new[] { ".gitignore", ".github/workflow.yml", "foo.git.txt", "checkout/.gitignore", "checkout/tracked.txt" })
        {
            rows.ShouldContainKey(relative, output);
            rows[relative].GetProperty("kind").GetString().ShouldBe("regular", relative);
            rows[relative].GetProperty("copied").GetBoolean().ShouldBeTrue(relative);
            File.ReadAllText(Path.Combine(archive, relative)).ShouldBe(relative);
        }
        var skipped = rows.Where(row => row.Value.GetProperty("kind").GetString() == "git-dir").ToList();
        skipped.Select(row => row.Key).ShouldContain("checkout/.git/HEAD");
        skipped.ShouldAllBe(row => row.Key.StartsWith("checkout/.git/", StringComparison.Ordinal) && !row.Value.GetProperty("copied").GetBoolean());
        var regular = rows.Where(row => row.Value.GetProperty("kind").GetString() == "regular").Select(row => row.Key).Order(StringComparer.Ordinal);
        var archived = Directory.EnumerateFiles(archive, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(archive, path).Replace('\\', '/'))
            .Where(path => path is not ("inventory.json" or "performance.json")).Order(StringComparer.Ordinal);
        archived.ShouldBe(regular);
    }
}
