using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Scripts;

// TUnit owns one immutable seed per test session. No mutable Docker state or
// proof is shared; each fault vector receives new JSON nodes and fixture files.
internal sealed class C1008ComposeSeed
{
    private readonly string _root;
    private readonly string _models;
    private readonly Dictionary<string, string> _files;

    internal C1008ComposeSeed()
    {
        using var setup = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "prepare");
        _root = Directory.CreateTempSubdirectory("c1008-compose-").FullName;
        try
        {
            var models = new JsonObject();
            foreach (var project in new[] { "antiphon-runner", "antiphon-runner-temp" })
            foreach (var previous in new[] { false, true })
            {
                var psi = new ProcessStartInfo("node")
                {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    RedirectStandardError = true, WorkingDirectory = DelegateScriptRunner.RepoRoot
                };
                foreach (var arg in new[] { "scripts/fixtures/c994-production-compose-model.mjs", _root, project,
                             project == "antiphon-runner-temp" ? "true" : "false" }) psi.ArgumentList.Add(arg);
                if (previous) psi.ArgumentList.Add("previous");
                string output;
                using (TestTiming.BeginProcess("node", psi.ArgumentList.Count, "cli"))
                using (var child = Process.Start(psi)!)
                {
                    var stdout = child.StandardOutput.ReadToEndAsync();
                    var stderr = child.StandardError.ReadToEndAsync();
                    if (!child.WaitForExit(30000))
                    {
                        child.Kill(true);
                        child.WaitForExit();
                        throw new InvalidOperationException("Compose config timeout");
                    }
                    if (child.ExitCode != 0)
                        throw new InvalidOperationException("Compose config: " + stderr.GetAwaiter().GetResult());
                    output = stdout.GetAwaiter().GetResult();
                    stderr.GetAwaiter().GetResult();
                }
                var resolved = JsonNode.Parse(output)!["model"]!.DeepClone();
                // Preserve the old deterministic fake-down order.
                var declarations = resolved["volumes"]!.AsObject();
                var ordered = new JsonObject();
                foreach (var role in new[] { "work", "runner-tmp", "dind-data", "runner-state" })
                    ordered[role] = declarations[role]!.DeepClone();
                foreach (var (key, value) in declarations)
                    if (!ordered.ContainsKey(key)) ordered[key] = value?.DeepClone();
                resolved["volumes"] = ordered;
                models[project + (previous ? "@" + C1008HostFixture.PreviousSha12 : "")] = resolved;
            }
            _models = models.ToJsonString();
            _files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(_root, path), File.ReadAllText);
        }
        finally { Directory.Delete(_root, recursive: true); }
    }

    internal JsonObject CloneInto(string root)
    {
        foreach (var (relative, text) in _files)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text.Replace(_root, root, StringComparison.Ordinal));
        }
        // Replace decoded values so quotes/backslashes in a fixture root cannot
        // turn path substitution into JSON syntax or retain another vector's paths.
        var models = JsonNode.Parse(_models)!.AsObject();
        Rebind(models, root);
        foreach (var directory in new[] { "codex", "grok", "github-token" })
            Directory.CreateDirectory(Path.Combine(root, directory));
        return models;
    }

    private void Rebind(JsonNode node, string root)
    {
        if (node is JsonObject obj)
            foreach (var (key, value) in obj.ToArray())
            {
                if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
                    obj[key] = text.Replace(_root, root, StringComparison.Ordinal);
                else if (value is not null) Rebind(value, root);
            }
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue scalar && scalar.TryGetValue<string>(out var text))
                    array[i] = text.Replace(_root, root, StringComparison.Ordinal);
                else if (array[i] is { } value) Rebind(value, root);
            }
    }
}
