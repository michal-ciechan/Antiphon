using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: every scenario captured from
/// scripts/retire-runner-volumes.ps1 and scripts/c1609-manual.ps1 before their deletion (RetireRunnerVolumesCaptures) runs through
/// the retire-runner-volumes command (RetireRunnerVolumes) on RetireRunnerVolumesWorld, a port of the capture's fakes, and must
/// give the same exit, stdout, stderr, call log (ssh's arguments and input shape, scp's arguments, the status request with its
/// token, git's arguments, the verifier's case and manifest) and every directory and file under the evidence roots. Both sides are
/// normalized the same way: each side's temporary root is &lt;DIR&gt; and separators are /, the evidence directory's UTC stamp and
/// nonce are &lt;STAMP&gt; and the manual run id's nonce &lt;ID&gt;, line ends are LF. The packet (c1008-retirement.sh and four
/// c590-remote.sh functions) and its C1609_CLOSE_TOOLING_DIGEST are compared byte for byte while those two files are the ones the
/// capture ran (ToolingSha256), else as &lt;PACKET&gt; and &lt;DIGEST&gt; (RetireRunnerVolumesTests pins the packet against the files).
/// The script threw an error as "THROW &lt;message&gt;"; the command prints "retire-runner-volumes: &lt;message&gt;". On a CurrentTooling
/// refusal the script's stdout held the c590-remote.sh functions it had already taken (PowerShell's exit wrote the pending
/// assignment out); the command prints nothing there.
/// </summary>
[Category("Unit")]
public sealed class RetireRunnerVolumesCaptureTests
{
    [Test]
    public async Task Every_captured_retirement_case_runs_the_same_in_the_command()
    {
        var scenarios = RetireRunnerVolumesCaptures.Scenarios.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        var captured = RetireRunnerVolumesCaptures.Captured.Select(c => JsonNode.Parse(c)!.AsObject()).ToDictionary(c => c["name"]!.GetValue<string>());
        scenarios.Length.ShouldBe(captured.Count);
        var strict = ToolingSha256() == RetireRunnerVolumesCaptures.ToolingSha256;
        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            var name = scenario["name"]!.GetValue<string>();
            var expected = Normalize(captured[name], captured[name]["dir"]!.GetValue<string>(), script: true, strict);
            using var world = new RetireRunnerVolumesWorld();
            var actual = Normalize(Run(world, scenario), world.Root, script: false, strict);
            // The capture keeps ssh's whole input shape for three scenarios; the others compare its SHA-256.
            var expectedCalls = expected["calls"]!.AsArray();
            var actualCalls = actual["calls"]!.AsArray();
            for (var i = 0; i < Math.Min(expectedCalls.Count, actualCalls.Count); i++)
                if (expectedCalls[i] is JsonObject e && !e.ContainsKey("stdin") && actualCalls[i] is JsonObject a) a.Remove("stdin");
            foreach (var key in new[] { "exit", "stdout", "stderr", "calls", "files" })
            {
                var e = expected[key]?.ToJsonString() ?? "null";
                var a = actual[key]?.ToJsonString() ?? "null";
                if (e != a) failures.Add(name + " " + key + "\n  script:  " + e + "\n  command: " + a);
            }
        }
        failures.ShouldBeEmpty(string.Join("\n", failures));
        await Task.CompletedTask;
    }

    /// <summary>The SHA-256 of scripts/c1008-retirement.sh and scripts/c590-remote.sh, joined by a colon.</summary>
    internal static string ToolingSha256() => string.Join(":", new[] { "c1008-retirement.sh", "c590-remote.sh" }
        .Select(n => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(C590Harness.RepoRoot, "scripts", n)))).ToLowerInvariant()));

    /// <summary>Runs one scenario on the world as the capture driver set it up; the result in the capture's shape.</summary>
    internal static JsonObject Run(RetireRunnerVolumesWorld world, JsonObject scenario)
    {
        if (scenario.ContainsKey("token")) world.WriteToken(scenario["token"]?.GetValue<string>());
        else world.WriteToken();
        if (scenario["scriptCut"] is JsonObject cuts)
            foreach (var (file, pattern) in cuts)
            {
                var path = Path.Combine(world.Checkout, "scripts", file);
                File.WriteAllText(path, Regex.Replace(File.ReadAllText(path), pattern!.GetValue<string>(), ""));
            }
        if (scenario["files"] is JsonObject files)
            foreach (var (file, text) in files)
            {
                var path = world.Expand(file, path: true);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, world.Expand(text!.GetValue<string>()));
            }
        // A single rule was written as an object (the capture handler read @($scenario.rules)).
        if (scenario["rules"] is JsonArray rules) foreach (var rule in rules) if (rule is JsonObject r) world.Rules.Add(r);
        if (scenario["rules"] is JsonObject single) world.Rules.Add(single);
        if (scenario["env"] is JsonObject env)
            foreach (var (key, value) in env) world.Variables[key] = value == null ? null : world.Expand(value.GetValue<string>());
        world.Http = scenario["http"] as JsonObject;
        world.VerifyAnswer = scenario["verify"] as JsonObject;
        var bin = scenario["bin"]?.GetValue<string>();
        world.SshMissing = bin == "nossh";
        world.ScpMissing = bin == "noscp";
        var p = scenario["params"]!.AsObject();
        bool B(string key) => p[key]?.GetValue<bool>() == true;
        string S(string key, bool path = false) => p[key] is JsonValue v ? world.Expand(v.GetValue<string>(), path) : "";
        var run = world.Run(new RetireRunnerVolumesOptions
        {
            Close = B("Close"), DryRun = B("DryRun"), Apply = B("Apply"), Preview = B("Preview"), Force = B("Force"), Manual = B("Manual"), Template = B("Template"),
            Resume = B("Resume"), OperationId = S("OperationId"), ExpectedJournalDigest = S("ExpectedJournalDigest"), ReasonFile = S("ReasonFile", true),
            AuthorityFile = S("AuthorityFile", true), Sha = S("Sha"), RetiredRunnerId = S("RetiredRunnerId"), ProjectId = S("ProjectId"),
            VolumeName = p["VolumeName"] is JsonArray names ? names.Select(n => n!.GetValue<string>()).ToArray() : [],
            ExpectedProofDigest = S("ExpectedProofDigest"), AttestationFile = S("AttestationFile", true), EvidenceDirectory = S("EvidenceDirectory", true),
        });
        var found = new JsonArray();
        foreach (var root in new[] { Path.Combine(world.Root, "evidence"), Path.Combine(world.Checkout, ".antiphon") })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                found.Add(new JsonObject { ["path"] = dir[(world.Root.Length + 1)..].Replace('\\', '/') + "/", ["text"] = null });
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                found.Add(new JsonObject { ["path"] = file[(world.Root.Length + 1)..].Replace('\\', '/'), ["text"] = File.ReadAllText(file) });
        }
        return new JsonObject
        {
            ["exit"] = run.Exit, ["stdout"] = run.Output, ["stderr"] = run.Error,
            ["calls"] = new JsonArray(world.Calls.Select(c => (JsonNode)c.DeepClone()).ToArray()), ["files"] = found,
        };
    }

    private static readonly Regex Stamp = new("manual-retire-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{32}");
    private static readonly Regex ManualRun = new("c1609manual[0-9a-f]{32}");
    private static readonly Regex ToolingDigest = new("C1609_CLOSE_TOOLING_DIGEST='[0-9a-f]{64}'");

    /// <summary>One side in the compared shape: exit, stdout, stderr, calls and files.</summary>
    internal static JsonObject Normalize(JsonObject run, string root, bool script, bool strict)
    {
        var escaped = JsonEncodedText.Encode(root).ToString();
        string Text(string value)
        {
            value = value.Replace(escaped, "<DIR>", StringComparison.OrdinalIgnoreCase).Replace(root, "<DIR>", StringComparison.OrdinalIgnoreCase);
            value = Stamp.Replace(value, "manual-retire-<STAMP>");
            value = ManualRun.Replace(value, "c1609manual<ID>");
            if (!strict) value = ToolingDigest.Replace(value, "C1609_CLOSE_TOOLING_DIGEST='<DIGEST>'");
            value = Regex.Replace(value, "<DIR>[^'\"\\s]*", m => m.Value.Replace("\\\\", "/", StringComparison.Ordinal).Replace('\\', '/'));
            return value.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        JsonObject Ssh(JsonObject o)
        {
            if (o["name"]?.GetValue<string>() != "ssh" || o["stdin"] is not JsonValue stdin) return o;
            var shape = Regex.Replace(stdin.GetValue<string>(), "C1609_CLOSE_TOOLING_DIGEST='[0-9a-f]{64}'", "C1609_CLOSE_TOOLING_DIGEST='<DIGEST>'");
            var copy = o.DeepClone().AsObject();
            copy["stdinSha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(shape))).ToLowerInvariant();
            return copy;
        }
        JsonNode? Node(JsonNode? node) => node switch
        {
            JsonObject o => new JsonObject(Ssh(o).Where(e => !(script && e.Key is "noProfile" or "nonInteractive" or "file") && !(!strict && e.Key == "packetSha"))
                .OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => KeyValuePair.Create(e.Key, Node(e.Value)))),
            JsonArray a => new JsonArray(a.Select(Node).ToArray()),
            JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Text(s)),
            _ => node?.DeepClone(),
        };
        var stdout = run["stdout"]!.GetValue<string>();
        if (script && stdout.StartsWith("c1008_volume() {", StringComparison.Ordinal) && run["stderr"]!.GetValue<string>().Contains("Refused: CurrentTooling", StringComparison.Ordinal))
            stdout = "";
        var stderr = run["stderr"]!.GetValue<string>().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (script && stderr.StartsWith("THROW ", StringComparison.Ordinal)) stderr = "retire-runner-volumes: " + stderr["THROW ".Length..];
        return new JsonObject
        {
            ["exit"] = run["exit"]!.GetValue<int>(),
            ["stdout"] = Text(stdout),
            ["stderr"] = Text(stderr),
            ["calls"] = Node(run["calls"]),
            ["files"] = new JsonArray(run["files"]!.AsArray().Select(f => (Path: Text(f!["path"]!.GetValue<string>()), Text: f["text"] == null ? null : Text(f["text"]!.GetValue<string>())))
                .OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => (JsonNode)new JsonObject { ["path"] = f.Path, ["text"] = f.Text }).ToArray()),
        };
    }
}
