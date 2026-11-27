using System.Text.Json.Nodes;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0737. deploy-parent must not touch bw unless a refresh was asked for, and it must not claim the runner logged out while an
// existing token file is left in place. Unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the live bridge is
// C590Bridge, so these rows run the verify-docker-stack command in process on C590BridgeWorld (fake ssh, scp and bw; a fake home for
// the ~/.bw-session pickup) where they ran scripts/c590-real.ps1 and verify-docker-stack.ps1's head in pwsh with a bw shim.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ClaudeTokenRefreshOptInTests
{
    private const string KeepLine =
        "Claude token: keeping the existing server2 file (no vault refresh requested)";

    private const string RefreshSkipped = "refresh skipped, previous file unchanged";

    private static JsonObject Manifest(params (string Key, JsonNode? Value)[] fields)
    {
        var manifest = new JsonObject { ["sourceSha"] = new string('a', 40), ["runId"] = "c737test" };
        foreach (var (key, value) in fields) manifest[key] = value;
        return manifest;
    }

    private static JsonArray Unreadable() => [new JsonObject { ["name"] = "bw", ["exit"] = 2 }];

    [Test]
    public void Default_path_does_not_call_bw_or_warn_logged_out_when_the_file_is_present()
    {
        using var world = new C590BridgeWorld();
        world.Variables["BW_SESSION"] = "c737-not-a-vault-session";
        var run = world.RunCase("deploy-parent", Manifest(), rules: Unreadable());
        run.Exit.ShouldBe(0, run.Output + run.Error);
        world.Names().ShouldNotContain("bw", run.Output);
        run.Output.ShouldContain(KeepLine);
        run.Output.ShouldNotContain("claudeAuth=logged-out");
        run.Output.ShouldNotContain("ClaudeOAuthTokenUnavailable");
    }

    [Test]
    public void Locked_vault_on_an_explicit_refresh_skips_and_keeps_the_previous_file()
    {
        // No BW_SESSION and no ~/.bw-session pickup.
        using var world = new C590BridgeWorld();
        var run = world.RunCase("deploy-parent", Manifest(), refresh: true, rules: Unreadable());
        run.Exit.ShouldBe(0, run.Output + run.Error);
        world.Names().ShouldNotContain("bw", run.Output);
        run.Output.ShouldContain(RefreshSkipped);
        run.Output.ShouldNotContain("claudeAuth=logged-out");

        // A session, but no bw that resolves.
        using var absent = new C590BridgeWorld { BwPresent = false };
        absent.Variables["BW_SESSION"] = "c737-not-a-vault-session";
        var missing = absent.RunCase("deploy-parent", Manifest(), refresh: true);
        missing.Output.ShouldContain(RefreshSkipped);
        absent.Names().ShouldNotContain("bw");
    }

    [Test]
    public void Manifest_refresh_calls_bw_and_skips_when_the_item_is_unreadable()
    {
        using var world = new C590BridgeWorld();
        File.WriteAllText(Path.Combine(world.Home, ".bw-session"), "  c737-pickup-session \n");
        var run = world.RunCase("deploy-parent", Manifest(("refreshClaudeToken", true)), rules: Unreadable());
        run.Exit.ShouldBe(0, run.Output + run.Error);
        world.Names().ShouldContain("bw", run.Output);
        world.BwSession.ShouldBe("c737-pickup-session", "the trimmed ~/.bw-session pickup reaches bw as its BW_SESSION");
        run.Output.ShouldContain(RefreshSkipped);
        run.Output.ShouldNotContain("claudeAuth=logged-out");
        run.Output.ShouldNotContain(KeepLine);
        world.Remotes().ShouldNotContain(r => r.StartsWith("umask 077", StringComparison.Ordinal), "no token write after an unreadable item");
    }

    [Test]
    [Arguments("verify-switch")]
    [Arguments("verify-env")]
    public void Verify_parent_threads_the_refresh_request_past_the_dot_source(string mode)
    {
        using var world = new C590BridgeWorld();
        world.Variables["BW_SESSION"] = "c737-not-a-vault-session";
        if (mode == "verify-env") world.Variables["ANTIPHON_REFRESH_CLAUDE_TOKEN"] = "1";
        var run = world.RunCase("deploy-parent", Manifest(), refresh: mode == "verify-switch", rules:
        [
            new JsonObject { ["name"] = "bw", ["stdout"] = new JsonArray("SENTINEL_CLAUDE_TOKEN_0123456789"), ["exit"] = 0 },
            new JsonObject { ["name"] = "ssh", ["match"] = "umask 077", ["readStdin"] = true, ["exit"] = 0 },
        ]);
        run.Exit.ShouldBe(0, run.Output + run.Error);
        world.Names().ShouldContain("bw");
        world.Remotes()[0].ShouldBe("umask 077 && mkdir -p /home/mc/antiphon-server2/secrets && cat > '/home/mc/antiphon-server2/secrets/claude_oauth_token.tmp'"
            + " && chmod 0600 '/home/mc/antiphon-server2/secrets/claude_oauth_token.tmp' && mv -f '/home/mc/antiphon-server2/secrets/claude_oauth_token.tmp'"
            + " '/home/mc/antiphon-server2/secrets/claude_oauth_token'");
        world.Calls.Single(c => c["end"] != null)["stdin"]!.GetValue<string>().ShouldBe("SENTINEL_CLAUDE_TOKEN_0123456789" + Environment.NewLine,
            "the token crosses on ssh's input with PowerShell's piped newline, never argv");
        world.Calls.Where(c => c["end"] == null).SelectMany(c => c["args"]!.AsArray()).ShouldNotContain(a => a!.GetValue<string>().Contains("SENTINEL", StringComparison.Ordinal));
        (run.Output + run.Error).ShouldNotContain("SENTINEL");
    }

    [Test]
    public void Deploy_parent_calls_the_gate_and_the_switch_is_threaded()
    {
        var bridge = DockerStackDocuments.Read("scripts/lib/Antiphon.Nightly/C590Bridge.cs");
        var verify = DockerStackDocuments.Read("scripts/lib/Antiphon.Nightly/DockerStackVerify.cs");
        var launcher = DockerStackDocuments.Read("scripts/verify-docker-stack.ps1");
        var remote = DockerStackDocuments.Read("scripts/c590-remote.sh");
        var credentials = DockerStackDocuments.Read("docs/agent-credentials.md");
        var stack = DockerStackDocuments.Read("docs/docker-stack.md");

        var start = bridge.IndexOf("if (RollingPs.Eq(hostCase, \"deploy-parent\")", StringComparison.Ordinal);
        var end = bridge.IndexOf("if (RollingPs.Eq(hostCase, \"git-credential-smoke\"))", StringComparison.Ordinal);
        (start >= 0 && end > start).ShouldBeTrue("deploy-parent block was not found");
        var block = bridge[start..end];
        block.ShouldContain("ClaudeTokenOnDeploy(manifest)");
        block.ShouldNotContain("SendClaudeToken");
        block.ShouldNotContain("BwPassword");

        bridge.ShouldContain(KeepLine);
        bridge.ShouldContain(RefreshSkipped);
        bridge.Contains("claudeAuth=logged-out", StringComparison.Ordinal)
            .ShouldBeFalse("the desktop bridge must not claim the runner logged out");
        bridge.ShouldContain("ANTIPHON_REFRESH_CLAUDE_TOKEN");
        verify.ShouldContain("\"--refresh-claude-token\"");
        launcher.ShouldContain("[switch]$RefreshClaudeToken");
        launcher.ShouldContain("'--refresh-claude-token'");

        var absent = remote.Split('\n').Single(line => line.Contains("WARN ClaudeOAuthTokenAbsent", StringComparison.Ordinal));
        absent.ShouldContain("-RefreshClaudeToken");
        absent.ShouldContain("claudeAuth=logged-out");

        credentials.ShouldContain("-RefreshClaudeToken");
        credentials.ShouldContain("does not need `BW_SESSION`");
        credentials.Contains("at deploy, `scripts/c590-real.ps1` reads it through the relay session", StringComparison.Ordinal)
            .ShouldBeFalse("a deploy is no longer described as always reading the vault");
        stack.ShouldContain("-RefreshClaudeToken");
        stack.Contains("delivers from the vault", StringComparison.Ordinal)
            .ShouldBeFalse("the stack runbook no longer says every deploy delivers the token from the vault");
    }
}
