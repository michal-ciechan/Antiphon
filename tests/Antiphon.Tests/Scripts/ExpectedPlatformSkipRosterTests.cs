using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// The Windows coverage gate's committed expected-skip roster. The harness spawns pwsh; the report row runs in-process.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ExpectedPlatformSkipRosterTests
{
    [Test]
    public Task Verdict_accepts_only_rostered_skips() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-expected-platform-skips.ps1", "EPS", "Verdict", 23,
            [
                "EPS expected-skip-accepted",
                "EPS prefix-extension-rejected",
                "EPS skipped-without-space-rejected",
                "EPS autoload-roster-accepted",
                "EPS unexpected-skip-rejected",
                "EPS reason-mismatch-rejected",
                "EPS all-skip-exit-8-tolerated",
                "EPS mixed-exit-8-rejected",
                "EPS other-nonzero-still-fails",
                "EPS ran-roster-entry-stays-complete",
                "EPS passed-with-expected-skip-complete",
                "EPS stale-roster-warns",
                "EPS trx-debug-trace",
                "EPS census-matches-source",
                "EPS census-reports-missing",
                "EPS reason-text-matches-source",
                "EPS reason-text-reports-mismatch",
                "EPS roster-drift-stable",
                "EPS roster-drift-reports-mismatch",
                "EPS live-context-any-platform",
                "EPS live-context-labeled",
                "EPS tolerated-list-and-digest",
                "EPS discovery-known-identities"
            ]);

    /// <summary>
    /// EPS title-omits-tolerated-exit (from Test-EpsVerdict; in-process since W2-nightly S06): a dry-run report of a run
    /// whose tolerated chunk exited 8 names only the real failures in its title and shows the roster digest and the
    /// tolerated skip in its body.
    /// </summary>
    [Test]
    public void Report_title_omits_the_tolerated_exit()
    {
        const string cleanup = "Antiphon.Tests.Application.C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation";
        var digest = new string('a', 64);
        using var fixture = NightlyFixture.Create("eps-report");
        var summary = NightlyReportTests.Save(new JsonObject
        {
            ["startedAt"] = "2026-11-10T00:00:00Z", ["succeeded"] = false, ["outcome"] = "TESTS", ["sha"] = "abc", ["gitRef"] = "rc/test", ["profile"] = "rc",
            ["durationSeconds"] = 1, ["builds"] = new JsonArray(),
            ["suites"] = new JsonArray(
                new JsonObject { ["id"] = "antiphon", ["name"] = "antiphon chunk-a", ["result"] = "pass", ["exitCode"] = 8, ["processExitTolerated"] = true, ["skipped"] = false, ["failed"] = 0 },
                new JsonObject { ["id"] = "antiphon", ["name"] = "antiphon chunk-b", ["result"] = "FAIL", ["exitCode"] = 1, ["skipped"] = false, ["failed"] = 2 }),
            ["expectedSkipRosterDigest"] = digest,
            ["toleratedSkips"] = new JsonArray(new JsonObject { ["identity"] = cleanup, ["reasonClass"] = "platform" }),
        }, fixture.Root);
        NightlyReportHttpResponse Board(string method, string uri, IReadOnlyDictionary<string, string> headers, string? body)
        {
            if (uri.EndsWith("/api/boards", StringComparison.Ordinal)) return new(200, "{\"id\":\"board-1\",\"name\":\"Antiphon\"}");
            if (uri.Contains("/columns", StringComparison.Ordinal)) return new(200, "[]");
            if (uri.Contains("/api/cards", StringComparison.Ordinal)) return new(200, "{\"cards\":[],\"truncated\":false}");
            return new(200, "");
        }
        var output = new StringWriter();
        var report = NightlyReport.Run(new NightlyReportOptions { Summary = summary, DryRun = true, Board = "Antiphon", Api = "http://nightly.test" },
            new NightlyReportSeams { Clock = TimeProvider.System, Http = Board, Environment = _ => null!, Out = output, Error = output });

        var titleOk = report.Title.Contains("2 failed", StringComparison.OrdinalIgnoreCase) && !report.Title.Contains("exit 8", StringComparison.OrdinalIgnoreCase)
            && report.Body.Contains(digest, StringComparison.Ordinal) && report.Body.Contains(cleanup, StringComparison.Ordinal)
            && report.Body.Contains("Tolerated skips", StringComparison.Ordinal);
        titleOk.ShouldBeTrue("EPS title-omits-tolerated-exit: " + report.Title + " | " + report.Body[..Math.Min(400, report.Body.Length)] + " | " + output);
    }
}
