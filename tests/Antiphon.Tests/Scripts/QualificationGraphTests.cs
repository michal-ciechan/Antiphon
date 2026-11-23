using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1514 S1 witnesses, in process. QualificationGraph.Read, QualificationGraphJson and QualificationGraphSchema
/// replaced scripts/lib/qualification-graph.ps1, and these cases replaced its driver scripts/test-qualification-graph.ps1
/// -Case SchemaAndIdentity and the launcher QualificationGraphContractTests (plan 2026-11-22-remaining-ps1-to-csharp.md,
/// unit 9). The rows keep the driver's names ("QGRAPH SchemaAndIdentity/&lt;id&gt;"). The inputs are the committed S1
/// fixtures (scripts/fixtures/qualification-graph/s1) and the checked-in schema; the pins are recomputed here,
/// independently of the reader, as a controller must.
/// </summary>
[Category("Unit")]
public sealed class QualificationGraphTests
{
    private static readonly string Repo = DelegateScriptRunner.RepoRoot;
    private static readonly string Fixtures = Path.Combine(Repo, "scripts", "fixtures", "qualification-graph", "s1");
    private static readonly Lazy<QualificationGraphSchema> SchemaFile =
        new(() => QualificationGraphSchema.Parse(File.ReadAllText(Path.Combine(Repo, QualificationGraphSchema.RelativePath))));

    private static QualificationGraphSchema Schema => SchemaFile.Value;

    private static string Load(string name) => File.ReadAllText(Path.Combine(Fixtures, name));

    private static string IndependentHash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Read-PinnedGraph: the test supplies the independent fixture or variant pin.</summary>
    private static QualificationGraphResult ReadPinned(string json, string catalogJson) =>
        QualificationGraph.Read(json, catalogJson, Schema, IndependentHash(catalogJson));

    /// <summary>Test-QualificationGraphSchema: the schema (and the explicit date check) alone.</summary>
    private static bool SchemaAccepts(string json) => Schema.Graph(QualificationGraphJson.Decode(json)).Accepted;

    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    private static string Json(JsonNode? node) => node is null ? "null" : node.ToJsonString();

    private static string Row(string id) => "QGRAPH SchemaAndIdentity/" + id;

    private static string Detail(QualificationGraphResult r) =>
        r.Category + ":" + r.Path + ":" + r.Reason + (r.SchemaPaths.Count == 0 ? "" : " schemaPaths=" + string.Join("|", r.SchemaPaths));

    /// <summary>The base graph (execute.valid.json) as an editable copy.</summary>
    private static JsonObject Base() => Node(Load("execute.valid.json")).AsObject();

    private static JsonObject Catalog() => Node(Load("catalog.json")).AsObject();

    /// <summary>
    /// Edit-Graph: one recipe edit at a JSON pointer. Changing the first target's id keeps the nodes' references coherent,
    /// so a dangling reference cannot mask the registry guard's mutant.
    /// </summary>
    private static JsonNode? Edit(JsonNode graph, string path, string op, JsonNode? value)
    {
        if (path == "/targets/0/id" && op == "set")
        {
            var oldId = (string)graph["targets"]![0]!["id"]!;
            foreach (var node in graph["nodes"]!.AsArray())
                if ((string)node!["targetId"]! == oldId) node["targetId"] = value?.DeepClone();
        }
        if (path.Length == 0) return value?.DeepClone();
        var parts = path.TrimStart('/').Split('/');
        var parent = graph;
        for (var j = 0; j < parts.Length - 1; j++)
            parent = parent is JsonArray array ? array[int.Parse(parts[j], CultureInfo.InvariantCulture)]! : parent[parts[j]]!;
        var key = parts[^1];
        if (op == "remove") parent.AsObject().Remove(key);
        else if (parent is JsonArray items) items[int.Parse(key, CultureInfo.InvariantCulture)] = value?.DeepClone();
        else parent.AsObject()[key] = value?.DeepClone();
        return graph;
    }

    private static void Reverse(JsonNode parent, string key)
    {
        var reversed = new JsonArray(parent[key]!.AsArray().Reverse().Select(n => n?.DeepClone()).ToArray());
        parent[key] = reversed;
    }

