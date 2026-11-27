using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: what RetireRunnerVolumesCaptureTests cannot pin from the
/// capture. The packets carry exactly scripts/c1008-retirement.sh and the four c590-remote.sh functions (the capture compares them as
/// &lt;PACKET&gt; once those files change), Close's tooling digest is that packet's SHA-256, the manual request carries no secret, a
/// transport deadline is JournalUnknown (the capture could not wait 90 s), the c1609-read-packet command prints RollingPs's packet
/// and sends it through the Close transport, and the in-process verifier answers a usage error as the CLI did. In process, on
/// RetireRunnerVolumesWorld; nothing is started.
/// </summary>
[Category("Unit")]
public sealed class RetireRunnerVolumesTests
{
    private const string Operation = "c1008aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string Digest = new('a', 64);

    private static string Script(string name) => File.ReadAllText(Path.Combine(C590Harness.RepoRoot, "scripts", name));

    private static string ExpectedPacket()
    {
        var remote = Script("c590-remote.sh");
        return Script("c1008-retirement.sh") + "\n" + string.Join("\n", RetireRunnerVolumes.PacketFunctions
            .Select(name => Regex.Match(remote, "(?ms)^" + name + "\\(\\) \\{\\n.*?^\\}").Value));
    }

    private static string[] Close(RetireRunnerVolumesWorld world) =>
        ["--close", "--operation-id", Operation, "--expected-journal-digest", Digest, "--reason-file", Path.Combine(world.Root, "reason.txt")];

