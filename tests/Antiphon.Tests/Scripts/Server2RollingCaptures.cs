using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 65: what scripts/deploy-server2.ps1 did, captured before its deletion (under pwsh 7.6.10 against its offline fakes, the
/// drivers' C727_TEST_* timings, a stub git and no ssh on PATH), and the comparison of an in-process run with it. A capture holds
/// the scenario (the driver's Run-C727 state and overrides, or a whole state), the arguments in kebab case, the environment
/// overrides, and the script's exit, stdout and stderr (a ConciseView error reduced to its message), its fake trace as signatures
/// and the evidence files it wrote (JSON in sorted-key form; the fixture-only manifests left out). Run ids, operation ids, the
/// evidence root, timestamps and elapsed times are compared normalised; consecutive runner status polls collapse to their runners
/// (the script polled on the wall clock, the command on a fake one).
/// </summary>
internal static partial class Server2RollingCaptures
{
    internal sealed record Capture(string Name, string Scenario, string Phase, string Set, bool WholeState, bool Token, string[] Args, string Env,
        int Exit, string Stdout, string Stderr, string[] Trace, string[] Files)
    {
        public override string ToString() => Name;
    }

    /// <summary>The world for a capture: its state, its token file and its environment.</summary>
    public static Server2RollingWorld World(Capture capture)
    {
        var set = JsonNode.Parse(capture.Set) as JsonObject ?? new JsonObject();
        var state = capture.WholeState ? set : Server2RollingWorld.DriverState(capture.Scenario, capture.Phase, set);
        var world = new Server2RollingWorld(state);
        if (!capture.Token) File.Delete(world.TokenPath);
        var env = JsonNode.Parse(capture.Env)!.AsObject();
        foreach (var (key, value) in env)
        {
            if (key.StartsWith("STUB_GIT_", StringComparison.Ordinal)) continue;
            world.Variables[key] = value?.GetValue<string>();
        }
        if (env["STUB_GIT_HEAD"] is JsonNode head)
        {
            var exit = env["STUB_GIT_EXIT"] is JsonNode code ? int.Parse(code.GetValue<string>()) : 0;
            world.GitAnswer = _ => new Antiphon.Nightly.Server2RollingChild(exit, head.GetValue<string>() + Environment.NewLine, "");
        }
        return world;
    }

    private static readonly Regex Evidence = new(@"[A-Za-z]?:?[^""\s]*?[\\/]+\.antiphon[\\/]+rolling-server2[\\/]+c727[0-9a-f]{12}");

    /// <summary>A text with the run's variable parts replaced.</summary>
    public static string Normalize(string text)
    {
        text = (text ?? "").Replace("\r\n", "\n");
        text = Evidence.Replace(text, "<evidence>");
        text = Regex.Replace(text, "c727[0-9a-f]{12}", "<run>");
        text = Regex.Replace(text, "c1008[0-9a-f]{32}", "<c1008>");
        text = Regex.Replace(text, "c994[0-9a-f]{32}", "<c994>");
        text = Regex.Replace(text, @"elapsedMs=\d+", "elapsedMs=<n>");
        text = Regex.Replace(text, @"""elapsedMs"":\d+", "\"elapsedMs\":0");
        text = Regex.Replace(text, @"""observedAtUtc"":""[^""]*""", "\"observedAtUtc\":\"<t>\"");
        text = Regex.Replace(text, @"path-length export-root=(\d+) longest-derived=(\d+)",
            m => "path-length export-root=<root> longest-derived=<root>+" + (int.Parse(m.Groups[2].Value) - int.Parse(m.Groups[1].Value)));
        return text;
    }

    /// <summary>A trace row's signature (the generator's Signature).</summary>
    public static string Signature(JsonObject row)
    {
        string S(string name) => row[name] is JsonValue v ? v.ToString() : "";
        switch (S("kind"))
        {
            case "http":
                if (S("path").Length > 0) return "read " + S("path");
                return "http " + S("method") + " " + S("runnerId") + S("suffix") + (row["body"] is JsonNode body ? " " + Sorted(body).ToJsonString() : "");
            case "case":
                return "case " + S("name") + (S("runnerId").Length > 0 ? " runner=" + S("runnerId") : "")
                    + (row.ContainsKey("dryRun") ? " dryRun=" + S("dryRun").ToLowerInvariant() : "") + (S("savedDonor").Length > 0 ? " donor=" + S("savedDonor") : "");
            case "prerequisite": return "jq " + S("phase") + " " + S("mode") + " " + S("outcome");
            case "census": return "census " + S("name");
            case "lock-request": return "lock";
            case "proof-read": return "proof-read";
            case "registration": return "registration " + S("result");
            case "marker": return "marker " + S("name") + " " + S("exit");
            default: return "other " + S("kind");
        }
    }

    /// <summary>The signatures with each block of consecutive runner status polls reduced to its distinct rows in order.</summary>
    public static string[] Collapse(IEnumerable<string> rows)
    {
        var output = new List<string>();
        var block = new List<string>();
        foreach (var row in rows.Append("<end>"))
        {
            if (Regex.IsMatch(row, "^http GET [^ ]+/status$")) { if (!block.Contains(row)) block.Add(row); continue; }
            output.AddRange(block);
            block.Clear();
            if (row != "<end>") output.Add(row);
        }
        return output.ToArray();
    }

    public static JsonNode Sorted(JsonNode node)
    {
        if (node is JsonObject o)
        {
            var result = new JsonObject();
            foreach (var pair in o.OrderBy(p => p.Key, StringComparer.Ordinal)) result[pair.Key] = pair.Value == null ? null : Sorted(pair.Value.DeepClone());
            return result;
        }
        if (node is JsonArray a)
        {
            var result = new JsonArray();
            foreach (var item in a) result.Add(item == null ? null : Sorted(item.DeepClone()));
            return result;
        }
        return node.DeepClone();
    }

    /// <summary>The evidence files of the run (the fake's own retire-temp-containers receipts left out), as the generator wrote them.</summary>
    public static string[] Files(Server2RollingWorld world, string runId)
    {
        var root = Path.Combine(world.EvidenceRoot, runId);
        if (runId.Length == 0 || !Directory.Exists(root)) return [];
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith("retire-temp-containers/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                var text = File.ReadAllText(Path.Combine(root, path));
                string norm;
                if (path.EndsWith(".json", StringComparison.Ordinal))
                {
                    try { norm = JsonNode.Parse(text) is JsonNode n ? Sorted(n).ToJsonString() : "null"; }
                    catch (System.Text.Json.JsonException) { norm = text.Trim(); }
                }
                else norm = text.Replace("\r\n", "\n");
                return path + " " + norm;
            }).ToArray();
    }
}
