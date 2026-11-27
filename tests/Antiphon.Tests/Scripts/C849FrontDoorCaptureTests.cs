using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 67 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: every scenario captured from
/// scripts/verify-card0849-caches.ps1 before its deletion (C849FrontDoorCaptures) runs through the verify-card0849-caches command
/// (C849CacheFrontDoor) on C849FrontDoorWorld, a port of the capture's function fakes of git and the verifier, and must give the
/// same exit, stdout, stderr, call log (git's arguments; each verifier case, manifest path and manifest text), the front door's own
/// files (the manifests) and every directory it made under .antiphon. Both sides are normalized the same way: each side's temporary
/// root is &lt;DIR&gt; and separators are /, the run id is &lt;RUN&gt;, line ends are LF. The script threw its refusals ("THROW
/// &lt;message&gt;" in the capture); the command prints "verify-card0849-caches: &lt;message&gt;". An unreadable JSON receipt keeps only
/// the "Conversion from JSON failed with error:" lead-in (the reader's own words differ).
/// </summary>
[Category("Unit")]
public sealed class C849FrontDoorCaptureTests
{
    [Test]
    public async Task Every_captured_front_door_case_runs_the_same_in_the_command()
    {
        var scenarios = C849FrontDoorCaptures.Scenarios.Select(s => JsonNode.Parse(s)!.AsObject()).ToArray();
        var captured = C849FrontDoorCaptures.Captured.Select(c => JsonNode.Parse(c)!.AsObject()).ToDictionary(c => c["name"]!.GetValue<string>());
        scenarios.Length.ShouldBe(captured.Count);
        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            var name = scenario["name"]!.GetValue<string>();
            var expected = Normalize(captured[name], captured[name]["dir"]!.GetValue<string>(), script: true);
            using var world = new C849FrontDoorWorld();
            var actual = Normalize(Run(world, scenario), world.Root, script: false);
            foreach (var key in new[] { "exit", "stdout", "stderr", "calls", "files", "dirs" })
            {
                var e = expected[key]?.ToJsonString() ?? "null";
                var a = actual[key]?.ToJsonString() ?? "null";
                if (e != a) failures.Add(name + " " + key + "\n  script:  " + e + "\n  command: " + a);
            }
        }
        failures.ShouldBeEmpty(string.Join("\n", failures));
        await Task.CompletedTask;
    }

    /// <summary>Runs one scenario on the world as the capture driver did; the result in the capture's shape.</summary>
    internal static JsonObject Run(C849FrontDoorWorld world, JsonObject scenario)
    {
        var given = new List<string>();
        if (scenario["files"] is JsonObject files)
            foreach (var (file, text) in files)
            {
                var path = world.Expand(file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text!.GetValue<string>());
                given.Add(Path.GetFullPath(path));
            }
        if (scenario.ContainsKey("head")) world.GitOutput = scenario["head"]?.GetValue<string>();
        if (scenario["gitExit"] is JsonValue gitExit) world.GitExit = gitExit.GetValue<int>();
        if (scenario["env"] is JsonObject env)
            foreach (var (key, value) in env) world.Variables[key] = value?.GetValue<string>();
        foreach (var verify in scenario["verify"]!.AsArray()) world.Verifies.Add(verify!.AsObject());
        var p = scenario["params"]!.AsObject();
        string S(string key) => p[key] is JsonValue v ? world.Expand(v.GetValue<string>()) : "";
        var run = world.Run(S("Case"), p.ContainsKey("Sha") ? S("Sha") : "", S("Preview"), p["SavedDonor"]?.GetValue<string>() ?? "", p["Cold"]?.GetValue<bool>() == true);
        var found = new JsonArray();
        var dirs = new JsonArray();
        var antiphon = Path.Combine(world.Checkout, ".antiphon");
        if (Directory.Exists(antiphon))
        {
            foreach (var file in Directory.GetFiles(antiphon, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = file[(world.Root.Length + 1)..].Replace('\\', '/');
                if (given.Contains(Path.GetFullPath(file)) || Regex.IsMatch(relative, "^repo/\\.antiphon/c849-c849[0-9a-f]{16}/\\d+/[^/]+/")) continue;
                found.Add(new JsonObject { ["path"] = relative, ["text"] = File.ReadAllText(file) });
            }
            foreach (var dir in Directory.GetDirectories(antiphon, "*", SearchOption.AllDirectories)) dirs.Add(dir[(world.Root.Length + 1)..].Replace('\\', '/'));
        }
        return new JsonObject
        {
            ["exit"] = run.Exit, ["stdout"] = run.Output, ["stderr"] = run.Error,
            ["calls"] = new JsonArray(world.Calls.Select(c => (JsonNode)c.DeepClone()).ToArray()), ["files"] = found, ["dirs"] = dirs,
        };
    }

    private static readonly Regex RunId = new("c849-(c849[0-9a-f]{16})");

    /// <summary>One side in the compared shape: exit, stdout, stderr, calls, files and dirs.</summary>
    internal static JsonObject Normalize(JsonObject run, string root, bool script)
    {
        var escaped = JsonEncodedText.Encode(root).ToString();
        var runIds = run["dirs"]!.AsArray().Select(d => RunId.Match(d!.GetValue<string>())).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToArray();
        string Text(string value)
        {
            value = value.Replace(escaped, "<DIR>", StringComparison.OrdinalIgnoreCase).Replace(root, "<DIR>", StringComparison.OrdinalIgnoreCase);
            foreach (var id in runIds) value = value.Replace(id, "<RUN>", StringComparison.Ordinal);
            value = Regex.Replace(value, "<DIR>[^'\"\\s]*", m => m.Value.Replace("\\\\", "/", StringComparison.Ordinal).Replace('\\', '/'));
            return value.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        JsonNode? Node(JsonNode? node) => node switch
        {
            JsonObject o => new JsonObject(o.Where(e => !(script && e.Key is "noProfile" or "nonInteractive" or "file")).OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => KeyValuePair.Create(e.Key, Node(e.Value)))),
            JsonArray a => new JsonArray(a.Select(Node).ToArray()),
            JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Text(s)),
            _ => node?.DeepClone(),
        };
        var stderr = run["stderr"]!.GetValue<string>().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (script && stderr.StartsWith("THROW ", StringComparison.Ordinal)) stderr = "verify-card0849-caches: " + stderr["THROW ".Length..];
        stderr = Regex.Replace(stderr, "(Conversion from JSON failed with error:).*", "$1");
        return new JsonObject
        {
            ["exit"] = run["exit"]!.GetValue<int>(),
            ["stdout"] = Text(run["stdout"]!.GetValue<string>()),
            ["stderr"] = Text(stderr),
            ["calls"] = Node(run["calls"]),
            // The manifests' text is each verifier call's manifest (the fake read the file), so the capture keeps their paths.
            ["files"] = new JsonArray(run["files"]!.AsArray().Select(f => Text(f is JsonObject o ? o["path"]!.GetValue<string>() : f!.GetValue<string>()))
                .Order(StringComparer.Ordinal).Select(f => (JsonNode)JsonValue.Create(f)).ToArray()),
            ["dirs"] = new JsonArray(run["dirs"]!.AsArray().Select(d => Text(d!.GetValue<string>())).Order(StringComparer.Ordinal).Select(d => (JsonNode)JsonValue.Create(d)).ToArray()),
        };
    }
}
