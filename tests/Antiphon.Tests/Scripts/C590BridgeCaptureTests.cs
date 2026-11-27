using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: every scenario captured from the PowerShell live
/// bridge before its deletion (C590BridgeCaptures) runs through the verify-docker-stack command (DockerStackVerify with the live
/// bridge) on C590BridgeWorld, a port of the capture's fake executables, and must give the same exit, stdout, stderr, fake call
/// log and files. Both sides are normalized the same way: each side's temporary root is &lt;DIR&gt; and separators are /; the private
/// tool stage id is &lt;STAGE&gt; and a generated run id &lt;RUN&gt;; a base64 remote script is decoded; the staged tools' digests and a
/// large upload's digest are &lt;SHA256&gt; (C590BridgeTests pins them against the files); line ends are LF; JSON lines compare with
/// sorted keys (PowerShell's hashtables wrote them in a per-process order) and every *Ms value as 0. The script printed a thrown
/// refusal as PowerShell's error record (the file, line and the message); the command prints "verify-docker-stack: &lt;message&gt;",
/// so the script's stderr is compared as that line. The receipt's failure-line was the PowerShell line and failure-script the
/// .ps1 that threw; the port records 0 and the C# source (C1609Coordinator.cs, C1609HostProof.cs).
/// </summary>
[Category("Unit")]
public sealed class C590BridgeCaptureTests
{
    private static readonly Dictionary<string, string> ScriptSites = new(StringComparer.Ordinal)
    {
        ["c590-real.ps1"] = C1609Coordinator.Site,
        ["c1609-host-proof.ps1"] = C1609HostProof.Site,
        ["c1008-host-proof.ps1"] = C1609HostProof.Site,
    };

