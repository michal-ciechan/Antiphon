using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The verify-docker-stack command (Antiphon.Nightly.DockerStackVerify, unit 47 of the 2026-11-22 plan) beyond the smoke and
/// recovery rows: the default arm, the record it writes, the commands it starts without the stub (on a recording launcher), the
/// e2e-staged-apphost consumer and the failures that were the script's thrown errors (exit 1). In process through C590Harness;
/// nothing starts. Unit 66: the live roster and a live case without the stub are the bridge's (C590BridgeTests); the head's
/// refusals run here for every case.
/// </summary>
[Category("Unit")]
public sealed class DockerStackVerifyTests
{
    private static readonly Dictionary<string, string?> NoStub = new() { [C590Boundary.StubVariable] = null };

    [Test]
    public void Unrouted_case_is_unknown_in_stub_mode_and_pending_otherwise()
    {
        var stub = C590Harness.Run("verify-docker-stack", C590Harness.Happy(), args: ["--case", "no-such-case"]);
        stub.ExitCode.ShouldBe(2);
        stub.ResultJson.ShouldBe("{\"accepted\":false,\"diagnosis\":\"UnknownCase\",\"exit\":2}" + Environment.NewLine);
        stub.Output.ShouldBe("DIAGNOSIS=UnknownCase" + Environment.NewLine);

        var real = C590Harness.Run("verify-docker-stack", C590Harness.Happy(), NoStub, args: ["--case", "No-Such-Case"]);
        real.ExitCode.ShouldBe(2);
        Diagnosis(real).ShouldBe("RealCasePending No-Such-Case");
    }

    [Test]
    public void Cases_match_ignoring_case_as_the_switch_did()
    {
        var run = C590Harness.Run("verify-docker-stack", Extra(new() { ["observedRevision"] = "ABC", ["sourceSha"] = "abc" }), args: ["--case", "Version-Check"]);
        run.ExitCode.ShouldBe(0, run.Output);
        run.ResultJson.ShouldBe("{\"accepted\":true,\"diagnosis\":\"\",\"exit\":0}" + Environment.NewLine);
    }

    [Test]
    public void Extras_follow_the_three_fields_in_the_record()
    {
        var summary = C590Harness.Run("verify-docker-stack", Extra(new() { ["runExit"] = "7" }), args: ["--case", "failure-summary"]);
        summary.ExitCode.ShouldBe(7);
        summary.ResultJson.ShouldBe("{\"accepted\":true,\"diagnosis\":\"\",\"exit\":7,\"delivery\":\"confirmed\",\"runExit\":7}" + Environment.NewLine);

        var secrets = C590Harness.Run("verify-docker-stack", Extra(new() { ["keyProtection"] = "x509certificate", ["certificatePresent"] = true }),
            args: ["--case", "managed-secrets"]);
        secrets.ResultJson.ShouldBe("{\"accepted\":true,\"diagnosis\":\"\",\"exit\":0,\"managedSecretReady\":true}" + Environment.NewLine);
    }

    [Test]
    public void Without_the_stub_a_case_starts_the_commands_it_records()
    {
        var run = C590Harness.Run("verify-docker-stack", Extra(new() { ["fixtureServerId"] = "server-1" }), NoStub, args: ["--case", "await-exit"]);
        run.ExitCode.ShouldBe(0, run.Output);
        run.Launches.Select(launch => launch.CommandText).ShouldBe(["observe-exited server-1", "docker start server-1"]);
        run.Launches.ShouldAllBe(launch => launch.CaptureOutput);
        run.Commands.ShouldBe(["{\"exe\":\"observe-exited\",\"args\":[\"server-1\"]}", "{\"exe\":\"docker\",\"args\":[\"start\",\"server-1\"]}"]);
    }

    [Test]
    public void A_command_that_cannot_start_is_exit_1_without_a_record()
    {
        var run = C590Harness.Run("verify-docker-stack", C590Harness.Happy(), NoStub,
            answer: _ => throw new System.ComponentModel.Win32Exception(2, "not found"), args: ["--case", "normal-down"]);
        run.ExitCode.ShouldBe(1);
        run.Output.ShouldContain("verify-docker-stack: cannot start docker: not found");
        run.ResultJson.ShouldBeEmpty();
    }

