using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2a-1: the live-lane input fingerprint is complete, canonical and shadow-only.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneFingerprintTests
{
    // Budgets are 2x an estimated Windows p95: the measured quiet Linux server2 p95 (5 direct
    // harness runs per case, 2026-11-03, after the shared document cache) times the observed
    // Windows rc slowdown. Provisional factor: rc run f40ad905 group-125 reached only 6/7
    // ReferenceClosureInvalidates sub-cases in 60.2 s, ~11 s of work on Linux, so ~6x. One
    // contended sample; re-derive from the CARD-1514 per-test timing of the next Windows run.
    private const double WindowsLoadFactor = 6;

    private static TimeSpan Budget(double linuxP95Seconds) =>
        TimeSpan.FromSeconds(Math.Ceiling(2 * WindowsLoadFactor * linuxP95Seconds / 30) * 30);

    [Test]
    public Task StableCanonicalInputs() => Run(nameof(StableCanonicalInputs), Budget(40.4),
        "deterministic", "sha-free", "path-free", "mode", "absent-vs-empty", "domain", "shadow-only", "missing-root", "real-head",
        "real-declaration");

    [Test]
    public Task ReferenceClosureInvalidates() => Run(nameof(ReferenceClosureInvalidates), Budget(12.0),
        "direct-edge", "transitive-edge", "edge-removed", "edge-added", "shared-build-inputs", "rename", "unrelated-stable");

    [Test]
    public Task TestAndFixtureInvalidates() => Run(nameof(TestAndFixtureInvalidates), Budget(7.7),
        "test-source", "linked-shared", "content-glob", "deletion", "other-test-project", "core-project-map");

    [Test]
    public Task UnknownDependencyRefuses() => Run(nameof(UnknownDependencyRefuses), Budget(17.6),
        "dynamic-import", "missing-linked", "cycle", "outside-root", "symlink", "submodule", "ambiguous-project",
        "missing-manifest", "declaration-schema", "catalog-only", "fault-outside-closure");

    [Test]
    public Task RuntimeScriptsIncluded() => Run(nameof(RuntimeScriptsIncluded), Budget(8.4),
        "hook", "contract-scripts", "manifest-and-declaration", "fixture-assets", "undeclared-runtime-literal", "unrelated-file");

    [Test]
    public Task RelevantNamespaceOutsideInputsRefuses() => Run(nameof(RelevantNamespaceOutsideInputsRefuses), Budget(7.0),
        "file-scoped", "block-scoped", "test-namespace", "foreign-namespace", "inside-inputs");

    [Test]
    public Task LoadedInstructionsOutsideInputsRefuse() => Run(nameof(LoadedInstructionsOutsideInputsRefuse), Budget(9.6),
        "root-instructions", "import-chain", "nested-and-claude-tree", "import-escape", "import-home", "import-missing",
        "import-cycle", "undeclared-instruction");

    private static Task Run(string method, TimeSpan budget, params string[] cases) =>
        ScriptHarness.RunHarnessCaseAsync("test-live-lane-fingerprint.ps1", "C1545", "C1545_" + method, cases.Length,
            cases.Select(name => $"C1545 {method}/{name}").ToArray(),
            new ScriptHarnessOptions(budget, TimeSpan.FromSeconds(10), PostExitDrainBudget: TimeSpan.FromSeconds(10)),
            CancellationToken.None);
}