    [Test]
    public void Fixture_pins_and_valid_graphs()
    {
        var rows = new NightlyCaseRows();
        var catalogJson = Load("catalog.json");
        var pinsOk = IndependentHash(catalogJson) == Load("catalog.sha256").Trim();
        foreach (var pin in Node(Load("identity.expected.json"))["catalogPins"]!.AsObject())
            pinsOk = pinsOk && IndependentHash(Load(pin.Key)) == (string)pin.Value!;
        rows.Check(pinsOk, Row("catalog-pin"));
        foreach (var (id, file) in new[] { ("valid-prepare", "prepare.valid.json"), ("valid-execute", "execute.valid.json"), ("valid-sentinel", "execute-preparation-failed.valid.json") })
        {
            var text = Load(file);
            var result = ReadPinned(text, catalogJson);
            using var original = JsonDocument.Parse(text);
            rows.Check(result.Accepted && JsonElement.DeepEquals(result.Graph, original.RootElement), Row(id), Detail(result));
        }
        rows.ShouldAllPass(4);
    }

    /// <summary>
    /// Every invalid-cases.json recipe: the schema alone refuses exactly the schema-boundary edits and accepts the
    /// identity-boundary ones; the reader refuses each in the recipe's category. A schema refusal names exactly the
    /// recipe's pointer and nothing else (the edit is the only difference from the accepted base, so this is the
    /// location proof the driver's minimal pair stood in for); an identity refusal names it as its path.
    /// </summary>
    [Test]
    public void Invalid_recipes_are_refused_at_their_pointer()
    {
        var rows = new NightlyCaseRows();
        var catalogJson = Load("catalog.json");
        var healthy = ReadPinned(Load("execute.valid.json"), catalogJson);
        var recipes = Node(Load("invalid-cases.json")).AsArray();
        foreach (var recipe in recipes)
        {
            var id = (string)recipe!["id"]!;
            var path = (string)recipe["path"]!;
            var boundary = (string)recipe["boundary"]!;
            var category = (string)recipe["category"]!;
            var text = Json(Edit(Base(), path, (string)recipe["op"]!, recipe["value"]));
            var direct = SchemaAccepts(text);
            var result = ReadPinned(text, catalogJson);
            var located = boundary == "identity" ? result.Path == path : result.SchemaPaths.SequenceEqual(new[] { path });
            rows.Check(healthy.Accepted && direct == (boundary == "identity") && !result.Accepted && result.Category == category && located,
                Row(id), "path=" + path + " schema=" + direct + " result=" + Detail(result));
        }
        recipes.Count.ShouldBe(70);
        rows.ShouldAllPass(70);
    }

    [Test]
    public void Catalog_refusals()
    {
        var rows = new NightlyCaseRows();
        var graphJson = Load("execute.valid.json");
        var catalogJson = Load("catalog.json");
        var healthy = ReadPinned(graphJson, catalogJson);

        var missing = ReadPinned(graphJson, "");
        rows.Check(healthy.Accepted && !missing.Accepted && missing.Reason == "catalog-required" && missing.Path == "/catalog", Row("catalog-missing"), Detail(missing));

        // Catalog membership must independently prove the graph's declared selection.
        foreach (var fault in new[] { "wrong-uid", "missing-uid", "extra-uid", "wrong-count", "wrong-digest" })
        {
            var g = Base();
            var cat = Catalog();
            switch (fault)
            {
                case "wrong-uid": cat["uids"]!["fixture-selection"] = new JsonArray("Fixture.A.0", "Fixture.WRONG"); break;
                case "missing-uid": cat["uids"]!["fixture-selection"] = new JsonArray("Fixture.A.0"); break;
                case "extra-uid": cat["uids"]!["fixture-selection"] = new JsonArray("Fixture.A.0", "Fixture.A.1", "Fixture.A.2"); break;
                case "wrong-count":
                    cat["nodes"]![3]!["selection"]!["expectedCount"] = 3;
                    g["nodes"]![3]!["selection"]!["expectedCount"] = 3;
                    break;
                case "wrong-digest":
                    cat["nodes"]![3]!["selection"]!["uidSetDigest"] = new string('f', 64);
                    g["nodes"]![3]!["selection"]!["uidSetDigest"] = new string('f', 64);
                    break;
            }
            var r = ReadPinned(Json(g), Json(cat));
            rows.Check(healthy.Accepted && !r.Accepted && r.Category == "identity" && r.Path == "/nodes/3/selection" && r.Reason == "catalog-uid-mismatch",
                Row("catalog-uid-mismatch/" + fault), Detail(r));
        }

        foreach (var phase in new[] { "prepare", "execute" })
        {
            var text = Load(phase + ".valid.json");
            var r = ReadPinned(text, text);
            rows.Check(healthy.Accepted && !r.Accepted && r.Category == "identity" && r.Path == "/catalog" && r.Reason == "catalog-invalid",
                Row("catalog-is-graph/" + phase), Detail(r));
        }

        // 1.0000000000000000000001e22 is an integer the schema accepts, and it is not the catalog's 1e22.
        var preciseGraph = graphJson.Replace("\"canonicalOrdinal\": 0", "\"canonicalOrdinal\": 1.0000000000000000000001e22");
        var preciseCatalog = catalogJson.Replace("\"canonicalOrdinal\": 0", "\"canonicalOrdinal\": 1e22");
        var precise = ReadPinned(preciseGraph, preciseCatalog);
        rows.Check(healthy.Accepted && preciseGraph != graphJson && SchemaAccepts(preciseGraph) && !precise.Accepted && precise.Category == "identity" &&
            precise.Path == "/nodes/0/canonicalOrdinal" && precise.Reason == "catalog-mismatch", Row("catalog-number-exact"), Detail(precise));

        var unpinned = QualificationGraph.Read(graphJson, catalogJson, Schema, null!);
        rows.Check(!unpinned.Accepted && unpinned.Reason == "catalog-pin-required", Row("catalog-pin-missing"), Detail(unpinned));
        var wrongPin = QualificationGraph.Read(graphJson, catalogJson, Schema, new string('0', 64));
        rows.Check(!wrongPin.Accepted && wrongPin.Reason == "catalog-pin-mismatch", Row("catalog-pin-wrong"), Detail(wrongPin));

        foreach (var fault in new[] { "missing-uids", "unknown-field", "wrong-node-shape", "wrong-uid-type" })
        {
            var cat = Catalog();
            switch (fault)
            {
                case "missing-uids": cat.Remove("uids"); break;
                case "unknown-field": cat["phase"] = "prepare"; break;
                case "wrong-node-shape": cat["nodes"]![0]!.AsObject().Remove("platformOfRecord"); break;
                case "wrong-uid-type": cat["uids"]!["fixture-selection"] = new JsonArray(42); break;
            }
            var r = ReadPinned(graphJson, Json(cat));
            rows.Check(!r.Accepted && r.Path == "/catalog" && r.Reason == "catalog-invalid", Row("catalog-shape/" + fault), Detail(r));
        }
        rows.ShouldAllPass(15);
    }

