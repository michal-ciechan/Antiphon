using System.Diagnostics;
using System.Text;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S1: the rc result ledger. A row carries only on the same platform and input digest, from a closed
/// chunk with complete provenance; everything else is dirty. The reader is default-deny: duplicate (also
/// case-folded) names, unknown names, wrong JSON types and missing fields are ledger-invalid before any carry.
/// </summary>
[Category("Unit")]
[Timeout(60_000)]
public sealed class RcIncrementalLedgerTests : IDisposable
{
    private static readonly string Same = new('a', 64);
    private static readonly string Other = new('b', 64);
    private const string ChunkId = "b-core-aaaaaaaaaaaaaaaa";

    private static RcLedgerIdentity Id(string outcome, string digest, string boundary, string method) => new()
    {
        Suite = "antiphon", ClassName = "SampleTests", Method = method, Boundary = boundary, InputDigest = digest, Outcome = outcome, DurationSeconds = 1.25,
    };

    private static RcLedgerChunk Chunk(string digest, string boundary, string outcome, params string[] methods) => new()
    {
        ChunkId = ChunkId, Boundary = boundary, Suite = "antiphon", IdentityKeys = methods.Select(m => "SampleTests/" + m).ToArray(),
        InputDigest = digest, Outcome = outcome, DurationSeconds = 2.5,
    };

    private static RcLedgerDocument Ledger(string platform, RcLedgerIdentity[] identities, RcLedgerChunk[] chunks, params string[] forcedFull) => new()
    {
        Platform = platform, Repository = "antiphon", CandidateId = "cand-1", CandidateSha = new string('1', 40), RunId = "run-1", Attempt = 1,
        Contract = new RcLedgerContract { PolicyHash = "p", CatalogHash = "c", SelectorHash = "s", RuntimeHash = "r" },
        ForcedFull = forcedFull, Identities = identities, Chunks = chunks,
    };

    private static RcLedgerDocument Ledger(string platform, RcLedgerIdentity identity, RcLedgerChunk chunk) => Ledger(platform, [identity], [chunk]);

    private static RcCarryDecision Decide(string platform, string boundary, string newDigest, RcLedgerDocument ledger, RcLedgerIdentity identity,
        RcLedgerChunk chunk, RcLedgerIdentity[] history = null, bool fixProven = false, string[] roster = null) =>
        RcLedger.IdentityCarryDecision(platform, boundary, newDigest, ledger, identity, chunk, history, fixProven, roster, []);

    private static RcCarryDecision ChunkDecide(RcLedgerDocument ledger, RcLedgerChunk chunk, params RcCarryDecision[] decisions) =>
        RcLedger.ChunkCarryDecision("windows", Same, ledger, chunk, decisions, []);

    private static string SampleJson(RcLedgerIdentity identity = null, RcLedgerChunk chunk = null) =>
        RcLedger.ToJson(RcLedger.Normalize(Ledger("windows", identity ?? Id("passed", Same, "core", "Runs"), chunk ?? Chunk(Same, "core", "passed", "Runs"))));

    private static string UpdateOnce(string text, string old, string replacement)
    {
        var at = text.IndexOf(old, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, "fixture missing " + old);
        return text.Remove(at, old.Length).Insert(at, replacement);
    }

    private static RcLedgerIdentity Carried()
    {
        var carried = Id("passed", Same, "core", "Runs");
        carried.CarriedFrom = new RcCarriedFrom { CandidateId = "cand-0", RunId = "run-0", CandidateSha = new string('2', 40), ChunkId = ChunkId, InputDigest = Same };
        return carried;
    }

    private readonly string directory = Directory.CreateTempSubdirectory("rl-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string WriteLedger(string name, string text, bool bom = false, bool crlf = false)
    {
        if (crlf) text = text.Replace(",\"", ",\r\n\"");
        var path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, text, new UTF8Encoding(bom));
        return path;
    }

