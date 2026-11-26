using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;

namespace Antiphon.Tests.Scripts;

public sealed partial class ReleaseImpactEnvelopeTests
{
    private static string P2Fixtures => Path.Combine(RepoRoot(), "tests", "Antiphon.Tests", "Scripts", "Fixtures", "C2069");
    private static JsonNode P2Json(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(P2Fixtures, name + ".json")))!;
    private static byte[] P2Hex(string name) => Convert.FromHexString(File.ReadAllText(Path.Combine(P2Fixtures, name + ".hex")).Trim());
    private static JsonElement Element(JsonNode? value) => JsonSerializer.SerializeToElement(value);
    private const string P2Format = "antiphon-p2-integrity-v1";

    private static void CheckCanonicalVectors()
    {
        var rows = new NightlyCaseRows();
        var schema = P2Json("schema");
        var root = P2Json("root").AsObject();
        var projections = P2Json("projections").AsObject();
        var hashes = P2Json("projection-digests").AsObject();
        var manifest = JsonNode.Parse("""
            [
              {"path":"é.txt","gitMode":"100644","blobId":"036ec5657a63660ac16b305a49ab95ee28ba3cf7",
               "rawByteCount":3,"rawSha256":"375672a4697884da6e72f09b3c4a56a59c0d18ccd78887d530a184a6fd76b375"},
              {"path":"Z.txt","gitMode":"100644","blobId":"bca70f35318f31dd1d1d1d2d2e64c19b880899ff",
               "rawByteCount":2,"rawSha256":"4adc33bd9fe74303c344be46e5916d65182fb218e248fe80452ab3f025b06c64"}
            ]
            """)!;

        void Vector(string id, int length, string hash, byte[] literal, byte[] independent, byte[] actual)
        {
            rows.Check(actual.AsSpan().SequenceEqual(literal) && independent.AsSpan().SequenceEqual(literal)
                && literal.Length == length, id + ".bytes", $"expected={length} actual={actual.Length} independent={independent.Length}");
            rows.Check(ReleaseImpactIntegrity.RawSha256(actual) == hash && ReleaseImpactIntegrity.RawSha256(independent) == hash,
                id + ".sha", $"expected={hash}");
        }

        Vector("C.root", 1441, "2394c81c2e3a71850ae04027a910ba41885e03ed6a6db4762131875b5817f1eb",
            P2Hex("root"), IndependentBytes(schema["root"]!, root), ReleaseImpactIntegrity.RootBytes(Element(root)));
        Vector("C.manifest.B", 328, "e7a9fd39463deae0e78897388a736fe3534e78a6510a43946769904dcc2297a3",
            P2Hex("manifest.B"), IndependentRole(schema, "baselineManifest", manifest),
            ReleaseImpactIntegrity.ProjectionBytes("baselineManifest", Element(manifest), false));
        Vector("C.manifest.A", 329, "3f28efa106b740b7fb61656f2417c6a12c8bf91639392540fd4f45a17a50e679",
            P2Hex("manifest.A"), IndependentRole(schema, "candidateManifest", manifest),
            ReleaseImpactIntegrity.ProjectionBytes("candidateManifest", Element(manifest), false));

        var primitiveShape = JsonNode.Parse("""
            {"string":"text","narrow":"u32","wide":"u64","off":"boolean","on":"boolean",
             "absent":["optional","text"],"empty":["optional","text"],"zero":["optional","u32"]}
            """)!;
        var primitive = JsonNode.Parse("""
            {"string":"é","narrow":258,"wide":4294967298,"off":false,"on":true,"absent":null,"empty":"","zero":0}
            """)!;
        using var framing = new ReleaseImpactIntegrity.CanonicalWriter(false);
        framing.String("é");
        framing.Number(258);
        framing.WideNumber(4_294_967_298);
        framing.Flag(false);
        framing.Flag(true);
        framing.Optional(null);
        framing.Optional(() => framing.String(""));
        framing.Optional(() => framing.Number(0));
        Vector("C.primitives", 31, "5bcf68390ae33a9b88a4b209b3bdd8ad7bb063aa312aeead4e05c41c2d2a88b6",
            P2Hex("primitives"), IndependentBytes(primitiveShape, primitive), framing.Bytes());

        string[] expectedRoles = ["target", "algorithm", "baselineManifest", "candidateManifest", "compatibility",
            "baselineIndex", "candidateIndex", "baselineGraph", "candidateGraph", "diff", "universe", "roster",
            "inventory", "receipts", "obligations", "trust", "selection"];
        rows.Check(expectedRoles.SequenceEqual(projections.Select(pair => pair.Key))
            && expectedRoles.SequenceEqual(ReleaseImpactIntegrity.StructuredRoles)
            && expectedRoles.SequenceEqual(hashes.Select(pair => pair.Key)), "C.roles.roster");
        foreach (var role in expectedRoles)
        {
            var literal = Convert.FromHexString(hashes[role]!["hex"]!.GetValue<string>());
            Vector("C.roles." + role, literal.Length, hashes[role]!["sha256"]!.GetValue<string>(),
                literal, IndependentRole(schema, role, projections[role]),
                ReleaseImpactIntegrity.ProjectionBytes(role, Element(projections[role]), false));
        }

        var envelope = IndependentEnvelope(schema, root, projections);
        using var before = new MemoryStream(Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        var reordered = new JsonObject(envelope.Reverse().Select(pair =>
            KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
        var permuted = reordered["projections"]!["target"]!["buildProperties"]!.AsArray();
        var first = permuted[0]!.DeepClone();
        permuted.RemoveAt(0);
        permuted.Add(first);
        var spaced = reordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\n", "\r\n", StringComparison.Ordinal);
        using var after = new MemoryStream(Encoding.UTF8.GetBytes(spaced));
        var decoded = ReleaseImpactIntegrity.Decode(before, false);
        rows.Check(ReleaseImpactIntegrity.Decode(after, false).RootDigest == decoded.RootDigest, "C.transport.rootDigest");
        var producerContext = new JsonObject(root.Where(pair => !pair.Key.EndsWith("Digest", StringComparison.Ordinal))
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
        var producer = ReleaseImpactIntegrity.Write(Element(producerContext),
            projections.ToDictionary(pair => pair.Key, pair => Element(pair.Value), StringComparer.Ordinal), false);
        using var producerWire = new MemoryStream(producer);
        rows.Check(ReleaseImpactIntegrity.Decode(producerWire, false).RootDigest == decoded.RootDigest, "C.transport.producer");

        bool ReversalDiffers(string role, JsonNode node, JsonArray sequence)
        {
            var original = ReleaseImpactIntegrity.ProjectionBytes(role, Element(node), false);
            var reverse = sequence.Last()!.DeepClone();
            sequence.RemoveAt(sequence.Count - 1);
            sequence.Insert(0, reverse);
            return !original.AsSpan().SequenceEqual(ReleaseImpactIntegrity.ProjectionBytes(role, Element(node), false));
        }
        var selection = projections["selection"]!.DeepClone();
        rows.Check(ReversalDiffers("selection", selection, selection["decisions"]![0]!["reasons"]![0]!["chain"]!.AsArray()),
            "C.ordered.chainDigestsDiffer");
        var target = projections["target"]!.DeepClone();
        rows.Check(ReversalDiffers("target", target, target["argv"]!.AsArray()), "C.ordered.argvDigestsDiffer");
        var roster = projections["roster"]!.DeepClone();
        var secondChunk = roster[0]!.DeepClone();
        secondChunk["chunk"] = "second";
        roster.AsArray().Add(secondChunk);
        rows.Check(ReversalDiffers("roster", roster, roster.AsArray()), "C.ordered.launchDigestsDiffer");
        var numeric = projections["obligations"]!["history"]!.AsArray();
        rows.Check(numeric[0]!["ordinal"]!.GetValue<int>() == 10 && numeric[1]!["ordinal"]!.GetValue<int>() == 2,
            "C.ordered.unsortedNumericInput");

        var lf = ReleaseImpactIntegrity.RawSha256("q\n"u8);
        var crlf = ReleaseImpactIntegrity.RawSha256("q\r\n"u8);
        rows.Check(lf == "4adc33bd9fe74303c344be46e5916d65182fb218e248fe80452ab3f025b06c64", "C.raw.lfSha");
        rows.Check(crlf == "375672a4697884da6e72f09b3c4a56a59c0d18ccd78887d530a184a6fd76b375",
            "C.raw.crlfSha");
        rows.Check(TestSelection.SelectorHash(_ => "q\n", _ => "q\n") ==
            TestSelection.SelectorHash(_ => "q\r\n", _ => "q\n"), "C.raw.normalizedSelector");
        rows.Check(ReleaseImpactIntegrity.RawSha256(Convert.FromHexString("EFBBBF710A")) ==
            "2606fab4b10ee18d42d71cece85a978a77032154667a8e4264cd5eb0025cdbff", "C.raw.bomSha");

        var text = envelope.ToJsonString();
        var duplicate = text.Replace("\"configuration\":", "\"configuration\":\"earlier\",\"configuration\":", StringComparison.Ordinal);
        rows.Check(RejectsWire(duplicate, "target"), "C.shape.nestedDuplicate.decodeAccepted");
        rows.Check(RejectsWire(text.Replace("\"repositoryId\":", "\"repositoryId\":\"earlier\",\"repositoryId\":",
            StringComparison.Ordinal), "envelope"), "C.shape.rootDuplicate");
        rows.Check(RejectsWire(text + "{}", "json"), "C.shape.trailing");
        foreach (var (id, value) in new[] { ("fraction", "2.5"), ("negative", "-1"), ("overflow", "4294967296"), ("null", "null") })
        {
            var changed = envelope.DeepClone();
            changed["attempt"] = JsonNode.Parse(value);
            rows.Check(RejectsWire(changed.ToJsonString(), "attempt"), "C.shape." + id);
        }
        rows.Check(RejectsWire(text.Replace("\"runId\":\"r-7\"", "\"runId\":\"\\uD800\"", StringComparison.Ordinal), "json"),
            "C.shape.unpaired");
        var malformed = Encoding.UTF8.GetBytes(text);
        malformed[Array.IndexOf(malformed, (byte)'r')] = 0xff;
        rows.Check(RejectsWire(malformed, "json"), "C.shape.utf8");

        var unknownRoot = root.DeepClone().AsObject();
        unknownRoot["format"] = "antiphon-p2-integrity-v2";
        var unknown = IndependentEnvelope(schema, unknownRoot, projections);
        rows.Check(RejectsWire(unknown.ToJsonString(), "format"), "C.version.unknownRecognized");
        unknownRoot["format"] = "antiphon-release-impact-v1";
        rows.Check(RejectsWire(IndependentEnvelope(schema, unknownRoot, projections).ToJsonString(), "format"), "C.version.legacy");

        var files = manifest.DeepClone().AsArray();
        files[0]!["path"] = "Dir/z.txt";
        files[1]!["path"] = "dir/Z.txt";
        rows.Check(ReleaseImpactIntegrity.ProjectionBytes("baselineManifest", Element(files), false).Length > 0,
            "C.keys.ordinalPairAccepted");
        rows.Check(Rejects(() => ReleaseImpactIntegrity.ProjectionBytes("baselineManifest", Element(files), true), "baselineManifest.path.target-alias"),
            "C.keys.foldedPairAccepted");
        files[0]!["path"] = "d/e\u0301.txt";
        files[1]!["path"] = "d/é.txt";
        rows.Check(Rejects(() => ReleaseImpactIntegrity.ProjectionBytes("baselineManifest", Element(files), true), "baselineManifest.path.target-alias"),
            "C.keys.normalizedPair");
        files[0]!["path"] = files[1]!["path"]!.DeepClone();
        rows.Check(Rejects(() => ReleaseImpactIntegrity.ProjectionBytes("baselineManifest", Element(files), false), "baselineManifest.duplicate-key"),
            "C.keys.duplicate");
        files[0]!["path"] = "../outside";
        rows.Check(Rejects(() => ReleaseImpactIntegrity.ProjectionBytes("baselineManifest", Element(files), false), "baselineManifest.path"),
            "C.keys.traversal");

        ReleaseImpactIntegrity.CheckCollectionCount(65_536);
        rows.Check(Rejects(() => ReleaseImpactIntegrity.CheckCollectionCount(65_537), "collection-size"),
            "C.limit.collection");
        rows.Check(ReleaseImpactIntegrity.EnvelopeLimit == 8_388_608 && ReleaseImpactIntegrity.ProofLimit == 16_777_216
            && ReleaseImpactIntegrity.AggregateProofLimit == 134_217_728 && ReleaseImpactIntegrity.DepthLimit == 64
            && ReleaseImpactIntegrity.StringLimit == 65_536, "C.limit.defaults");
        var realSize = CheckRealIndexShape(schema, projections);
        rows.Check(realSize, "C.limit.realIndex");

        string[] required =
        [
            "C.root.bytes", "C.root.sha", "C.manifest.B.bytes", "C.manifest.B.sha", "C.manifest.A.bytes", "C.manifest.A.sha",
            "C.primitives.bytes", "C.primitives.sha", "C.roles.roster", "C.transport.rootDigest", "C.transport.producer",
            "C.ordered.chainDigestsDiffer", "C.ordered.argvDigestsDiffer", "C.ordered.launchDigestsDiffer",
            "C.ordered.unsortedNumericInput", "C.raw.lfSha", "C.raw.crlfSha", "C.raw.bomSha", "C.raw.normalizedSelector", "C.shape.nestedDuplicate.decodeAccepted",
            "C.shape.rootDuplicate", "C.shape.trailing", "C.shape.fraction", "C.shape.negative", "C.shape.overflow", "C.shape.null",
            "C.shape.unpaired", "C.shape.utf8", "C.version.unknownRecognized", "C.version.legacy", "C.keys.ordinalPairAccepted",
            "C.keys.foldedPairAccepted", "C.keys.normalizedPair", "C.keys.duplicate", "C.keys.traversal",
            "C.limit.collection", "C.limit.defaults", "C.limit.realIndex",
        ];
        required = [.. required, .. expectedRoles.SelectMany(role => new[] { $"C.roles.{role}.bytes", $"C.roles.{role}.sha" })];
        rows.ShouldAllPass(required.Length, required);
    }

    private static bool CheckRealIndexShape(JsonNode schema, JsonObject example)
    {
        // The checked-in index at the design pin may have moved on another branch. Reproduce its full measured
        // shape from the pinned census without requiring Git history to be present in shallow CI.
        var projections = example.DeepClone().AsObject();
        var edges = new JsonArray();
        for (var path = 0; path < 2_627; path++)
        {
            var classes = path < 768 ? 6 : 5;
            for (var @class = 0; @class < classes; @class++)
                edges.Add(new JsonObject { ["path"] = $"src/fixture-{path:D4}.txt", ["class"] = $"Probe.Class{@class}" });
        }
        foreach (var role in new[] { "baselineIndex", "candidateIndex" })
        {
            var index = projections[role]!;
            index["rawByteCount"] = 922_895ul;
            index["edges"] = edges.DeepClone();
            index["cappedPaths"] = new JsonArray(Enumerable.Range(0, 165)
                .Select(i => (JsonNode)JsonValue.Create($"src/capped-{i:D3}.txt")).ToArray());
        }

        var envelope = IndependentEnvelope(schema, P2Json("root").AsObject(), projections);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        var result = ReleaseImpactIntegrity.Decode(stream, false);
        return stream.Length <= ReleaseImpactIntegrity.EnvelopeLimit && new[] { "baselineIndex", "candidateIndex" }.All(role =>
        {
            var index = result.Projections[role];
            return index.GetProperty("edges").GetArrayLength() == 13_903 && index.GetProperty("cappedPaths").GetArrayLength() == 165
                && index.GetProperty("rawByteCount").GetUInt64() == 922_895;
        });
    }

    private static bool RejectsWire(string wire, string component) => RejectsWire(Encoding.UTF8.GetBytes(wire), component);
    private static bool RejectsWire(byte[] wire, string component) =>
        Rejects(() => ReleaseImpactIntegrity.Decode(new MemoryStream(wire), false), component);
    private static bool Rejects(Action action, string component)
    {
        try { action(); }
        catch (ReleaseImpactIntegrity.WireException error) { return error.Component == component; }
        return false;
    }

    private static JsonObject IndependentEnvelope(JsonNode schema, JsonObject root, JsonObject projections)
    {
        var envelope = root.DeepClone().AsObject();
        foreach (var (role, value) in projections)
            envelope[role + "Digest"] = IndependentDigest(IndependentRole(schema, role, value));
        envelope["rootDigest"] = IndependentDigest(IndependentBytes(schema["root"]!, envelope));
        envelope["projections"] = projections.DeepClone();
        return envelope;
    }

    private static string IndependentDigest(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    private static byte[] IndependentRole(JsonNode schema, string role, JsonNode? value) =>
        [.. IndependentBytes(JsonValue.Create("text")!, JsonValue.Create(P2Format + "/" + role)),
            .. IndependentBytes(schema["roles"]![role]!, value)];

    // The fixture schema and this encoder are test-owned. No product framing, field enumeration, ordering or hash
    // helper is called here. The fixed vectors calibrate this implementation before it seals any proposed evidence.
    private static byte[] IndependentBytes(JsonNode shape, JsonNode? value)
    {
        using var buffer = new MemoryStream();
        void WriteU32(uint number)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, number);
            buffer.Write(bytes);
        }

        void Write(JsonNode schema, JsonNode? member)
        {
            if (schema is JsonValue scalar)
            {
                switch (scalar.GetValue<string>())
                {
                    case "u32":
                        WriteU32(Element(member).GetUInt32());
                        break;
                    case "u64":
                        Span<byte> wide = stackalloc byte[8];
                        BinaryPrimitives.WriteUInt64BigEndian(wide, Element(member).GetUInt64());
                        buffer.Write(wide);
                        break;
                    case "boolean":
                        buffer.WriteByte(member!.GetValue<bool>() ? (byte)1 : (byte)0);
                        break;
                    default:
                        var bytes = Encoding.UTF8.GetBytes(member!.GetValue<string>());
                        WriteU32((uint)bytes.Length);
                        buffer.Write(bytes);
                        break;
                }
                return;
            }
            if (schema is JsonObject record)
            {
                foreach (var (key, fieldShape) in record)
                    Write(fieldShape!, member![key]);
                return;
            }
            var type = schema[0]!.GetValue<string>();
            if (type == "optional")
            {
                buffer.WriteByte(member is null ? (byte)0 : (byte)1);
                if (member is not null) Write(schema[1]!, member);
                return;
            }

            var items = member!.AsArray().ToArray();
            if (type == "set")
            {
                var keys = schema[2]!.AsArray().Select(key => key!.GetValue<string>()).ToArray();
                Array.Sort(items, (left, right) =>
                {
                    if (keys.Length == 0) return Compare(left, right);
                    foreach (var key in keys)
                    {
                        var difference = Compare(left![key], right![key]);
                        if (difference != 0) return difference;
                    }
                    return 0;
                });
            }
            WriteU32((uint)items.Length);
            foreach (var item in items) Write(schema[1]!, item);
        }

        static int Compare(JsonNode? left, JsonNode? right)
        {
            if (left is null || right is null) return left is null ? right is null ? 0 : -1 : 1;
            if (left is JsonValue text && text.TryGetValue<string>(out var leftText))
                return string.CompareOrdinal(leftText, right.GetValue<string>());
            return Element(left).GetUInt64().CompareTo(Element(right).GetUInt64());
        }

        Write(shape, value);
        return buffer.ToArray();
    }
}
