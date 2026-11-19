using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LongPathsCheckoutScriptTests
{
    // The rc checkout guard, launch refusal, and clone flag. Local git fixtures only.
    private static Task Run(string name, int rows, string[] required, CancellationToken token) =>
        ScriptHarness.RunHarnessCaseAsync("test-longpaths-checkout.ps1", "LP", name, rows, required,
            new ScriptHarnessOptions(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(15)), token);

    [Test, Timeout(180_000)]
    public Task LP_GuardStderr(CancellationToken token) =>
        Run(nameof(LP_GuardStderr), 3,
        [
            "LP guard exits 1 when worktree add fails",
            "LP guard failure contains git stderr",
            "LP guard worktree add passes core.longpaths"
        ], token);

    [Test, Timeout(180_000)]
    public Task LP_PathArithmetic(CancellationToken token) =>
        Run(nameof(LP_PathArithmetic), 3,
        [
            "LP guard temp prefix is 105 characters",
            "LP exactly one tracked path exceeds MAX_PATH under the guard prefix",
            "LP guard documents the 269 character sum and passes core.longpaths"
        ], token);

    [Test, Timeout(180_000)]
    public Task LP_LaunchRefuses(CancellationToken token) =>
        Run(nameof(LP_LaunchRefuses), 4,
        [
            "LP launch refuses longpaths-not-enabled",
            "LP launch names the fix command",
            "LP launch with longpaths continues to build-slot",
            "LP launch with longpaths does not refuse longpaths"
        ], token);

    [Test, Timeout(180_000)]
    public Task LP_NoSyncRefuses(CancellationToken token) =>
        Run(nameof(LP_NoSyncRefuses), 4,
        [
            "LP nosync refuses longpaths-not-enabled",
            "LP nosync names the fix command",
            "LP nosync with longpaths reaches whatif",
            "LP nosync with longpaths has an empty refusal"
        ], token);

    [Test, Timeout(180_000)]
    public Task LP_PreflightRefuses(CancellationToken token) =>
        Run(nameof(LP_PreflightRefuses), 3,
        [
            "LP preflight refuses longpaths-not-enabled",
            "LP preflight names the fix command",
            "LP preflight accepts core.longpaths true"
        ], token);

    [Test, Timeout(180_000)]
    public Task LP_CloneFlag(CancellationToken token) =>
        Run(nameof(LP_CloneFlag), 2,
        [
            "LP release clone argv passes core.longpaths",
            "LP release whatif names core.longpaths"
        ], token);
}
