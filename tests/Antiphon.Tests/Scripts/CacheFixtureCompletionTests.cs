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
    public async Task Bridge_retains_diagnostics_and_refuses_failed_transport()
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-NoProfile", "-File", Path.Combine(DelegateScriptRunner.RepoRoot,
                "scripts/fixtures/cache-bridge-diagnostics.ps1"), "-Repo", DelegateScriptRunner.RepoRoot }
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        var output = await stdout + await stderr;
        process.ExitCode.ShouldBe(0, output);
        output.ShouldContain("BRIDGE_COMPLETE", customMessage: output);
    }

    private static void Run(string script, string mode, string witness, string argument = "")
    {
        var output = RemoteScriptContractTests.LinuxShell(
            "bash \"$repo/scripts/fixtures/" + script + "\" " + mode + " \"$repo\" " + argument +
            "\nprintf 'MODEL_EXIT=%s\\n' \"$?\"\n", "repo");
        output.ShouldContain("MODEL_EXIT=0", customMessage: output);
        output.ShouldContain(witness, customMessage: output);
        System.Text.RegularExpressions.Regex.IsMatch(output, @"(?m)^(FAIL |C1192_FAIL)")
            .ShouldBeFalse(output);
    }
}
