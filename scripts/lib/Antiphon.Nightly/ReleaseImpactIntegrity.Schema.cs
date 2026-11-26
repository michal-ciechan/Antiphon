#nullable enable
using System.Collections.Generic;

namespace Antiphon.Nightly;

public static partial class ReleaseImpactIntegrity
{
    // The role order is the D-3 root order, excluding non-projection fields. Records below
    // are ordered tuples. Set keys name the full identity and sequences preserve program order.
    private static Dictionary<string, Shape> CreateRoles()
    {
        var side = Enum("B", "A");
        var file = Record(("path", Path), ("gitMode", Enum("100644", "100755", "120000", "160000")),
            ("blobId", GitId), ("rawByteCount", UInt64), ("rawSha256", Digest));
        var files = Set(file, "path");
        var property = Record(("name", Text), ("value", Text));
        var observed = Record(("side", side), ("chunk", Optional(Text)), ("role", Text),
            ("digest", Digest), ("provenanceDigest", Digest), ("sourceSha", GitId));
        var contract = Record(("catalogHash", Digest), ("policyHash", Digest), ("runtimeHash", Digest), ("selectorHash", Digest));
        var metadata = Record(("name", Text), ("digest", Digest));
        var simpleEdge = Record(("path", Path), ("class", Text));
        var index = Record(("side", side), ("commit", GitId), ("path", Path), ("blobId", GitId),
            ("rawByteCount", UInt64), ("rawSha256", Digest), ("schema", UInt32), ("builderDigest", Digest),
            ("builderInputs", files), ("metadata", Set(metadata, "name")),
            ("edges", Set(simpleEdge, "path", "class")), ("cappedPaths", Set(Path)));
        var qualifiedEdge = Record(("kind", Enum("derived", "owner", "project", "runtime", "linked", "generator")),
            ("source", Path), ("destination", Path), ("class", Text));
        var @class = Record(("project", Path), ("class", Text));
        var identity = Record(("project", Path), ("assembly", Text), ("class", Text), ("method", Text), ("caseUid", Text));
        var identities = Set(identity, "project", "assembly", "class", "method", "caseUid");
        var chunk = Record(("chunk", Text), ("suite", Text), ("executionKind", Text), ("project", Path),
            ("contentDigest", Digest), ("baselineInputDigest", Optional(Digest)), ("candidateInputDigest", Digest),
            ("cases", identities));
        var inventory = Record(("format", Text), ("runId", Text), ("attempt", UInt32), ("revision", UInt32),
            ("sha", GitId), ("rawByteCount", UInt64), ("rawSha256", Digest));
        var origin = Record(("sha", GitId), ("runId", Text), ("attempt", UInt32), ("inventorySeal", Digest),
            ("evidenceId", Path), ("evidenceDigest", Digest));
        var result = Record(("caseUid", Text), ("class", Text), ("method", Text), ("outcome", Text));
        var receipt = Record(("chunk", Text), ("disposition", Enum("executed", "carried", "missing")),
            ("origin", Optional(origin)), ("platform", Text), ("architecture", Text), ("profile", Text),
            ("contentDigest", Optional(Digest)), ("inputDigest", Optional(Digest)), ("result", Text),
            ("exitCode", Optional(UInt32)), ("skipped", Boolean), ("timedOut", Boolean), ("tolerated", Boolean),
            ("expectedCases", identities), ("observedCount", UInt32), ("observedCases", Sequence(result)),
            ("evidenceKind", Optional(Enum("trx"))), ("reason", Optional(Text)));
        var obligation = Record(("chunk", Text), ("reason", Text));
        var history = Record(("ordinal", UInt32), ("sha", GitId), ("runId", Text), ("attempt", UInt32),
            ("revision", UInt32), ("chunk", Text), ("status", Enum("open", "closed", "withdrawn")), ("reason", Text));
        var owned = Record(("chunk", Text), ("cases", Set(Text)));
        var grant = Record(("project", Path), ("configurations", Set(Text)), ("downstream", Set(Path)),
            ("owned", Set(owned, "chunk")), ("contractProducer", Text));
        var trustContract = Record(("project", Path), ("side", side), ("sha", GitId),
            ("extractor", Text), ("contractHash", Digest), ("evidenceDigest", Digest));
        var reason = Record(("kind", Text), ("path", Optional(Path)), ("side", Optional(side)), ("chain", Sequence(Text)));
        var decision = Record(("chunk", Text), ("disposition", Enum("execute", "held", "carry-candidate")),
            ("origin", Optional(origin)), ("reasons", Sequence(reason)));

        return new Dictionary<string, Shape>(System.StringComparer.Ordinal)
        {
            ["target"] = Record(("os", Enum("linux", "windows", "macos")), ("architecture", Text),
                ("profile", Text), ("tfm", Text), ("rid", Text), ("configuration", Text), ("foldedPaths", Boolean),
                ("buildProperties", Set(property, "name")), ("argv", Sequence(Text)), ("observed", Set(observed, "side", "role", "chunk"))),
            ["algorithm"] = Record(("rawClosureDigest", Digest), ("p2Codec", Enum(Format)), ("evaluator", Text),
                ("graph", Text), ("extractor", Text), ("replaySelector", Digest), ("sources", files)),
            ["baselineManifest"] = files,
            ["candidateManifest"] = files,
            ["compatibility"] = Record(("baseline", contract), ("candidate", contract),
                ("scoped", Set(property, "name")), ("globalReasons", Set(Text))),
            ["baselineIndex"] = index,
            ["candidateIndex"] = index,
            ["baselineGraph"] = Set(qualifiedEdge, "kind", "source", "destination", "class"),
            ["candidateGraph"] = Set(qualifiedEdge, "kind", "source", "destination", "class"),
            ["diff"] = Set(Record(("path", Path), ("status", Enum("A", "D", "M", "T")), ("side", side)), "path", "side"),
            ["universe"] = identities,
            ["roster"] = Sequence(chunk),
            ["inventory"] = inventory,
            ["receipts"] = Set(receipt, "chunk"),
            ["obligations"] = Record(("watermark", Text), ("observedAt", Text), ("approved", Boolean), ("withdrawn", Boolean),
                ("history", Set(history, "ordinal")), ("owed", Set(obligation, "chunk"))),
            ["trust"] = Record(("format", Enum(RcTrustedProjectGate.Format)), ("baselinePolicy", file), ("candidatePolicy", file),
                ("baselinePolicyDigest", Digest), ("candidatePolicyDigest", Digest), ("grants", Set(grant, "project")),
                ("contracts", Set(trustContract, "project", "side")), ("ownExecutions", Set(receipt, "chunk")), ("reasons", Set(Text))),
            ["selection"] = Record(("complete", Boolean), ("fallback", Enum("Full", "Scoped")),
                ("globalReasons", Set(Text)), ("incompleteReasons", Set(Text)), ("classes", Set(@class, "project", "class")),
                ("decisions", Sequence(decision))),
        };
    }
}