    private void AssertRejected(string label, string text, bool bom = false, bool crlf = false, string token = "ledger-invalid")
    {
        var path = WriteLedger(label, text, bom, crlf);
        var refusal = Should.Throw<RcLedgerRefusal>(() => RcLedger.Read(path), label);
        refusal.Message.ShouldStartWith("ledger-invalid", customMessage: label);
        refusal.Message.ShouldContain(token, customMessage: label);
    }

    private static void AssertCarriesFile(string path)
    {
        var document = RcLedger.Read(path);
        var identity = document.Identities[0];
        var decision = Decide("windows", "core", identity.InputDigest, document, identity, document.Chunks[0]);
        decision.Decision.ShouldBe("carry", path);
        decision.Reason.ShouldBe("carry", path);
        identity.Outcome.ShouldBe("passed", path);
    }

    [Test]
    public void MissingDigestIsDirty()
    {
        var id = Id("passed", Same, "core", "Runs");
        var chunk = Chunk(Same, "core", "passed", "Runs");
        var ledger = Ledger("windows", id, chunk);
        foreach (var stored in new[] { "unknown", "", "ab", new string('A', 64) })
        {
            var missing = Decide("windows", "core", Same, ledger, Id("passed", stored, "core", "Runs"), chunk);
            missing.Reason.ShouldBe("missing-digest", "stored digest " + stored);
            missing.Decision.ShouldBe("dirty");
            missing.CarriedFrom.ShouldBeNull();
        }
        Decide("windows", "core", "unknown", ledger, id, chunk).Reason.ShouldBe("missing-digest");
        var changed = Decide("windows", "core", Other, ledger, id, chunk);
        changed.Reason.ShouldBe("digest-mismatch");
        changed.CarriedFrom.ShouldBeNull();
        RcLedger.CompareInputDigest("unknown", Same).ShouldBe(RcDigestRelation.Invalid);
        RcLedger.CompareInputDigest(Same, Same).ShouldBe(RcDigestRelation.Same);
        RcLedger.CompareInputDigest(Same, Other).ShouldBe(RcDigestRelation.Different);
        Decide("windows", "core", Same, null, null, null).Reason.ShouldBe("missing-row");
    }

    [Test]
    public void UnexpectedSkipNeverCarries()
    {
        var id = Id("passed", Same, "core", "Runs");
        var chunk = Chunk(Same, "core", "passed", "Runs");
        var ledger = Ledger("windows", id, chunk);
        var skip = Decide("windows", "core", Same, ledger, Id("unexpected-skip", Same, "core", "Runs"), chunk, roster: ["SampleTests/Runs"]);
        skip.Reason.ShouldBe("unexpected-skip");
        skip.CarriedFrom.ShouldBeNull();
        var raw = Id("NotExecuted", Same, "core", "Runs");
        Decide("windows", "core", Same, ledger, raw, chunk).Reason.ShouldBe("unexpected-skip");
        var named = Decide("windows", "core", Same, ledger, raw, chunk, roster: ["SampleTests/Runs"]);
        named.Decision.ShouldBe("carry");
        named.Outcome.ShouldBe("expected-skip");
        foreach (var outcome in new[] { "interrupted", "not-run", "failed" })
            Decide("windows", "core", Same, ledger, Id(outcome, Same, "core", "Runs"), chunk).Reason.ShouldBe(outcome);
        var openChunk = Chunk(Same, "core", "interrupted", "Runs");
        Decide("windows", "core", Same, ledger, id, openChunk).Reason.ShouldBe("chunk-interrupted");
        ChunkDecide(ledger, openChunk, named).Reason.ShouldBe("chunk-interrupted");
    }

