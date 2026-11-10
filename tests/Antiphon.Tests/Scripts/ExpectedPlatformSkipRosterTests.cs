using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// The Windows coverage gate's committed expected-skip roster. The harness spawns pwsh.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ExpectedPlatformSkipRosterTests
{
    [Test]
    public Task Verdict_accepts_only_rostered_skips() =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-expected-platform-skips.ps1", "EPS", "Verdict", 24,
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
                "EPS title-omits-tolerated-exit",
                "EPS discovery-known-identities"
            ]);
}
