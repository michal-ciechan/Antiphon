using System.Security.Cryptography;
using System.Text;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1842 S6: ledger durations and the chunk host. Chunk durations come from the timing sidecar of the same suite
/// and chunk; identity durations are the summed TRX results of the same class and method. Only the stamp's
/// include-host path writes antiphon-rc-ledger-v2 and a chunk host, and the host is the caller's argument.
/// </summary>
[Category("Unit")]
[Timeout(60_000)]
public sealed class RcLedgerDurationTests : IDisposable
{
    private static readonly string DigestA = new('a', 64);
    private static readonly string DigestB = new('b', 64);
    private const string ChunkA = "b-core-aaaaaaaaaaaaaaaa";
    private const string ChunkB = "b-core-bbbbbbbbbbbbbbbb";

    private readonly string directory = Directory.CreateTempSubdirectory("rd-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private sealed record Fixture(string Ledger, string[] Timing, string[] Trx, string Output);

    private static RcLedgerIdentity Identity(string method, string digest) => new()
    {
        Suite = "antiphon", ClassName = "SampleTests", Method = method, Boundary = "core", InputDigest = digest, Outcome = "passed",
    };

    private static RcLedgerDocument SampleDocument() => RcLedger.Normalize(new RcLedgerDocument
    {
        Platform = "linux", Repository = "antiphon", CandidateId = "cand-1", CandidateSha = new string('1', 40), RunId = "run-1", Attempt = 1,
        Contract = new RcLedgerContract { PolicyHash = "p", CatalogHash = "c", SelectorHash = "s", RuntimeHash = "r" },
        ForcedFull = [],
        Identities = [Identity("Runs", DigestA), Identity("Other", DigestA), Identity("Third", DigestB)],
        Chunks =
        [
            new RcLedgerChunk { ChunkId = ChunkA, Boundary = "core", Suite = "antiphon", IdentityKeys = ["SampleTests/Runs", "SampleTests/Other"], InputDigest = DigestA, Outcome = "passed" },
            new RcLedgerChunk { ChunkId = ChunkB, Boundary = "core", Suite = "antiphon", IdentityKeys = ["SampleTests/Third"], InputDigest = DigestB, Outcome = "passed" },
        ],
    });

    private string NewDirectory(string prefix) => Directory.CreateDirectory(Path.Combine(directory, prefix + "-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void WriteUtf8(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    private Fixture NewFixture()
    {
        var dir = NewDirectory("fixture");
        var ledger = Path.Combine(dir, "ledger-in.json");
        RcLedger.Write(ledger, SampleDocument());
        var timingA = Path.Combine(dir, "antiphon-" + ChunkA + ".timing.json");
        WriteUtf8(timingA, "{\"suite\":\"antiphon\",\"chunk\":\"" + ChunkA + "\",\"lastPhase\":\"complete\",\"runWallSeconds\":42.5}");
        // The second chunk's sidecar has no wall; its duration stays null.
        var timingB = Path.Combine(dir, "antiphon-" + ChunkB + ".timing.json");
        WriteUtf8(timingB, "{\"suite\":\"antiphon\",\"chunk\":\"" + ChunkB + "\",\"lastPhase\":\"run\"}");
        // An unrelated sidecar must not land on either chunk.
        var timingC = Path.Combine(dir, "other-x.timing.json");
        WriteUtf8(timingC, "{\"suite\":\"other\",\"chunk\":\"" + ChunkB + "\",\"lastPhase\":\"complete\",\"runWallSeconds\":999}");
        var trx = Path.Combine(dir, "run.trx");
        WriteUtf8(trx, "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results>"
            + "<UnitTestResult testId=\"1\" testName=\"Runs\" outcome=\"Passed\" duration=\"00:00:01.2500000\" />"
            + "<UnitTestResult testId=\"2\" testName=\"Other(1)\" outcome=\"Passed\" duration=\"00:00:00.5000000\" />"
            + "<UnitTestResult testId=\"3\" testName=\"Other(2)\" outcome=\"Passed\" duration=\"00:00:00.2500000\" />"
            + "<UnitTestResult testId=\"4\" testName=\"Runs\" outcome=\"Passed\" duration=\"00:01:00.0000000\" />"
            + "</Results><TestDefinitions>"
            + "<UnitTest id=\"1\" name=\"Runs\"><TestMethod className=\"SampleTests\" name=\"Runs\" /></UnitTest>"
            + "<UnitTest id=\"2\" name=\"Other(1)\"><TestMethod className=\"SampleTests\" name=\"Other\" /></UnitTest>"
            + "<UnitTest id=\"3\" name=\"Other(2)\"><TestMethod className=\"SampleTests\" name=\"Other\" /></UnitTest>"
            + "<UnitTest id=\"4\" name=\"Runs\"><TestMethod className=\"ElsewhereTests\" name=\"Runs\" /></UnitTest>"
            + "</TestDefinitions><ResultSummary outcome=\"Completed\"><Counters total=\"4\" executed=\"4\" passed=\"4\" failed=\"0\" /></ResultSummary></TestRun>");
        return new Fixture(ledger, [timingA, timingB, timingC], [trx], Path.Combine(dir, "ledger-out.json"));
    }

    private static void Stamp(Fixture fixture, bool includeHost = false, string hostId = null, string os = null) =>
        RcLedger.Stamp(fixture.Ledger, fixture.Output, fixture.Timing, fixture.Trx, includeHost, hostId, os);

    private static string Refusal(Action action) => Should.Throw<RcLedgerRefusal>(action).Message;

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertDurations(RcLedgerDocument document)
    {
        document.Chunks.Single(c => c.ChunkId == ChunkA).DurationSeconds.ShouldBe(42.5);
        document.Chunks.Single(c => c.ChunkId == ChunkB).DurationSeconds.ShouldBeNull();
        document.Identities.Single(i => i.Method == "Runs").DurationSeconds.ShouldBe(1.25);
        document.Identities.Single(i => i.Method == "Other").DurationSeconds!.Value.ShouldBe(0.75, 0.000001, "Other sums its cases");
        document.Identities.Single(i => i.Method == "Third").DurationSeconds.ShouldBeNull();
    }

    private void AssertReadRefused(string label, string text, string token)
    {
        var path = Path.Combine(NewDirectory("reject"), label + ".json");
        WriteUtf8(path, text);
        var message = Refusal(() => RcLedger.Read(path));
        message.ShouldStartWith("ledger-invalid", customMessage: label);
        message.ShouldContain(token, customMessage: label);
    }

    private void AssertReads(string label, string text)
    {
        var path = Path.Combine(NewDirectory("read"), label + ".json");
        WriteUtf8(path, text);
        Should.NotThrow(() => RcLedger.Read(path), label);
    }

    [Test]
    public void V1DurationFromSidecarAndTrx()
    {
        var fixture = NewFixture();
        var inputSha = Sha(fixture.Ledger);
        Stamp(fixture);
        var read = RcLedger.Read(fixture.Output);
        read.Format.ShouldBe(RcLedger.Format);
        AssertDurations(read);
        Sha(fixture.Ledger).ShouldBe(inputSha, "input ledger changed");
        // In memory: the filler sets durations and leaves the format and the host alone.
        var filled = RcLedger.SetDurations(RcLedger.Read(fixture.Ledger), fixture.Timing, fixture.Trx);
        filled.Format.ShouldBe(RcLedger.Format);
        AssertDurations(filled);
        foreach (var chunk in filled.Chunks) (chunk.HostIdNamed || chunk.OsNamed).ShouldBeFalse("filler set a host");
        RcLedger.CheckDocument(filled).Errors.ShouldBeEmpty();
    }

    [Test]
    public void V1RejectsHostBeforeWrite()
    {
        // A v1 chunk that carries a host is refused before any byte is written.
        var document = SampleDocument();
        document.Chunks[0].HostId = "server2";
        var path = Path.Combine(NewDirectory("write"), "v1-host.json");
        Refusal(() => RcLedger.Write(path, document)).ShouldStartWith("ledger-invalid");
        File.Exists(path).ShouldBeFalse("v1 host file was written");
        var osDocument = SampleDocument();
        osDocument.Chunks[1].Os = "linux";
        var osPath = Path.Combine(NewDirectory("write"), "v1-os.json");
        Refusal(() => RcLedger.Write(osPath, osDocument)).ShouldStartWith("ledger-invalid");
        File.Exists(osPath).ShouldBeFalse("v1 os file was written");
        // The host setter refuses a v1 document and leaves it untouched.
        var plain = SampleDocument();
        Refusal(() => RcLedger.SetHost(plain, "server2", "linux")).ShouldBe("ledger-schema");
        plain.Chunks[0].HostIdNamed.ShouldBeFalse("v1 host setter changed the document");
        // A host argument without include-host is refused before the output exists.
        var fixture = NewFixture();
        Refusal(() => Stamp(fixture, hostId: "server2", os: "linux")).ShouldStartWith("ledger-host:");
        File.Exists(fixture.Output).ShouldBeFalse("host without include-host wrote the output");
        // A v1 file with a chunk host stays invalid on read.
        var json = RcLedger.ToJson(SampleDocument());
        AssertReadRefused("v1-read-host", json.Replace("{\"chunkId\":", "{\"hostId\":\"server2\",\"chunkId\":"), "hostId");
        AssertReadRefused("v1-read-os", json.Replace("{\"chunkId\":", "{\"os\":\"linux\",\"chunkId\":"), "os");
    }

    [Test]
    public void V2AcceptsExplicitHost()
    {
        var fixture = NewFixture();
        Stamp(fixture, includeHost: true, hostId: "server2", os: "linux");
        var read = RcLedger.Read(fixture.Output);
        read.Format.ShouldBe(RcLedger.FormatV2);
        AssertDurations(read);
        foreach (var chunk in read.Chunks)
        {
            chunk.HostId.ShouldBe("server2");
            chunk.Os.ShouldBe("linux");
        }
        RcLedger.CheckDocument(read).Errors.ShouldBeEmpty();
        // The host is never invented: include-host without a host id, or with an unknown os, is refused.
        var missing = NewFixture();
        Refusal(() => Stamp(missing, includeHost: true, os: "linux")).ShouldBe("ledger-host: hostId");
        File.Exists(missing.Output).ShouldBeFalse("missing host wrote the output");
        var unix = NewFixture();
        Refusal(() => Stamp(unix, includeHost: true, hostId: "server2", os: "Unix")).ShouldBe("ledger-host: os");
        File.Exists(unix.Output).ShouldBeFalse("Unix os wrote the output");
        // SetHost on a v2 document sets exactly the passed values.
        var document = SampleDocument();
        document.Format = RcLedger.FormatV2;
        var hosted = RcLedger.SetHost(document, "desk-1", "windows");
        hosted.Chunks[0].HostId.ShouldBe("desk-1");
        hosted.Chunks[1].Os.ShouldBe("windows");
        // v2 host fields are optional and nullable; a wrong type is invalid.
        var v2 = RcLedger.ToJson(SampleDocument()).Replace(RcLedger.Format, RcLedger.FormatV2);
        AssertReads("v2-no-host", v2);
        AssertReads("v2-null-host", v2.Replace("{\"chunkId\":", "{\"hostId\":null,\"os\":null,\"chunkId\":"));
        AssertReadRefused("v2-number-host", v2.Replace("{\"chunkId\":", "{\"hostId\":5,\"chunkId\":"), "type");
        AssertReadRefused("v2-identity-host", v2.Replace("{\"suite\":\"antiphon\",\"className\"", "{\"hostId\":\"x\",\"suite\":\"antiphon\",\"className\""), "hostId");
    }

    [Test]
    public void V2OmitsHostWhenSwitchOff()
    {
        var fixture = NewFixture();
        Stamp(fixture);
        var text = File.ReadAllText(fixture.Output);
        text.ShouldNotContain("hostId");
        text.ShouldNotContain("\"os\"");
        text.ShouldNotContain(RcLedger.FormatV2);
        text.ShouldContain("\"durationSeconds\":42.5");
        // The stamp refuses an existing output path, as Write does.
        Refusal(() => Stamp(fixture, includeHost: true, hostId: "server2", os: "linux")).ShouldBe("ledger-exists");
        File.ReadAllText(fixture.Output).ShouldBe(text, "existing output changed");
    }
}