    [Test]
    public void CrossPlatformNeverCarries()
    {
        var id = Id("passed", Same, "core", "Runs");
        var chunk = Chunk(Same, "core", "passed", "Runs");
        var linux = Ledger("linux", id, chunk);
        var toWindows = Decide("windows", "core", Same, linux, id, chunk);
        toWindows.Reason.ShouldBe("cross-platform");
        toWindows.CarriedFrom.ShouldBeNull();
        Decide("linux", "core", Same, Ledger("windows", id, chunk), id, chunk).Reason.ShouldBe("cross-platform");
        var samePlatform = Decide("linux", "core", Same, linux, id, chunk);
        samePlatform.Decision.ShouldBe("carry");
        samePlatform.CarriedFrom.RunId.ShouldBe("run-1");
        samePlatform.CarriedFrom.CandidateId.ShouldBe("cand-1");
        ChunkDecide(linux, chunk, samePlatform).Reason.ShouldBe("cross-platform");
    }

    [Test]
    public void SameDigestCarriesProvenance()
    {
        var id = Id("passed", Same, "core", "Runs");
        var chunk = Chunk(Same, "core", "passed", "Runs");
        var ledger = Ledger("windows", id, chunk);
        var carry = Decide("windows", "core", Same, ledger, id, chunk);
        carry.Decision.ShouldBe("carry");
        carry.Reason.ShouldBe("carry");
        carry.CarriedFrom.RunId.ShouldBe("run-1");
        carry.CarriedFrom.CandidateId.ShouldBe("cand-1");
        carry.CarriedFrom.CandidateSha.ShouldBe(new string('1', 40));
        carry.CarriedFrom.ChunkId.ShouldBe(ChunkId);
        carry.CarriedFrom.InputDigest.ShouldBe(Same);

        var fail = Id("failed", Same, "core", "Runs");
        var pass = Id("passed", Same, "core", "Runs");
        var flake = Decide("windows", "core", Same, ledger, pass, chunk, [fail, pass]);
        flake.Reason.ShouldBe("flake");
        flake.CarriedFrom.ShouldBeNull();
        Decide("windows", "core", Same, ledger, pass, chunk, [pass, fail]).Reason.ShouldBe("flake", "flake order");
        var proven = Decide("windows", "core", Same, ledger, pass, chunk, [fail, pass], fixProven: true);
        proven.Decision.ShouldBe("carry");
        proven.CarriedFrom.RunId.ShouldBe("run-1");
        Decide("windows", "unassigned", Same, ledger, id, chunk).Reason.ShouldBe("unknown-boundary");
        Decide("windows", "unknown", Same, ledger, id, chunk).Reason.ShouldBe("unknown-boundary");

        ChunkDecide(ledger, chunk, carry).Decision.ShouldBe("carry");
        var otherDecision = new RcCarryDecision { Decision = "dirty", Reason = "flake", IdentityKey = "SampleTests/Other" };
        var forward = ChunkDecide(ledger, chunk, carry, otherDecision);
        forward.Reason.ShouldBe("identity:SampleTests/Other:flake");
        ChunkDecide(ledger, chunk, otherDecision, carry).Reason.ShouldBe(forward.Reason, "chunk order");

        var second = Id("passed", Same, "core", "Other");
        var both = Chunk(Same, "core", "passed", "Runs", "Other");
        var docA = RcLedger.Normalize(Ledger("windows", [second, id], [both]));
        var docB = RcLedger.Normalize(Ledger("windows", [id, second], [both]));
        var jsonA = RcLedger.ToJson(docA);
        jsonA.ShouldBe(RcLedger.ToJson(docB), "writer order");
        RcLedger.ToJson(RcLedger.Normalize(Ledger("windows", [id], [Chunk(Same, "core", "passed", "Runs")], "later", "earlier")))
            .ShouldContain("\"forcedFull\":[\"earlier\",\"later\"]");
        jsonA.ShouldContain("\"format\":\"antiphon-rc-ledger-v1\"");

        var pathA = Path.Combine(directory, "a.json");
        RcLedger.Write(pathA, docA);
        RcLedger.Write(Path.Combine(directory, "b.json"), docB);
        var read = RcLedger.Read(pathA);
        read.Format.ShouldBe("antiphon-rc-ledger-v1");
        RcLedger.CheckDocument(read).Errors.ShouldBeEmpty();
        Should.Throw<RcLedgerRefusal>(() => RcLedger.Write(pathA, docA)).Message.ShouldBe("ledger-exists");
    }

