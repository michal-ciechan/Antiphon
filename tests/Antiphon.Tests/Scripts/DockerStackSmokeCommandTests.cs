using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DockerStackSmokeCommandTests
{
    [Test]
    public async Task Wrong_live_revision_is_refused()
    {
        var run = await Run("version-check", new() { ["observedRevision"] = "wrong", ["sourceSha"] = "abc" });
        run.ExitCode.ShouldNotBe(0);
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public async Task Foreign_state_owner_refuses()
    {
        var run = await Run("foreign-state", new() { ["foreignOwner"] = true });
        string.Join('\n', run.Commands).ShouldNotContain("chown");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Auto_keyring_without_protector_is_unavailable()
    {
        var run = await Run("managed-secrets", new() { ["keyProtection"] = "Auto", ["keyDirectoryExists"] = true, ["certificatePresent"] = false });
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("managedSecretReady").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Parent_as_child_is_refused()
    {
        var run = await Run("parent-child", new() { ["childProject"] = "same", ["parentProject"] = "same" });
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Manifest_precedes_create()
    {
        var run = await Run("manifest-order", new());
        var text = string.Join('\n', run.Commands);
        text.IndexOf("write-manifest", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("create", StringComparison.Ordinal));
    }

    [Test]
    public async Task Wrong_context_source_is_refused()
    {
        var run = await Run("source-sha", new() { ["contextSha"] = "other", ["sourceSha"] = "abc" });
        Diagnosis(run).ShouldBe("SourceMismatch");
        string.Join('\n', run.Commands).ShouldNotContain("build");
    }

    [Test]
    public async Task Runner_local_bind_source_is_refused()
    {
        var run = await Run("bind-source", new() { ["bindSource"] = "/work/test-evidence" });
        string.Join('\n', run.Commands).ShouldNotContain("/work/test-evidence");
    }

    [Test]
    public async Task Foreign_resource_id_is_refused()
    {
        var run = await Run("foreign-id", new() { ["inspectedId"] = "other", ["ownedId"] = "owned" });
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Foreign_resource_label_is_refused()
    {
        var run = await Run("foreign-label", new() { ["inspectedLabel"] = "other", ["ownedLabel"] = "owned", ["ownedId"] = "owned" });
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Normal_down_retains_volumes()
    {
        var run = await Run("normal-down", new());
        var text = string.Join('\n', run.Commands);
        text.ShouldNotContain("--volumes");
        text.ShouldNotContain("\"-v\"");
    }

    [Test]
    public async Task Global_cleanup_is_forbidden()
    {
        var run = await Run("global-cleanup", new());
        string.Join('\n', run.Commands).ShouldNotContain("prune");
    }

    [Test]
    public async Task Raw_challenge_is_required()
    {
        var run = await Run("raw-challenge", new() { ["rawMarker"] = false });
        Diagnosis(run).ShouldBe("RawChallengeMissing");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public async Task Raw_exit_is_required()
    {
        var run = await Run("raw-exit", new() { ["rawMarker"] = true, ["rawExit"] = false });
        Diagnosis(run).ShouldBe("RawExitMissing");
    }

    [Test]
    public async Task Session_created_run_is_required()
    {
        var run = await Run("session-origin", new() { ["sessionId"] = "" });
        Diagnosis(run).ShouldBe("SessionOriginMissing");
    }

    [Test]
    public async Task Child_probe_cannot_launch_parent()
    {
        var run = await Run("child-probe", new());
        string.Join('\n', run.Commands).ShouldNotContain("parent-launch");
    }

    [Test]
    public async Task Linux_custody_advertisement_is_refused()
    {
        var run = await Run("linux-custody", new() { ["capabilities"] = "VerificationCustodyV1" });
        Diagnosis(run).ShouldBe("UnsupportedCustodyAdvertised");
        Accepted(run).ShouldBeFalse();
    }

    // --- CARD-0604 S4. Every server2 row refuses before it changes standing state. ---

    [Test]
    public async Task Missing_deploy_key_refuses_deploy()
    {
        var run = await Run("deploy-parent", Deploy(d => d["deployKeyPresent"] = false));
        Diagnosis(run).ShouldBe("DeployKeyMissing");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Missing_phone_home_secret_refuses_deploy()
    {
        var run = await Run("deploy-parent", Deploy(d => d["phoneHomeSecretPresent"] = false));
        Diagnosis(run).ShouldBe("PhoneHomeSecretMissing");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Non_persistent_policy_refuses_deploy()
    {
        var run = await Run("deploy-parent", Deploy(d => d["restartPolicy"] = "no"));
        Diagnosis(run).ShouldBe("NotPersistent");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public async Task Sibling_daemon_refuses_deploy()
    {
        var run = await Run("deploy-parent", Deploy(d => d["daemonName"] = "server2"));
        Diagnosis(run).ShouldBe("SiblingDaemonRefused");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public async Task Not_dispatch_eligible_refuses_nested_stack_case()
    {
        var run = await Run("session-nested-stack", Nested(d => d["dispatchEligible"] = false));
        Diagnosis(run).ShouldBe("RunnerNotDispatchEligible");
        run.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Session_origin_is_required_for_nested_stack()
    {
        var run = await Run("session-nested-stack", Nested(d => d["sessionOrigin"] = ""));
        Diagnosis(run).ShouldBe("SessionOriginMissing");
    }

    [Test]
    public async Task Host_residue_refuses_nested_stack_case()
    {
        var run = await Run("session-nested-stack", Nested(d => d["hostResidue"] = "c604abc-child"));
        Diagnosis(run).ShouldBe("HostResidue");
    }

    [Test]
    public async Task Nested_residue_refuses_nested_stack_case()
    {
        var run = await Run("session-nested-stack", Nested(d => d["nestedResidue"] = "c604abc_pgdata"));
        Diagnosis(run).ShouldBe("NestedResidue");
    }

    [Test]
    public async Task Still_running_session_is_not_a_result()
    {
        var run = await Run("await-nested-stack", Await(d => d["sessionState"] = "Working"));
        Diagnosis(run).ShouldBe("SessionStillRunning");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public async Task Dead_session_without_marker_is_incomplete()
    {
        var run = await Run("await-nested-stack", Await(d =>
        {
            d["sessionState"] = "Idle";
            d["markerSeen"] = false;
        }));
        Diagnosis(run).ShouldBe("SessionIncomplete");
    }

    [Test]
    public async Task Subordinate_failure_refuses_acceptance()
    {
        var run = await Run("await-nested-stack", Await(d => d["subordinates"] = new[]
        {
            new Dictionary<string, object?> { ["name"] = "deployment-state", ["accepted"] = true },
            new Dictionary<string, object?> { ["name"] = "dotnet-filter", ["accepted"] = false },
        }));
        Diagnosis(run).ShouldBe("SubordinateFailed dotnet-filter");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public async Task Lost_nested_store_refuses_restart_case()
    {
        var run = await Run("persistent-restart", Restart(d => d["storeIdBefore"] = ""));
        Diagnosis(run).ShouldBe("LostNestedStore");
    }

    [Test]
    public async Task Changed_store_id_refuses_restart_case()
    {
        var run = await Run("persistent-restart", Restart(d => d["storeIdAfter"] = "a-different-store"));
        Diagnosis(run).ShouldBe("ChangedStoreId");
        Accepted(run).ShouldBeFalse();
    }

    [Test]
    public async Task Undeleted_smoke_branch_is_refused()
    {
        var run = await Run("session-git-smoke", new()
        {
            ["pushedSha"] = new string('a', 40),
            ["branchDeleted"] = false,
        });
        Diagnosis(run).ShouldBe("SmokeBranchNotDeleted");
        Accepted(run).ShouldBeFalse();
    }

    private static Dictionary<string, object?> Deploy(Action<Dictionary<string, object?>> mutate)
    {
        var extra = new Dictionary<string, object?>
        {
            ["deployKeyPresent"] = true,
            ["phoneHomeSecretPresent"] = true,
            ["restartPolicy"] = "unless-stopped",
            ["daemonName"] = "runner-host",
            ["runnerHostname"] = "runner-host",
        };
        mutate(extra);
        return extra;
    }

    private static Dictionary<string, object?> Nested(Action<Dictionary<string, object?>> mutate)
    {
        var extra = new Dictionary<string, object?>
        {
            ["dispatchEligible"] = true,
            ["sessionOrigin"] = "https://antiphon.desktop.codeperf.net",
            ["hostResidue"] = "",
            ["nestedResidue"] = "",
        };
        mutate(extra);
        return extra;
    }

    private static Dictionary<string, object?> Await(Action<Dictionary<string, object?>> mutate)
    {
        var extra = new Dictionary<string, object?>
        {
            ["sessionState"] = "Idle",
            ["markerSeen"] = true,
            ["subordinates"] = Array.Empty<Dictionary<string, object?>>(),
        };
        mutate(extra);
        return extra;
    }

    private static Dictionary<string, object?> Restart(Action<Dictionary<string, object?>> mutate)
    {
        var extra = new Dictionary<string, object?>
        {
            ["storeIdBefore"] = "store-1",
            ["storeIdAfter"] = "store-1",
            ["imagesRetained"] = true,
        };
        mutate(extra);
        return extra;
    }

    private static Task<C590Run> Run(string caseName, Dictionary<string, object?> extra)
    {
        var manifest = C590Harness.Happy();
        foreach (var pair in extra)
            manifest[pair.Key] = pair.Value;
        return C590Harness.RunAsync("verify-docker-stack.ps1", manifest, args: ["-Case", caseName]);
    }

    private static string Diagnosis(C590Run run) =>
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("diagnosis").GetString() ?? "";

    private static bool Accepted(C590Run run) =>
        JsonDocument.Parse(run.ResultJson).RootElement.GetProperty("accepted").GetBoolean();
}
