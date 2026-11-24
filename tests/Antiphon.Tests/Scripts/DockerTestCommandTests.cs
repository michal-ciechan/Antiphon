using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0590 test entry guards: the test-docker and test-docker-container commands (scripts/lib/Antiphon.Nightly/DockerTestEntry.cs,
/// which replaced scripts/test-docker.ps1 and scripts/test-docker-container.ps1 in unit 47 of the 2026-11-22 plan) in process through
/// C590Harness: the same rows the pwsh runs of the scripts checked, and the live throwaway entry on a recording launcher.
/// </summary>
[Category("Unit")]
public sealed class DockerTestCommandTests
{
    [Test]
    public void Socket_gid_mismatch_refuses()
    {
        var manifest = C590Harness.Happy();
        manifest["socket"] = new Dictionary<string, object?> { ["present"] = true, ["gid"] = 2, ["groups"] = new[] { 1 } };
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        Diagnosis(run).ShouldBe("SocketGroupMismatch");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Sibling_daemon_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["daemon"] = new Dictionary<string, object?> { ["present"] = true, ["name"] = "server2", ["hostname"] = "runner-host" };
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldBe(2);
        Diagnosis(run).ShouldBe("SiblingDaemonRefused");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Missing_nested_daemon_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["daemon"] = new Dictionary<string, object?> { ["present"] = false, ["name"] = "", ["hostname"] = "runner-host" };
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldBe(2);
        Diagnosis(run).ShouldBe("NestedDaemonUnavailable");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Missing_socket_fails_before_execution()
    {
        var manifest = C590Harness.Happy();
        manifest["socket"] = new Dictionary<string, object?> { ["present"] = false, ["gid"] = 1, ["groups"] = new[] { 1 } };
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Unreachable_mapped_database_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["dbProbeOk"] = false;
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Broker_lane_is_enabled()
    {
        var run = C590Harness.Run("test-docker-container", C590Harness.Happy(), args: ["--checkpoint", "messaging"]);
        string.Join('\n', run.Commands).ShouldContain("ANTIPHON_BROKER_TESTS=1");
    }

    [Test]
    public void Live_credentials_are_removed()
    {
        var run = C590Harness.Run(
            "test-docker-container",
            C590Harness.Happy(),
            new Dictionary<string, string?> { ["ANTIPHON_TG_TEST_TOKEN"] = "harmless-sentinel-token" },
            args: ["--checkpoint", "messaging"]);
        string.Join('\n', run.Commands).ShouldNotContain("harmless-sentinel-token");
    }

    [Test]
    public void Nonzero_test_exit_is_preserved()
    {
        var manifest = C590Harness.Happy();
        manifest["childExit"] = 23;
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldBe(23);
    }

    [Test]
    public void Missing_report_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["reportPresent"] = false;
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.ResultJson.ShouldNotContain("result-ready");
    }

    [Test]
    public void Zero_execution_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(executed: 0);
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        Diagnosis(run).ShouldBe("ZeroExecuted");
        run.ExitCode.ShouldNotBe(0);
    }

    [Test]
    public void Missing_class_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["expectedClasses"] = new[] { "Needed" };
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.Output.ShouldContain("Needed");
        run.ExitCode.ShouldNotBe(0);
    }

    [Test]
    public void Unexpected_skip_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(skipped: 1);
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        Diagnosis(run).ShouldBe("UnexpectedSkip");
    }

    [Test]
    public void Exit_result_disagreement_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(failed: 1);
        manifest["childExit"] = 0;
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        Diagnosis(run).ShouldBe("ResultExitMismatch");
        run.ExitCode.ShouldNotBe(0);
    }

    [Test]
    public void Export_precedes_container_removal()
    {
        var run = C590Harness.Run("test-docker", C590Harness.Happy(), args: ["--group", "small"]);
        var text = string.Join('\n', run.Commands);
        var export = text.IndexOf("\"cp\"", StringComparison.Ordinal);
        var remove = text.IndexOf("\"rm\"", StringComparison.Ordinal);
        remove.ShouldBeGreaterThan(export);
    }

    [Test]
    public void Wrong_artifact_digest_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["copiedDigest"] = "other";
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.ResultJson.ShouldNotContain("\"accepted\":true");
    }

    [Test]
    public void Test_environment_refuses_application_runner()
    {
        var run = C590Harness.Run("test-docker-container", C590Harness.Happy(), args: ["--checkpoint", "backend"]);
        string.Join('\n', run.Commands).ShouldContain("SessionRunner__BaseUrl=http://127.0.0.1:1");
    }

    [Test]
    public void Unknown_group_is_refused()
    {
        var run = C590Harness.Run("test-docker", C590Harness.Happy(), args: ["--group", "nope"]);
        Diagnosis(run).ShouldBe("UnknownGroup");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Sourced_task_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["task"] = new Dictionary<string, object?> { ["readable"] = true, ["sourceLandingOperationId"] = "op-1" };
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Unreadable_task_binding_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["task"] = new Dictionary<string, object?> { ["readable"] = false, ["sourceLandingOperationId"] = "" };
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        Diagnosis(run).ShouldBe("TaskBindingUnavailable");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Verification_path_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["sourceRoot"] = Path.Combine(C590Harness.RepoRoot, "verification", "snapshot");
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Outside_checkout_root_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["checkoutRoot"] = C590Harness.RepoRoot;
        manifest["sourceRoot"] = C590Harness.RepoRoot + "-evil";
        var sibling = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        sibling.Commands.ShouldBeEmpty();

        var escaped = C590Harness.Happy();
        escaped["canonicalPath"] = Path.Combine(Path.GetTempPath(), "c590-escape");
        var symlink = C590Harness.Run("test-docker", escaped, args: ["--group", "small"]);
        symlink.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Stale_report_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(runId: "old");
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small"]);
        Diagnosis(run).ShouldBe("StaleResult");
        run.ResultJson.ShouldNotContain("\"accepted\":true");
    }

    [Test]
    public void Accepted_run_writes_the_result_after_the_export_commands()
    {
        var run = C590Harness.Run("test-docker", C590Harness.Happy(), args: ["--group", "SMALL"]);
        run.ExitCode.ShouldBe(0, run.Output);
        run.ResultJson.ShouldBe("{\"accepted\":true,\"diagnosis\":\"\",\"exit\":0}" + Environment.NewLine);
        run.Output.ShouldBe("DIAGNOSIS=" + Environment.NewLine);
        run.Commands.ShouldBe([
            "{\"exe\":\"checkpoint\",\"args\":[\"SMALL\"]}",
            "{\"exe\":\"docker\",\"args\":[\"cp\",\"export\"]}",
            "{\"exe\":\"hash\",\"args\":[\"artifacts\"]}",
            "{\"exe\":\"commit-manifest\",\"args\":[\"result-ready\"]}",
            "{\"exe\":\"docker\",\"args\":[\"rm\",\"container\"]}",
        ]);
        run.Launches.ShouldBeEmpty("the stub records commands and starts none");
    }

    [Test]
    public void Nonzero_child_exit_writes_no_result()
    {
        var manifest = C590Harness.Happy();
        manifest["childExit"] = 5;
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "all"]);
        run.ExitCode.ShouldBe(5);
        run.ResultJson.ShouldBeEmpty();
        run.Commands.Count.ShouldBe(5);
    }

    [Test]
    public void Without_the_stub_each_recorded_command_starts()
    {
        var run = C590Harness.Run("test-docker", C590Harness.Happy(), new Dictionary<string, string?> { ["ANTIPHON_C590_STUB"] = null },
            args: ["--group", "backend"]);
        run.ExitCode.ShouldBe(0, run.Output);
        run.Launches.Select(launch => launch.CommandText).ShouldBe(["checkpoint backend", "docker cp export", "hash artifacts", "commit-manifest result-ready", "docker rm container"]);
        run.Launches.ShouldAllBe(launch => launch.CaptureOutput && launch.Environment == null);
        run.Commands.Count.ShouldBe(5, "the sink still records every command");
    }

    [Test]
    public void Throwaway_stack_runs_the_remote_throwaway_case_on_the_inherited_environment()
    {
        var run = C590Harness.Run("test-docker", C590Harness.Happy(),
            new Dictionary<string, string?> { ["ANTIPHON_C590_STUB"] = null, ["C780_KEPT"] = "kept" },
            answer: _ => new NightlyLaunchResult { ExitCode = 7 },
            args: ["--group", "all", "--throwaway-stack"]);
        run.ExitCode.ShouldBe(7);
        var launch = run.Launches.ShouldHaveSingleItem();
        launch.FileName.ShouldBe("bash");
        launch.Arguments.ShouldBe([Path.Combine(C590Harness.RepoRoot, "scripts", "c590-remote.sh")]);
        launch.CaptureOutput.ShouldBeFalse("the live entry shares this console");
        launch.Environment.ShouldNotBeNull();
        launch.Environment!["C590_CASE"].ShouldBe("throwaway-all");
        launch.Environment["C590_REEXEC"].ShouldBe("1");
        launch.Environment["C780_KEPT"].ShouldBe("kept");
        run.ResultJson.ShouldBeEmpty();
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Throwaway_stack_keeps_a_reexec_marker_already_set()
    {
        var environment = DockerTestEntry.ThrowawayEnvironment(new Dictionary<string, string> { ["C590_REEXEC"] = "2", ["C590_CASE"] = "other" });
        environment["C590_REEXEC"].ShouldBe("2");
        environment["C590_CASE"].ShouldBe("throwaway-all");
    }

    [Test]
    public void Throwaway_stack_under_the_stub_stays_the_manifest_boundary()
    {
        var manifest = C590Harness.Happy();
        manifest["dbProbeOk"] = false;
        var run = C590Harness.Run("test-docker", manifest, args: ["--group", "small", "--throwaway-stack"]);
        Diagnosis(run).ShouldBe("UnreachableDatabase");
        run.Launches.ShouldBeEmpty();
    }

    [Test]
    public void Neither_container_mode_is_a_usage_error()
    {
        var run = C590Harness.Run("test-docker-container", C590Harness.Happy());
        run.ExitCode.ShouldBe(2);
        run.Output.ShouldContain("Checkpoint or ValidateRoster is required");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Checkpoint_launch_prints_its_marker_and_records_the_child_environment()
    {
        var run = C590Harness.Run("test-docker-container", C590Harness.Happy(), args: ["--checkpoint", "backend"]);
        run.ExitCode.ShouldBe(0);
        run.Output.ShouldBe("checkpoint-launched" + Environment.NewLine);
        run.Commands.ShouldBe(["{\"exe\":\"child-env\",\"args\":[\"ANTIPHON_BROKER_TESTS=1\",\"SessionRunner__BaseUrl=http://127.0.0.1:1\"]}"]);
    }

    private static Dictionary<string, object?> Report(int executed = 1, int failed = 0, int skipped = 0, string runId = "run-1") => new()
    {
        ["executed"] = executed, ["passed"] = executed - failed, ["failed"] = failed, ["skipped"] = skipped,
        ["classes"] = new[] { "A" }, ["runId"] = runId,
    };

    private static string Diagnosis(C590Run run) =>
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("diagnosis").GetString() ?? "";
}
