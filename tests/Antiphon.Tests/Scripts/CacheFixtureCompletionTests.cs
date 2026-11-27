using static Antiphon.Tests.TestHelpers.RemoteScriptContractSupport;
using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CacheFixtureCompletionTests
{
    [Test]
    [Arguments("host")]
    [Arguments("volume")]
    public void Preview_reads_recovery_in_its_payload_filesystem(string backend) =>
        Run("c913-seed-contract.sh", "preview-" + backend, "PASS preview-" + backend + "-complete");

    [Test]
    public void Fixture_prune_consumes_real_preview_and_exercises_controls() =>
        Run("c913-seed-contract.sh", "fixture-prune", "PASS fixture-prune-complete");

    [Test]
    [Arguments("preview")]
    [Arguments("apply")]
    public void Fixture_prune_retains_inner_refusal(string phase) =>
        Run("c913-seed-contract.sh", "prune-diagnostic-" + phase, "PASS inner-diagnostic-retained");

    [Test]
    [Arguments("donor")]
    [Arguments("donor-foreign")]
    [Arguments("donor-replaced")]
    [Arguments("donor-unknown")]
    public void Finalization_releases_donor_before_project_absence_audit(string scenario) =>
        Run("c1192-workspace-contract.sh", "compose-absence", "donor-checked=1", scenario);

    [Test]
    public void Running_inventory_refuses_each_failed_probe() =>
        Run("c913-seed-contract.sh", "inventory-running", "PASS inventory-running-complete");

    [Test]
    public void Seed_metadata_pipeline_failure_is_refused() =>
        Run("c913-seed-contract.sh", "seed-metadata", "PASS seed-metadata-complete");

    [Test]
    public void Seed_helper_removal_failure_is_refused() =>
        Run("c913-seed-contract.sh", "seed-helper-removal", "PASS seed-helper-removal-complete");

    [Test]
    public void Retention_failed_volume_inspect_is_not_absence() =>
        Run("c1192-workspace-contract.sh", "retention-inspect", "PASS retention-inspect-refused");

    [Test]
    public void Fixture_seed_tool_hash_failure_is_named() =>
        Run("c1315-fail-closed.sh", "seed-tool-hash", "PASS seed-tool-hash-named");

    [Test]
    public void Fixture_prune_manifest_hash_failure_is_named() =>
        Run("c1315-fail-closed.sh", "prune-manifest-hash", "PASS prune-manifest-hash-named");

    [Test]
    public void Fixture_namespace_inspect_failure_is_not_free() =>
        Run("c1315-fail-closed.sh", "namespace-inspect", "PASS namespace-inspect-not-free");

    [Test]
    public void Retired_cache_inspect_failure_is_not_absence() =>
        Run("c1315-fail-closed.sh", "retired-inspect", "PASS retired-inspect-not-absence");

    [Test]
    public void Modern_daemon_missing_volume_text_is_positive_absence() =>
        Run("c1315-fail-closed.sh", "modern-volume-absent", "PASS modern-volume-absent");

    // Unit 66: the live bridge's receipt (C590Bridge's Invoke-C590Ssh) in process on C590BridgeWorld, where
    // scripts/fixtures/cache-bridge-diagnostics.ps1 drove scripts/c590-real.ps1 with function fakes of ssh, scp and Write-C590Result.
    [Test]
    public void Bridge_retains_diagnostics_and_refuses_failed_transport()
    {
        var noise = new System.Text.Json.Nodes.JsonArray("CacheRecoveryInvalid credential=TOKEN_SENTINEL_BRIDGE", "DIAGNOSIS=FixturePrunePreviewFailed", "password=TOKEN_SENTINEL_BRIDGE");
        foreach (var _ in Enumerable.Range(0, 300)) noise.Add("CacheRecoveryInvalid");
        foreach (var scenario in new[] { "inner", "transport", "missing" })
        {
            using var world = new C590BridgeWorld();
            var caseRoot = Path.Combine(world.Checkout, ".antiphon", "bridge-model", scenario);
            var remote = Path.Combine(world.Root, "remote-case");
            Directory.CreateDirectory(remote);
            File.WriteAllText(Path.Combine(remote, "c590-result.json"),
                "{\"accepted\":" + (scenario == "transport" ? "true" : "false") + ",\"diagnosis\":\"FixturePrunePreviewFailed\",\"exit\":2}");
            var run = world.RunCase("runner-cache-fixture", new System.Text.Json.Nodes.JsonObject
            {
                ["evidenceRoot"] = caseRoot, ["sourceSha"] = new string('a', 40), ["runId"] = "c849aaaaaaaaaaaaaaaa0",
            }, rules:
            [
                new System.Text.Json.Nodes.JsonObject { ["name"] = "ssh", ["match"] = "bash '/home/mc/antiphon-c590/c590-remote\\.sh'", ["stdout"] = noise.DeepClone(), ["exit"] = scenario == "inner" ? 2 : 255 },
                new System.Text.Json.Nodes.JsonObject { ["name"] = "scp", ["match"] = "^-o BatchMode=yes -r ", ["copyFrom"] = scenario == "missing" ? "" : remote, ["exit"] = 0 },
            ]);
            run.Exit.ShouldBe(2, scenario + ": transport accepted; " + run.Output + run.Error);
            var result = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(caseRoot, "c590-result.json")))!;
            result["accepted"]!.GetValue<bool>().ShouldBeFalse(scenario);
            var log = File.ReadAllText(Path.Combine(caseRoot, "remote-output.txt"));
            log.Length.ShouldBeLessThanOrEqualTo(32768, "unbounded diagnostic output");
            var expectedExit = scenario == "inner" ? 2 : 255;
            Retained(log, expectedExit).ShouldBeTrue(scenario + ": safe diagnostic and numeric exit retained; " + log);
            result["diagnosis"]!.GetValue<string>().ShouldBe(scenario switch
            {
                "inner" => "FixturePrunePreviewFailed",
                "transport" => "RemoteTransportFailed",
                _ => "RemoteResultMissing exit=255",
            });
        }
        // Retained text shaped like a Windows receipt: CRLF, a drive path, native wording.
        Retained("C:\\Antiphon\\worktrees\\card-task-fbd9be0e\\scripts\\c590-real.ps1: CacheRecoveryInvalid\r\nssh.exe : CacheRecoveryInvalid\r\n"
            + "The system cannot find the file specified\r\ntransport-exit=2\r\n", 2).ShouldBeTrue();
        Retained("CacheRecoveryInvalid\rtransport-exit=2\r", 2).ShouldBeTrue();
        Retained("CacheRecoveryInvalid\ntransport-exit=2\n", 2).ShouldBeTrue();
        Retained("The system cannot find the file specified\ntransport-exit=2\n", 2).ShouldBeFalse();
        Retained("CacheRecoveryInvalid credential=TOKEN_SENTINEL_BRIDGE\ntransport-exit=2\n", 2).ShouldBeFalse();
        Retained("password=TOKEN_SENTINEL_BRIDGE\nCacheRecoveryInvalid\ntransport-exit=2\n", 2).ShouldBeFalse();
    }

    // A receipt keeps the safe token, never a secret, and its numeric transport exit.
    private static bool Retained(string text, int expectedExit)
    {
        var log = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.IsMatch(log, "(?m)(?<![A-Za-z0-9])CacheRecoveryInvalid(?![A-Za-z0-9])")
            && !System.Text.RegularExpressions.Regex.IsMatch(log, "TOKEN_SENTINEL|password|credential", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(log, "(?m)^transport-exit=" + expectedExit + "$");
    }
    private static void Run(string script, string mode, string witness, string argument = "")
    {
        var output = LinuxShell(
            "bash \"$repo/scripts/fixtures/" + script + "\" " + mode + " \"$repo\" " + argument +
            "\nprintf 'MODEL_EXIT=%s\\n' \"$?\"\n", "repo");
        output.ShouldContain("MODEL_EXIT=0", customMessage: output);
        output.ShouldContain(witness, customMessage: output);
        System.Text.RegularExpressions.Regex.IsMatch(output, @"(?m)^(FAIL |C1192_FAIL)")
            .ShouldBeFalse(output);
    }
}
