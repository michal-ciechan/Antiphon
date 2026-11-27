using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the live server2 bridge (C590Bridge, which replaced
/// scripts/c590-real.ps1's live cases) in process, on C590BridgeWorld: the CARD-1609 private tool stage and its seal over the
/// checkout's own tools, the live roster, a live case without the stub reaching the bridge, and the deploy-server2 host case running
/// the verifier in its own process. The Claude token opt-in and the retirement instant keep their classes
/// (ClaudeTokenRefreshOptInTests, RetireTempRunnerTimestampTests); every captured case is C590BridgeCaptureTests.
/// </summary>
[Category("Unit")]
public sealed class C590BridgeTests
{
    private static JsonObject Manifest() => new() { ["sourceSha"] = new string('a', 40), ["runId"] = "c66test" };

    [Test]
    public void Staged_tools_are_sealed_with_their_own_digests_in_order()
    {
        using var world = new C590BridgeWorld();
        var run = world.RunCase("deploy-temp-runner", Manifest());
        run.Exit.ShouldBe(0, run.Output + run.Error);
        var starts = world.Calls.Where(c => c["end"] == null).ToArray();
        starts.Select(c => c["name"]!.GetValue<string>()).ShouldBe(["ssh", "scp", "scp", "scp", "scp", "ssh", "ssh", "scp"]);
        var uploads = starts[1..5];
        uploads.Select(u => Path.GetFileName(u["args"]!.AsArray()[4]!.GetValue<string>())).ShouldBe(C590Bridge.StagedTools);
        var target = uploads[0]["args"]!.AsArray()[5]!.GetValue<string>();
        target.ShouldStartWith("mc@server2:/home/mc/antiphon-c590/retirement-tools/");
        var dir = target["mc@server2:".Length..target.LastIndexOf('/')];
        uploads.ShouldAllBe(u => u["args"]!.AsArray()[5]!.GetValue<string>() == "mc@server2:" + dir + "/" + Path.GetFileName(u["args"]!.AsArray()[4]!.GetValue<string>()));
        var rows = C590Bridge.StagedTools.Select(name =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(world.Checkout, "scripts", name)))).ToLowerInvariant() + "  " + name);
        var seal = "set -eu\ncd '" + dir + "'\nprintf '%s\\n' '" + string.Join("\n", rows) + "' > tooling.sha256\nsha256sum -c tooling.sha256 >/dev/null\nchmod 0400 -- ./*.sh tooling.sha256\n";
        var remotes = world.Remotes();
        remotes[1].ShouldBe("printf '%s' '" + Convert.ToBase64String(Encoding.UTF8.GetBytes(seal)) + "' | base64 -d | bash");
        remotes[2].ShouldEndWith("; bash '" + dir + "/c590-remote.sh'");
        var prepare = Encoding.UTF8.GetString(Convert.FromBase64String(remotes[0]["printf '%s' '".Length..remotes[0].IndexOf("' | base64", StringComparison.Ordinal)]));
        prepare.ShouldEndWith("\nmkdir -- '" + dir + "'");
        prepare.ShouldStartWith("set -eu\numask 077\nparent=/home/mc/antiphon-c590/retirement-tools\n");
    }

    [Test]
    public void Live_roster_is_the_bridge_roster_in_its_order()
    {
        DockerStackVerify.LiveCases.ShouldBe([
            "deploy-parent", "deploy-temp-runner", "retire-temp-runner", "retire-temp-containers", "runner-cache-inventory", "runner-cache-fixture",
            "runner-cache-seed", "runner-cache-reset", "verify-runner-caches", "verify-runner-caches-retired", "runner-cache-prune-preview", "runner-cache-prune",
            "nested-residue", "persistent-restart", "server2-independent-handoff", "git-credential-smoke", "baseline-build-failure", "server-image-payload",
            "runner-image-payload", "testing-runner-payload", "custody-containment", "parent-native-and-command-session", "child-server-image-payload",
            "child-runner-image-payload", "test-image-context-and-tools", "receipt-runner-image-payload", "fixture-image-payload", "deployment-state", "client-lint",
            "client-tests", "messaging-tests", "dotnet-filter", "session-result-export-and-child-cleanup", "session-denied-socket", "interrupted-export-cleanup",
            "stock-idle", "stock-busy", "insert-refused", "insert-committed-idle", "insert-committed-busy", "attempt-committed", "body-before-enter",
            "recipient-before-ingestion", "transcript-save-fails-release", "transcript-save-fails-restart", "receipt-before-verdict", "response-before-client",
            "receipt-before-manifest", "changed-generation", "failure-summary", "runtime-context-engine", "test-context-engine", "throwaway-all",
        ]);
    }

    [Test]
    [Arguments("deploy-parent", true)]
    [Arguments("Custody-Containment", false)]
    [Arguments("failure-summary", false)]
    public void A_live_case_without_the_stub_runs_the_bridge(string caseName, bool staged)
    {
        using var world = new C590BridgeWorld();
        var run = world.RunCase(caseName, Manifest());
        run.Exit.ShouldBe(0, run.Output + run.Error);
        run.Output.ShouldEndWith("DIAGNOSIS=c780-fake-remote" + Environment.NewLine);
        var remotes = world.Remotes();
        if (staged) remotes[0].ShouldStartWith("printf '%s' '");
        else remotes[0].ShouldBe("mkdir -p /home/mc/antiphon-c590");
        remotes.Last().ShouldContain("export C590_CASE='" + caseName + "'", Case.Sensitive, "the case crosses as it was given");
    }

    [Test]
    public void Rolling_host_case_runs_the_verifier_in_process()
    {
        using var world = new C590BridgeWorld();
        var manifest = Path.Combine(world.Root, "manifest.json");
        var evidence = Path.Combine(world.Root, "evidence");
        File.WriteAllText(manifest, new JsonObject { ["evidenceRoot"] = evidence, ["observedRevision"] = "wrong", ["sourceSha"] = "abc" }.ToJsonString());
        var os = new Server2RollingOs(world.Checkout, new Dictionary<string, string> { ["ANTIPHON_C590_STUB"] = "1", ["ANTIPHON_C590_SINK"] = Path.Combine(world.Root, "sink.jsonl") });
        using var output = new StringWriter();
        using var error = new StringWriter();
        os.HostCase("version-check", manifest, output, error).ShouldBe(2, error.ToString());
        output.ToString().ShouldBe("DIAGNOSIS=RevisionMismatch" + Environment.NewLine);
        File.ReadAllText(Path.Combine(evidence, "c590-result.json")).ShouldBe("{\"accepted\":false,\"diagnosis\":\"RevisionMismatch\",\"exit\":2}" + Environment.NewLine);
        // The head runs in the same process: a cleanup context on another case is refused before the case.
        File.WriteAllText(manifest, "{\"evidenceRoot\":" + JsonSerializer.Serialize(evidence) + ",\"tempContainerCleanup\":{\"version\":1}}");
        using var refusedOut = new StringWriter();
        using var refusedError = new StringWriter();
        os.HostCase("version-check", manifest, refusedOut, refusedError).ShouldBe(1);
        refusedError.ToString().ShouldBe("verify-docker-stack: TempContainerContextInvalid" + Environment.NewLine);
        using var missingOut = new StringWriter();
        using var missingError = new StringWriter();
        os.HostCase("version-check", manifest + ".gone", missingOut, missingError).ShouldBe(2);
        missingError.ToString().ShouldContain("Manifest is required");
        var source = File.ReadAllText(Path.Combine(C590Harness.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "Server2RollingOs.cs"));
        source.ShouldNotContain("\"pwsh\"");
        source.ShouldContain("DockerStackVerify.Run(");
    }
}
