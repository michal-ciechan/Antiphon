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

    private static void Run(string script, string mode, string witness, string argument = "")
    {
        var output = RemoteScriptContractTests.LinuxShell(
            "bash \"$repo/scripts/fixtures/" + script + "\" " + mode + " \"$repo\" " + argument +
            "\nprintf 'MODEL_EXIT=%s\\n' \"$?\"\n", "repo");
        output.ShouldContain("MODEL_EXIT=0", customMessage: output);
        output.ShouldContain(witness, customMessage: output);
        output.ShouldNotContain("FAIL", customMessage: output);
    }
}