    [Test]
    public void Raw_and_wiring_refusals()
    {
        var rows = new NightlyCaseRows();
        var graphJson = Load("execute.valid.json");
        var catalogJson = Load("catalog.json");
        var healthy = ReadPinned(graphJson, catalogJson);

        var duplicate = Base();
        duplicate["nodes"]!.AsArray().Add(duplicate["nodes"]![0]!.DeepClone());
        var dup = ReadPinned(Json(duplicate), catalogJson);
        rows.Check(healthy.Accepted && !dup.Accepted && dup.Reason == "duplicate-node" && dup.Path == "/nodes/5/id", Row("duplicate-node"), Detail(dup));

        var rawCases = new (string Id, string Text, string Path, string Reason)[]
        {
            ("duplicate-key/root", Load("duplicate-key.raw.json"), "/schemaVersion", "duplicate-key"),
            ("duplicate-key/nested", graphJson.Replace("\"repositoryId\": \"fixture-repo\"", "\"repositoryId\":\"wrong\",\"repositoryId\":\"fixture-repo\""), "/candidate/repositoryId", "duplicate-key"),
            ("nonfinite/NaN", graphJson.Replace("\"seconds\": 1.25", "\"seconds\": NaN"), "", "invalid-json"),
            ("nonfinite/Infinity", graphJson.Replace("\"seconds\": 1.25", "\"seconds\": Infinity"), "", "invalid-json"),
            ("nonfinite/overflow", graphJson.Replace("\"seconds\": 1.25", "\"seconds\": 1e9999"), "/nodes/0/estimate/seconds", "nonfinite"),
        };
        foreach (var (id, text, path, reason) in rawCases)
        {
            var r = ReadPinned(text, catalogJson);
            rows.Check(healthy.Accepted && text != graphJson && !r.Accepted && r.Category == "raw" && r.Path == path && r.Reason == reason, Row(id), Detail(r));
        }

        var extra = Base();
        extra["fixtureUnexpected"] = true;
        var wired = ReadPinned(Json(extra), catalogJson);
        rows.Check(healthy.Accepted && !wired.Accepted && wired.Category == "schema", Row("schema-wired"), Detail(wired));
        rows.ShouldAllPass(7);
    }

