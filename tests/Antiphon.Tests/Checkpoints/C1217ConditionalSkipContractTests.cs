using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;
using F = Antiphon.Tests.Checkpoints.C1217ConditionalSkipFixture;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class C1217ConditionalSkipContractTests : CheckpointTestBase
{
    [Test]
    public void DeclarationRoundTrips()
    {
        var root = TempDir();
        var imported = PlanTableImporter.ImportMarkdown(F.Table);
        imported.ExitCode.ShouldBe(0, imported.Error);
        var saved = Path.Combine(root, "selected.yaml");
        File.WriteAllText(saved, ManifestLoader.ToYaml(imported.Manifest!));
        File.ReadAllText(saved).ShouldContain("conditionalSkips: c1217-fixture-v1");
        var loaded = ManifestLoader.LoadFile(saved, root);
        var selected = loaded.Checkpoints.Single(r => AfterSelector.IsSelected(r.After, ["S1"]));
        var resolved = JsonSerializer.SerializeToElement(F.Resolve(F.ParseCatalog(F.Catalog), loaded, selected));
        resolved.GetProperty("Id").GetString().ShouldBe("c1217-fixture-v1");
        resolved.GetProperty("Project").GetString().ShouldBe("tests/Fixture");
        resolved.GetProperty("Filter").GetString().ShouldBe("/*/Fixture/Cases/*");
        resolved.GetProperty("ExpectedTotal").GetInt32().ShouldBe(3);
        resolved.GetProperty("MinExecuted").GetInt32().ShouldBe(1);
        var legacy = ManifestLoader.LoadYaml(F.Yaml, root);
        ManifestLoader.ToYaml(legacy).ShouldNotContain("conditionalSkips:");
    }

    [Test]
    public void MalformedDeclarationsRefuse()
    {
        var root = TempDir();
        ManifestLoader.LoadYaml(F.Yaml, root).Checkpoints.Count.ShouldBe(1);
        // Test malformed input before testing new APIs: the base silently admits a blank/null key.
        foreach (var value in new[] { "''", "null", "NULL", "Null", "~", "", "[]", "{}", "1", "true", "TRUE", "[id]", "{id: value}", "!!int 1", "' id'", "'id '" })
            Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml(F.Yaml + "\n    conditionalSkips: " + value, root), "preserved:decl:admitted=false " + value)
                .ExitCode.ShouldBe(2, "preserved:decl:admitted=false " + value);
        foreach (var cell in new[] { "", "``", "null", "[]", "{}", "two ids", "*", "1" })
            PlanTableImporter.ImportMarkdown(F.Table.Replace("| c1217-fixture-v1 |", "| " + cell + " |", StringComparison.Ordinal))
                .ExitCode.ShouldBe(2, "preserved:table-decl:admitted=false " + cell);
        F.ParseCatalog(F.Catalog).ShouldNotBeNull();
        foreach (var (key, json) in InvalidCatalogs())
            Should.Throw<ManifestValidationException>(() => F.ParseCatalog(json), "preserved:" + key + ":admitted=false").ExitCode.ShouldBe(2);
        var imported = PlanTableImporter.ImportMarkdown(F.Table).Manifest!;
        var catalog = F.ParseCatalog(F.Catalog);
        foreach (var field in new[] { "id", "project", "filter", "floor" })
        {
            var row = ManifestLoader.LoadYaml(ManifestLoader.ToYaml(imported), root).Checkpoints[0];
            var manifest = ManifestLoader.LoadYaml(ManifestLoader.ToYaml(imported), root);
            if (field == "id")
            {
                var changed = ManifestLoader.ToYaml(imported).Replace("c1217-fixture-v1", "unknown-contract", StringComparison.Ordinal);
                row = ManifestLoader.LoadYaml(changed, root).Checkpoints[0];
            }
            if (field == "project") manifest.Builds[0].Project = "tests/Other";
            if (field == "filter") row.Filter = "/*/Fixture/Other/*";
            if (field == "floor") row.MinExecuted = 2;
            Should.Throw<ManifestValidationException>(() => F.Resolve(catalog, manifest, row)).ExitCode.ShouldBe(2, field);
        }
    }

    [Test]
    public void StrictTrxPreservesIdentityAndReason()
    {
        var path = Path.Combine(TempDir(), "strict.trx");
        File.WriteAllText(path, F.UnavailableTrx);
        var parsed = F.Strict(path);
        parsed.GetProperty("Ok").GetBoolean().ShouldBeTrue();
        parsed.GetProperty("Total").GetInt32().ShouldBe(3);
        parsed.GetProperty("Executed").GetInt32().ShouldBe(1);
        parsed.GetProperty("Passed").GetInt32().ShouldBe(1);
        parsed.GetProperty("Failed").GetInt32().ShouldBe(0);
        parsed.GetProperty("Skipped").GetInt32().ShouldBe(2);
        var results = parsed.GetProperty("Results").EnumerateArray().ToArray();
        results.Select(r => r.GetProperty("Name").GetString()).ShouldBe(["Fixture.Cases.Sentinel", "Fixture.Cases.LinkA", "Fixture.Cases.LinkB"]);
        results.Select(r => r.GetProperty("ExecutionId").GetString()).ShouldBe(["es", "ea", "eb"]);
        results.Skip(1).Select(r => r.GetProperty("SkipReason").GetString()).ShouldBe(["fixture capability unavailable", "fixture capability unavailable"]);
        results[1].GetProperty("DebugTrace").GetString().ShouldBe("Skipped: fixture capability unavailable");
        File.WriteAllText(path, F.AvailableTrx);
        var available = F.Strict(path);
        available.GetProperty("Ok").GetBoolean().ShouldBeTrue();
        available.GetProperty("Executed").GetInt32().ShouldBe(3);
        available.GetProperty("Skipped").GetInt32().ShouldBe(0);
        // Same qualified method can have multiple instances; execution identity must stay distinct.
        File.WriteAllText(path, F.UnavailableTrx.Replace("name=\"LinkB\"", "name=\"LinkA\"", StringComparison.Ordinal));
        F.Strict(path).GetProperty("Results").EnumerateArray().Count(r => r.GetProperty("Name").GetString() == "Fixture.Cases.LinkA").ShouldBe(2);
        foreach (var (key, xml) in InvalidTrx())
        {
            File.WriteAllText(path, xml);
            F.Strict(path).GetProperty("Ok").GetBoolean().ShouldBeFalse("preserved:strict-trx:" + key);
        }
        File.WriteAllText(path, F.UnavailableTrx);
        F.Strict(path, 4).GetProperty("Ok").GetBoolean().ShouldBeFalse("expected total");
    }

    [Test]
    public void LegacyParsingIsUnchanged()
    {
        var path = Path.Combine(TempDir(), "legacy.trx");
        File.WriteAllText(path, """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>
            <UnitTestResult testId="missing" testName="display fallback" outcome="Passed" />
            </Results></TestRun>
            """);
        var parsed = TrxReport.Parse(path);
        parsed.Ok.ShouldBeTrue();
        parsed.Executed.ShouldBe(1);
        parsed.Passed.ShouldBe(1);
        parsed.ExecutedNames.ShouldBe(["display fallback"]);
        ManifestLoader.LoadYaml(F.Yaml, TempDir()).Checkpoints.Count.ShouldBe(1);
    }

    [Test]
    public void UnsupportedCombinationsRefuse()
    {
        var root = TempDir();
        var conditional = F.Yaml + "\n    conditionalSkips: c1217-fixture-v1";
        foreach (var (key, yaml) in new[]
        {
            ("command", conditional.Replace("    filter: /*/Fixture/Cases/*\n    minExecuted: 1", "    command: echo fixture", StringComparison.Ordinal)),
            ("repeat", conditional + "\n    repeat: 2"),
            ("skip-only", conditional.Replace("minExecuted: 1", "minExecuted: 0", StringComparison.Ordinal)),
            ("known-flaky", conditional + "\nrerun:\n  knownFlaky: [Fixture.Cases.LinkA]"),
            ("mixed-repeat", conditional.Replace("checkpoints:", "  - id: bin-other\n    project: tests/Fixture\n    outputPath: bin-other/\ncheckpoints:", StringComparison.Ordinal)
                + "\n  - id: CP-2\n    after: [S1]\n    build: bin-other\n    filter: /*/Fixture/Cases/*\n    repeat: 2\n"),
        })
        {
            Should.Throw<ManifestValidationException>(() => ManifestLoader.LoadYaml(yaml, root), "preserved:" + key + ":admitted=false").ExitCode.ShouldBe(2);
            // Each ordinary twin remains supported, with independent builds for mixed repeats.
            var ordinary = yaml.Replace("    conditionalSkips: c1217-fixture-v1", "", StringComparison.Ordinal);
            ManifestLoader.LoadYaml(ordinary, root).ShouldNotBeNull();
        }
        var manifest = ManifestLoader.LoadYaml(conditional, root);
        F.Combinations(manifest, false);
        Should.Throw<ManifestValidationException>(() => F.Combinations(manifest, true)).ExitCode.ShouldBe(2, "merged");
        F.Combinations(ManifestLoader.LoadYaml(F.Yaml, root), true);
    }

    private static IEnumerable<(string, string)> InvalidCatalogs()
    {
        yield return ("unknown-probe", F.Catalog.Replace("file-symlink-create-v1", "unknown-probe", StringComparison.Ordinal));
        yield return ("catalog-shape", F.Catalog.Replace("\"probe\":", "\"extra\":1,\"probe\":", StringComparison.Ordinal));
        yield return ("duplicate-property", F.Catalog.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));
        yield return ("version", F.Catalog.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
        yield return ("duplicate-identity", F.Catalog.Replace("\"LinkB\"", "\"LinkA\"", StringComparison.Ordinal));
        var entry = JsonDocument.Parse(F.Catalog).RootElement.GetProperty("contracts")[0].GetRawText();
        yield return ("duplicate-contract", "{\"schemaVersion\":1,\"contracts\":[" + entry + "," + entry + "]}");
        foreach (var value in new[] { "0", "-1", "2147483648", "1.0", "\"1\"", "null", "true" })
            yield return ("count-" + value, F.Catalog.Replace("\"count\":1", "\"count\":" + value, StringComparison.Ordinal));
        foreach (var value in new[] { "\"\"", "null", "[]", "1" })
            yield return ("reason-" + value, F.Catalog.Replace("\"fixture capability unavailable\"", value, StringComparison.Ordinal));
        yield return ("catalog-limit", F.Catalog.Replace("fixture capability unavailable", new string('a', 2049), StringComparison.Ordinal));
        yield return ("bytes-limit", F.Catalog + new string(' ', 65536));
        yield return ("wildcard", F.Catalog.Replace("LinkA", "Link*", StringComparison.Ordinal));
        yield return ("missing", F.Catalog.Replace("\"minExecuted\":1,", "", StringComparison.Ordinal));
        yield return ("impossible-floor", F.Catalog.Replace("\"minExecuted\":1", "\"minExecuted\":5", StringComparison.Ordinal));
        yield return ("skip-only", F.Catalog.Replace("\"expectedTotal\":3", "\"expectedTotal\":2", StringComparison.Ordinal));
        yield return ("path", F.Catalog.Replace("tests/Fixture", "tests/../Fixture", StringComparison.Ordinal));
        yield return ("many-contracts", "{\"schemaVersion\":1,\"contracts\":[" + string.Join(",", Enumerable.Range(0, 65).Select(i => entry.Replace("c1217-fixture-v1", "c" + i, StringComparison.Ordinal))) + "]}");
        var skip = JsonDocument.Parse(entry).RootElement.GetProperty("skips")[0].GetRawText();
        var many = string.Join(",", Enumerable.Range(0, 65).Select(i => skip.Replace("LinkA", "Link" + i, StringComparison.Ordinal)));
        yield return ("many-identities", "{\"schemaVersion\":1,\"contracts\":[{\"id\":\"many\",\"probe\":\"file-symlink-create-v1\",\"project\":\"tests/Fixture\",\"filter\":\"/*/Fixture/Cases/*\",\"expectedTotal\":66,\"minExecuted\":1,\"skips\":[" + many + "]}]}");
        foreach (var name in new[] { "id", "probe", "project", "filter", "expectedTotal", "minExecuted", "skips" })
        foreach (var value in new[] { "null", "true", "{}", "[]" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(F.Catalog)!;
            node["contracts"]![0]![name] = System.Text.Json.Nodes.JsonNode.Parse(value);
            yield return ("type-" + name + "-" + value, node.ToJsonString());
        }
        foreach (var path in new[] { "/tests/Fixture", "../Fixture", "tests//Fixture", "tests/./Fixture", "C:/Fixture", "tests\\Fixture" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(F.Catalog)!;
            node["contracts"]![0]!["project"] = path;
            yield return ("project-" + path, node.ToJsonString());
        }
    }

    private static IEnumerable<(string, string)> InvalidTrx()
    {
        foreach (var (from, to) in new[]
        {
            ("TestRun", "Other"), ("TeamTest/2010", "TeamTest/2006"),
            ("executionId=\"ea\"", "executionId=\"es\""), ("executionId=\"ea\"", ""),
            ("testId=\"a\"", "testId=\"unknown\""), ("className=\"Fixture.Cases\"", "className=\"\""),
            ("outcome=\"Passed\"", "outcome=\"Mystery\""), ("outcome=\"Passed\"", "outcome=\"Failed\""),
            ("<DebugTrace>", "<StdOut>"),
        })
        {
            var changed = F.UnavailableTrx.Replace(from, to, StringComparison.Ordinal);
            if (from == "<DebugTrace>") changed = changed.Replace("</DebugTrace>", "</StdOut>", StringComparison.Ordinal);
            yield return (from + "->" + to, changed);
        }
        foreach (var section in new[] { "Results", "TestDefinitions", "ResultSummary" })
        {
            var doc = XDocument.Parse(F.UnavailableTrx); var element = doc.Root!.Elements().Single(e => e.Name.LocalName == section);
            element.Remove(); yield return ("missing-" + section, doc.ToString());
            doc = XDocument.Parse(F.UnavailableTrx); element = doc.Root!.Elements().Single(e => e.Name.LocalName == section);
            element.AddAfterSelf(new XElement(element)); yield return ("duplicate-" + section, doc.ToString());
        }
        foreach (var name in new[] { "UnitTest", "TestMethod", "Counters", "DebugTrace" })
        {
            var doc = XDocument.Parse(F.UnavailableTrx); var element = doc.Descendants().First(e => e.Name.LocalName == name);
            element.AddAfterSelf(new XElement(element)); yield return ("duplicate-" + name, doc.ToString());
            doc = XDocument.Parse(F.UnavailableTrx); doc.Descendants().First(e => e.Name.LocalName == name).Remove();
            yield return ("missing-" + name, doc.ToString());
        }
        foreach (var counter in new[] { "total", "executed", "passed", "failed", "notExecuted" })
        foreach (var value in new[] { null, "-1", "2147483648", "wrong", "99" })
        {
            var doc = XDocument.Parse(F.UnavailableTrx);
            doc.Descendants().Single(e => e.Name.LocalName == "Counters").SetAttributeValue(counter, value);
            yield return (counter + ":" + value, doc.ToString());
        }
    }
}
