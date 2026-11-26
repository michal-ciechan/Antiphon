using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using Antiphon.LiveLane;
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
            "context-tuple-pid", "context-tuple-runId", "context-tuple-lane", "context-tuple-start",
            "native-disabled", "full-inputs", "policy-edit", "reader-edit", "committed-edit", "crlf-inputs", "sentinel-moved",
            "legacy-state");
    }

    [Test]
    public Task ChildEnvironmentIsScoped() => Run("ChildEnvironmentIsScoped", "allowlist", "empty", "parent");

    [Test]
    public Task DeadlineJoinsOwnedChildren() => Run("DeadlineJoinsOwnedChildren", "wait75", "deadline",
        "cleanup-unknown", "context-write", "launch-failure", "wait-failure", "terminal-read", "receipt-write", "foreign-lock");

    // W2 slice S21, first step: the pin owner is C# (Antiphon.LiveLane's LiveLaneInputDigest, printed by
    // scripts/antiphon live-lane-input-digest). Before the PowerShell is deleted, the C# list and pin are the driver's own
    // (read as text, never run) and the C# digest of this checkout is that pin.
    [Test]
    public void PinOwnerIsCSharp()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var driver = File.ReadAllText(Path.Combine(root, "scripts/test-live-lane-run.ps1"));
        Regex.Match(driver, @"\$script:PinnedInputDigest = '([0-9a-f]{64})'").Groups[1].Value.ShouldBe(LiveLaneInputDigest.Pinned);
        var list = Regex.Match(driver, @"\$script:ProtectedInputs = @\((?<items>[^)]*)\)").Groups["items"].Value;
        Regex.Matches(list, "'([^']+)'").Select(m => m.Groups[1].Value).ShouldBe(LiveLaneInputDigest.ProtectedInputs);
        LiveLaneInputDigest.Digest(root).ShouldBe(LiveLaneInputDigest.Pinned);
        var check = LiveLaneInputDigest.Check(root, global::Antiphon.Scripts.LiveLaneInputCensusReader.Read);
        check.Ok.ShouldBeTrue("the census sentinels of this checkout");
        var output = new StringWriter();
        LiveLaneInputDigestCommand.Run(LiveLaneInputDigestOptions.Parse(["--check"], root), global::Antiphon.Scripts.LiveLaneInputCensusReader.Read,
            output, new StringWriter()).ShouldBe(0);
        output.ToString().ShouldBe(LiveLaneInputDigest.Pinned + Environment.NewLine);
    }

    // run-live-lane.ps1's native mode, captured before the script goes: the exact line and exit code, with and without
    // its six parameters. live-lane-run keeps both, and its flags cannot change the answer.
    private const string NativeLine = "{\"ExitCode\":20,\"Reason\":\"native-unqualified\",\"DiagnosticOnly\":true,\"GateCredit\":false,\"TerminalComplete\":false,"
        + "\"RawChildExit\":null,\"CheckpointRunId\":null,\"Classification\":\"INCONCLUSIVE\",\"Rows\":[],\"RawChildOutcome\":null,\"ChildExits\":[],\"Probes\":[]}";

    [Test]
    public void NativeModeIsUnqualified()
    {
        foreach (var argv in new[]
                 {
                     Array.Empty<string>(),
                     ["--lane", "live-claude-core-windows", "--candidate-ref", "HEAD", "--expected-sha", new string('a', 40), "--manifest", "m.json",
                      "--coordination-root", "C:/nowhere/coord", "--results-root", "C:/nowhere/results"]
                 })
        {
            var output = new StringWriter();
            LiveLaneRunCommand.Run(LiveLaneRunOptions.Parse(argv), output).ShouldBe(20);
            output.ToString().ShouldBe(NativeLine + Environment.NewLine);
        }
        foreach (var bad in new[] { new[] { "--bogus" }, ["--lane"], ["--lane", "a", "--lane", "b"], ["positional"], ["-Lane", "a"] })
            Should.Throw<LiveLaneUsageException>(() => LiveLaneRunOptions.Parse(bad));
    }

    private static Task Run(string method, params string[] cases) => ScriptHarness.RunHarnessCaseAsync(
        "test-live-lane-run.ps1", "C1545", "C1545_" + method, cases.Length,
        cases.Select(c => $"C1545 {method}/{c}").ToArray(),
        new ScriptHarnessOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            PostExitDrainBudget: TimeSpan.FromSeconds(10)), CancellationToken.None);
}
