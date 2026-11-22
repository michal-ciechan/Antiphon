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

    /// <summary>
    /// In-process since W2-nightly S20 (was the test-longpaths-checkout.ps1 case on the PowerShell run core): a -NoSync -WhatIf
    /// NightlyRun.Run with real git on a prepared clone refuses longpaths-not-enabled and names the fix command until the clone
    /// opts in, then reaches whatif with an empty refusal.
    /// </summary>
    [Test, Timeout(180_000)]
    public void LP_NoSyncRefuses(CancellationToken token)
    {
        const string origin = "https://example.invalid/antiphon.git";
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("lp-nosync");
        var clone = fixture.PathOf("checkout");
        Directory.CreateDirectory(clone);
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "init", "-q", "-b", "master");
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "-c", "user.name=lp", "-c", "user.email=lp@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-q", "-m", "lp");
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "remote", "add", "origin", origin);
        File.WriteAllText(Path.Combine(clone, ".antiphon-nightly-owned"), "{\"origin\":\"" + origin + "\",\"kind\":\"nightly-clone\"}");
        (Antiphon.Nightly.NightlyRunResult Result, string Text) Run()
        {
            var before = fixture.Out.ToString().Length;
            var result = Antiphon.Nightly.NightlyRun.Run(new Antiphon.Nightly.NightlyRunOptions
            {
                NoSync = true, WhatIf = true, CheckoutRoot = clone, StateRoot = fixture.PathOf("state"), LogRoot = fixture.PathOf("logs"),
                CoordinationRoot = fixture.PathOf("coord"), RemoteUrl = origin, Ref = "master", Trigger = "scheduled", Profile = "nightly", SelectionMode = "Full",
                RunId = "lp" + Guid.NewGuid().ToString("N"),
            }, Antiphon.Tests.Scripts.Nightly.NightlyRunTests.RealGitHost(fixture));
            return (result, fixture.Out.ToString()[before..]);
        }
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var bad = Run();
        rows.Check(bad.Result.ExitCode == 3 && bad.Result.Refusal == "longpaths-not-enabled" && bad.Text.Contains("longpaths-not-enabled", StringComparison.Ordinal),
            "LP nosync refuses longpaths-not-enabled", "exit=" + bad.Result.ExitCode + " refusal=" + bad.Result.Refusal + " " + bad.Text);
        rows.Check(bad.Text.Contains("config --local core.longpaths true", StringComparison.Ordinal) && bad.Text.Contains("git -C ", StringComparison.Ordinal),
            "LP nosync names the fix command", bad.Text);
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "config", "--local", "core.longpaths", "true");
        var good = Run();
        rows.Check(good.Result.ExitCode == 0 && good.Result.Phase == "whatif", "LP nosync with longpaths reaches whatif",
            "exit=" + good.Result.ExitCode + " phase=" + good.Result.Phase + " " + good.Text);
        rows.Check(good.Result.Refusal == "", "LP nosync with longpaths has an empty refusal", "refusal=" + good.Result.Refusal);
        rows.ShouldAllPass(4, "LP nosync refuses longpaths-not-enabled", "LP nosync names the fix command", "LP nosync with longpaths reaches whatif",
            "LP nosync with longpaths has an empty refusal");
    }

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
