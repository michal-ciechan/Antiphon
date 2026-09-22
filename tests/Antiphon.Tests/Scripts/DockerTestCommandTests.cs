using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DockerTestCommandTests
{
    [Test]
    public async Task Socket_gid_mismatch_refuses()
    {
        var manifest = C590Harness.Happy();
        manifest["socket"] = new Dictionary<string, object?> { ["present"] = true, ["gid"] = 2, ["groups"] = new[] { 1 } };
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        Diagnosis(run).ShouldBe("SocketGroupMismatch");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Sibling_daemon_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["daemon"] = new Dictionary<string, object?> { ["present"] = true, ["name"] = "server2", ["hostname"] = "runner-host" };
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldBe(2);
        Diagnosis(run).ShouldBe("SiblingDaemonRefused");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Missing_nested_daemon_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["daemon"] = new Dictionary<string, object?> { ["present"] = false, ["name"] = "", ["hostname"] = "runner-host" };
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldBe(2);
        Diagnosis(run).ShouldBe("NestedDaemonUnavailable");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Missing_socket_fails_before_execution()
    {
        var manifest = C590Harness.Happy();
        manifest["socket"] = new Dictionary<string, object?> { ["present"] = false, ["gid"] = 1, ["groups"] = new[] { 1 } };
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Unreachable_mapped_database_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["dbProbeOk"] = false;
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Broker_lane_is_enabled()
    {
        var run = await C590Harness.RunAsync("test-docker-container.ps1", C590Harness.Happy(), args: ["-Checkpoint", "messaging"]);
        string.Join('\n', run.Commands).ShouldContain("ANTIPHON_BROKER_TESTS=1");
    }

    [Test]
    public async Task Live_credentials_are_removed()
    {
        var run = await C590Harness.RunAsync(
            "test-docker-container.ps1",
            C590Harness.Happy(),
            new Dictionary<string, string> { ["ANTIPHON_TG_TEST_TOKEN"] = "harmless-sentinel-token" },
            "-Checkpoint", "messaging");
        string.Join('\n', run.Commands).ShouldNotContain("harmless-sentinel-token");
    }

    [Test]
    public async Task Nonzero_test_exit_is_preserved()
    {
        var manifest = C590Harness.Happy();
        manifest["childExit"] = 23;
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldBe(23);
    }

    [Test]
    public async Task Missing_report_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["reportPresent"] = false;
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.ResultJson.ShouldNotContain("result-ready");
    }

    [Test]
    public async Task Zero_execution_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(executed: 0);
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        Diagnosis(run).ShouldBe("ZeroExecuted");
        run.ExitCode.ShouldNotBe(0);
    }

    [Test]
    public async Task Missing_class_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["expectedClasses"] = new[] { "Needed" };
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.Output.ShouldContain("Needed");
        run.ExitCode.ShouldNotBe(0);
    }

    [Test]
    public async Task Unexpected_skip_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(skipped: 1);
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        Diagnosis(run).ShouldBe("UnexpectedSkip");
    }

    [Test]
    public async Task Exit_result_disagreement_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(failed: 1);
        manifest["childExit"] = 0;
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        Diagnosis(run).ShouldBe("ResultExitMismatch");
        run.ExitCode.ShouldNotBe(0);
    }

    [Test]
    public async Task Export_precedes_container_removal()
    {
        var run = await C590Harness.RunAsync("test-docker.ps1", C590Harness.Happy(), args: ["-Group", "small"]);
        var text = string.Join('\n', run.Commands);
        var export = text.IndexOf("\"cp\"", StringComparison.Ordinal);
        var remove = text.IndexOf("\"rm\"", StringComparison.Ordinal);
        remove.ShouldBeGreaterThan(export);
    }

    [Test]
    public async Task Wrong_artifact_digest_fails()
    {
        var manifest = C590Harness.Happy();
        manifest["copiedDigest"] = "other";
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.ExitCode.ShouldNotBe(0);
        run.ResultJson.ShouldNotContain("\"accepted\":true");
    }

    [Test]
    public async Task Test_environment_refuses_application_runner()
    {
        var run = await C590Harness.RunAsync("test-docker-container.ps1", C590Harness.Happy(), args: ["-Checkpoint", "backend"]);
        string.Join('\n', run.Commands).ShouldContain("SessionRunner__BaseUrl=http://127.0.0.1:1");
    }

    [Test]
    public async Task Unknown_group_is_refused()
    {
        var run = await C590Harness.RunAsync("test-docker.ps1", C590Harness.Happy(), args: ["-Group", "nope"]);
        Diagnosis(run).ShouldBe("UnknownGroup");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Sourced_task_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["task"] = new Dictionary<string, object?> { ["readable"] = true, ["sourceLandingOperationId"] = "op-1" };
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Unreadable_task_binding_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["task"] = new Dictionary<string, object?> { ["readable"] = false, ["sourceLandingOperationId"] = "" };
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        Diagnosis(run).ShouldBe("TaskBindingUnavailable");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Verification_path_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["sourceRoot"] = Path.Combine(C590Harness.RepoRoot, "verification", "snapshot");
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Outside_checkout_root_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["checkoutRoot"] = C590Harness.RepoRoot;
        manifest["sourceRoot"] = C590Harness.RepoRoot + "-evil";
        var sibling = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        sibling.Commands.ShouldBeEmpty();

        var escaped = C590Harness.Happy();
        escaped["canonicalPath"] = Path.Combine(Path.GetTempPath(), "c590-escape");
        var symlink = await C590Harness.RunAsync("test-docker.ps1", escaped, args: ["-Group", "small"]);
        symlink.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Stale_report_is_refused()
    {
        var manifest = C590Harness.Happy();
        manifest["report"] = Report(runId: "old");
        var run = await C590Harness.RunAsync("test-docker.ps1", manifest, args: ["-Group", "small"]);
        Diagnosis(run).ShouldBe("StaleResult");
        run.ResultJson.ShouldNotContain("\"accepted\":true");
    }

    private static Dictionary<string, object?> Report(int executed = 1, int failed = 0, int skipped = 0, string runId = "run-1") => new()
    {
        ["executed"] = executed, ["passed"] = executed - failed, ["failed"] = failed, ["skipped"] = skipped,
        ["classes"] = new[] { "A" }, ["runId"] = runId,
    };

    private static string Diagnosis(C590Run run) =>
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("diagnosis").GetString() ?? "";
}