    [Test]
    public void Close_packet_carries_the_tooling_and_its_digest()
    {
        using var world = new RetireRunnerVolumesWorld();
        world.WriteToken();
        File.WriteAllText(Path.Combine(world.Root, "reason.txt"), "  observed absent\n");
        world.Rules.Add(new JsonObject { ["name"] = "ssh", ["stdout"] = new JsonArray("TempRetirementClosed operation=" + Operation + " idempotent=false") });
        var run = world.Run(RetireRunnerVolumesOptions.Parse(Close(world), world.Checkout));
        run.Exit.ShouldBe(0, run.Error);
        run.Output.ShouldBe("TempRetirementClosed operation=" + Operation + " idempotent=false" + Environment.NewLine);
        var packet = ExpectedPacket();
        var script = RetireRunnerVolumes.CloseScript(Operation, Digest, "observed absent", "operator", "operator-token", packet);
        var encoded = Regex.Match(script, "eval \"\\$\\(printf '%s' '([A-Za-z0-9+/=]+)' \\| base64 -d\\)\"").Groups[1].Value;
        Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).ShouldBe(packet);
        script.ShouldContain("C1609_CLOSE_TOOLING_DIGEST='" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packet))).ToLowerInvariant() + "'");
        script.ShouldStartWith("timeout --kill-after=5s 75s bash <<'C1609_CLOSE_PACKET_EOF'\nset -euo pipefail\n");
        script.ShouldEndWith("c1609_close_host '" + Digest + "' 'operator' 'operator-token' '" + Convert.ToBase64String(Encoding.UTF8.GetBytes("observed absent")) + "'\nC1609_CLOSE_PACKET_EOF\n");
        RetireRunnerVolumes.Packet(Script("c1008-retirement.sh"), "no functions here").ShouldBeNull();
        world.Inputs().Single().ShouldContain("<PACKET>");
    }

    [Test]
    public void Manual_request_names_the_operation_and_carries_no_secret()
    {
        using var world = new RetireRunnerVolumesWorld();
        world.WriteToken();
        var attestation = "{\"actor\":\"operator\",\"authorityRef\":\"operator-token\",\"mode\":\"manual\",\"operationId\":\"" + Operation + "\",\"phase\":\"Preview\",\"reason\":\"fixture\"}";
        File.WriteAllText(Path.Combine(world.Root, "attestation.json"), attestation);
        world.Rules.Add(new JsonObject { ["name"] = "ssh", ["stdout"] = new JsonArray("{\"attestationId\":\"" + new string('b', 64) + "\"}") });
        world.Rules.Add(new JsonObject { ["name"] = "scp", ["match"] = "receipt\\.json", ["writeLast"] = "{\"schema\":2,\"operationId\":\"" + Operation
            + "\",\"mode\":\"manual\",\"project\":\"antiphon-runner-temp\",\"state\":\"Ready\",\"consumed\":false,\"targets\":[\"antiphon-runner-temp_runner-state\",\"antiphon-runner-temp_dind-data\",\"antiphon-runner-temp_runner-tmp\",\"antiphon-runner-temp_work\"]}" });
        world.Rules.Add(new JsonObject { ["name"] = "scp", ["match"] = "proof\\.md", ["writeLast"] = "proof\n" });
        world.Http = new JsonObject { ["body"] = "{\"retiredAt\":\"2026-11-20T00:00:00Z\"}" };
        var run = world.Run(RetireRunnerVolumesOptions.Parse(["--preview", "--manual", "--operation-id", Operation, "--retired-runner-id", "server2-temp",
            "--project-id", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1", "--volume-name", string.Join(",", RetireRunnerVolumes.Targets),
            "--evidence-directory", Path.Combine(world.Root, "evidence"), "--attestation-file", Path.Combine(world.Root, "attestation.json")], world.Checkout));
        run.Exit.ShouldBe(0, run.Error);
        run.Output.ShouldContain("C1609_MANUAL state=Ready operation=" + Operation + " digest=");
        world.Names().ShouldBe(["ssh", "http", "git", "verify", "scp", "scp"]);
        world.Inputs().Single().ShouldContain("request=\"$(printf '%s' '<b64:{\"operationId\":\"" + Operation
            + "\",\"phase\":\"Preview\",\"actor\":\"operator\",\"authorityRef\":\"operator-token\",\"template\":false,\"resume\":false,\"expectedDigest\":\"\",\"attestation\":\"<b64:"
            + attestation + ">\"}>' | base64 -d)\"");
        var calls = string.Join("\n", world.Calls.Where(c => c["name"]!.GetValue<string>() != "http").Select(c => c.ToJsonString()));
        (calls + run.Output + run.Error).ShouldNotContain(RetireRunnerVolumesWorld.Token);
        world.Calls.Single(c => c["name"]!.GetValue<string>() == "http")["token"]!.GetValue<string>().ShouldBe(RetireRunnerVolumesWorld.Token);
        var request = JsonNode.Parse(File.ReadAllText(world.Calls.Single(c => c["name"]!.GetValue<string>() == "verify")["manifestPath"]!.GetValue<string>()))!;
        request["tempRetiredAt"]!.GetValue<string>().ShouldBe("2026-11-20T00:00:00.0000000+00:00");
        request["recycle"]!["dryRun"]!.GetValue<bool>().ShouldBeTrue();
        request["recycle"]!.AsObject().ContainsKey("expectedProofDigest").ShouldBeFalse();
    }

    [Test]
    public void A_transport_deadline_is_journal_unknown_in_close_and_the_manual_entry()
    {
        using var close = new RetireRunnerVolumesWorld { DeadlineSsh = true };
        close.WriteToken();
        File.WriteAllText(Path.Combine(close.Root, "reason.txt"), "observed absent");
        var closed = close.Run(RetireRunnerVolumesOptions.Parse(Close(close), close.Checkout));
        closed.Exit.ShouldBe(2);
        closed.Error.ShouldBe("TempRetirementCloseRefused: JournalUnknown" + Environment.NewLine);
        using var manual = new RetireRunnerVolumesWorld { DeadlineSsh = true };
        manual.WriteToken();
        var refused = manual.Run(RetireRunnerVolumesOptions.Parse(["--preview", "--manual", "--template", "--operation-id", Operation, "--retired-runner-id", "server2-temp",
            "--project-id", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1", "--volume-name", string.Join(",", RetireRunnerVolumes.Targets), "--evidence-directory", Path.Combine(manual.Root, "evidence")],
            manual.Checkout));
        refused.Exit.ShouldBe(2);
        refused.Error.ShouldBe("TempRetirementCloseRefused: JournalUnknown" + Environment.NewLine, "Invoke-Server2Bash's Fail-Close exits before the manual entry's catch");
    }

    [Test]
    public void Read_packet_command_prints_the_rolling_packet_and_sends_it_on_the_close_transport()
    {
        using var world = new RetireRunnerVolumesWorld();
        foreach (var (operation, clearable) in new[] { (Operation, false), ("", true) })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            C1609ReadPacket.Run(new C1609ReadPacketOptions { Operation = operation, Clearable = clearable, Checkout = world.Checkout }, world.Host(output, error), new FileSystem(), world)
                .ShouldBe(0, error.ToString());
            output.ToString().ShouldBe(RollingPs.NewC1609RetirementReadPacket(Script("c1008-retirement.sh"), Script("c590-remote.sh"), operation, clearable));
        }
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            C1609ReadPacket.Run(new C1609ReadPacketOptions { Operation = "foreign", Checkout = world.Checkout }, world.Host(output, error), new FileSystem(), world).ShouldBe(1);
            error.ToString().ShouldBe("c1609-read-packet: RecycleContextInvalid" + Environment.NewLine);
        }
        world.Calls.ShouldBeEmpty("printing contacts nothing");
        world.Rules.Add(new JsonObject { ["name"] = "ssh", ["stdout"] = new JsonArray("{\"operationId\":\"" + Operation + "\",\"digest\":\"" + Digest + "\",\"state\":\"Ready\",\"mode\":\"scripted\"}") });
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            C1609ReadPacket.Run(new C1609ReadPacketOptions { Operation = Operation, Send = true, Checkout = world.Checkout }, world.Host(output, error), new FileSystem(), world).ShouldBe(0, error.ToString());
            output.ToString().ShouldBe("{\"operationId\":\"" + Operation + "\",\"digest\":\"" + Digest + "\",\"state\":\"Ready\",\"mode\":\"scripted\"}\n" + Environment.NewLine);
            world.Calls.Single()["args"]!.AsArray().Select(a => a!.GetValue<string>()).ShouldBe(["-o", "BatchMode=yes", "-o", "ConnectTimeout=15", "mc@server2", "bash", "-s"]);
        }
        world.Rules.Clear();
        world.Rules.Add(new JsonObject { ["name"] = "ssh", ["stderr"] = new JsonArray("TempRetirementCloseRefused: Daemon"), ["exit"] = 2 });
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            C1609ReadPacket.Run(new C1609ReadPacketOptions { Operation = Operation, Send = true, Checkout = world.Checkout }, world.Host(output, error), new FileSystem(), world).ShouldBe(2);
            error.ToString().ShouldBe("TempRetirementCloseRefused: Daemon" + Environment.NewLine);
        }
    }

    [Test]
    public void The_in_process_verifier_answers_a_usage_error_as_the_cli_did()
    {
        using var world = new RetireRunnerVolumesWorld();
        using var output = new StringWriter();
        using var error = new StringWriter();
        DockerStackVerify.RunInProcess("retire-temp-runner", Path.Combine(world.Root, "absent.json"), world.Checkout, world.Host(output, error), new FileSystem(), null).ShouldBe(2);
        error.ToString().ShouldStartWith("usage: antiphon-scripts verify-docker-stack: Manifest is required");
    }
}
