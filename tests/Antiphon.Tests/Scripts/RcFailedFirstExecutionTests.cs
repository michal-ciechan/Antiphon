using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1404/1514: failed-first execution through the real coordinator, suite runner, journal and nightly-tests.ps1
/// entry. W2-nightly S14: in-process (the actual-loop cases still run the PowerShell nightly-tests.ps1 entry and the
/// qualification heartbeat library as pwsh children until S17/S10); were cases of scripts/test-nightly-failed-first.ps1.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel(new[] { "nightly-history-statics", nameof(QualificationTimingTests), "release-authority-card-reader" })]
public sealed class RcFailedFirstExecutionTests
{
    // The release authority a prepared arm runs beside stays byte-identical and valid (Test-FFAuthorityConservation).
    [Test]
    public void ImportedAuthorityFixtureInitializesTiming()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-authority");
        FailedFirstArmCases.AuthorityConservation(fixture, rows, nameof(ImportedAuthorityFixtureInitializesTiming));
        rows.ShouldAllPass(2);
    }
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
    public async Task ActualLoopFollowsSchedule(CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-loop");
        await FailedFirstLoopRows.ActualLoopFollowsSchedule(fixture, rows, cancellationToken);
        rows.ShouldAllPass(17);
    }

    // The heartbeat's planned-cost binding: suite-qualified identity, the legacy singleton, every refusal and the
    // journal liveness checks (Test-FFHeartbeatBindings; also part of ActualLoopFollowsSchedule).
    [Test, Timeout(300_000)]
    public void HeartbeatBindings()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-heartbeat");
        FailedFirstLoopRows.HeartbeatBindings(fixture, rows, nameof(HeartbeatBindings));
        rows.ShouldAllPass(4);
    }

    // Actual entrypoint journal custody (CARD-1514). The D-15 diagnostic/authority matrix it used to repeat runs in
    // C1590_ArchiveHoldsNoGitRepository (ConservationDiagnostics) and RcFailedFirstPlannerTests.FullBytesAndCoverageConserved.
    [Test, Timeout(541_000)]
    public async Task JournalCustodySurvivesInterleaving(CancellationToken cancellationToken)
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-journal");
        await FailedFirstLoopRows.JournalCustody(fixture, rows, cancellationToken);
        rows.ShouldAllPass(9);
    }

    // CARD-1590: the retained archive keeps the authority fixture inputs but never the fixture checkout's Git admin
    // files, so no nested repository lands in the worktree. The ConservationDiagnostics rows run first.
    [Test, Timeout(541_000)]
    public void C1590_ArchiveHoldsNoGitRepository()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-diagnostics");
        FailedFirstConservation.Run(fixture, rows, "ConservationDiagnostics", diagnosticsOnly: true);
        rows.ShouldAllPass(14);
        var archive = FailedFirstArmCases.Retain(fixture.Root, "ConservationDiagnostics", "Antiphon.Tests.Scripts.RcFailedFirstPlannerTests");
        Directory.Exists(archive).ShouldBeTrue();
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
    public void C1590_ArchiveKeepsGitNamedFiles()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-segments");
        FailedFirstArmCases.ArchiveSegments(fixture.Root, rows);
        rows.ShouldAllPass(1, "FF C1590_ArchiveSegments.fixture");
        var archive = FailedFirstArmCases.Retain(fixture.Root, "C1590_ArchiveSegments", "Antiphon.Tests.Scripts.RcFailedFirstPlannerTests");
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(archive, "inventory.json")));
        var rows2 = inventory.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
            file => file.GetProperty("path").GetString()!.Replace('\\', '/'), file => file, StringComparer.Ordinal);
        foreach (var relative in FailedFirstArmCases.ArchiveSegmentFiles)
        {
            rows2.ShouldContainKey(relative);
            rows2[relative].GetProperty("kind").GetString().ShouldBe("regular", relative);
            rows2[relative].GetProperty("copied").GetBoolean().ShouldBeTrue(relative);
            File.ReadAllText(Path.Combine(archive, relative)).ShouldBe(relative);
        }
        var skipped = rows2.Where(row => row.Value.GetProperty("kind").GetString() == "git-dir").ToList();
        skipped.Select(row => row.Key).ShouldContain("checkout/.git/HEAD");
        skipped.ShouldAllBe(row => row.Key.StartsWith("checkout/.git/", StringComparison.Ordinal) && !row.Value.GetProperty("copied").GetBoolean());
        var regular = rows2.Where(row => row.Value.GetProperty("kind").GetString() == "regular").Select(row => row.Key).Order(StringComparer.Ordinal);
        var archived = Directory.EnumerateFiles(archive, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(archive, path).Replace('\\', '/'))
            .Where(path => path is not ("inventory.json" or "performance.json")).Order(StringComparer.Ordinal);
        archived.ShouldBe(regular);
    }
}