    [Test]
    public void DuplicateOutcomeNeverCarries() =>
        AssertRejected("duplicate-outcome", UpdateOnce(SampleJson(), "\"outcome\":\"passed\"", "\"outcome\":\"failed\",\"outcome\":\"passed\""));

    [Test]
    public void NestedDuplicateIsInvalid()
    {
        var json = SampleJson();
        AssertRejected("duplicate-contract", UpdateOnce(json, "\"policyHash\":\"p\"", "\"policyHash\":\"p\",\"policyHash\":\"q\""));
        AssertRejected("duplicate-chunk", UpdateOnce(json, "\"chunkId\":\"" + ChunkId + "\"", "\"chunkId\":\"" + ChunkId + "\",\"chunkId\":\"b-core-bbbbbbbbbbbbbbbb\""));
        AssertRejected("duplicate-carried", UpdateOnce(SampleJson(Carried()), "\"candidateId\":\"cand-0\"", "\"candidateId\":\"cand-0\",\"candidateId\":\"cand-9\""));
    }

    [Test]
    public void CaseDuplicateIsInvalid()
    {
        var json = SampleJson();
        AssertRejected("case-outcome", UpdateOnce(json, "\"outcome\":\"passed\"", "\"outcome\":\"failed\",\"Outcome\":\"passed\""), token: "duplicate-property");
        AssertRejected("case-contract", UpdateOnce(json, "\"policyHash\":\"p\"", "\"policyHash\":\"p\",\"PolicyHash\":\"q\""), token: "duplicate-property");
    }

    [Test]
    public void UnknownPropertyIsInvalid()
    {
        var json = SampleJson();
        AssertRejected("unknown-top", UpdateOnce(json, "{\"format\":", "{\"doNotCarry\":true,\"format\":"));
        AssertRejected("unknown-contract", UpdateOnce(json, "\"policyHash\":\"p\"", "\"policyHash\":\"p\",\"extra\":true"));
        AssertRejected("unknown-identity", UpdateOnce(json, "{\"suite\":\"antiphon\"", "{\"extra\":1,\"suite\":\"antiphon\""));
        AssertRejected("unknown-chunk", UpdateOnce(json, "{\"chunkId\":", "{\"extra\":true,\"chunkId\":"));
        // CARD-1842: hostId is a v2 chunk field only; a v1 chunk that carries it stays invalid.
        AssertRejected("v1-chunk-host", UpdateOnce(json, "{\"chunkId\":", "{\"hostId\":\"server2\",\"chunkId\":"));
        AssertRejected("unknown-carried", UpdateOnce(SampleJson(Carried()), "\"carriedFrom\":{\"candidateId\":", "\"carriedFrom\":{\"extra\":true,\"candidateId\":"));
    }

    [Test]
    public void WrongTypeIsInvalid()
    {
        var json = SampleJson();
        AssertRejected("number-repository", json.Replace("\"repository\":\"antiphon\"", "\"repository\":1"));
        AssertRejected("string-attempt", json.Replace("\"attempt\":1", "\"attempt\":\"1\""));
        const string contract = "\"contract\":{\"policyHash\":\"p\",\"catalogHash\":\"c\",\"selectorHash\":\"s\",\"runtimeHash\":\"r\"}";
        AssertRejected("bool-contract", json.Replace(contract, "\"contract\":true"));
        AssertRejected("array-contract", json.Replace(contract, "\"contract\":[]"));
        var start = json.IndexOf("\"identities\":[", StringComparison.Ordinal);
        var end = json.IndexOf("],\"chunks\":", StringComparison.Ordinal);
        AssertRejected("bool-identity", json.Remove(start, end - start + 1).Insert(start, "\"identities\":[true]"));
    }

