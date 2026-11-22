using System.Globalization;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1313: provider and shared ownership, runtime edges and catalog graph refusals of the selection proposal, in-process
/// on C1313SelectionRepository (moved from scripts/test-test-selection.ps1 by the 2026-11-22 plan, unit 3).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionDependencyTests
{
    [Test]
    public void C1313_ProviderAndShared()
    {
        using var fx = C1313SelectionRepository.Create();
        var bad = new List<string>();
        var sharedBad = new List<string>();
        string[] providers = ["claude", "codex", "grok"];
        foreach (var provider in providers)
        {
            var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(provider);
            var exact = fx.Select(fx.Candidate(["src/Antiphon.Agents.Pty/" + title + "Detectors.cs"]));
            string[] expected = ["core", "provider." + provider + ".offline", "provider." + provider + ".fake-native", "provider." + provider + ".tui-stub",
                "provider." + provider + ".tui-live", "provider." + provider + ".vendor-offline", "provider." + provider + ".server-runner", "main.remainder", "e2e.remainder"];
            if (!C1313.Same(C1313.SetIds(exact), expected) || !C1313.Dicts(exact["changedPaths"]).SelectMany(p => C1313.Strings(p["ruleIds"])).Contains("P-" + provider.ToUpperInvariant()))
                bad.Add(provider);
            var shared = fx.Select(fx.Candidate(["src/Antiphon.Agents.Pty/" + title + "NewShared.cs"]));
            foreach (var p in providers) if (!C1313.SetIds(shared).Contains("provider." + p + ".tui-live")) sharedBad.Add(p);
        }
        bad.ShouldBeEmpty("C1313 provider-exact");
        var runner = fx.Select(fx.Candidate(["src/Antiphon.Agents.Pty/PtyAgentRunner.cs"]));
        foreach (var p in providers) if (!C1313.SetIds(runner).Contains("provider." + p + ".offline")) sharedBad.Add(p);
        var unknown = fx.Select(fx.Candidate(["src/Antiphon.Agents.PtyExtra/Unknown.cs"]));
        (sharedBad.Count == 0 && unknown["mode"] as string == "full").ShouldBeTrue("C1313 shared-all-providers: " + string.Join(",", sharedBad));

        var catalog = C1313SelectionRepository.CatalogJson();
        catalog["rules"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "P-OVERLAP", ["paths"] = new JsonArray("src/Antiphon.Agents.Pty/ClaudeDetectors.cs"), ["sets"] = new JsonArray("core"), ["full"] = true,
        });
        fx.PolicyCandidate(catalog);
        var overlap = fx.Select(fx.CommitStaged("src/Antiphon.Agents.Pty/ClaudeDetectors.cs", "overlap"));
        (overlap["mode"] as string == "full" && C1313.Strings(overlap["fallbackReasons"]).Contains("rule-full:P-OVERLAP")
            && C1313.Dicts(overlap["changedPaths"]).SelectMany(p => C1313.Strings(p["ruleIds"])).Contains("P-CLAUDE")).ShouldBeTrue("C1313 full-dominates");
    }

    [Test]
    public void C1313_RuntimeEdges()
    {
        using var fx = C1313SelectionRepository.Create();
        foreach (var (path, edge, set, label) in new[]
        {
            ("tests/Antiphon.Messaging.Tests/FakeSlack/FakeSlackServer.cs", "linked-slack", "e2e.remainder", "linked-consumer"),
            ("tests/Antiphon.Checkpoints.FilterHost/FilterHostTests.cs", "staged-filter-host", "main.remainder", "staged-consumer"),
            ("scripts/lib/nightly-common.ps1", "helper-nightly", "scripts", "helper-consumer"),
            ("client/dist/index.html", "bundle-client", "e2e.browser", "bundle-consumer"),
        })
        {
            var r = fx.Select(fx.Candidate([path]));
            (C1313.SetIds(r).Contains(set) && C1313.Dicts(r["explanationEdges"]).Any(e => e["id"] as string == edge)).ShouldBeTrue("C1313 " + label);
        }
        // Rule boundaries reach the selector, not just a regular-expression helper.
        C1313.Dicts(fx.Select(fx.Candidate(["src/Antiphon.FakeLlmApi/FakeLlmApiServer.cs"]))["changedPaths"]).SelectMany(p => C1313.Strings(p["ruleIds"]))
            .ShouldContain("P-FAKE", "fake consumer");
        foreach (var path in new[] { "src/Antiphon.FakeOther/Program.cs", "tests/UnmappedSharedHelper.cs" })
            fx.Select(fx.Candidate([path]))["mode"].ShouldBe("full", "unknown helper " + path);
    }

    [Test]
    public void C1313_InvalidGraph()
    {
        using var fx = C1313SelectionRepository.Create();
        fx.Select(fx.B)["mode"].ShouldBe("narrow");
        var results = new Dictionary<string, string>();
        foreach (var (variant, reason) in new[]
        {
            ("missing-set", "missing-dependency"), ("cycle", "dependency-cycle"), ("missing-prerequisite", "missing-prerequisite"),
            ("prerequisite-cycle", "dependency-cycle"), ("bad-regex", "invalid-rule"), ("script", "invalid-catalog-field:command"),
            ("regex-timeout", "invalid-rule-timeout"),
        })
        {
            var catalog = C1313SelectionRepository.CatalogJson();
            var sets = catalog["sets"]!.AsArray();
            var prerequisites = catalog["prerequisites"]!.AsArray();
            var rules = catalog["rules"]!.AsArray();
            switch (variant)
            {
                case "missing-set": sets[0]!["dependsOn"] = new JsonArray("missing"); break;
                case "cycle": sets[0]!["dependsOn"] = new JsonArray((string?)sets[0]!["id"]); break;
                case "missing-prerequisite": sets[0]!["prerequisites"] = new JsonArray("missing"); break;
                case "prerequisite-cycle":
                    prerequisites[0]!["dependsOn"] = new JsonArray((string?)prerequisites[1]!["id"]);
                    prerequisites[1]!["dependsOn"] = new JsonArray((string?)prerequisites[0]!["id"]);
                    break;
                case "bad-regex": rules.Add(new JsonObject { ["id"] = "bad", ["pattern"] = "^([$", ["sets"] = new JsonArray("core"), ["full"] = false }); break;
                case "script": rules[0]!["command"] = "Write-Output unsafe"; break;
                case "regex-timeout": rules.Add(new JsonObject { ["id"] = "slow", ["pattern"] = "^(a+)+$", ["sets"] = new JsonArray("core"), ["full"] = false }); break;
            }
            var a = fx.PolicyCandidate(catalog);
            if (variant == "regex-timeout")
            {
                var blob = fx.Git("rev-parse", fx.B + ":" + TestSelection.CatalogPath);
                fx.Git("update-index", "--add", "--cacheinfo", "100644," + blob + "," + new string('a', 200) + "!");
                a = fx.Git("commit-tree", fx.Git("write-tree"), "-p", fx.B, "-m", "regex");
            }
            var run = fx.Command(a, fx.B, json: true);
            results[variant] = run.Exit == 2 && run.Error.Contains(reason, StringComparison.Ordinal) ? "" : run.Exit + " " + run.Error;
        }
        var graphBad = results.Where(r => r.Value.Length > 0 && r.Key is not ("bad-regex" or "script" or "regex-timeout")).Select(r => r.Key + ": " + r.Value).ToList();
        var ruleBad = results.Where(r => r.Value.Length > 0 && r.Key is "bad-regex" or "script" or "regex-timeout").Select(r => r.Key + ": " + r.Value).ToList();

        // A full rule over a provider path still names the provider rule, and an added dependency is explained.
        var overlap = C1313SelectionRepository.CatalogJson();
        overlap["rules"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "P-OVERLAP", ["paths"] = new JsonArray("src/Antiphon.Agents.Pty/ClaudeDetectors.cs"), ["sets"] = new JsonArray("core"), ["full"] = true,
        });
        overlap["sets"]!.AsArray().Single(s => (string?)s!["id"] == "provider.claude.offline")!["dependsOn"] = new JsonArray("provider.codex.offline");
        fx.PolicyCandidate(overlap);
        var closure = fx.Select(fx.CommitStaged("src/Antiphon.Agents.Pty/ClaudeDetectors.cs", "overlap"));
        if (closure["mode"] as string != "full" || !C1313.Strings(closure["fallbackReasons"]).Contains("rule-full:P-OVERLAP")
            || !C1313.Dicts(closure["changedPaths"]).SelectMany(p => C1313.Strings(p["ruleIds"])).Contains("P-CLAUDE")
            || !C1313.Dicts(closure["explanationEdges"]).Any(e => e["id"] as string == "set:provider.claude.offline->provider.codex.offline")) graphBad.Add("overlap/dependency-closure");
        graphBad.ShouldBeEmpty("C1313 invalid-graph");
        ruleBad.ShouldBeEmpty("C1313 invalid-rule");
        results["prerequisite-cycle"].ShouldBe("", "C1313 cycle-refused");
        results["regex-timeout"].ShouldBe("", "C1313 regex-timeout-refused");
        results["script"].ShouldBe("", "C1313 data-not-code");
    }
}