    [Test]
    public async Task Every_captured_bridge_case_runs_the_same_in_the_command()
    {
        var scenarios = C590BridgeCaptures.Scenarios.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        var captured = C590BridgeCaptures.Captured.Select(c => JsonNode.Parse(c)!.AsObject()).ToDictionary(c => c["name"]!.GetValue<string>());
        scenarios.Length.ShouldBe(captured.Count);
        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            var name = scenario["name"]!.GetValue<string>();
            var expected = Normalize(captured[name], captured[name]["dir"]!.GetValue<string>(), script: true);
            using var world = new C590BridgeWorld();
            var actual = Normalize(Run(world, scenario), world.Root, script: false);
            foreach (var key in new[] { "exit", "stdout", "stderr", "calls", "ends", "files" })
            {
                var e = expected[key]?.ToJsonString() ?? "null";
                var a = actual[key]?.ToJsonString() ?? "null";
                if (e != a) failures.Add(name + " " + key + "\n  script:  " + e + "\n  command: " + a);
            }
        }
        failures.ShouldBeEmpty(string.Join("\n", failures));
        await Task.CompletedTask;
    }

    /// <summary>Runs one scenario on the world as the capture driver set it up; the result in the capture's shape.</summary>
    internal static JsonObject Run(C590BridgeWorld world, JsonObject scenario)
    {
        var evidence = scenario["outsideEvidence"]?.GetValue<bool>() == true ? Path.Combine(world.Root, "evidence") : Path.Combine(world.Checkout, ".antiphon", "evidence");
        var map = new Dictionary<string, string> { ["$CHECKOUT"] = world.Checkout, ["$EVID"] = evidence, ["$REMOTE"] = world.Remote, ["$DIR"] = world.Root };
        string Expand(string text) => map.Aggregate(text, (t, pair) => t.Replace(pair.Key, pair.Value, StringComparison.Ordinal));
        string ExpandJson(string text) => map.Aggregate(text, (t, pair) => t.Replace(pair.Key, JsonEncodedText.Encode(pair.Value).ToString(), StringComparison.Ordinal));
        if (scenario["remoteEvidence"] is JsonObject remote)
            foreach (var (file, text) in remote)
            {
                var path = Path.Combine(world.Remote, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Expand(text!.GetValue<string>()));
            }
        if (scenario["files"] is JsonObject files)
            foreach (var (file, text) in files)
            {
                var path = Expand(file).Replace('\\', Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Expand(text!.GetValue<string>()));
            }
        world.Rules(scenario["rules"] is JsonNode rules ? JsonNode.Parse(ExpandJson(rules.ToJsonString()))!.AsArray() : null);
        if (scenario["env"] is JsonObject env)
            foreach (var (key, value) in env) world.Variables[key] = value == null ? null : Expand(value.GetValue<string>());
        var manifest = Path.Combine(world.Root, "manifest.json");
        File.WriteAllText(manifest, ExpandJson(scenario["raw"]?.GetValue<string>() ?? scenario["manifest"]!.ToJsonString()));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var options = new DockerStackVerifyOptions
        {
            Case = scenario["case"]!.GetValue<string>(),
            Manifest = manifest,
            RefreshClaudeToken = scenario["refresh"]?.GetValue<bool>() == true,
            Checkout = world.Checkout,
        };
        var exit = DockerStackVerify.Run(options, world.Host(output, error), new FileSystem(), world.Seams());
        var found = new JsonArray();
        foreach (var root in new[] { Path.Combine(world.Checkout, ".antiphon"), evidence }.Distinct())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = file[(world.Root.Length + 1)..].Replace('\\', '/');
                if (found.Any(f => f!["path"]!.GetValue<string>() == relative)) continue;
                found.Add(new JsonObject { ["path"] = relative, ["text"] = File.ReadAllText(file) });
            }
        }
        return new JsonObject
        {
            ["exit"] = exit, ["stdout"] = output.ToString(), ["stderr"] = error.ToString(),
            ["calls"] = new JsonArray(world.Calls.Select(c => (JsonNode)c.DeepClone()).ToArray()), ["files"] = found,
        };
    }

    private static readonly Regex Stage = new("retirement-tools/[0-9a-f]{32}");
    private static readonly Regex Encoded = new("printf '%s' '([A-Za-z0-9+/=]+)' \\| base64 -d \\| bash");
    private static readonly Regex ToolDigest = new("[0-9a-f]{64}  (c590-remote\\.sh|c1008-retirement\\.sh|c1008-content-audit\\.sh|c1008-volume-metadata\\.sh)");
    private static readonly Regex UploadDigest = new("^sha256=[0-9a-f]{64} bytes=[0-9]+$");

    /// <summary>One side in the compared shape: exit, stdout, stderr, calls (start records), ends (end records), files.</summary>
    internal static JsonObject Normalize(JsonObject run, string root, bool script)
    {
        var runIds = new List<string>();
        foreach (var file in run["files"]!.AsArray())
            if (file!["path"]!.GetValue<string>().EndsWith("c590-server2/run-id.txt", StringComparison.Ordinal))
                runIds.Add(file["text"]!.GetValue<string>().Trim());
        string Text(string value)
        {
            value = value.Replace(root, "<DIR>", StringComparison.OrdinalIgnoreCase);
            value = Encoded.Replace(value, m => "printf '%s' '<b64:" + Text(Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value))) + ">' | base64 -d | bash");
            value = Stage.Replace(value, "retirement-tools/<STAGE>");
            value = ToolDigest.Replace(value, "<SHA256>  $1");
            foreach (var id in runIds.Where(i => i.Length > 0 && i != "u66runfile")) value = value.Replace(id, "<RUN>", StringComparison.Ordinal);
            value = Regex.Replace(value, "<DIR>[^'\"\\s]*", m => m.Value.Replace('\\', '/'));
            return value.Replace("\r\n", "\n");
        }
        JsonNode? Node(JsonNode? node) => node switch
        {
            JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
                KeyValuePair.Create(p.Key, p.Key.EndsWith("Ms", StringComparison.Ordinal) && p.Value is JsonValue ? (JsonNode?)JsonValue.Create(0) : Node(p.Value)))),
            JsonArray a => new JsonArray(a.Select(Node).ToArray()),
            JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Text(s)),
            _ => node?.DeepClone(),
        };
        string FileText(string path, string text)
        {
            text = Text(text);
            if (path.EndsWith(".jsonl", StringComparison.Ordinal))
                return string.Join("\n", text.Split('\n').Where(l => l.Length > 0).Select(l => Node(JsonNode.Parse(l))!.ToJsonString())) + "\n";
            if (script && path.EndsWith("remote-output.txt", StringComparison.Ordinal))
            {
                text = Regex.Replace(text, "(?m)^failure-line=[0-9]+$", "failure-line=0");
                text = Regex.Replace(text, "(?m)^failure-script=(.+)$", m => "failure-script=" + (ScriptSites.TryGetValue(m.Groups[1].Value, out var site) ? site : m.Groups[1].Value));
            }
            return text;
        }
        var stderr = run["stderr"]!.GetValue<string>();
        // A fake's own error record (the capture handler could not copy a remote directory that was never made) is not the script's.
        if (script && stderr.Contains("\\fake\\handler.ps1", StringComparison.Ordinal)) stderr = "";
        if (script && stderr.Length > 0)
        {
            var message = Regex.Matches(stderr.Replace("\r\n", "\n"), "(?m)^\\s+\\| (.*)$").LastOrDefault()?.Groups[1].Value.Trim();
            if (message != null) stderr = "verify-docker-stack: " + message + "\n";
        }
        var calls = new JsonArray();
        var ends = new JsonArray();
        foreach (var call in run["calls"]!.AsArray())
        {
            var record = call!.AsObject().DeepClone().AsObject();
            // Unit 68: the bridge stopped copying scripts/c849-import-saved-donor.ps1 for runner-cache-seed (c590-remote.sh runs
            // the c849-import-saved-donor command from server2's host-published CLI); the script's scp of it is the one call dropped.
            if (script && record["name"]?.GetValue<string>() == "scp" && record["args"] is JsonArray copied
                && copied.Any(a => a!.GetValue<string>().EndsWith("c849-import-saved-donor.ps1", StringComparison.Ordinal))) continue;
            if (record["upload"] is JsonValue upload && UploadDigest.IsMatch(upload.GetValue<string>())) record["upload"] = "<SHA256>";
            if (record.ContainsKey("stdinHex"))
            {
                var hex = record["stdinHex"]!.GetValue<string>();
                record.Remove("stdinHex");
                record["stdin"] = hex.Length == 0 ? "" : Encoding.UTF8.GetString(hex.Split('-').Select(h => Convert.ToByte(h, 16)).ToArray());
            }
            (record["end"]?.GetValue<bool>() == true ? ends : calls).Add(Node(record));
        }
        var filesOut = new JsonArray(run["files"]!.AsArray()
            .Select(f => (Path: Text(f!["path"]!.GetValue<string>()), Text: FileText(f["path"]!.GetValue<string>(), f["text"]!.GetValue<string>())))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => (JsonNode)new JsonObject { ["path"] = f.Path, ["text"] = f.Text }).ToArray());
        return new JsonObject
        {
            ["exit"] = run["exit"]!.GetValue<int>(),
            ["stdout"] = Text(run["stdout"]!.GetValue<string>()),
            ["stderr"] = Text(stderr),
            ["calls"] = calls,
            ["ends"] = ends,
            ["files"] = filesOut,
        };
    }
}
