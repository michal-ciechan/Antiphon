using System.Diagnostics;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S2: boundary metadata and chunk identities. A chunk id is the canonical SHA-256 of length-delimited
/// UTF-8 membership, independent of packing order; one identity belongs to one boundary; a short-id prefix
/// collision is rebuilt as unassigned, keyed by the full digest, and stays dirty.
/// </summary>
[Category("Unit")]
[Timeout(60_000)]
public sealed class RcBoundaryChunkIdTests
{
    private static readonly Lazy<RcBoundaryMap> Map = new(() => RcBoundary.ReadMap(RepoRoot()));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "tests", "Antiphon.Tests", "Layering", "layers.txt"))) return dir.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    private static RcBoundaryRow Row(string boundary, string key, string suite = "antiphon", string caseKey = null) =>
        new() { Boundary = boundary, Suite = suite, IdentityKey = key, CaseKey = caseKey };

    private static RcBoundaryMembership Membership(string boundary, string suite, params (string Key, string Case)[] identities) => new()
    {
        Suite = suite, Boundary = boundary, Identities = identities.Select(i => new RcBoundaryMember { IdentityKey = i.Key, CaseKey = i.Case }).ToArray(),
    };

    private static string Id(string suite, string boundary, params string[] keys) => RcBoundary.ChunkId(suite, boundary, keys);

    private static string Refusal(Action action) => Should.Throw<RcBoundaryRefusal>(action).Message;

    private static string Boundary(string suite, string path, string source) => RcBoundary.ClassBoundary(Map.Value, suite, path, source);

    [Test]
    public void OrdinalSortAndSingleDigest()
    {
        var ascending = Enumerable.Range(0, 800).Select(i => Row("core", "A" + i.ToString("D4") + "Tests/M")).ToArray();
        var shuffled = ascending.ToArray();
        var random = new Random(1871);
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        var reversed = ascending.Reverse().ToArray();
        RcBoundary.NewChunk(shuffled);
        foreach (var sample in new[] { shuffled, reversed })
        {
            var clock = Stopwatch.StartNew();
            var chunk = RcBoundary.NewChunk(sample);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            chunk.IdentityKeys.ShouldBe(ascending.Select(r => r.IdentityKey).ToArray());
        }
        RcBoundaryIdentity I(string c, string m, string k) => new() { Class = c, Method = m, CaseKey = k };
        var ordered = RcBoundary.SortIdentities([I("a", "M", ""), I("Z", "M", ""), I("A", "m", ""), I("A", "M", "a"), I("A", "M", "Z"), I("A", "M", "")]);
        string.Join(":", ordered.Select(o => o.Class + "/" + o.Method + "/" + o.CaseKey)).ShouldBe("A/M/:A/M/Z:A/M/a:A/m/:Z/M/:a/M/");
        var withNul = RcBoundary.SortIdentities([I("A\0", "M", ""), I("A", "M\0", ""), I("A", "M", "\0"), I("A", "M", "")]);
        withNul[0].CaseKey.ShouldBe("");
        withNul[1].CaseKey.ShouldBe("\0");
        withNul[2].Method.ShouldBe("M\0");
        withNul[3].Class.ShouldBe("A\0");
        Id("antiphon", "core", "ZTests/B", "ATests/A").ShouldBe("b-core-eca0b76b8b1ed195");
    }

    [Test]
    public void ExplicitCaseKeyCount()
    {
        Refusal(() => RcBoundary.ChunkId("antiphon", "core", ["ATests/M", "BTests/M"], [""])).ShouldBe("boundary-chunk: case key count");
        Refusal(() => RcBoundary.ChunkId("antiphon", "core", ["ATests/M"], [])).ShouldBe("boundary-chunk: case key count");
        Refusal(() => RcBoundary.ChunkId("antiphon", "core", ["ATests/M"], null)).ShouldStartWith("boundary-chunk:");
        var omitted = Id("antiphon", "core", "ZTests/B", "ATests/A");
        omitted.ShouldBe("b-core-eca0b76b8b1ed195");
        RcBoundary.ChunkId("antiphon", "core", ["ZTests/B", "ATests/A"], ["", ""]).ShouldBe(omitted);
        RcBoundary.ChunkId("antiphon", "core", ["ATests/A"], [""]).ShouldMatch("^b-core-[0-9a-f]{16}$");
    }

    [Test]
    public void DuplicateMembershipsAndUnassignedCollisions()
    {
        var one = Membership("core", "antiphon", ("ATests/M", ""));
        var overlap = Membership("core", "antiphon", ("ATests/M", ""), ("BTests/M", null));
        var otherBoundary = Membership("git", "antiphon", ("ATests/M", ""));
        Refusal(() => RcBoundary.ResolveChunks([one, one])).ShouldStartWith("boundary-chunk:");
        Refusal(() => RcBoundary.ResolveChunks([one, overlap])).ShouldStartWith("boundary-chunk:");
        Refusal(() => RcBoundary.ResolveChunks([one, otherBoundary])).ShouldStartWith("boundary-chunk:");
        Refusal(() => RcBoundary.NewChunk([Row("core", "ATests/M"), Row("core", "ATests/M")])).ShouldStartWith("boundary-chunk:");
        var distinct = RcBoundary.ResolveChunks([one, Membership("core", "scripts", ("ATests/M", "")), Membership("core", "antiphon", ("ATests/M", "case"))]);
        distinct.Length.ShouldBe(3);
        foreach (var chunk in distinct) (chunk.Collision || chunk.AlwaysDirty).ShouldBeFalse();
        // Force only the digest prefix; retain the real SHA suffix and all real construction/rebuild paths.
        // A real unassigned 64-bit collision is costly.
        static string Forced(string suite, string boundary, RcBoundaryIdentity[] identities)
        {
            var hex = RcBoundary.CanonicalDigest(suite, boundary, identities);
            return boundary == "unassigned" ? "0000000000000000" + hex.Substring(16) : hex;
        }
        var chunks = RcBoundary.ResolveChunks([Membership("unassigned", "antiphon", ("ATests/M", "left")), Membership("unassigned", "antiphon", ("BTests/M", "right"))], Forced);
        chunks.Length.ShouldBe(2);
        foreach (var chunk in chunks)
        {
            (chunk.Collision && chunk.AlwaysDirty && chunk.Boundary == "unassigned").ShouldBeTrue();
            chunk.FullDigest.ShouldMatch("^[0-9a-f]{64}$");
            chunk.ChunkId.ShouldBe("b-unassigned-" + chunk.FullDigest);
        }
        chunks.Select(c => c.ChunkId).Distinct().Count().ShouldBe(2, "colliding membership erased");
        string.Join(",", chunks.Select(c => c.CaseKeys[0])).ShouldBe("left,right");
        RcBoundary.IsChunkId(chunks[0].ChunkId).ShouldBeTrue();
        chunks[0].ChunkId.Length.ShouldBe(77);
        RcBoundary.IsChunkId("b-core-eca0b76b8b1ed195").ShouldBeTrue();
        RcBoundary.IsChunkId("b-session-runner-0123456789abcdef").ShouldBeTrue();
        RcBoundary.IsChunkId("b-unassigned-0123456789abcdef").ShouldBeTrue();
        RcBoundary.IsChunkId("b-core-" + chunks[0].FullDigest).ShouldBeFalse();
        RcBoundary.IsChunkId("b-unassigned-" + string.Concat(Enumerable.Repeat("ab", 16))).ShouldBeFalse();
        RcBoundary.IsChunkId("b-not-a-boundary-0123456789abcdef").ShouldBeFalse();
        RcBoundary.IsChunkId("b-core-eca0b76b8b1ed195\n").ShouldBeFalse();
    }

    [Test]
    public void LoneSurrogatesFailClosed()
    {
        foreach (var code in new[] { 0xd800, 0xdc00 })
        {
            var bad = ((char)code).ToString();
            foreach (var key in new[] { bad + "Tests/M", "ATests/" + bad })
            {
                Refusal(() => Id("antiphon", "core", key)).ShouldStartWith("boundary-chunk:");
                Refusal(() => RcBoundary.NewChunk([Row("core", key)])).ShouldStartWith("boundary-chunk:");
            }
            Refusal(() => RcBoundary.ChunkId("antiphon", "core", ["ATests/M"], [bad])).ShouldStartWith("boundary-chunk:");
        }
        // A well-formed pair and the replacement character remain legal and distinct.
        RcBoundary.ChunkId("antiphon", "core", ["ATests/M"], [char.ConvertFromUtf32(0x1f600)])
            .ShouldNotBe(RcBoundary.ChunkId("antiphon", "core", ["ATests/M"], ["�"]));
    }

    [Test]
    public void StableIdIgnoresOrder()
    {
        var id = Id("antiphon", "core", "ZTests/B", "ATests/A");
        // Independently pinned SHA-256 of length-delimited UTF-8 membership bytes.
        id.ShouldBe("b-core-eca0b76b8b1ed195");
        Id("antiphon", "core", "ATests/A", "ZTests/B").ShouldBe(id, "packing order changed id");
        Id("scripts", "core", "ATests/A", "ZTests/B").ShouldNotBe(id, "suite omitted");
        Id("antiphon", "git", "ATests/A", "ZTests/B").ShouldNotBe(id, "boundary omitted");
        Id("antiphon", "core", "ATests/a", "ZTests/B").ShouldNotBe(id, "identity case folded");
        Should.Throw<RcBoundaryRefusal>(() => Id("antiphon", "core"));
        Should.Throw<RcBoundaryRefusal>(() => Id("antiphon", "core", "ATests/A", "ATests/A"));
    }

    [Test]
    public void MovedClassChangesId()
    {
        var beforeA = Id("antiphon", "core", "ATests/A", "BTests/B");
        var beforeB = Id("antiphon", "core", "CTests/C");
        Id("antiphon", "core", "ATests/A").ShouldNotBe(beforeA, "source bin id survived move");
        Id("antiphon", "core", "BTests/B", "CTests/C").ShouldNotBe(beforeB, "destination bin id survived move");
        Id("antiphon", "core", "ATests/A", "BTests/Other").ShouldNotBe(beforeA, "method omitted");
    }

    [Test]
    public void UnassignedStaysAlone()
    {
        Boundary("antiphon", "tests/Antiphon.Tests/Infrastructure/Unknown.cs", "namespace Antiphon.Tests.Infrastructure;").ShouldBe("unassigned");
        Boundary("unknown", "tests/Elsewhere.cs", "").ShouldBe("unassigned");
        Should.Throw<RcBoundaryRefusal>(() => RcBoundary.NewChunk([Row("core", "ATests/A"), Row("unassigned", "BTests/B")]));
        var alone = RcBoundary.NewChunk([Row("unassigned", "BTests/B")]);
        alone.Boundary.ShouldBe("unassigned");
        alone.AlwaysDirty.ShouldBeTrue();
        alone.ChunkId.ShouldMatch("^b-unassigned-[0-9a-f]{16}$");
    }

    [Test]
    public void TwoBoundariesNeverShare()
    {
        Should.Throw<RcBoundaryRefusal>(() => RcBoundary.NewChunk([Row("core", "ATests/A"), Row("git", "BTests/B")]));
        Should.Throw<RcBoundaryRefusal>(() => RcBoundary.NewChunk([Row("core", "ATests/A"), Row("core", "BTests/B", "scripts")]));
        var chunk = RcBoundary.NewChunk([Row("core", "ZTests/B"), Row("core", "ATests/A")]);
        (chunk.Boundary, chunk.Suite, chunk.AlwaysDirty).ShouldBe(("core", "antiphon", false));
        chunk.IdentityKeys.ShouldBe(["ATests/A", "ZTests/B"]);
        // Lane path wins over both production references and namespace fallback.
        foreach (var (folder, lane) in new[] { ("Scripts", "scripts"), ("Checkpoints", "checkpoints"), ("AgentTui", "tui") })
            Boundary("antiphon", "tests/Antiphon.Tests/" + folder + "/Example.cs", "namespace Antiphon.Tests.Domain; string p = \"server/Domain/Example.cs\";").ShouldBe(lane);
        foreach (var suite in new[] { "e2e", "client", "messaging", "session-runner", "pty-host", "agents-pty", "observability", "server-git", "markdown-pdf" })
            Boundary(suite, "arbitrary", "").ShouldBe(suite);
        foreach (var (path, expected) in new[]
        {
            ("server/Api/Foo.cs", "host"), ("server/Domain/Foo.cs", "core"), ("server/Infrastructure/Data/Foo.cs", "persistence"),
            ("server/Application/Services/GitIndexLock.cs", "git"), ("server/Infrastructure/Agents/Pty/Foo.cs", "runners"),
            ("server/Infrastructure/Agents/Foo.cs", "integrations"), ("server/Application/Services/Foo.cs", "application"),
            ("server/Infrastructure/Resilience/NpgsqlTransientFailureClassifier.cs", "persistence"),
        })
            Boundary("antiphon", "tests/Antiphon.Tests/Other/Foo.cs", "namespace Antiphon.Tests.Domain; string p = \"" + path + "\";").ShouldBe(expected, path);
        Boundary("antiphon", "other", "namespace Antiphon.Tests.Domain; string a=\"server/Application/Services/Foo.cs\"; string b=\"server/Domain/Foo.cs\";")
            .ShouldBe("unassigned", "conflicting rules assigned");
        Boundary("antiphon", "other", "string a=\"server/Domain/Foo.cs\"; string b=\"server/Domain/Bar.cs\";").ShouldBe("core");
        Boundary("antiphon", "other", "namespace Antiphon.Tests.Domain.Sub;").ShouldBe("core");
        Boundary("antiphon", "other", "namespace Antiphon.Tests.DomainExtra;").ShouldBe("unassigned");
        Map.Value.Edges["host"].Length.ShouldBe(6);
        Map.Value.Edges["git"][0].ShouldBe("core");
        Boundary("antiphon", "other", "string p=\"server/Infrastructure/Agents/nested/Foo.cs\";").ShouldBe("unassigned", "single star crossed directory");
    }

    [Test]
    public void CaseKeyChangesIdentity()
    {
        var held = RcBoundary.NewChunk([Row("core", "ATests/A", caseKey: "one<"), Row("core", "ATests/A", caseKey: "two&'")]);
        held.CaseKeys.ShouldBe(["one<", "two&'"]);
        held.ChunkId.ShouldNotBe(Id("antiphon", "core", "ATests/A"), "caseKey omitted from digest");
    }

    [Test]
    public void CanonicalEncodingKeepsSpecialCharacters() =>
        // Independently computed length-delimited SHA-256 of a key containing < > & '.
        Id("antiphon", "core", "A<T>/M>&'").ShouldBe("b-core-f2e0eae2ff0cf3c2");

    [Test]
    public void ShortIdCollisionIsUnassigned()
    {
        // Forced 64-bit prefix collision of CTests/M under suite antiphon, boundary core, found by a
        // distinguished-point search over the canonical digest.
        const string left = "10788987499786589518";
        const string right = "17490688019260441962";
        var idLeft = RcBoundary.ChunkId("antiphon", "core", ["CTests/M"], [left]);
        idLeft.ShouldBe(RcBoundary.ChunkId("antiphon", "core", ["CTests/M"], [right]), "fixture is not a short-id collision");
        idLeft.ShouldBe("b-core-360a4ef28b10a96c");
        var one = RcBoundary.NewChunk([Row("core", "ATests/A")]);
        one.FullDigest.ShouldMatch("^[0-9a-f]{64}$");
        one.ChunkId.ShouldBe("b-core-" + one.FullDigest.Substring(0, 16));
        var resolved = RcBoundary.ResolveChunks([Membership("core", "antiphon", ("CTests/M", left)), Membership("core", "antiphon", ("CTests/M", right))]);
        resolved.Length.ShouldBe(2);
        foreach (var row in resolved)
        {
            (row.Boundary == "unassigned" && row.AlwaysDirty && row.Collision).ShouldBeTrue("short-id collision accepted");
            row.ChunkId.ShouldStartWith("b-unassigned-");
        }
        string.Join("|", resolved.Select(r => r.CaseKeys[0])).ShouldBe(left + "|" + right);
        resolved[0].ChunkId.ShouldNotBe(resolved[1].ChunkId);
        resolved[0].FullDigest.ShouldNotBe(resolved[1].FullDigest);
        var clean = RcBoundary.ResolveChunks([Membership("core", "antiphon", ("ATests/A", "")), Membership("core", "antiphon", ("ZTests/B", ""))]);
        clean.Length.ShouldBe(2);
        foreach (var row in clean) (row.Boundary == "core" && !row.Collision && !row.AlwaysDirty).ShouldBeTrue("distinct short ids refused");
        Should.Throw<RcBoundaryRefusal>(() => RcBoundary.ResolveChunks([Membership("core", "antiphon", ("ATests/A", "")), Membership("core", "antiphon", ("ATests/A", ""))]));
    }
}
