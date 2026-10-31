using Antiphon.Checkpoints;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneRunnerTests
{
    [Test]
    public Task LockBeforeSlot() => Run("LockBeforeSlot",
        "ordered", "busy", "slot-timeout", "commission", "budget", "candidate-sha", "candidate-dirty");

    [Test]
    public async Task WrapperOnlyPaidManifest()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var yaml = await File.ReadAllTextAsync(Path.Combine(root, "tests/live-lane-checkpoints/live-claude-core-windows.yaml"));
        var rejected = false;
        try { ManifestLoader.LoadYaml(yaml, root); }
        catch (ManifestValidationException ex) { rejected = ex.Field == "schemaVersion"; }
        rejected.ShouldBeTrue("The real manifest loader must refuse the wrapper spec before any executor.");
        await Run("WrapperOnlyPaidManifest", "valid-context", "context-missing", "context-lane", "context-uid",
            "context-source", "context-executable", "context-profile", "context-owner", "context-forged",
            "native-disabled", "full-inputs", "policy-edit", "reader-edit", "legacy-state");
    }

    [Test]
    public Task ChildEnvironmentIsScoped() => Run("ChildEnvironmentIsScoped", "allowlist", "empty", "parent");

    [Test]
    public Task DeadlineJoinsOwnedChildren() => Run("DeadlineJoinsOwnedChildren", "wait75", "deadline",
        "cleanup-unknown", "context-write", "launch-failure", "wait-failure", "terminal-read", "receipt-write", "foreign-lock");

    private static Task Run(string method, params string[] cases) => ScriptHarness.RunHarnessCaseAsync(
        "test-live-lane-run.ps1", "C1545", "C1545_" + method, cases.Length,
        cases.Select(c => $"C1545 {method}/{c}").ToArray(),
        new ScriptHarnessOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            PostExitDrainBudget: TimeSpan.FromSeconds(10)), CancellationToken.None);
}