    [Test]
    public void Canonical_identity()
    {
        var rows = new NightlyCaseRows();
        var catalogJson = Load("catalog.json");
        var healthy = ReadPinned(Load("execute.valid.json"), catalogJson);
        var golden = Load("execute.canonical.json");
        var expected = Node(Load("identity.expected.json"));

        var reordered = new JsonObject();
        foreach (var member in Base().Reverse()) reordered[member.Key] = member.Value?.DeepClone();
        var r = ReadPinned(Json(reordered), catalogJson);
        var primitiveExpected = "{\"Z\":\"\\n\",\"a\":\"\u00e9\",\"\u00e4\":1.25}";
        var oldCulture = CultureInfo.CurrentCulture;
        string primitive;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            primitive = QualificationGraphJson.CanonicalJson("{\"\\u00e4\":1.25,\"a\":\"\\u00e9\",\"Z\":\"\\n\"}");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        rows.Check(r.Accepted && r.CanonicalJson == golden && primitive == primitiveExpected, Row("canonical-keys"), Detail(r) + " primitive=" + primitive);

        var sets = Base();
        Reverse(sets, "nodes");
        Reverse(sets, "edges");
        Reverse(sets, "targets");
        Reverse(sets["targets"]![0]!, "resourceKeys");
        r = ReadPinned(Json(sets), catalogJson);
        rows.Check(healthy.Accepted && healthy.CanonicalJson == golden && r.Accepted && r.CanonicalJson == golden, Row("canonical-sets"), Detail(r));

        var nullJson = QualificationGraphJson.CanonicalJson("null");
        var empty = QualificationGraphJson.CanonicalJson("\"\"");
        rows.Check(nullJson == "null" && empty == "\"\"" && IndependentHash(nullJson) != IndependentHash(empty), Row("typed-null"));

        var number = QualificationGraphJson.CanonicalJson("1.25");
        var text = QualificationGraphJson.CanonicalJson("\"1.25\"");
        rows.Check(number == "1.25" && text == "\"1.25\"" && IndependentHash(number) != IndependentHash(text) &&
            QualificationGraphJson.CanonicalJson("1e-9999") == "1e-9999" &&
            QualificationGraphJson.CanonicalJson("12345678901234567890123456789.1") == "12345678901234567890123456789.1", Row("typed-number"));

        // The graph digest is the SHA-256 of the canonical text's UTF-8 bytes (TestPhaseRecords.Sha256Text), non-ASCII included.
        rows.Check(healthy.CanonicalJson == golden && healthy.GraphDigest == (string)expected["graphDigest"]! &&
            healthy.GraphDigest == IndependentHash(golden) && TestPhaseRecords.Sha256Text(primitive) == IndependentHash(primitiveExpected), Row("utf8-hash"));
        rows.ShouldAllPass(5);
    }

    [Test]
    public void Binding_and_target_changes()
    {
        var rows = new NightlyCaseRows();
        var catalogJson = Load("catalog.json");
        var healthy = ReadPinned(Load("execute.valid.json"), catalogJson);
        var expected = Node(Load("identity.expected.json"));
        var expectedNodeIds = expected["nodeIds"]!.AsArray().Select(n => (string)n!).ToArray();
        var expectedUidDigest = (string)expected["uidSetDigest"]!;
        foreach (var binding in new[] { "binding", "target" })
        {
            var g = Base();
            g["targets"]![0]!["bindingDigest"] = new string('f', 64);
            if (binding == "target")
            {
                g["targets"]![0]!["id"] = "windows-B";
                foreach (var n in g["nodes"]!.AsArray()) n!["targetId"] = "windows-B";
            }
            var r = ReadPinned(Json(g), Load("catalog-" + binding + "-B.json"));
            var gold = Load("execute-" + binding + "-B.canonical.json");
            rows.Check(r.Accepted && r.NodeIds.SequenceEqual(expectedNodeIds), Row(binding + "-node-stable"), Detail(r));
            rows.Check(r.Accepted && r.CanonicalJson == gold && r.GraphDigest == IndependentHash(gold) && r.GraphDigest != healthy.GraphDigest,
                Row(binding + "-graph-changed"), Detail(r));
            rows.Check(r.Accepted && r.Graph.GetProperty("nodes")[3].GetProperty("selection").GetProperty("uidSetDigest").GetString() == expectedUidDigest &&
                r.UidSetDigests[3] == expectedUidDigest, Row(binding + "-uid-stable"), Detail(r));
        }

        var changedUids = new[] { "Fixture.A.0", "Fixture.A.2" };
        var changedDigest = IndependentHash("[\"Fixture.A.0\",\"Fixture.A.2\"]");
        var graph = Base();
        var cat = Catalog();
        cat["uids"]!["fixture-selection"] = new JsonArray(changedUids.Select(u => (JsonNode)u).ToArray());
        cat["nodes"]![3]!["selection"]!["uidSetDigest"] = changedDigest;
        graph["nodes"]![3]!["selection"]!["uidSetDigest"] = changedDigest;
        var changed = ReadPinned(Json(graph), Json(cat));
        rows.Check(changed.Accepted && QualificationGraphJson.UidSetDigest(changedUids) == changedDigest && changedDigest != expectedUidDigest &&
            QualificationGraphJson.UidSetDigest(new[] { "a", "A" }) == IndependentHash("[\"A\",\"a\"]"), Row("uid-members-changed"), Detail(changed));
        rows.ShouldAllPass(7);
    }