    [Test]
    public void MissingFieldIsInvalid()
    {
        var json = SampleJson();
        AssertRejected("missing-format", json.Replace("\"format\":\"antiphon-rc-ledger-v1\",", ""));
        AssertRejected("missing-forced", json.Replace("\"forcedFull\":[],", ""));
        AssertRejected("missing-duration", UpdateOnce(json, "\"durationSeconds\":1.25,", ""));
        AssertRejected("missing-outcome", UpdateOnce(json, "\"outcome\":\"passed\",", ""));
        AssertRejected("truncated", json.Substring(0, 40));
        AssertRejected("newer-format", json.Replace("antiphon-rc-ledger-v1", "antiphon-rc-ledger-v3"));
    }

    [Test]
    public void BomAndCrlfStillRead()
    {
        var json = SampleJson();
        var bomPath = WriteLedger("bom", json, bom: true);
        File.ReadAllBytes(bomPath).Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        AssertCarriesFile(bomPath);
        var crlfPath = WriteLedger("crlf", json, crlf: true);
        File.ReadAllText(crlfPath).ShouldContain("\r\n");
        AssertCarriesFile(crlfPath);
        AssertCarriesFile(WriteLedger("both", json, bom: true, crlf: true));
        var duplicate = UpdateOnce(json, "\"outcome\":\"passed\"", "\"outcome\":\"failed\",\"outcome\":\"passed\"");
        AssertRejected("crlf-duplicate", duplicate, crlf: true);
        AssertRejected("bom-duplicate", duplicate, bom: true);
    }

    [Test]
    public void LargeLedgerStaysLinear()
    {
        const int count = 2500;
        var sb = new StringBuilder("{\"format\":\"antiphon-rc-ledger-v1\",\"platform\":\"windows\",\"repository\":\"antiphon\",\"candidateId\":\"cand-1\",\"candidateSha\":\"")
            .Append('1', 40)
            .Append("\",\"baselineSha\":null,\"baselineRef\":null,\"runId\":\"run-1\",\"attempt\":1,\"contract\":{\"policyHash\":\"p\",\"catalogHash\":\"c\",\"selectorHash\":\"s\",\"runtimeHash\":\"r\"},\"forcedFull\":[],\"identities\":[");
        var keys = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) { sb.Append(','); keys.Append(','); }
            keys.Append("\"SampleTests/M").Append(i).Append('"');
            sb.Append("{\"suite\":\"antiphon\",\"className\":\"SampleTests\",\"method\":\"M").Append(i)
                .Append("\",\"boundary\":\"core\",\"inputDigest\":\"").Append(Same).Append("\",\"outcome\":\"passed\",\"durationSeconds\":1,\"carriedFrom\":null}");
        }
        sb.Append("],\"chunks\":[{\"chunkId\":\"").Append(ChunkId).Append("\",\"boundary\":\"core\",\"suite\":\"antiphon\",\"identityKeys\":[").Append(keys)
            .Append("],\"inputDigest\":\"").Append(Same).Append("\",\"outcome\":\"passed\",\"durationSeconds\":1}]}");
        var wide = sb.ToString();
        wide.Length.ShouldBeGreaterThan(400_000);
        // 14198 ms in PowerShell on 2026-11-10; about 160 ms compiled. The bounds are safety margins, not targets.
        var valid = Stopwatch.StartNew();
        AssertCarriesFile(WriteLedger("wide-valid", wide));
        valid.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        const string marker = "\"outcome\":\"passed\",\"durationSeconds\":1,\"carriedFrom\":null";
        var at = wide.LastIndexOf(marker, StringComparison.Ordinal);
        var bad = wide.Remove(at, marker.Length).Insert(at, "\"outcome\":\"failed\",\"outcome\":\"passed\",\"durationSeconds\":1,\"carriedFrom\":null");
        var duplicate = Stopwatch.StartNew();
        AssertRejected("wide-duplicate", bad);
        duplicate.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8));
    }

    [Test]
    public void WellFormedRowStillCarries()
    {
        var path = WriteLedger("valid", SampleJson());
        AssertCarriesFile(path);
        RcLedger.CheckDocument(RcLedger.Read(path)).Errors.ShouldBeEmpty();
    }
}
