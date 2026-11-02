using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2a-1: the live-lane input fingerprint is complete, canonical and shadow-only.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneFingerprintTests
{
    private static readonly TimeSpan MethodBudget = TimeSpan.FromSeconds(60);

    [Test]
    public Task StableCanonicalInputs() => Run(nameof(StableCanonicalInputs), TimeSpan.FromSeconds(120),
        "deterministic", "sha-free", "path-free", "mode", "absent-vs-empty", "domain", "shadow-only", "missing-root", "real-head");

    [Test]
    public Task ReferenceClosureInvalidates() => Run(nameof(ReferenceClosureInvalidates), MethodBudget,
        "direct-edge", "transitive-edge", "edge-removed", "edge-added", "shared-build-inputs", "rename", "unrelated-stable");

    [Test]
    public Task TestAndFixtureInvalidates() => Run(nameof(TestAndFixtureInvalidates), MethodBudget,
        "test-source", "linked-shared", "content-glob", "deletion", "other-test-project", "core-project-map");

    [Test]
    public Task UnknownDependencyRefuses() => Run(nameof(UnknownDependencyRefuses), MethodBudget,
        "dynamic-import", "missing-linked", "cycle", "outside-root", "symlink", "submodule", "ambiguous-project",
        "missing-manifest", "declaration-schema", "catalog-only", "fault-outside-closure");

    [Test]
    public Task RuntimeScriptsIncluded() => Run(nameof(RuntimeScriptsIncluded), MethodBudget,
        "hook", "contract-scripts", "manifest-and-declaration", "fixture-assets", "undeclared-runtime-literal", "unrelated-file");

    [Test]
    public Task RelevantNamespaceOutsideInputsRefuses() => Run(nameof(RelevantNamespaceOutsideInputsRefuses), MethodBudget,
        "file-scoped", "block-scoped", "test-namespace", "foreign-namespace", "inside-inputs");

    [Test]
    public Task LoadedInstructionsOutsideInputsRefuse() => Run(nameof(LoadedInstructionsOutsideInputsRefuse), MethodBudget,
        "root-instructions", "import-chain", "nested-and-claude-tree", "import-escape", "import-home", "import-missing",
        "import-cycle", "undeclared-instruction");

    private static Task Run(string method, TimeSpan budget, params string[] cases) =>
        ScriptHarness.RunHarnessCaseAsync("test-live-lane-fingerprint.ps1", "C1545", "C1545_" + method, cases.Length,
            cases.Select(name => $"C1545 {method}/{name}").ToArray(),
            new ScriptHarnessOptions(budget, TimeSpan.FromSeconds(10), PostExitDrainBudget: TimeSpan.FromSeconds(10)),
            CancellationToken.None);
}