    /// <summary>
    /// The schema evaluator's subset (new with the port: the PowerShell used Test-Json): a keyword, draft or $ref outside
    /// it fails closed when the schema is parsed, integers and minimum are exact, a duplicate key in the schema refuses,
    /// and the shipped schema parses.
    /// </summary>
    [Test]
    public void Schema_subset_is_exact_and_closed()
    {
        var rows = new NightlyCaseRows();
        const string draft = "\"$schema\":\"https://json-schema.org/draft/2020-12/schema\"";
        static bool Unsupported(string schema)
        {
            try { QualificationGraphSchema.Parse(schema); return false; }
            catch (QualificationSchemaUnsupported) { return true; }
        }
        var shipped = File.ReadAllText(Path.Combine(Repo, QualificationGraphSchema.RelativePath));
        rows.Check(QualificationGraphSchema.Parse(shipped) is not null, Row("schema-subset/shipped"));
        rows.Check(Unsupported(shipped.Replace("\"minItems\": 1,", "\"minItems\": 1, \"maxItems\": 9,")), Row("schema-subset/unknown-keyword"));
        rows.Check(Unsupported(shipped.Replace("https://json-schema.org/draft/2020-12/schema", "http://json-schema.org/draft-07/schema#")), Row("schema-subset/draft"));
        rows.Check(Unsupported(shipped.Replace("\"$ref\": \"#/$defs/id\"", "\"$ref\": \"#/$defs/missing\"")), Row("schema-subset/ref-missing"));
        var duplicateKey = false;
        try { QualificationGraphSchema.Parse("{" + draft + "," + draft + "}"); }
        catch (QualificationGraphRawRefusal refusal) { duplicateKey = refusal.Reason == "duplicate-key" && refusal.Path == "/$schema"; }
        rows.Check(duplicateKey, Row("schema-subset/duplicate-key"));

        var evaluator = new QualificationSchemaEvaluator(QualificationGraphJson.Decode(
            "{" + draft + ",\"type\":\"object\",\"properties\":{\"n\":{\"type\":\"integer\",\"minimum\":0},\"s\":{\"type\":\"array\",\"uniqueItems\":true}}}"));
        IReadOnlyList<string> Violations(string json) => evaluator.Violations(QualificationGraphJson.Decode(json));
        rows.Check(Violations("{\"n\":1.0}").Count == 0 && Violations("{\"n\":1.0000000000000000000001e22}").Count == 0 && Violations("{\"n\":-0}").Count == 0,
            Row("schema-subset/integer-exact"));
        rows.Check(Violations("{\"n\":0.5}").SequenceEqual(new[] { "/n" }) && Violations("{\"n\":1e-9999}").SequenceEqual(new[] { "/n" }) &&
            Violations("{\"n\":-1}").SequenceEqual(new[] { "/n" }), Row("schema-subset/fraction-and-minimum"));
        rows.Check(Violations("{\"s\":[1,1.0]}").SequenceEqual(new[] { "/s" }) && Violations("{\"s\":[{\"a\":1,\"b\":2},{\"b\":2,\"a\":1.0}]}").SequenceEqual(new[] { "/s" }) &&
            Violations("{\"s\":[1,\"1\"]}").Count == 0, Row("schema-subset/unique-by-value"));
        rows.ShouldAllPass(8);
    }

    [Test]
    public void Sources_are_ascii()
    {
        var rows = new NightlyCaseRows();
        foreach (var file in new[] { "QualificationGraph.cs", "QualificationGraphSchema.cs" })
            rows.Check(File.ReadAllBytes(Path.Combine(Repo, "scripts", "lib", "Antiphon.Nightly", file)).All(b => b < 128), Row("ascii/" + file));
        rows.ShouldAllPass(2);
    }
}
