using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0590 delivery recovery guards: the verify-docker-stack command's recovery cases (unit 47 of the 2026-11-22 plan) in process
/// through C590Harness, with the stub set: the same rows the pwsh runs of scripts/verify-docker-stack.ps1 checked.
/// </summary>
[Category("Unit")]
public sealed class DockerDeliveryRecoveryCommandTests
{
    [Test]
    public void Cleanup_failure_preserves_residue()
    {
        var run = Run("cleanup-residue", new() { ["survivingId"] = "container-9" });
        run.ExitCode.ShouldNotBe(0);
        run.ResultJson.ShouldContain("container-9");
    }

    [Test]
    public void Interrupted_cleanup_rechecks_identity()
    {
        var run = Run("interrupted-cleanup", new() { ["replacementId"] = "new", ["ownedId"] = "old" });
        string.Join('\n', run.Commands).ShouldNotContain("rm");
    }

    [Test]
    public void Expectation_is_durable_before_post()
    {
        var run = Run("expectation-before-post", new() { ["bodySha"] = "abc" });
        var text = string.Join('\n', run.Commands);
        text.IndexOf("write-expectation", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("POST", StringComparison.Ordinal));
        text.ShouldContain("abc");
    }

    [Test]
    public void Unknown_ack_never_reposts()
    {
        var run = Run("unknown-ack", new());
        Count(run, "POST").ShouldBe(1);
    }

    [Test]
    public void Live_request_absence_is_not_refusal()
    {
        var run = Run("live-absence", new() { ["requestOwnerAlive"] = true });
        Diagnosis(run).ShouldBe("Incomplete");
        Count(run, "POST").ShouldBe(0);
    }

    [Test]
    public void Refused_insert_retry_requires_absence()
    {
        var run = Run("refused-retry", new() { ["nativePrompt"] = true, ["bodyWritten"] = false, ["enterWritten"] = false });
        Count(run, "POST").ShouldBe(0);
    }

    [Test]
    public void Crash_requires_exported_reached_record()
    {
        var run = Run("crash-export", new() { ["fixtureServerId"] = "server-1" });
        var text = string.Join('\n', run.Commands);
        text.IndexOf("export-reached", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("kill", StringComparison.Ordinal));
    }

    [Test]
    public void Crash_target_is_only_fixture_server()
    {
        var run = Run("crash-target", new() { ["fixtureServerId"] = "server-1", ["runnerId"] = "runner-1" });
        var text = string.Join('\n', run.Commands);
        text.ShouldContain("server-1");
        text.ShouldNotContain("runner-1");
    }

    [Test]
    public void Crash_action_intent_is_durable()
    {
        var run = Run("crash-intent", new() { ["fixtureServerId"] = "server-1" });
        var text = string.Join('\n', run.Commands);
        text.IndexOf("write-intent", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("kill", StringComparison.Ordinal));
    }

    [Test]
    public void Server_exit_is_awaited()
    {
        var run = Run("await-exit", new() { ["fixtureServerId"] = "server-1" });
        var text = string.Join('\n', run.Commands);
        text.IndexOf("observe-exited", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("start", StringComparison.Ordinal));
    }

    [Test]
    public void Restart_identity_change_is_refused()
    {
        var run = Run("restart-identity", new() { ["runnerIdBefore"] = "runner-a", ["runnerIdAfter"] = "runner-b" });
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public void Retype_recovery_charges_once()
    {
        var run = Run("retype-charge", new() { ["attempts"] = 1, ["floor1"] = false, ["floor2"] = false });
        Diagnosis(run).ShouldBe("RetypeCharge");
    }

    [Test]
    public void Enter_only_recovery_forbids_second_body()
    {
        var run = Run("enter-only-body", new() { ["extraBody"] = 1 });
        Diagnosis(run).ShouldBe("SecondBody");
    }

    [Test]
    public void Received_recovery_requires_zero_writes()
    {
        var run = Run("zero-writes", new() { ["additionalBody"] = 1, ["additionalEnter"] = 1 });
        Diagnosis(run).ShouldBe("ExtraWrite");
    }

    [Test]
    public void Receipt_manifest_resume_is_read_only()
    {
        var run = Run("resume-manifest", new());
        Count(run, "POST").ShouldBe(0);
    }

    [Test]
    public void Received_failure_summary_remains_failure()
    {
        var run = Run("failure-summary", new() { ["runExit"] = 23 });
        run.ResultJson.ShouldContain("confirmed");
        run.ExitCode.ShouldBe(23);
    }

    [Test]
    public void Missing_reached_cut_fails()
    {
        var run = Run("missing-cut", new() { ["reached"] = false });
        Diagnosis(run).ShouldBe("CutNotReached");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public void Stock_receipts_are_independent_gates()
    {
        var run = Run("stock-gates", new() { ["stockIdle"] = true, ["stockBusy"] = false });
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public void Interrupted_export_is_incomplete()
    {
        var run = Run("interrupted-export", new() { ["exportComplete"] = false });
        string.Join('\n', run.Commands).ShouldNotContain("result-ready");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public void Receipt_probe_requires_when_idle()
    {
        var run = Run("when-idle", new());
        string.Join('\n', run.Commands).ShouldContain("WhenIdle");
    }

    [Test]
    public void Enter_only_recovery_keeps_tuple()
    {
        var run = Run("enter-only-tuple", new() { ["attempts"] = 2, ["floor"] = "9", ["originalFloor"] = "4", ["generation"] = "g", ["originalGeneration"] = "g" });
        Diagnosis(run).ShouldBe("TupleChanged");
    }

    [Test]
    public void Retype_requires_second_committed_tuple()
    {
        var run = Run("retype-second-tuple", new() { ["attempt2Snapshot"] = false });
        Diagnosis(run).ShouldBe("MissingSecondTuple");
    }

    [Test]
    public void Foreign_resume_manifest_path_is_refused()
    {
        var sibling = Run("foreign-resume", new() { ["resumePath"] = C590Harness.RepoRoot + "-evil", ["evidenceRoot"] = C590Harness.RepoRoot });
        sibling.Commands.ShouldBeEmpty();
        var escaped = Run("foreign-resume", new()
        {
            ["resumePath"] = C590Harness.RepoRoot,
            ["canonicalResume"] = Path.Combine(Path.GetTempPath(), "c590-outside"),
        });
        escaped.ExitCode.ShouldNotBe(0);
    }

    [Test]
    public void Private_credential_is_not_exported()
    {
        var run = Run("private-credential", new() { ["secretSentinel"] = "sentinel-secret-value" });
        (run.Output + run.ResultJson + string.Join('\n', run.Commands)).ShouldNotContain("sentinel-secret-value");
    }

    private static C590Run Run(string caseName, Dictionary<string, object?> extra)
    {
        var manifest = C590Harness.Happy();
        foreach (var pair in extra)
            manifest[pair.Key] = pair.Value;
        return C590Harness.Run("verify-docker-stack", manifest, args: ["--case", caseName]);
    }

    private static int Count(C590Run run, string token) =>
        run.Commands.Count(line => line.Contains(token, StringComparison.Ordinal));

    private static string Diagnosis(C590Run run) =>
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("diagnosis").GetString() ?? "";

    private static bool Accepted(C590Run run) =>
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("accepted").GetBoolean();
}