    [Test]
    public void A_value_that_does_not_cast_is_exit_1()
    {
        var run = C590Harness.Run("verify-docker-stack", Extra(new() { ["extraBody"] = "many" }), args: ["--case", "enter-only-body"]);
        run.ExitCode.ShouldBe(1);
        run.Output.ShouldContain("cannot convert extraBody to an integer");
    }

    [Test]
    public void A_subordinate_list_that_is_missing_is_one_unnamed_failure_as_at_array_made_it()
    {
        var manifest = Extra(new() { ["sessionState"] = "Idle", ["markerSeen"] = true });
        var run = C590Harness.Run("verify-docker-stack", manifest, args: ["--case", "await-nested-stack"]);
        Diagnosis(run).ShouldBe("SubordinateFailed ");
    }

    [Test]
    public void E2e_staged_apphost_reads_the_consumer_from_the_checkout_and_needs_the_staged_apphost()
    {
        var consumer = Path.Combine([C590Harness.RepoRoot, .. DockerStackVerify.StagedApphostConsumer]);
        var staged = Path.Combine(C590Harness.FixtureRoot, "apphost", "Antiphon.AppHost.dll");
        var files = new Dictionary<string, string> { [consumer] = "var path = TestAppHostPath;", [staged] = "dll" };

        var accepted = C590Harness.Run("verify-docker-stack", Extra(new() { ["stagedApphost"] = staged }), files: files, args: ["--case", "e2e-staged-apphost"]);
        accepted.ExitCode.ShouldBe(0, accepted.Output);

        var missing = C590Harness.Run("verify-docker-stack", Extra(new() { ["stagedApphost"] = staged + ".gone" }), files: files, args: ["--case", "e2e-staged-apphost"]);
        Diagnosis(missing).ShouldBe("MissingApphost");

        var noConsumer = C590Harness.Run("verify-docker-stack", Extra(new() { ["stagedApphost"] = staged }),
            files: new Dictionary<string, string> { [consumer] = "no consumer here", [staged] = "dll" }, args: ["--case", "e2e-staged-apphost"]);
        Diagnosis(noConsumer).ShouldBe("MissingConsumer");
    }

    [Test]
    public void The_committed_consumer_still_names_the_staged_apphost()
    {
        File.ReadAllText(Path.Combine([C590Harness.RepoRoot, .. DockerStackVerify.StagedApphostConsumer])).ShouldContain("TestAppHostPath");
    }

    [Test]
    public void The_head_refuses_before_any_case_in_stub_mode_too()
    {
        // Unit 66: the launcher's head is the command's: a repeated name, a cleanup context on another case and a recycle context
        // on a case that cannot carry one exit 1 before the case answers.
        var duplicate = C590Harness.Run("verify-docker-stack", Extra(new() { ["tempContainerCleanup"] = new Dictionary<string, object?> { ["version"] = 1 } }),
            args: ["--case", "version-check"]);
        duplicate.ExitCode.ShouldBe(1);
        duplicate.Output.ShouldContain("verify-docker-stack: TempContainerContextInvalid");
        duplicate.ResultJson.ShouldBeEmpty();
        var recycle = C590Harness.Run("verify-docker-stack", Extra(new() { ["Recycle"] = new Dictionary<string, object?> { ["version"] = 1 } }),
            args: ["--case", "deploy-parent"]);
        recycle.ExitCode.ShouldBe(1);
        recycle.Output.ShouldContain("verify-docker-stack: RecycleContextInvalid");
        recycle.Commands.ShouldBeEmpty();
    }

    [Test]
    public void Missing_case_or_manifest_is_a_usage_error()
    {
        var run = C590Harness.Run("verify-docker-stack", C590Harness.Happy());
        run.ExitCode.ShouldBe(2);
        run.Output.ShouldContain("missing --case");
        Should.Throw<NightlyUsageException>(() => DockerStackVerifyOptions.Parse(["--case", "x"], "")).Message.ShouldBe("missing --manifest");
    }

    private static Dictionary<string, object?> Extra(Dictionary<string, object?> extra)
    {
        var manifest = C590Harness.Happy();
        foreach (var pair in extra) manifest[pair.Key] = pair.Value;
        return manifest;
    }

    private static string Diagnosis(C590Run run) =>
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("diagnosis").GetString() ?? "";
}
