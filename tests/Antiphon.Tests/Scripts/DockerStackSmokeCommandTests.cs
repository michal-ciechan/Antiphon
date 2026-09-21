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
