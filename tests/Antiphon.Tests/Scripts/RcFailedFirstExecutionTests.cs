using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFailedFirstExecutionTests
{
    [Test]
    public Task ImportedAuthorityFixtureInitializesTiming(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(ImportedAuthorityFixtureInitializesTiming), 2,
            ["FF ImportedAuthorityFixtureInitializesTiming.authorityBytes", "FF ImportedAuthorityFixtureInitializesTiming.authorityBinding"],
            ScriptHarnessOptions.Default, cancellationToken);

    // A previous run's progress orders a new complete attempt: failed and unfinished rows first, each lane rotated after
    // its last pass; a missing, stale, foreign or malformed record keeps the canonical order (W2-nightly S13: in-process,
    // was Test-FFPreviousOrder, the PreviousOrder case and ActualLoopFollowsSchedule.progressOrder).
    [Test]
    public void PreviousOrder()
    {
        var rows = new NightlyCaseRows();
        var sha = new string('a', 40);
        var now = new DateTime(2026, 10, 31, 6, 0, 0, DateTimeKind.Utc);
        var entries = new[] { "A", "B" }.SelectMany(suite => Enumerable.Range(1, 5).Select(id => (object)new PlainObject(("suite", suite), ("chunk", id.ToString(System.Globalization.CultureInfo.InvariantCulture))))).ToArray();
        static PlainObject Row(string id, string chunk, string result, int exitCode) => new(("id", id), ("chunk", chunk), ("result", result), ("exitCode", exitCode));
        static PlainObject Previous(string completedAt, string runSha, object suites) =>
            new(("schemaVersion", 1), ("liveness", "closed"), ("sha", runSha), ("completedAt", completedAt), ("suites", suites));
        static string Keys(object order) => string.Join(",", PsValue.AsArray(PsValue.Prop(order, "entries")).Select(e => PsValue.Str(PsValue.Prop(e, "suite")) + PsValue.Str(PsValue.Prop(e, "chunk"))));
        object[] progress = [Row("A", "1", "pass", 0), Row("A", "3", "pass", 0), Row("B", "2", "FAIL", 1)];
        var order = DispatchSchedule.PreviousRunOrder(entries, Previous("2026-10-31T01:00:00Z", sha, progress), sha, now);
        var progressOk = Keys(order) == "B2,A4,A5,A2,B1,B3,B4,B5,A1,A3" && PsValue.Str(PsValue.Prop(order, "reason")) == "previous-run-order";
        var canonical = string.Join(",", entries.Select(e => PsValue.Str(PsValue.Prop(e, "suite")) + PsValue.Str(PsValue.Prop(e, "chunk"))));
        var variants = new Dictionary<string, object?>
        {
            ["missing"] = null,
            ["stale"] = Previous("2026-09-01T00:00:00Z", sha, progress),
            ["candidate"] = Previous("2026-10-31T01:00:00Z", new string('b', 40), progress),
            ["malformed"] = Previous("2026-10-31T01:00:00Z", sha, "wrong"),
        };
        foreach (var (name, record) in variants)
        {
            var fallback = DispatchSchedule.PreviousRunOrder(entries, record, sha, now);
            var reason = PsValue.Str(PsValue.Prop(fallback, "reason"));
            if (Keys(fallback) != canonical || reason.Length == 0 || reason == "previous-run-order") progressOk = false;
        }
        object[] rotated = [Row("A", "1", "FAIL", 1), Row("A", "3", "pass", 0), Row("A", "4", "FAIL", 1), Row("B", "1", "FAIL", 1), Row("B", "3", "pass", 0), Row("B", "5", "FAIL", 1)];
        var keys = Keys(DispatchSchedule.PreviousRunOrder(entries, Previous("2026-10-31T01:00:00Z", sha, rotated), sha, now));
        rows.Check(progressOk && keys == "A4,A1,B5,B1,A5,A2,B4,B2,A3,B3", "FF PreviousOrder.progressOrder", "failed-and-unfinished-lane-cursors actual=" + keys);
        rows.ShouldAllPass(1);
    }

    // Provisional full replay bound uses S2's measured 156-launch cost (CARD-1514).
    [Test, Timeout(1_140_000)]
    public Task ActualLoopFollowsSchedule(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(ActualLoopFollowsSchedule), 17,
            ["FF ActualLoopFollowsSchedule.actualTrace", "FF ActualLoopFollowsSchedule.permutation",
             "FF ActualLoopFollowsSchedule.oneBuildDiscovery", "FF ActualLoopFollowsSchedule.historyBinding",
             "FF ActualLoopFollowsSchedule.allChunksVerdict", "FF ActualLoopFollowsSchedule.finalProjection",
             "FF ActualLoopFollowsSchedule.fallback",
             "FF ActualLoopFollowsSchedule.heartbeatCollision", "FF ActualLoopFollowsSchedule.heartbeatLegacy",
             "FF ActualLoopFollowsSchedule.heartbeatRefusals", "FF ActualLoopFollowsSchedule.heartbeatIo",
             "FF ActualLoopFollowsSchedule.heartbeatLiveness",
             "FF ActualLoopFollowsSchedule.chainProducer", "FF ActualLoopFollowsSchedule.chainPreviousOrder",
             "FF ActualLoopFollowsSchedule.chainRegrouped",
             "FF ActualLoopFollowsSchedule.chainDigestMissing", "FF ActualLoopFollowsSchedule.chainIngress"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(1080), TimeSpan.FromSeconds(10)), cancellationToken);

    // Actual entrypoint journal custody (CARD-1514). The D-15 diagnostic/authority matrix
    // it used to repeat runs in C1590_ArchiveHoldsNoGitRepository (ConservationDiagnostics)
    // and RcFailedFirstPlannerTests.FullBytesAndCoverageConserved.
    // R5 (CARD-1638) budget kept from the matrix-inclusive measurement (240.3s on loaded server2).
    [Test, Timeout(541_000)]
    public Task JournalCustodySurvivesInterleaving(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(JournalCustodySurvivesInterleaving), 9,
            ["FF JournalCustodySurvivesInterleaving.interleaving", "FF JournalCustodySurvivesInterleaving.hostDiscovery",
             "FF JournalCustodySurvivesInterleaving.prefixState", "FF JournalCustodySurvivesInterleaving.journalBindings",
             "FF JournalCustodySurvivesInterleaving.oneContextClose", "FF JournalCustodySurvivesInterleaving.timeoutPermutation",
             "FF JournalCustodySurvivesInterleaving.custody", "FF JournalCustodySurvivesInterleaving.fieldPolicies",
             "FF JournalCustodySurvivesInterleaving.activeIdentity"],
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
